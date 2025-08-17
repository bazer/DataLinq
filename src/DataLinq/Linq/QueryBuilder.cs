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

        Where<T> where;

        // If the operation is IsNullOrEmpty or IsNullOrWhiteSpace, we need to add a sub-group with (first-condition OR second-conditon)
        if (operation == SqlOperationType.IsNullOrEmpty || operation == SqlOperationType.IsNullOrWhiteSpace)
        {
            var subGroup = new WhereGroup<T>(query, BooleanType.Or, isConditionNegated);
            group.AddSubGroup(subGroup, connectionType);

            where = subGroup.AddWhere(field, null, connectionType);
        }
        else
            where = group.AddWhere(field, null, connectionType, isConditionNegated);

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
            case SqlOperationType.IsNullOrEmpty: where.EqualToNull().Where(Operand.RawSql(GetSqlForFunction(SqlFunctionType.StringLength, field))).EqualTo(0); break;
            case SqlOperationType.IsNullOrWhiteSpace: where.EqualToNull().Where(Operand.RawSql(GetSqlForFunction(SqlFunctionType.StringTrim, field))).EqualTo(""); break;
            default: throw new NotImplementedException($"Operation '{operation}' in AddWhereToGroup is not implemented.");
        }
    }

    internal void AddComparison(Comparison comparison)
    {
        // --- Nullable Bool Logic ---
        if (IsNegatedNullableBool(comparison, out var columnOperand, out var valueOperand))
        {
            // Handle negated nullable bools specifically
            var columnName = columnOperand?.ColumnDefinition.DbName ?? throw new InvalidQueryException("Column definition is required for nullable bool comparison.");
            var boolValue = valueOperand?.FirstValue as bool? ?? throw new InvalidQueryException("Value must be a boolean for nullable bool comparison.");
            // Add a group to handle the negation logic
            // This will create a group that handles the negation of nullable bools correctly.
        
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

        // --- Regular Comparison Logic ---
        var isNegated = Negations > 0;
        if (isNegated)
            DecrementNegations();

        CurrentParentGroup.AddWhere(comparison, GetNextConnectionType(), isNegated);
    }

    protected bool IsNegatedNullableBool(Comparison comparison, out ColumnOperandWithDefinition? columnOperand, out ValueOperand? valueOperand)
    {
        columnOperand = null;
        valueOperand = null;

        if (comparison.Operator != Operator.NotEqual)
            return false; // Only interested in NotEqual comparisons for negated nullable bools

        if (comparison.Left is ColumnOperandWithDefinition columnOperand1 &&
            comparison.Right is ValueOperand valueOperand1 &&
            IsNegatedNullableBool(columnOperand1, valueOperand1))
        {
            columnOperand = columnOperand1;
            valueOperand = valueOperand1;
            return true; // This indicates a negated nullable bool condition
        }

        if (comparison.Left is ValueOperand valueOperand2 &&
            comparison.Right is ColumnOperandWithDefinition columnOperand2 &&
            IsNegatedNullableBool(columnOperand2, valueOperand2))
        {
            columnOperand = columnOperand2;
            valueOperand = valueOperand2;
            return true; // This indicates a negated nullable bool condition
        }

        return false; // Not a negated nullable bool condition
    }

    // Check if the comparison is on a nullable bool column
    protected bool IsNegatedNullableBool(ColumnOperandWithDefinition columnOperand, ValueOperand valueOperand) =>
        columnOperand.ColumnDefinition.ValueProperty.CsNullable &&
        columnOperand.ColumnDefinition.ValueProperty.CsType.Type == typeof(bool) &&
        valueOperand.HasOneValue &&
        valueOperand.FirstValue is bool;

    internal Comparison? ParseComparison(BinaryExpression node)
    {
        var left = GetOperand(node.Left);
        var right = GetOperand(node.Right);

        return new Comparison(left, GetOperator(node.NodeType), right);
    }

    protected Operand GetOperand(Expression expression)
    {
        // Unwrap the Convert expression to get to the underlying member
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            expression = unary.Operand;

        if (expression is MemberExpression memberExpr)
        {
            // Is it a simple column? (e.g., x.emp_no)
            if (memberExpr.Expression is QuerySourceReferenceExpression)
                return Operand.Column(GetColumn(memberExpr));

            // Is it a function on a column? (e.g., x.from_date.Day)
            if (GetSqlFunction(memberExpr) is var (colName, funcType, arguments) && colName != null)
                return Operand.RawSql(GetSqlForFunction(funcType, colName, arguments));
        }
        // Handle instance method calls like ToUpper(), Substring(), etc.
        else if (expression is MethodCallExpression methodCallExpr)
        {
            if (methodCallExpr.Object is MemberExpression instanceMember)
            {
                if (GetSqlFunction(instanceMember, methodCallExpr) is var (colName, funcType, arguments) && colName != null)
                    return Operand.RawSql(GetSqlForFunction(funcType, colName, arguments));
            }
        }

        // If it's anything else, it must be a literal value to be evaluated
        return Operand.Value(GetConstant(expression));
    }

    internal object? GetConstant(Expression expression)
    {
        if (expression is ConstantExpression constExp)
            return constExp.Value;

        var evaluatedExpression = Evaluator.PartialEval(expression, e => !(e is QuerySourceReferenceExpression) && !(e is SubQueryExpression));

        if (evaluatedExpression is ConstantExpression constAfterEval)
            return constAfterEval.Value;

        throw new InvalidQueryException($"Could not evaluate expression to a constant value: {expression}");
    }

    internal string GetSqlForFunction(SqlFunctionType functionType, string columnName, params object[]? arguments) =>
        query.DataSource.Provider.GetSqlForFunction(functionType, columnName, arguments);

    internal (string columnName, SqlFunctionType function, object[]? arguments)? GetSqlFunction(MemberExpression functionExpr, MethodCallExpression? methodCallExpr = null)
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
        object[]? arguments = null;

        // Date/Time Properties
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

        // String Properties and Methods
        if (functionType == null && underlyingType == typeof(string))
        {
            // Handle property access like .Length
            if (methodCallExpr == null)
            {
                functionType = functionExpr.Member.Name switch
                {
                    "Length" => SqlFunctionType.StringLength,
                    _ => null
                };
            }
            // Handle method calls like .ToUpper()
            else
            {
                functionType = methodCallExpr.Method.Name switch
                {
                    "ToUpper" => SqlFunctionType.StringToUpper,
                    "ToLower" => SqlFunctionType.StringToLower,
                    "Trim" => SqlFunctionType.StringTrim,
                    "Substring" => SqlFunctionType.StringSubstring,
                    _ => null
                };

                if (functionType == SqlFunctionType.StringSubstring)
                {
                    var startIndex = (int)GetConstant(methodCallExpr.Arguments[0])! + 1; // SQL is 1-indexed
                    var length = (int)GetConstant(methodCallExpr.Arguments[1])!;
                    arguments = [startIndex, length];
                }
            }
        }

        if (functionType.HasValue)
            return (functionOnColumnName, functionType.Value, arguments);

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

    internal Operator GetOperator(ExpressionType type) => type switch
    {
        ExpressionType.Equal => Operator.Equal,
        ExpressionType.NotEqual => Operator.NotEqual,
        ExpressionType.GreaterThan => Operator.GreaterThan,
        ExpressionType.GreaterThanOrEqual => Operator.GreaterThanOrEqual,
        ExpressionType.LessThan => Operator.LessThan,
        ExpressionType.LessThanOrEqual => Operator.LessThanOrEqual,
        _ => throw new NotImplementedException($"Expression type '{type}' is not supported for relation mapping.")
    };
}
