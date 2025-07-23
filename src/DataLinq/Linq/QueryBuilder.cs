using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Exceptions;
using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.Utils;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace DataLinq.Linq;

// A private record to hold the structured result of parsing a comparison expression.
internal record Comparison(
    ColumnDefinition? Column, // Now nullable
    ExpressionType Operator,
    object? Value,
    Comparison.ValueType Type,
    bool Swapped = false)
{
    public string? RawSqlColumn { get; init; } // New property
    public enum ValueType { Literal, Column, RawSql }
}

internal class QueryBuilder<T>(SqlQuery<T> query)
{
    private readonly NonNegativeInt negations = new(0); // Tracks pending NOT operations
    private readonly NonNegativeInt ors = new(0);       // Tracks if the next item should be ORed
    private readonly Stack<WhereGroup<T>> whereGroups = new(); // Manages logical grouping (parentheses)

    internal WhereGroup<T> CurrentParentGroup => whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();

    internal int ORs => ors.Value;
    internal void IncrementORs() => ors.Increment();
    internal void DecrementORs() => ors.Decrement();

    internal int Negations => negations.Value;
    internal void IncrementNegations() => negations.Increment();
    internal void DecrementNegations() => negations.Decrement();

    internal void PushWhereGroup(WhereGroup<T> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        whereGroups.Push(group);
    }

    internal WhereGroup<T> PopWhereGroup() => whereGroups.Pop();

    internal void AddOrderBy(MemberExpression memberExpression, OrderingDirection direction) =>
        query.OrderBy(GetColumn(memberExpression), null, direction == OrderingDirection.Asc);

    internal WhereGroup<T> AddNewSubGroup(BinaryExpression node)
    {
        var currentParentGroupForThisOperation = CurrentParentGroup;

        bool isThisOperationGroupNegated = Negations > 0;
        if (isThisOperationGroupNegated)
            DecrementNegations();

        BooleanType internalJoinTypeForThisOperationGroup = (node.NodeType == ExpressionType.OrElse) ? BooleanType.Or : BooleanType.And;
        var newGroupForThisOperation = new WhereGroup<T>(query, internalJoinTypeForThisOperationGroup, isThisOperationGroupNegated);

        return currentParentGroupForThisOperation.AddSubGroup(newGroupForThisOperation, GetNextConnectionType());
    }

    internal void AddWhereToGroup(WhereGroup<T> group, BooleanType connectionType, SqlOperationType operation, string field, params object?[] values)
    {
        // 'negations' applies to the individual 'where' condition being added.
        bool isConditionNegated = Negations > 0;
        if (isConditionNegated)
            DecrementNegations();

        var where = isConditionNegated
            ? group.AddWhereNot(field, null, connectionType)
            : group.AddWhere(field, null, connectionType);

        switch (operation)
        {
            case SqlOperationType.Equal: where.EqualTo(values[0]); break;
            case SqlOperationType.EqualNull: where.EqualToNull(); break;
            case SqlOperationType.NotEqual: where.NotEqualTo(values[0]); break;
            case SqlOperationType.NotEqualNull: where.NotEqualToNull(); break;
            case SqlOperationType.GreaterThan: where.GreaterThan(values[0]); break;
            case SqlOperationType.GreaterThanOrEqual: where.GreaterThanOrEqual(values[0]); break;
            case SqlOperationType.LessThan: where.LessThan(values[0]); break;
            case SqlOperationType.LessThanOrEqual: where.LessThanOrEqual(values[0]); break;
            case SqlOperationType.StartsWith: where.Like(values[0] + "%"); break;
            case SqlOperationType.EndsWith: where.Like("%" + values[0]); break;
            case SqlOperationType.StringContains: where.Like("%" + values[0] + "%"); break;
            case SqlOperationType.ListContains: where.In(values); break; // This is for pre-evaluated list.Contains results
            default: throw new NotImplementedException($"Operation '{operation}' in AddWhereToGroup is not implemented.");
        }
    }

