using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning.Expressions;

internal static class ExpressionLocalValueEvaluator
{
    public static object? Evaluate(Expression expression, ParameterExpression? parameter = null, object? parameterValue = null)
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
                    Evaluate(unary.Operand, parameter, parameterValue),
                    GetNonNullableType(unary.Type),
                    CultureInfo.InvariantCulture);

            case MemberExpression member:
                var instance = member.Expression is null
                    ? null
                    : Evaluate(member.Expression, parameter, parameterValue);

                return member.Member switch
                {
                    FieldInfo field => field.GetValue(instance),
                    PropertyInfo property => property.GetValue(instance),
                    _ => throw UnsupportedMember(member)
                };

            case NewArrayExpression newArray:
                return newArray.Expressions
                    .Select(item => Evaluate(item, parameter, parameterValue))
                    .ToArray();

            case MethodCallExpression methodCall when TryEvaluateSupportedMethod(methodCall, out var value):
                return value;

            case MethodCallExpression methodCall:
                throw new QueryTranslationException(
                    $"Local method call '{methodCall.Method.Name}' is not supported in DataLinq expression parsing. " +
                    "Capture the value before building the query or use a documented DataLinq query function.");

            default:
                throw new QueryTranslationException($"Local expression '{expression}' is not supported in DataLinq expression parsing.");
        }
    }

    private static bool TryEvaluateSupportedMethod(MethodCallExpression methodCall, out object? value)
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

        value = null;
        return false;
    }

    private static readonly MethodInfo ArrayEmptyMethod = ((Func<int[]>)Array.Empty<int>)
        .Method
        .GetGenericMethodDefinition();

    private static readonly MethodInfo EnumerableEmptyMethod = ((Func<IEnumerable<int>>)Enumerable.Empty<int>)
        .Method
        .GetGenericMethodDefinition();

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
