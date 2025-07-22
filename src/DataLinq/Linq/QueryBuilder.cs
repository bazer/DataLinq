using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Exceptions;
using DataLinq.Linq.Visitors;
using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.Utils;
using Remotion.Linq.Clauses;

namespace DataLinq.Linq;

public interface IQueryBuilder<T>
{
}

public class QueryBuilder<T>(SqlQuery<T> query) : IQueryBuilder<T>
{
    private NonNegativeInt negations = new NonNegativeInt(0); // Tracks pending NOT operations
    private NonNegativeInt ors = new NonNegativeInt(0);       // Tracks if the next item should be ORed
    private Stack<WhereGroup<T>> whereGroups = new Stack<WhereGroup<T>>(); // Manages logical grouping (parentheses)

    public WhereGroup<T> CurrentParentGroup => whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();

    public int ORs => ors.Value;
    public void IncrementORs() => ors.Increment();
    public void DecrementORs() => ors.Decrement();

    public int Negations => negations.Value;
    public void IncrementNegations() => negations.Increment();
    public void DecrementNegations() => negations.Decrement();

    public void PushWhereGroup(WhereGroup<T> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        whereGroups.Push(group);
    }
    public WhereGroup<T> PopWhereGroup() => whereGroups.Pop();

    public void AddOrderBy(MemberExpression memberExpression, OrderingDirection direction)
    {
        var column = GetColumn(memberExpression);

        if (column == null)
            throw new Exception($"Database column for property '{memberExpression.Member.Name}' not found");

        query.OrderBy(column, null, direction == OrderingDirection.Asc);
    }

    public WhereGroup<T> AddNewSubGroup(BinaryExpression node)
    {
        var currentParentGroupForThisOperation = CurrentParentGroup;

        bool isThisOperationGroupNegated = Negations > 0;
        if (isThisOperationGroupNegated)
            DecrementNegations();

        BooleanType internalJoinTypeForThisOperationGroup = (node.NodeType == ExpressionType.OrElse) ? BooleanType.Or : BooleanType.And;
        var newGroupForThisOperation = new WhereGroup<T>(query, internalJoinTypeForThisOperationGroup, isThisOperationGroupNegated);

        BooleanType connectionToOuterFlow;
        if (currentParentGroupForThisOperation.Length == 0)
        {
            if (ors.Value > 0)
            { // Check 'ors' if this is the first child of its parent
                connectionToOuterFlow = BooleanType.Or;
                ors.Decrement();
            }
            else
            {
                connectionToOuterFlow = BooleanType.And;
            }
        }
        else // Subsequent child in parent
        {
            if (ors.Value > 0)
            { // 'ors' for *this specific connection* takes precedence
                connectionToOuterFlow = BooleanType.Or;
                ors.Decrement();
            }
            else
            {
                connectionToOuterFlow = currentParentGroupForThisOperation.InternalJoinType;
            }
        }

        return currentParentGroupForThisOperation.AddSubGroup(newGroupForThisOperation, connectionToOuterFlow);
    }

    public Expression HandleMemberToValueComparison(BinaryExpression node, Expression left, Expression right)
    {
        var fields = GetFields(left, right);

        // To be used in VisitBinary (simple ops), VisitMethodCall, VisitExtension (for subquery conditions)
        var currentActiveGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        BooleanType connectionTypeToUse;

        if (ors.Value > 0) // Higher priority: if an 'OR' is explicitly signaled for this connection
        {
            connectionTypeToUse = BooleanType.Or;
            ors.Decrement(); // Consume the 'ors' signal for this specific connection
        }
        else // No explicit 'OR' signaled, use group's logic
        {
            if (currentActiveGroup.Length == 0)
            {
                // First child in the current group.
                connectionTypeToUse = BooleanType.And;
            }
            else
            {
                // Subsequent child in currentActiveGroup, use its InternalJoinType.
                connectionTypeToUse = currentActiveGroup.InternalJoinType;
            }
        }

        if (node.NodeType == ExpressionType.NotEqual &&
            fields.Value is bool bVal && bVal == true &&
            (left as MemberExpression ?? right as MemberExpression)?.Type.IsNullableTypeWhereVisitor() == true)
        {
            bool isOuterNegationAppliedToGroup = negations.Value > 0;
            if (isOuterNegationAppliedToGroup) negations.Decrement();

            // Create a new group for (IS NULL OR = false), its internal join is OR.
            var orGroupForNotTrue = new WhereGroup<T>(query, BooleanType.Or, isOuterNegationAppliedToGroup);
            orGroupForNotTrue.AddWhere(fields.Key, null, BooleanType.And).EqualToNull(); // First in this sub-group
            orGroupForNotTrue.AddWhere(fields.Key, null, BooleanType.Or).EqualTo(false);   // Second, ORed to first

            currentActiveGroup.AddSubGroup(orGroupForNotTrue, connectionTypeToUse);
        }
        else
        {
            // AddWhereToGroup already handles negations for the simple condition itself.
            AddWhereToGroup(currentActiveGroup, connectionTypeToUse, SqlOperation.GetOperationForExpressionType(node.NodeType), fields.Key, fields.Value);
        }
        return node;
    }