    internal void AddComparison(Comparison comparison)
    {
        var columnName = comparison.Column?.DbName ?? comparison.RawSqlColumn
            ?? throw new InvalidQueryException("Comparison must have a column.");

        // --- Nullable Bool Logic ---
        if (comparison.Column != null &&
            comparison.Column.ValueProperty.CsNullable &&
            comparison.Column.ValueProperty.CsType.Type == typeof(bool) &&
            comparison.Operator == ExpressionType.NotEqual &&
            comparison.Value is bool boolValue)
        {
            // Case: x.NullableBool != true  (C# wants false or null)
            // Case: x.NullableBool != false (C# wants true or null)
            var orGroup = new WhereGroup<T>(query, BooleanType.Or)
                .Where(columnName).EqualTo(!boolValue)
                .Where(columnName).EqualToNull();

            CurrentParentGroup.AddSubGroup(orGroup, GetNextConnectionType());
            return;

            // Cases for == true and == false are handled correctly by the default logic below,
            // as SQL's `col = 1` and `col = 0` correctly exclude NULLs, matching C#'s behavior.
        }

        var where = CurrentParentGroup.AddWhere(columnName, null, GetNextConnectionType());
        AddWhereClause(where, comparison.Operator, comparison.Value,
            isColumn: comparison.Type == Comparison.ValueType.Column,
            isRaw: comparison.Type == Comparison.ValueType.RawSql,
            swapped: comparison.Swapped);
    }

    private void AddWhereClause(Where<T> where, ExpressionType op, object? value, bool isColumn = false, bool isRaw = false, bool swapped = false)
    {
        if (Negations > 0)
        {
            where.IsNegated = true;
            DecrementNegations();
        }

        if (swapped)
        {
            op = op switch
            {
                ExpressionType.GreaterThan => ExpressionType.LessThan,
                ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                ExpressionType.LessThan => ExpressionType.GreaterThan,
                ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
                _ => op
            };
        }

        if (isRaw)
        {
            var sql = value?.ToString() ?? throw new ArgumentException($"Value cannot be null for expression '{op}' in query '{where}'.");
            switch (op)
            {
                case ExpressionType.Equal: where.EqualToRaw(sql); break;
                case ExpressionType.NotEqual: where.NotEqualToRaw(sql); break;
                case ExpressionType.GreaterThan: where.GreaterThanRaw(sql); break;
                case ExpressionType.GreaterThanOrEqual: where.GreaterThanOrEqualToRaw(sql); break;
                case ExpressionType.LessThan: where.LessThanRaw(sql); break;
                case ExpressionType.LessThanOrEqual: where.LessThanOrEqualToRaw(sql); break;
                default: throw new NotImplementedException($"Operator '{op}' not supported for raw SQL comparison.");
            }
        }
        else if (isColumn)
        {
            var colName = value?.ToString() ?? throw new ArgumentException($"Value cannot be null for expression '{op}' in query '{where}'.");
            switch (op)
            {
                case ExpressionType.Equal: where.EqualToColumn(colName); break;
                case ExpressionType.NotEqual: where.NotEqualToColumn(colName); break;
                case ExpressionType.GreaterThan: where.GreaterThanColumn(colName); break;
                case ExpressionType.GreaterThanOrEqual: where.GreaterThanOrEqualToColumn(colName); break;
                case ExpressionType.LessThan: where.LessThanColumn(colName); break;
                case ExpressionType.LessThanOrEqual: where.LessThanOrEqualToColumn(colName); break;
                default: throw new NotImplementedException($"Operator '{op}' not supported for column comparison.");
            }
        }
        else
        {
            switch (op)
            {
                case ExpressionType.Equal: where.EqualTo(value); break;
                case ExpressionType.NotEqual: where.NotEqualTo(value); break;
                case ExpressionType.GreaterThan: where.GreaterThan(value); break;
                case ExpressionType.GreaterThanOrEqual: where.GreaterThanOrEqual(value); break;
                case ExpressionType.LessThan: where.LessThan(value); break;
                case ExpressionType.LessThanOrEqual: where.LessThanOrEqual(value); break;
                default: throw new NotImplementedException($"Operator '{op}' not supported for value comparison.");
            }
        }
    }

