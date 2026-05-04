using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace DataLinq.Linq;

internal static class ProjectionExpressionEvaluator
{
    public static object? Evaluate(
        Expression expression,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?>? parameterValues = null)
    {
        return EvaluateCore(expression, querySourceValues, parameterValues ?? EmptyParameters);
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

    private static readonly IReadOnlyDictionary<IQuerySource, object?> EmptyQuerySources = new Dictionary<IQuerySource, object?>();
    private static readonly IReadOnlyDictionary<ParameterExpression, object?> EmptyParameters = new Dictionary<ParameterExpression, object?>();

    private static object? EvaluateCore(
        Expression expression,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        expression = UnwrapQuote(expression);

        return expression switch
        {
            ConstantExpression constant => constant.Value,
            QuerySourceReferenceExpression querySource => GetQuerySourceValue(querySource, querySourceValues),
            ParameterExpression parameter => GetParameterValue(parameter, parameterValues),
            UnaryExpression unary => EvaluateUnary(unary, querySourceValues, parameterValues),
            BinaryExpression binary => EvaluateBinary(binary, querySourceValues, parameterValues),
            MemberExpression member => EvaluateMember(member, querySourceValues, parameterValues),
            MethodCallExpression methodCall => EvaluateMethodCall(methodCall, querySourceValues, parameterValues),
            NewExpression newExpression => EvaluateNew(newExpression, querySourceValues, parameterValues),
            NewArrayExpression newArray => EvaluateNewArray(newArray, querySourceValues, parameterValues),
            ConditionalExpression conditional => EvaluateConditional(conditional, querySourceValues, parameterValues),
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
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        var value = EvaluateCore(unary.Operand, querySourceValues, parameterValues);

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
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        var left = EvaluateCore(binary.Left, querySourceValues, parameterValues);
        var right = EvaluateCore(binary.Right, querySourceValues, parameterValues);

        if (binary.Method is not null)
            return binary.Method.Invoke(null, [left, right]);

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
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        var instance = member.Expression is null
            ? null
            : EvaluateCore(member.Expression, querySourceValues, parameterValues);

        return member.Member switch
        {
            FieldInfo field => field.GetValue(instance),
            PropertyInfo property => property.GetValue(instance),
            _ => throw Unsupported(member)
        };
    }

    private static object? EvaluateMethodCall(
        MethodCallExpression methodCall,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        var instance = methodCall.Object is null
            ? null
            : EvaluateCore(methodCall.Object, querySourceValues, parameterValues);
        var arguments = methodCall.Arguments
            .Select(argument => EvaluateCore(argument, querySourceValues, parameterValues))
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
                _ => methodCall.Method.Invoke(instance, arguments)
            };
        }

        return methodCall.Method.Invoke(instance, arguments);
    }

    private static object? EvaluateNew(
        NewExpression newExpression,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        if (newExpression.Constructor is null)
            throw Unsupported(newExpression);

        var arguments = newExpression.Arguments
            .Select(argument => EvaluateCore(argument, querySourceValues, parameterValues))
            .ToArray();

        return newExpression.Constructor.Invoke(arguments);
    }

    private static object? EvaluateNewArray(
        NewArrayExpression newArray,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        var elementType = newArray.Type.GetElementType()
            ?? throw Unsupported(newArray);
        var values = newArray.Expressions
            .Select(expression => EvaluateCore(expression, querySourceValues, parameterValues))
            .ToArray();
        var array = Array.CreateInstance(elementType, values.Length);

        for (var i = 0; i < values.Length; i++)
            array.SetValue(ConvertValue(values[i], elementType), i);

        return array;
    }

    private static object? EvaluateConditional(
        ConditionalExpression conditional,
        IReadOnlyDictionary<IQuerySource, object?> querySourceValues,
        IReadOnlyDictionary<ParameterExpression, object?> parameterValues)
    {
        var test = EvaluateCore(conditional.Test, querySourceValues, parameterValues);
        return Convert.ToBoolean(test, CultureInfo.InvariantCulture)
            ? EvaluateCore(conditional.IfTrue, querySourceValues, parameterValues)
            : EvaluateCore(conditional.IfFalse, querySourceValues, parameterValues);
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (targetType == typeof(void))
            return null;

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (value is null)
            return nullableType is not null || !targetType.IsValueType
                ? null
                : Activator.CreateInstance(targetType);

        if (targetType.IsInstanceOfType(value))
            return value;

        var conversionType = nullableType ?? targetType;
        if (conversionType.IsEnum)
            return Enum.ToObject(conversionType, value);

        return Convert.ChangeType(value, conversionType, CultureInfo.InvariantCulture);
    }

    private static QueryTranslationException Unsupported(Expression expression) =>
        new($"Projection expression '{expression}' is not supported without runtime expression compilation.");
}
