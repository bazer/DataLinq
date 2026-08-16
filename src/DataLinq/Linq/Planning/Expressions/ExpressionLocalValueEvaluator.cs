using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning.Expressions;

internal readonly record struct ExpressionLocalValueEvaluationOptions(
    bool AllowCompatibilityMemberReflection,
    bool AllowCompatibilityMethodReflection)
{
    public static ExpressionLocalValueEvaluationOptions Default { get; } = new(
        AllowCompatibilityMemberReflection: true,
        AllowCompatibilityMethodReflection: true);

    public static ExpressionLocalValueEvaluationOptions AotStrict { get; } = new(
        AllowCompatibilityMemberReflection: false,
        AllowCompatibilityMethodReflection: false);
}

internal static class ExpressionLocalValueEvaluator
{
    public static object? Evaluate(Expression expression, ParameterExpression? parameter = null, object? parameterValue = null)
        => Evaluate(expression, parameter, parameterValue, ExpressionLocalValueEvaluationOptions.Default);

    public static object? Evaluate(
        Expression expression,
        ParameterExpression? parameter,
        object? parameterValue,
        ExpressionLocalValueEvaluationOptions options)
    {
        expression = UnwrapConvert(expression);
        switch (expression)
        {
            case ConstantExpression constant:
                return constant.Value;

            case ParameterExpression current when parameter is not null && current == parameter:
                return parameterValue;

            case UnaryExpression unary when unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked:
                return Convert.ChangeType(
                    Evaluate(unary.Operand, parameter, parameterValue, options),
                    GetNonNullableType(unary.Type),
                    CultureInfo.InvariantCulture);

            case UnaryExpression unary when unary.NodeType is ExpressionType.Negate or ExpressionType.NegateChecked:
                return EvaluateNegation(unary, parameter, parameterValue, options);

            case MemberExpression member:
                var instance = member.Expression is null
                    ? null
                    : Evaluate(member.Expression, parameter, parameterValue, options);

                if (TryEvaluateSupportedMember(member, instance, out var supportedValue))
                    return supportedValue;

                if (!options.AllowCompatibilityMemberReflection)
                    throw new QueryTranslationException(
                        $"Local member '{member.Member.Name}' requires compatibility member reflection in DataLinq expression parsing. Expression: {member}");

                return member.Member switch
                {
                    FieldInfo field => field.GetValue(instance),
                    PropertyInfo property => property.GetValue(instance),
                    _ => throw UnsupportedMember(member)
                };

            case NewArrayExpression newArray:
                return newArray.Expressions
                    .Select(item => Evaluate(item, parameter, parameterValue, options))
                    .ToArray();

            case BinaryExpression binary when binary.NodeType == ExpressionType.ArrayIndex:
                return EvaluateIndex(binary, binary.Left, [binary.Right], parameter, parameterValue, options);

            case IndexExpression index:
                return EvaluateIndex(index, index.Object, index.Arguments, parameter, parameterValue, options);

            case MethodCallExpression methodCall when TryEvaluateSupportedMethod(methodCall, parameter, parameterValue, options, out var value):
                return value;

            case MethodCallExpression methodCall:
                return EvaluateCompatibilityMethod(methodCall, parameter, parameterValue, options);

            default:
                throw new QueryTranslationException($"Local expression '{expression}' is not supported in DataLinq expression parsing.");
        }
    }