    public Expression HandleMemberToMemberComparison(BinaryExpression node, MemberExpression leftMember, MemberExpression rightMember)
    {
        var currentActiveGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        var connectionTypeToUse = (ors.Value > 0) ? BooleanType.Or : currentActiveGroup.InternalJoinType;
        if (ors.Value > 0) ors.Decrement();

        var leftColumnName = GetColumn(leftMember)?.DbName ?? leftMember.Member.Name;
        var rightColumnName = GetColumn(rightMember)?.DbName ?? rightMember.Member.Name;

        var where = currentActiveGroup.AddWhere(leftColumnName, null, connectionTypeToUse);

        switch (node.NodeType)
        {
            case ExpressionType.Equal: where.EqualToColumn(rightColumnName); break;
            case ExpressionType.NotEqual: where.NotEqualToColumn(rightColumnName); break;
            case ExpressionType.GreaterThan: where.GreaterThanColumn(rightColumnName); break;
            case ExpressionType.GreaterThanOrEqual: where.GreaterThanOrEqualToColumn(rightColumnName); break;
            case ExpressionType.LessThan: where.LessThanColumn(rightColumnName); break;
            case ExpressionType.LessThanOrEqual: where.LessThanOrEqualToColumn(rightColumnName); break;
            default: throw new NotImplementedException($"Binary operator '{node.NodeType}' between two members is not supported.");
        }
        return node;
    }

    public Expression HandleMemberToFunctionComparison(BinaryExpression node, MemberExpression memberExpr, MemberExpression functionExpr)
    {
        if (functionExpr.Expression is not MemberExpression innerMember)
            throw new InvalidQueryException("Function call on a non-member expression is not supported.");

        var columnName = GetColumn(memberExpr)?.DbName ?? memberExpr.Member.Name;
        var functionOnColumnName = $"{query.EscapeCharacter}{GetColumn(innerMember)?.DbName ?? innerMember.Member.Name}{query.EscapeCharacter}";

        SqlFunctionType? functionType = null;
        if (innerMember.Type == typeof(DateOnly) || innerMember.Type == typeof(DateTime))
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

        if (functionType == null && (innerMember.Type == typeof(TimeOnly) || innerMember.Type == typeof(DateTime)))
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

        if (functionType == null)
            throw new NotImplementedException($"Function '{functionExpr.Member.Name}' on type '{innerMember.Type.Name}' is not supported.");

        string sqlFunction = query.DataSource.Provider.GetSqlForFunction(functionType.Value, functionOnColumnName);

        var currentActiveGroup = whereGroups.Count > 0 ? whereGroups.Peek() : query.GetBaseWhereGroup();
        var connectionTypeToUse = (ors.Value > 0) ? BooleanType.Or : currentActiveGroup.InternalJoinType;
        if (ors.Value > 0) ors.Decrement();

        var where = currentActiveGroup.AddWhere(columnName, null, connectionTypeToUse);

        switch (node.NodeType)
        {
            case ExpressionType.Equal: where.EqualToRaw(sqlFunction); break;
            case ExpressionType.NotEqual: where.NotEqualToRaw(sqlFunction); break;
            case ExpressionType.GreaterThan: where.GreaterThanRaw(sqlFunction); break;
            case ExpressionType.GreaterThanOrEqual: where.GreaterThanOrEqualToRaw(sqlFunction); break;
            case ExpressionType.LessThan: where.LessThanRaw(sqlFunction); break;
            case ExpressionType.LessThanOrEqual: where.LessThanOrEqualToRaw(sqlFunction); break;
            default: throw new NotImplementedException($"Binary operator '{node.NodeType}' between a member and a function is not supported.");
        }

        return node;
    }

    public void AddWhereToGroup(WhereGroup<T> group, BooleanType connectionType, SqlOperationType operation, string field, params object?[] values)
    {
        // 'negations' applies to the individual 'where' condition being added.
        bool isConditionNegated = negations.Value > 0;
        if (isConditionNegated) negations.Decrement();

        Where<T> where;
        if (isConditionNegated)
            where = group.AddWhereNot(field, null, connectionType);
        else
            where = group.AddWhere(field, null, connectionType);

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

    

    

    internal KeyValuePair<string, object?> GetFields(Expression left, Expression right)
    {
        if (left is ConstantExpression && right is ConstantExpression)
            throw new InvalidQueryException("Unable to compare 2 constants.");

        if (left is MemberExpression && right is MemberExpression)
            throw new InvalidQueryException("Unable to compare 2 members.");

        if (left is MemberExpression memberExp && right is ConstantExpression constantExpr)
            return GetValues(memberExp, constantExpr);
        else if (right is MemberExpression memberExp2 && left is ConstantExpression constantExpr2)
            return GetValues(memberExp2, constantExpr2);

        throw new InvalidQueryException($"Unable to compare {left.GetType().Name} with {right.GetType().Name}. Only MemberExpression and ConstantExpression are supported.");
    }

    internal KeyValuePair<string, object?> GetValues(MemberExpression field, ConstantExpression value) =>
        new KeyValuePair<string, object?>(GetColumnDbName(field), value.Value);

    internal string GetColumnDbName(MemberExpression expression) =>
        GetColumn(expression)?.DbName ?? throw new InvalidQueryException($"Could not find column with name '{expression.Member.Name}' in table '{query.Table.DbName}'");

    internal object? GetValue(Expression expression)
    {
        if (expression is ConstantExpression constExp)
            return constExp.Value;
        else if (expression is MemberExpression propExp)
            return GetColumnDbName(propExp);
        else
            throw new InvalidQueryException($"Expression '{expression}' is not a member or constant.");
    }

    internal ColumnDefinition? GetColumn(MemberExpression expression)
    {
        return query.Table.Columns.SingleOrDefault(x => x.ValueProperty.PropertyName == expression.Member.Name);
    }
}
