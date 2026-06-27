using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;
using DataLinq.Instances;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace DataLinq.Linq;

internal readonly record struct ProjectionEvaluationOptions(
    bool AllowCompatibilityObjectConstruction,
    bool AllowCompatibilityMemberReflection)
{
    public static ProjectionEvaluationOptions Default { get; } = new(
        AllowCompatibilityObjectConstruction: true,
        AllowCompatibilityMemberReflection: true);

    public static ProjectionEvaluationOptions AotStrict { get; } = new(
        AllowCompatibilityObjectConstruction: false,
        AllowCompatibilityMemberReflection: false);
}

internal static class ProjectionExpressionEvaluator
{
    public static object? Evaluate(
        Expression expression,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?>? parameterValues = null)
    {
        return Evaluate(expression, querySourceValues, parameterValues, ProjectionEvaluationOptions.Default);
    }

    public static object? Evaluate(
        Expression expression,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?>? parameterValues,
        ProjectionEvaluationOptions options)
    {
        return EvaluateCore(expression, querySourceValues, parameterValues ?? EmptyParameters, options);
    }

    public static object? Evaluate(Expression expression)
    {
        return Evaluate(expression, EmptyQuerySources, EmptyParameters);
    }

    public static object? Evaluate(
        Expression expression,
        IQuerySource querySource,
        object? value)
    {
        return Evaluate(expression, new Dictionary<IQuerySource, object?> { [querySource] = value });
    }

    public static object? Evaluate(
        Expression expression,
        ParameterExpression parameter,
        object? value)
    {
        return Evaluate(expression, EmptyQuerySources, new Dictionary<ParameterExpression, object?> { [parameter] = value });
    }

    public static object? Evaluate(
        Expression expression,
        ParameterExpression parameter,
        object? value,
        ProjectionEvaluationOptions options)
    {
        return Evaluate(expression, EmptyQuerySources, new Dictionary<ParameterExpression, object?> { [parameter] = value }, options);
    }

    private static readonly IReadOnlyDictionary<IQuerySource, object?> EmptyQuerySources = new Dictionary<IQuerySource, object?>();
    private static readonly IReadOnlyDictionary<ParameterExpression, object?> EmptyParameters = new Dictionary<ParameterExpression, object?>();

    private static object? EvaluateCore(
        Expression expression,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        expression = UnwrapQuote(expression);

        return expression switch
        {
            ConstantExpression constant => constant.Value,
            QuerySourceReferenceExpression querySource => GetQuerySourceValue(querySource, querySourceValues),
            ParameterExpression parameter => GetParameterValue(parameter, parameterValues),
            UnaryExpression unary => EvaluateUnary(unary, querySourceValues, parameterValues, options),
            BinaryExpression binary => EvaluateBinary(binary, querySourceValues, parameterValues, options),
            MemberExpression member => EvaluateMember(member, querySourceValues, parameterValues, options),
            MethodCallExpression methodCall => EvaluateMethodCall(methodCall, querySourceValues, parameterValues, options),
            NewExpression newExpression => EvaluateNew(newExpression, querySourceValues, parameterValues, options),
            NewArrayExpression newArray => EvaluateNewArray(newArray, querySourceValues, parameterValues, options),
            ConditionalExpression conditional => EvaluateConditional(conditional, querySourceValues, parameterValues, options),
            _ => throw Unsupported(expression)
        };
    }

    private static Expression UnwrapQuote(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            expression = quote.Operand;

        return expression;
    }

    private static object? GetQuerySourceValue(
        QuerySourceReferenceExpression querySource,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues)
    {
        if (querySourceValues.TryGetValue(querySource.ReferencedQuerySource, out var value))
            return value;

        throw new QueryTranslationException($"Projection references unsupported query source '{querySource.ReferencedQuerySource.ItemName}'.");
    }

    private static object? GetParameterValue(
        ParameterExpression parameter,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        if (parameterValues.TryGetValue(parameter, out var value))
            return value;

        throw new QueryTranslationException($"Projection references unsupported parameter '{parameter.Name}'.");
    }

    private static object? EvaluateUnary(
        UnaryExpression unary,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        var value = EvaluateCore(unary.Operand, querySourceValues, parameterValues, options);

        return unary.NodeType switch
        {
            ExpressionType.Convert or ExpressionType.ConvertChecked => ConvertValue(value, unary.Type),
            ExpressionType.Not => value is bool boolean ? !boolean : throw Unsupported(unary),
            _ => throw Unsupported(unary)
        };
    }