    private static object? EvaluateNegation(
        UnaryExpression unary,
        ParameterExpression? parameter,
        object? parameterValue,
        ExpressionLocalValueEvaluationOptions options)
    {
        var resultType = GetNonNullableType(unary.Type);
        if (unary.Method is not null && !IsDecimalNegation(unary.Method, resultType))
        {
            throw new QueryTranslationException(
                $"Local user-defined unary operator '{unary.Method}' is not supported in DataLinq expression parsing. Expression: {unary}");
        }

        var operand = Evaluate(unary.Operand, parameter, parameterValue, options);
        if (operand is null)
        {
            if (Nullable.GetUnderlyingType(unary.Type) is not null)
                return null;

            throw new QueryTranslationException(
                $"Local numeric negation '{unary}' produced a null operand for non-nullable result type '{unary.Type}'.");
        }

        var isChecked = unary.NodeType == ExpressionType.NegateChecked;
        return Type.GetTypeCode(resultType) switch
        {
            TypeCode.Int16 when isChecked => checked((short)-Convert.ToInt16(operand, CultureInfo.InvariantCulture)),
            TypeCode.Int16 => unchecked((short)-Convert.ToInt16(operand, CultureInfo.InvariantCulture)),
            TypeCode.Int32 when isChecked => checked(-Convert.ToInt32(operand, CultureInfo.InvariantCulture)),
            TypeCode.Int32 => unchecked(-Convert.ToInt32(operand, CultureInfo.InvariantCulture)),
            TypeCode.Int64 when isChecked => checked(-Convert.ToInt64(operand, CultureInfo.InvariantCulture)),
            TypeCode.Int64 => unchecked(-Convert.ToInt64(operand, CultureInfo.InvariantCulture)),
            TypeCode.Single => -Convert.ToSingle(operand, CultureInfo.InvariantCulture),
            TypeCode.Double => -Convert.ToDouble(operand, CultureInfo.InvariantCulture),
            TypeCode.Decimal => -Convert.ToDecimal(operand, CultureInfo.InvariantCulture),
            _ => throw new QueryTranslationException(
                $"Local numeric negation for result type '{unary.Type}' is not supported in DataLinq expression parsing. Expression: {unary}")
        };
    }

    private static bool IsDecimalNegation(MethodInfo method, Type resultType)
        => resultType == typeof(decimal) &&
           method.DeclaringType == typeof(decimal) &&
           method.Name == "op_UnaryNegation" &&
           method.IsStatic &&
           method.ReturnType == typeof(decimal) &&
           method.GetParameters() is [{ ParameterType: { } parameterType }] &&
           parameterType == typeof(decimal);

    private static bool TryEvaluateSupportedMember(MemberExpression member, object? instance, out object? value)
    {
        if (member.Expression is not null &&
            Nullable.GetUnderlyingType(member.Expression.Type) is not null)
        {
            if (member.Member.Name == nameof(Nullable<int>.HasValue))
            {
                value = instance is not null;
                return true;
            }

            if (member.Member.Name == nameof(Nullable<int>.Value))
            {
                value = instance ?? throw new InvalidOperationException("Nullable object must have a value.");
                return true;
            }
        }

        if (instance is string text &&
            member.Member.Name == nameof(string.Length))
        {
            value = text.Length;
            return true;
        }

        value = null;
        return false;
    }

    private static object? EvaluateIndex(
        Expression expression,
        Expression? instanceExpression,
        IReadOnlyList<Expression> indexExpressions,
        ParameterExpression? parameter,
        object? parameterValue,
        ExpressionLocalValueEvaluationOptions options)
    {
        var instance = instanceExpression is null
            ? null
            : Evaluate(instanceExpression, parameter, parameterValue, options);
        var indexes = indexExpressions
            .Select(index => Evaluate(index, parameter, parameterValue, options))
            .ToArray();

        if (indexes is [{ } indexValue] &&
            Convert.ToInt32(indexValue, CultureInfo.InvariantCulture) is var index)
        {
            return instance switch
            {
                Array array => array.GetValue(index),
                IList list => list[index],
                _ => throw new QueryTranslationException($"Local index expression '{expression}' is not supported in DataLinq expression parsing.")
            };
        }

        throw new QueryTranslationException($"Local index expression '{expression}' is not supported in DataLinq expression parsing.");
    }

    private static bool TryEvaluateSupportedMethod(
        MethodCallExpression methodCall,
        ParameterExpression? parameter,
        object? parameterValue,
        ExpressionLocalValueEvaluationOptions options,
        out object? value)
    {
        if (methodCall.Arguments.Count == 0 &&
            methodCall.Method.IsGenericMethod &&
            methodCall.Method.GetGenericMethodDefinition() == ArrayEmptyMethod)
        {
            value = Array.Empty<object?>();
            return true;
        }

        if (methodCall.Arguments.Count == 0 &&
            methodCall.Method.IsGenericMethod &&
            methodCall.Method.GetGenericMethodDefinition() == EnumerableEmptyMethod)
        {
            value = Array.Empty<object?>();
            return true;
        }

        if (TryGetSupportedStringMethod(methodCall, out var stringMethod))
        {
            var instance = Evaluate(methodCall.Object!, parameter, parameterValue, options);
            var arguments = methodCall.Arguments
                .Select(argument => Evaluate(argument, parameter, parameterValue, options))
                .ToArray();
            var text = instance as string
                ?? throw new NullReferenceException("Cannot invoke a string method on a null receiver.");

            value = stringMethod switch
            {
                SupportedStringMethod.Trim => text.Trim(),
                SupportedStringMethod.ToUpper => text.ToUpper(CultureInfo.CurrentCulture),
                SupportedStringMethod.ToLower => text.ToLower(CultureInfo.CurrentCulture),
                SupportedStringMethod.SubstringFrom => text.Substring(Convert.ToInt32(arguments[0], CultureInfo.InvariantCulture)),
                SupportedStringMethod.SubstringRange => text.Substring(
                    Convert.ToInt32(arguments[0], CultureInfo.InvariantCulture),
                    Convert.ToInt32(arguments[1], CultureInfo.InvariantCulture)),
                _ => throw new QueryTranslationException(
                    $"Supported string method '{methodCall.Method.Name}' has no local evaluation implementation.")
            };

            return true;
        }

        value = null;
        return false;
    }

