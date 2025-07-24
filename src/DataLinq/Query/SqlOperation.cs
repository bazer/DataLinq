using System;
using System.Linq.Expressions;

namespace DataLinq.Query;

public enum SqlOperationType
{
    Equal, EqualNull, NotEqual, NotEqualNull,
    GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
    StartsWith, EndsWith, StringContains, ListContains,
    IsNullOrEmpty, IsNullOrWhiteSpace
}

public static class SqlOperation
{
    public static SqlOperationType GetOperationForExpressionType(ExpressionType expressionType) => expressionType switch
    {
        ExpressionType.Equal => SqlOperationType.Equal,
        ExpressionType.NotEqual => SqlOperationType.NotEqual,
        ExpressionType.GreaterThan => SqlOperationType.GreaterThan,
        ExpressionType.GreaterThanOrEqual => SqlOperationType.GreaterThanOrEqual,
        ExpressionType.LessThan => SqlOperationType.LessThan,
        ExpressionType.LessThanOrEqual => SqlOperationType.LessThanOrEqual,
        _ => throw new NotImplementedException($"ExpressionType '{expressionType}' cannot be mapped to an Operation."),
    };

    public static SqlOperationType GetOperationForMethodName(string methodName) => methodName switch
    {
        "Contains" => SqlOperationType.StringContains,
        "StartsWith" => SqlOperationType.StartsWith,
        "EndsWith" => SqlOperationType.EndsWith,
        "IsNullOrEmpty" => SqlOperationType.IsNullOrEmpty,
        "IsNullOrWhiteSpace" => SqlOperationType.IsNullOrWhiteSpace,
        _ => throw new NotImplementedException($"Method name '{methodName}' cannot be mapped to an Operation."),
    };
}