    private static object? EvaluateBinary(
        BinaryExpression binary,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        var left = EvaluateCore(binary.Left, querySourceValues, parameterValues, options);
        var right = EvaluateCore(binary.Right, querySourceValues, parameterValues, options);

        return binary.NodeType switch
        {
            ExpressionType.Add => Add(left, right),
            ExpressionType.Equal => Equals(left, right),
            ExpressionType.NotEqual => !Equals(left, right),
            ExpressionType.AndAlso => Convert.ToBoolean(left, CultureInfo.InvariantCulture) && Convert.ToBoolean(right, CultureInfo.InvariantCulture),
            ExpressionType.OrElse => Convert.ToBoolean(left, CultureInfo.InvariantCulture) || Convert.ToBoolean(right, CultureInfo.InvariantCulture),
            ExpressionType.GreaterThan => Compare(left, right) > 0,
            ExpressionType.GreaterThanOrEqual => Compare(left, right) >= 0,
            ExpressionType.LessThan => Compare(left, right) < 0,
            ExpressionType.LessThanOrEqual => Compare(left, right) <= 0,
            _ => throw Unsupported(binary)
        };
    }

    private static object? Add(object? left, object? right)
    {
        if (left is string || right is string)
            return string.Concat(left, right);

        if (left is null || right is null)
            throw new QueryTranslationException("Projection arithmetic does not support null operands.");

        var leftType = Nullable.GetUnderlyingType(left.GetType()) ?? left.GetType();
        var rightType = Nullable.GetUnderlyingType(right.GetType()) ?? right.GetType();

        if (leftType == typeof(decimal) || rightType == typeof(decimal))
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture) + Convert.ToDecimal(right, CultureInfo.InvariantCulture);

        if (leftType == typeof(double) || rightType == typeof(double) ||
            leftType == typeof(float) || rightType == typeof(float))
            return Convert.ToDouble(left, CultureInfo.InvariantCulture) + Convert.ToDouble(right, CultureInfo.InvariantCulture);

        if (leftType == typeof(long) || rightType == typeof(long))
            return Convert.ToInt64(left, CultureInfo.InvariantCulture) + Convert.ToInt64(right, CultureInfo.InvariantCulture);