    private static bool TryGetSupportedStringMethod(
        MethodCallExpression methodCall,
        out SupportedStringMethod supportedMethod)
    {
        supportedMethod = default;
        if (methodCall.Object is null ||
            methodCall.Method.IsStatic ||
            methodCall.Method.DeclaringType != typeof(string) ||
            methodCall.Method.ReturnType != typeof(string))
        {
            return false;
        }

        if (methodCall.Method.Name == nameof(string.Trim) &&
            methodCall.Arguments.Count == 0)
        {
            supportedMethod = SupportedStringMethod.Trim;
            return true;
        }

        if (methodCall.Method.Name == nameof(string.ToUpper) &&
            methodCall.Arguments.Count == 0)
        {
            supportedMethod = SupportedStringMethod.ToUpper;
            return true;
        }

        if (methodCall.Method.Name == nameof(string.ToLower) &&
            methodCall.Arguments.Count == 0)
        {
            supportedMethod = SupportedStringMethod.ToLower;
            return true;
        }

        if (methodCall.Method.Name == nameof(string.Substring) &&
            methodCall.Arguments.Count == 1 &&
            methodCall.Arguments[0].Type == typeof(int))
        {
            supportedMethod = SupportedStringMethod.SubstringFrom;
            return true;
        }

        if (methodCall.Method.Name == nameof(string.Substring) &&
            methodCall.Arguments.Count == 2 &&
            methodCall.Arguments[0].Type == typeof(int) &&
            methodCall.Arguments[1].Type == typeof(int))
        {
            supportedMethod = SupportedStringMethod.SubstringRange;
            return true;
        }

        return false;
    }

    private static object? EvaluateCompatibilityMethod(
        MethodCallExpression methodCall,
        ParameterExpression? parameter,
        object? parameterValue,
        ExpressionLocalValueEvaluationOptions options)
    {
        if (!options.AllowCompatibilityMethodReflection)
        {
            throw new QueryTranslationException(
                $"Local method call '{methodCall.Method.Name}' requires compatibility method reflection in DataLinq expression parsing. " +
                "Capture the value before building the query or use a documented DataLinq query function.");
        }

        var instance = methodCall.Object is null
            ? null
            : Evaluate(methodCall.Object, parameter, parameterValue, options);
        var arguments = methodCall.Arguments
            .Select(argument => Evaluate(argument, parameter, parameterValue, options))
            .ToArray();

        try
        {
            return methodCall.Method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new QueryTranslationException(
                $"Local method call '{methodCall.Method.Name}' threw while DataLinq evaluated it as a local value.",
                exception.InnerException);
        }
    }

    private static readonly MethodInfo ArrayEmptyMethod = ((Func<int[]>)Array.Empty<int>)
        .Method
        .GetGenericMethodDefinition();

    private static readonly MethodInfo EnumerableEmptyMethod = ((Func<IEnumerable<int>>)Enumerable.Empty<int>)
        .Method
        .GetGenericMethodDefinition();

    private enum SupportedStringMethod
    {
        Trim,
        ToUpper,
        ToLower,
        SubstringFrom,
        SubstringRange
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert ||
                unary.NodeType == ExpressionType.ConvertChecked ||
                unary.NodeType == ExpressionType.Quote))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static Type GetNonNullableType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static QueryTranslationException UnsupportedMember(MemberExpression member) =>
        new($"Local member '{member.Member.Name}' is not supported in DataLinq expression parsing.");
}