    internal string GetSqlForFunction(string colName, SqlFunctionType funcType) =>
        query.DataSource.Provider.GetSqlForFunction(funcType, $"{query.EscapeCharacter}{colName}{query.EscapeCharacter}");

    internal (string columnName, SqlFunctionType function)? GetSqlFunction(MemberExpression functionExpr)
    {
        // Use the recursive helper to find the root column expression
        var rootColumnExpr = QueryBuilder<T>.FindRootColumn(functionExpr);
        if (rootColumnExpr == null)
            return null;

        var functionOnColumnName = GetColumnMaybe(rootColumnExpr)?.DbName ?? rootColumnExpr.Member.Name;

        var columnType = rootColumnExpr.Type;

        // Handle nullable types by getting the underlying type (e.g., DateTime? -> DateTime)
        var underlyingType = Nullable.GetUnderlyingType(columnType) ?? columnType;

        SqlFunctionType? functionType = null;
        if (underlyingType == typeof(DateOnly) || underlyingType == typeof(DateTime))
        {
            functionType = functionExpr.Member.Name switch
            {
                "Year" => SqlFunctionType.DatePartYear,
                "Month" => SqlFunctionType.DatePartMonth,
                "Day" => SqlFunctionType.DatePartDay,
                "DayOfYear" => SqlFunctionType.DatePartDayOfYear,
                "DayOfWeek" => SqlFunctionType.DatePartDayOfWeek,
                _ => null
            };
        }

        if (functionType == null && (underlyingType == typeof(TimeOnly) || underlyingType == typeof(DateTime)))
        {
            functionType = functionExpr.Member.Name switch
            {
                "Hour" => SqlFunctionType.TimePartHour,
                "Minute" => SqlFunctionType.TimePartMinute,
                "Second" => SqlFunctionType.TimePartSecond,
                "Millisecond" => SqlFunctionType.TimePartMillisecond,
                _ => null
            };
        }

        if (functionType.HasValue)
            return (functionOnColumnName, functionType.Value);

        return null;
    }

    // Recursive helper to find the root database column from any expression
    private static MemberExpression? FindRootColumn(Expression expression)
    {
        return expression switch
        {
            // Base case: We've found a member directly on the query source (e.g., "x.created_at")
            MemberExpression { Expression: QuerySourceReferenceExpression } memberExpr => memberExpr,
            // Recursive step: Unwrap a member of a member (e.g., the ".Value" in "x.created_at.Value")
            MemberExpression { Expression: not null } memberExpr => FindRootColumn(memberExpr.Expression),
            // Recursive step: Unwrap a conversion (e.g., the Convert in "Convert(x.created_at, DateTime)")
            UnaryExpression { NodeType: ExpressionType.Convert } unaryExpr => FindRootColumn(unaryExpr.Operand),
            _ => null,
        };
    }

    internal BooleanType GetNextConnectionType()
    {
        if (ORs > 0)
        {
            DecrementORs();
            return BooleanType.Or;
        }

        return CurrentParentGroup.Length == 0 ? BooleanType.And : CurrentParentGroup.InternalJoinType;
    }

    internal ColumnDefinition GetColumn(MemberExpression expression) =>
        GetColumnMaybe(expression) ?? throw new InvalidQueryException($"Column '{expression.Member.Name}' not found in table '{query.Table.DbName}'");

    internal ColumnDefinition? GetColumnMaybe(MemberExpression expression) =>
        query.Table.Columns.SingleOrDefault(x => x.ValueProperty.PropertyName == expression.Member.Name);
}