        return Convert.ToInt32(left, CultureInfo.InvariantCulture) + Convert.ToInt32(right, CultureInfo.InvariantCulture);
    }

    private static int Compare(object? left, object? right)
    {
        if (left is IComparable comparable)
            return comparable.CompareTo(right);

        throw new QueryTranslationException($"Projection comparison is not supported for value '{left}'.");
    }

    private static object? EvaluateMember(
        MemberExpression member,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        var instance = member.Expression is null
            ? null
            : EvaluateCore(member.Expression, querySourceValues, parameterValues, options);

        if (TryEvaluateSupportedMember(member, instance, out var supportedValue))
            return supportedValue;

        if (!options.AllowCompatibilityMemberReflection)
            throw new QueryTranslationException(
                $"Projection member '{member.Member.Name}' requires compatibility member reflection. Expression: {member}");

        return member.Member switch
        {
            FieldInfo field => field.GetValue(instance),
            PropertyInfo property => property.GetValue(instance),
            _ => throw Unsupported(member)
        };
    }

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

        if (instance is IModelInstance model &&
            model.Metadata().ValueProperties.TryGetValue(member.Member.Name, out var valueProperty))
        {
            value = model.GetRowData().GetValue(valueProperty.Column);
            return true;
        }

        value = null;
        return false;
    }

    private static object? EvaluateMethodCall(
        MethodCallExpression methodCall,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        var instance = methodCall.Object is null
            ? null
            : EvaluateCore(methodCall.Object, querySourceValues, parameterValues, options);
        var arguments = methodCall.Arguments
            .Select(argument => EvaluateCore(argument, querySourceValues, parameterValues, options))
            .ToArray();

        if (instance is string text)
        {
            return methodCall.Method.Name switch
            {
                nameof(string.Trim) when arguments.Length == 0 => text.Trim(),
                nameof(string.ToUpper) when arguments.Length == 0 => text.ToUpper(CultureInfo.CurrentCulture),
                nameof(string.ToLower) when arguments.Length == 0 => text.ToLower(CultureInfo.CurrentCulture),
                nameof(string.Substring) when arguments.Length == 1 => text.Substring((int)arguments[0]!),
                nameof(string.Substring) when arguments.Length == 2 => text.Substring((int)arguments[0]!, (int)arguments[1]!),
                _ => throw UnsupportedMethod(methodCall)
            };
        }

        throw UnsupportedMethod(methodCall);
    }

    private static object? EvaluateNew(
        NewExpression newExpression,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        if (newExpression.Constructor is null)
            throw Unsupported(newExpression);

        var arguments = newExpression.Arguments
            .Select(argument => EvaluateCore(argument, querySourceValues, parameterValues, options))
            .ToArray();

        if (!options.AllowCompatibilityObjectConstruction)
            throw new QueryTranslationException(
                $"Projection object construction for '{newExpression.Type.FullName}' requires compatibility constructor invocation. Expression: {newExpression}");

        return newExpression.Constructor.Invoke(arguments);
    }

    private static object? EvaluateNewArray(
        NewArrayExpression newArray,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        var elementType = newArray.Type.GetElementType()
            ?? throw Unsupported(newArray);
        var values = newArray.Expressions
            .Select(expression => EvaluateCore(expression, querySourceValues, parameterValues, options))
            .ToArray();
        if (elementType == typeof(string))
            return values.Select(value => (string?)ConvertValue(value, elementType)).ToArray();
        if (elementType == typeof(int))
            return CreateArray<int>(values, elementType);
        if (elementType == typeof(long))
            return CreateArray<long>(values, elementType);
        if (elementType == typeof(short))
            return CreateArray<short>(values, elementType);
        if (elementType == typeof(byte))
            return CreateArray<byte>(values, elementType);
        if (elementType == typeof(bool))
            return CreateArray<bool>(values, elementType);
        if (elementType == typeof(decimal))
            return CreateArray<decimal>(values, elementType);
        if (elementType == typeof(double))
            return CreateArray<double>(values, elementType);
        if (elementType == typeof(float))
            return CreateArray<float>(values, elementType);
        if (elementType == typeof(Guid))
            return CreateArray<Guid>(values, elementType);
        if (elementType == typeof(DateTime))
            return CreateArray<DateTime>(values, elementType);
        if (elementType == typeof(DateOnly))
            return CreateArray<DateOnly>(values, elementType);
        if (elementType == typeof(TimeOnly))
            return CreateArray<TimeOnly>(values, elementType);
        if (elementType == typeof(object))
            return values;

        throw new QueryTranslationException(
            $"Projection array creation for element type '{elementType.FullName}' is not supported without runtime array activation.");
    }

    private static T[] CreateArray<T>(object?[] values, Type elementType) =>
        values.Select(value => (T)ConvertValue(value, elementType)!).ToArray();

    private static object? EvaluateConditional(
        ConditionalExpression conditional,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues,
        ProjectionEvaluationOptions options)
    {
        var test = EvaluateCore(conditional.Test, querySourceValues, parameterValues, options);
        return Convert.ToBoolean(test, CultureInfo.InvariantCulture)
            ? EvaluateCore(conditional.IfTrue, querySourceValues, parameterValues, options)
            : EvaluateCore(conditional.IfFalse, querySourceValues, parameterValues, options);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (targetType == typeof(void))
            return null;

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (value is null)
            return nullableType is not null || !targetType.IsValueType
                ? null
                : GetDefaultValue(targetType);

        if (targetType.IsInstanceOfType(value))
            return value;

        var conversionType = nullableType ?? targetType;
        if (conversionType.IsEnum)
            return Enum.ToObject(conversionType, value);

        return Convert.ChangeType(value, conversionType, CultureInfo.InvariantCulture);
    }

    private static object GetDefaultValue(Type targetType)
    {
        if (targetType == typeof(bool))
            return false;
        if (targetType == typeof(byte))
            return default(byte);
        if (targetType == typeof(sbyte))
            return default(sbyte);
        if (targetType == typeof(short))
            return default(short);
        if (targetType == typeof(ushort))
            return default(ushort);
        if (targetType == typeof(int))
            return default(int);
        if (targetType == typeof(uint))
            return default(uint);
        if (targetType == typeof(long))
            return default(long);
        if (targetType == typeof(ulong))
            return default(ulong);
        if (targetType == typeof(float))
            return default(float);
        if (targetType == typeof(double))
            return default(double);
        if (targetType == typeof(decimal))
            return default(decimal);
        if (targetType == typeof(char))
            return default(char);
        if (targetType == typeof(DateTime))
            return default(DateTime);
        if (targetType == typeof(DateOnly))
            return default(DateOnly);
        if (targetType == typeof(TimeOnly))
            return default(TimeOnly);
        if (targetType == typeof(Guid))
            return default(Guid);
        if (targetType.IsEnum)
            return Enum.ToObject(targetType, 0);

        throw new QueryTranslationException(
            $"Projection cannot materialize null as non-nullable value type '{targetType.FullName}' without runtime activation.");
    }

    private static QueryTranslationException Unsupported(Expression expression) =>
        new($"Projection expression '{expression}' is not supported without runtime expression compilation.");

    private static QueryTranslationException UnsupportedMethod(MethodCallExpression methodCall) =>
        new($"Projection method '{methodCall.Method.Name}' is not supported without runtime method invocation. Expression: {methodCall}");
}
