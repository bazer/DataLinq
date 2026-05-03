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
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq;

internal class QueryBuilder<T>(SqlQuery<T> query)
{
    private readonly NonNegativeInt negations = new(0); // Tracks pending NOT operations
    private readonly NonNegativeInt ors = new(0);       // Tracks if the next item should be ORed
    private readonly Stack<WhereGroup<T>> whereGroups = new(); // Manages logical grouping (parentheses)
    private int relationSubqueryCounter;

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
            default: throw new QueryTranslationException($"SQL operation '{operation}' is not supported while translating a LINQ predicate.");
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

        if (TryAddNullableNotEqualComparison(comparison))
            return;

        // --- Regular Comparison Logic ---
        var isNegated = Negations > 0;
        if (isNegated)
            DecrementNegations();

        CurrentParentGroup.AddWhere(comparison, GetNextConnectionType(), isNegated);
    }

    internal bool TryAddRelationAnySubQuery(SubQueryExpression subQuery, BooleanType connectionType, bool isNegated)
    {
        if (!TryGetRelationProperty(subQuery.QueryModel.MainFromClause.FromExpression, out var relationProperty))
            return false;

        if (subQuery.QueryModel.ResultOperators.Count != 1 ||
            subQuery.QueryModel.ResultOperators[0] is not AnyResultOperator)
        {
            throw new QueryTranslationException($"Relation subquery '{subQuery.QueryModel}' is not supported. Only relation Any(...) predicates are supported in this context.");
        }

        var predicate = GetOptionalRelationWherePredicate(subQuery.QueryModel.BodyClauses, subQuery.QueryModel.ToString());
        AddRelationExists(relationProperty, predicate, subQuery.QueryModel.MainFromClause, null, connectionType, isNegated);
        return true;
    }

    internal bool TryAddRelationAnyMethodCall(MethodCallExpression node, BooleanType connectionType)
    {
        if (!IsEnumerableMethod(node, nameof(Enumerable.Any)) || node.Arguments.Count is not (1 or 2))
            return false;

        if (!TryGetRelationProperty(node.Arguments[0], out var relationProperty))
            return false;

        var isNegated = Negations > 0;
        if (isNegated)
            DecrementNegations();

        var (predicate, parameter) = node.Arguments.Count == 2
            ? GetRelationLambdaPredicate(node.Arguments[1], node.ToString())
            : (null, null);

        AddRelationExists(relationProperty, predicate, null, parameter, connectionType, isNegated);
        return true;
    }

    internal bool TryAddRelationCountComparison(BinaryExpression node)
    {
        if (TryGetRelationCount(node.Left, out var relationProperty, out var predicate, out var childQuerySource, out var childParameter) &&
            TryGetConstantInt(node.Right, out var constant))
        {
            return AddRelationCountComparison(relationProperty, predicate, childQuerySource, childParameter, node.NodeType, constant);
        }

        if (TryGetRelationCount(node.Right, out relationProperty, out predicate, out childQuerySource, out childParameter) &&
            TryGetConstantInt(node.Left, out constant))
        {
            return AddRelationCountComparison(relationProperty, predicate, childQuerySource, childParameter, ReverseExpressionType(node.NodeType), constant);
        }

        return false;
    }

    private bool AddRelationCountComparison(
        RelationProperty relationProperty,
        Expression? predicate,
        MainFromClause? childQuerySource,
        ParameterExpression? childParameter,
        ExpressionType comparisonType,
        int constant)
    {
        if (!TryGetCountExistsSemantics(comparisonType, constant, out var shouldExist))
        {
            throw new QueryTranslationException(
                $"Relation Count() comparison '{comparisonType} {constant}' is not supported. " +
                "Use Count() > 0, Count() >= 1, Count() != 0, Count() == 0, Count() <= 0, or Count() < 1.");
        }

        var isNegated = Negations > 0;
        if (isNegated)
            DecrementNegations();

        if (isNegated)
            shouldExist = !shouldExist;

        AddRelationExists(relationProperty, predicate, childQuerySource, childParameter, GetNextConnectionType(), isNegated: !shouldExist);
        return true;
    }

    private static bool TryGetCountExistsSemantics(ExpressionType comparisonType, int constant, out bool shouldExist)
    {
        switch (comparisonType)
        {
            case ExpressionType.GreaterThan when constant == 0:
            case ExpressionType.GreaterThanOrEqual when constant == 1:
            case ExpressionType.NotEqual when constant == 0:
                shouldExist = true;
                return true;

            case ExpressionType.Equal when constant == 0:
            case ExpressionType.LessThanOrEqual when constant == 0:
            case ExpressionType.LessThan when constant == 1:
                shouldExist = false;
                return true;

            default:
                shouldExist = false;
                return false;
        }
    }

    private static ExpressionType ReverseExpressionType(ExpressionType expressionType) => expressionType switch
    {
        ExpressionType.GreaterThan => ExpressionType.LessThan,
        ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
        ExpressionType.LessThan => ExpressionType.GreaterThan,
        ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
        _ => expressionType
    };

    private bool TryGetRelationCount(
        Expression expression,
        out RelationProperty relationProperty,
        out Expression? predicate,
        out MainFromClause? childQuerySource,
        out ParameterExpression? childParameter)
    {
        expression = UnwrapConvert(expression);

        if (expression is SubQueryExpression subQuery &&
            subQuery.QueryModel.ResultOperators.Count == 1 &&
            subQuery.QueryModel.ResultOperators[0] is CountResultOperator &&
            TryGetRelationProperty(subQuery.QueryModel.MainFromClause.FromExpression, out relationProperty))
        {
            predicate = GetOptionalRelationWherePredicate(subQuery.QueryModel.BodyClauses, subQuery.QueryModel.ToString());
            childQuerySource = subQuery.QueryModel.MainFromClause;
            childParameter = null;
            return true;
        }

        if (expression is MethodCallExpression methodCall &&
            IsEnumerableMethod(methodCall, nameof(Enumerable.Count)) &&
            methodCall.Arguments.Count is 1 or 2 &&
            TryGetRelationProperty(methodCall.Arguments[0], out relationProperty))
        {
            if (methodCall.Arguments.Count == 2)
            {
                (predicate, childParameter) = GetRelationLambdaPredicate(methodCall.Arguments[1], methodCall.ToString());
            }
            else
            {
                predicate = null;
                childParameter = null;
            }

            childQuerySource = null;
            return true;
        }

        relationProperty = null!;
        predicate = null;
        childQuerySource = null;
        childParameter = null;
        return false;
    }

    private static Expression? GetOptionalRelationWherePredicate(IList<IBodyClause> bodyClauses, string expression)
    {
        if (bodyClauses.Count == 0)
            return null;

        if (bodyClauses.Count == 1 && bodyClauses[0] is WhereClause whereClause)
            return whereClause.Predicate;

        throw new QueryTranslationException($"Relation predicate '{expression}' is not supported. Only a single Where predicate inside Any(...) or Count(...) is supported.");
    }

    private static (Expression predicate, ParameterExpression parameter) GetRelationLambdaPredicate(Expression expression, string sourceExpression)
    {
        expression = UnwrapConvert(expression);
        if (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            expression = quote.Operand;

        if (expression is LambdaExpression lambda && lambda.Parameters.Count == 1)
            return (lambda.Body, lambda.Parameters[0]);

        throw new QueryTranslationException($"Relation predicate lambda '{sourceExpression}' is not supported.");
    }

    private bool TryGetRelationProperty(Expression expression, out RelationProperty relationProperty)
    {
        expression = UnwrapConvert(expression);

        if (expression is MemberExpression memberExpression &&
            memberExpression.Expression is QuerySourceReferenceExpression querySource &&
            querySource.Type == query.Table.Model.CsType.Type &&
            query.Table.Model.RelationProperties.TryGetValue(memberExpression.Member.Name, out relationProperty!))
        {
            return true;
        }

        relationProperty = null!;
        return false;
    }

    private void AddRelationExists(
        RelationProperty relationProperty,
        Expression? predicate,
        MainFromClause? childQuerySource,
        ParameterExpression? childParameter,
        BooleanType connectionType,
        bool isNegated)
    {
        var relationPart = relationProperty.RelationPart;
        if (relationPart.Type != RelationPartType.CandidateKey)
        {
            throw new QueryTranslationException(
                $"Relation property '{relationProperty.PropertyName}' is not supported in relation predicate translation. " +
                "Only collection relations from the candidate-key side are supported.");
        }

        var childPart = relationPart.GetOtherSide();
        var parentColumns = relationPart.ColumnIndex.Columns;
        var childColumns = childPart.ColumnIndex.Columns;
        if (parentColumns.Count != childColumns.Count)
            throw new QueryTranslationException($"Relation property '{relationProperty.PropertyName}' has mismatched relation column counts.");

        var childTable = childPart.ColumnIndex.Table;
        ValidateRelationPredicateSource(relationProperty, childTable, childQuerySource, childParameter);

        var alias = $"r{relationSubqueryCounter++}";
        var existsQuery = new SqlQuery<object>(childTable, query.DataSource, alias);
        var existsWhere = existsQuery.GetBaseWhereGroup();

        for (var index = 0; index < parentColumns.Count; index++)
        {
            existsWhere
                .AddWhere(childColumns[index].DbName, alias, BooleanType.And)
                .EqualToRaw(FormatOuterColumn(parentColumns[index]));
        }

        if (predicate is not null)
            AddRelationPredicate(existsWhere, childTable, alias, childQuerySource, childParameter, predicate, BooleanType.And);

        CurrentParentGroup.AddExists(existsQuery, connectionType, isNegated);
    }

    private static void ValidateRelationPredicateSource(
        RelationProperty relationProperty,
        TableDefinition childTable,
        MainFromClause? childQuerySource,
        ParameterExpression? childParameter)
    {
        var childType = childTable.Model.CsType.Type;
        if (childQuerySource is not null && childQuerySource.ItemType != childType)
        {
            throw new QueryTranslationException(
                $"Relation property '{relationProperty.PropertyName}' subquery item type '{childQuerySource.ItemType}' does not match related table model '{childType}'.");
        }

        if (childParameter is not null && childParameter.Type != childType)
        {
            throw new QueryTranslationException(
                $"Relation property '{relationProperty.PropertyName}' predicate parameter type '{childParameter.Type}' does not match related table model '{childType}'.");
        }
    }

    private void AddRelationPredicate(
        WhereGroup<object> group,
        TableDefinition childTable,
        string childAlias,
        MainFromClause? childQuerySource,
        ParameterExpression? childParameter,
        Expression predicate,
        BooleanType connectionType)
    {
        predicate = UnwrapConvert(predicate);

        if (predicate is BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } compound)
        {
            var joinType = compound.NodeType == ExpressionType.OrElse ? BooleanType.Or : BooleanType.And;
            var subGroup = new WhereGroup<object>(group.Query, joinType);
            group.AddSubGroup(subGroup, connectionType);
            AddRelationPredicate(subGroup, childTable, childAlias, childQuerySource, childParameter, compound.Left, BooleanType.And);
            AddRelationPredicate(subGroup, childTable, childAlias, childQuerySource, childParameter, compound.Right, joinType);
            return;
        }

        if (predicate is not BinaryExpression comparison)
        {
            throw new QueryTranslationException($"Relation predicate '{predicate}' is not supported. Only simple comparison predicates are supported.");
        }

        if (!TryAddRelationPredicateComparison(group, childTable, childAlias, childQuerySource, childParameter, comparison, connectionType))
        {
            throw new QueryTranslationException(
                $"Relation predicate '{predicate}' is not supported. " +
                "Expected a direct related-row member compared with a local value.");
        }
    }

    private bool TryAddRelationPredicateComparison(
        WhereGroup<object> group,
        TableDefinition childTable,
        string childAlias,
        MainFromClause? childQuerySource,
        ParameterExpression? childParameter,
        BinaryExpression comparison,
        BooleanType connectionType)
    {
        if (TryGetRelationPredicateColumn(comparison.Left, childTable, childQuerySource, childParameter, out var column) &&
            TryGetRelationPredicateValue(comparison.Right, out var value))
        {
            AddRelationPredicateWhere(group, childAlias, column, GetOperator(comparison.NodeType), value, connectionType);
            return true;
        }

        if (TryGetRelationPredicateColumn(comparison.Right, childTable, childQuerySource, childParameter, out column) &&
            TryGetRelationPredicateValue(comparison.Left, out value))
        {
            AddRelationPredicateWhere(group, childAlias, column, ReverseOperator(GetOperator(comparison.NodeType)), value, connectionType);
            return true;
        }

        return false;
    }

    private bool TryGetRelationPredicateColumn(
        Expression expression,
        TableDefinition childTable,
        MainFromClause? childQuerySource,
        ParameterExpression? childParameter,
        out ColumnDefinition column)
    {
        expression = UnwrapNullableValueMember(UnwrapConvert(expression));

        if (expression is MemberExpression memberExpression &&
            IsRelationPredicateSource(memberExpression.Expression, childQuerySource, childParameter))
        {
            column = childTable.Columns.SingleOrDefault(x => x.ValueProperty.PropertyName == memberExpression.Member.Name)!;
            return column is not null;
        }

        column = null!;
        return false;
    }

    private static bool IsRelationPredicateSource(Expression? expression, MainFromClause? childQuerySource, ParameterExpression? childParameter)
    {
        if (expression is null)
            return false;

        expression = UnwrapConvert(expression);

        if (expression is QuerySourceReferenceExpression querySource && childQuerySource is not null)
            return ReferenceEquals(querySource.ReferencedQuerySource, childQuerySource);

        if (expression is ParameterExpression parameter && childParameter is not null)
            return parameter == childParameter;

        return false;
    }

    private bool TryGetRelationPredicateValue(Expression expression, out object? value)
    {
        expression = UnwrapConvert(expression);

        if (ContainsQuerySource(expression))
        {
            value = null;
            return false;
        }

        value = GetConstant(expression);
        return true;
    }

    private static bool ContainsQuerySource(Expression? expression)
    {
        if (expression is null)
            return false;

        expression = UnwrapConvert(expression);

        return expression switch
        {
            QuerySourceReferenceExpression => true,
            SubQueryExpression => true,
            MemberExpression memberExpression => ContainsQuerySource(memberExpression.Expression),
            MethodCallExpression methodCall => ContainsQuerySource(methodCall.Object) || methodCall.Arguments.Any(ContainsQuerySource),
            UnaryExpression unaryExpression => ContainsQuerySource(unaryExpression.Operand),
            BinaryExpression binaryExpression => ContainsQuerySource(binaryExpression.Left) || ContainsQuerySource(binaryExpression.Right),
            _ => false
        };
    }

    private static void AddRelationPredicateWhere(
        WhereGroup<object> group,
        string childAlias,
        ColumnDefinition column,
        Operator @operator,
        object? value,
        BooleanType connectionType)
    {
        value = NormalizeRelationPredicateValue(column, value);

        if (@operator == Operator.NotEqual &&
            column.ValueProperty.CsNullable &&
            value is not null)
        {
            var orGroup = new WhereGroup<object>(group.Query, BooleanType.Or);
            group.AddSubGroup(orGroup, connectionType);
            orGroup.AddWhere(column.DbName, childAlias, BooleanType.And).NotEqualTo(value);
            orGroup.AddWhere(column.DbName, childAlias, BooleanType.Or).EqualToNull();
            return;
        }

        var where = group.AddWhere(column.DbName, childAlias, connectionType);
        switch (@operator)
        {
            case Operator.Equal:
                where.EqualTo(value);
                break;
            case Operator.NotEqual:
                where.NotEqualTo(value);
                break;
            case Operator.GreaterThan:
                where.GreaterThan(value);
                break;
            case Operator.GreaterThanOrEqual:
                where.GreaterThanOrEqual(value);
                break;
            case Operator.LessThan:
                where.LessThan(value);
                break;
            case Operator.LessThanOrEqual:
                where.LessThanOrEqual(value);
                break;
            default:
                throw new QueryTranslationException($"Relation predicate operator '{@operator}' is not supported.");
        }
    }

    private static object? NormalizeRelationPredicateValue(ColumnDefinition column, object? value)
    {
        var columnType = GetNonNullableColumnType(column);
        if (columnType == typeof(char))
            return NormalizeCharComparisonValue(value);

        return value;
    }

    private string FormatOuterColumn(ColumnDefinition column)
    {
        var qualifier = string.IsNullOrWhiteSpace(query.Alias)
            ? EscapeIdentifier(query.Table.DbName)
            : query.Alias;

        return $"{qualifier}.{EscapeIdentifier(column.DbName)}";
    }

    private string EscapeIdentifier(string identifier)
        => $"{query.EscapeCharacter}{identifier}{query.EscapeCharacter}";

    private static bool TryGetConstantInt(Expression expression, out int value)
    {
        expression = UnwrapConvert(expression);

        if (expression is ConstantExpression constantExpression)
        {
            value = Convert.ToInt32(constantExpression.Value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool IsEnumerableMethod(MethodCallExpression node, string methodName)
    {
        if (!node.Method.IsGenericMethod || node.Method.Name != methodName)
            return false;

        var genericDefinition = node.Method.GetGenericMethodDefinition();
        return genericDefinition.DeclaringType == typeof(Enumerable);
    }

    private static Operator ReverseOperator(Operator @operator) => @operator switch
    {
        Operator.GreaterThan => Operator.LessThan,
        Operator.GreaterThanOrEqual => Operator.LessThanOrEqual,
        Operator.LessThan => Operator.GreaterThan,
        Operator.LessThanOrEqual => Operator.GreaterThanOrEqual,
        _ => @operator
    };

    private bool TryAddNullableNotEqualComparison(Comparison comparison)
    {
        if (comparison.Operator != Operator.NotEqual || Negations > 0)
            return false;

        if (TryGetNullableColumnAndNonNullValue(comparison.Left, comparison.Right, out var columnOperand, out var valueOperand) ||
            TryGetNullableColumnAndNonNullValue(comparison.Right, comparison.Left, out columnOperand, out valueOperand))
        {
            var orGroup = new WhereGroup<T>(query, BooleanType.Or)
                .Where(columnOperand.ColumnDefinition.DbName).NotEqualTo(valueOperand.FirstValue)
                .Where(columnOperand.ColumnDefinition.DbName).EqualToNull();

            CurrentParentGroup.AddSubGroup(orGroup, GetNextConnectionType());
            return true;
        }

        return false;
    }

    private static bool TryGetNullableColumnAndNonNullValue(
        Operand left,
        Operand right,
        out ColumnOperandWithDefinition columnOperand,
        out ValueOperand valueOperand)
    {
        if (left is ColumnOperandWithDefinition column &&
            column.ColumnDefinition.ValueProperty.CsNullable &&
            right is ValueOperand { HasOneValue: true, IsNull: false } value)
        {
            columnOperand = column;
            valueOperand = value;
            return true;
        }

        columnOperand = null!;
        valueOperand = null!;
        return false;
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
        (left, right) = NormalizeValueOperandsForColumnTypes(left, right);

        return new Comparison(left, GetOperator(node.NodeType), right);
    }

    private static (Operand left, Operand right) NormalizeValueOperandsForColumnTypes(Operand left, Operand right)
    {
        if (left is ColumnOperandWithDefinition leftColumn && right is ValueOperand rightValue)
            right = NormalizeValueOperandForColumnType(leftColumn.ColumnDefinition, rightValue);

        if (right is ColumnOperandWithDefinition rightColumn && left is ValueOperand leftValue)
            left = NormalizeValueOperandForColumnType(rightColumn.ColumnDefinition, leftValue);

        return (left, right);
    }

    private static ValueOperand NormalizeValueOperandForColumnType(ColumnDefinition column, ValueOperand valueOperand)
    {
        var columnType = GetNonNullableColumnType(column);
        if (columnType != typeof(char))
            return valueOperand;

        return Operand.Value(valueOperand.Values.Select(NormalizeCharComparisonValue).ToArray());
    }

    private static Type GetNonNullableColumnType(ColumnDefinition column)
    {
        var columnType = column.ValueProperty.CsType.Type
            ?? throw new QueryTranslationException($"Column '{column.DbName}' has no CLR type metadata.");

        return Nullable.GetUnderlyingType(columnType) ?? columnType;
    }

    private static object? NormalizeCharComparisonValue(object? value) => value switch
    {
        int intValue when intValue >= char.MinValue && intValue <= char.MaxValue => (char)intValue,
        string stringValue when stringValue.Length == 1 => stringValue[0],
        _ => value
    };

    protected Operand GetOperand(Expression expression)
    {
        // Unwrap the Convert expression to get to the underlying member
        if (expression is UnaryExpression unary && unary.NodeType == ExpressionType.Convert)
            expression = unary.Operand;

        expression = UnwrapNullableValueMember(expression);

        // Detect chained string functions like x.first_name.Trim().Length
        if (expression is MemberExpression m1 && TryGetChainedStringFunction(m1, out var chainSql))
            return Operand.RawSql(chainSql);

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

    private static Expression UnwrapNullableValueMember(Expression expression)
    {
        if (expression is MemberExpression { Member.Name: "Value", Expression: not null } memberExpression &&
            Nullable.GetUnderlyingType(memberExpression.Expression.Type) is not null)
        {
            return memberExpression.Expression;
        }

        return expression;
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked || unary.NodeType == ExpressionType.Quote))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private bool TryGetChainedStringFunction(MemberExpression outerMember, out string sql)
    {
        sql = null!;
        // Allow outerMember.Expression to be either a MethodCallExpression (e.g., Length after Trim) or another MemberExpression in longer chains
        if (outerMember.Expression is not MethodCallExpression && outerMember.Expression is not MemberExpression)
            return false;

        var rootColumnExpr = FindRootColumn(outerMember);
        if (rootColumnExpr == null)
            return false;
        var current = GetColumnMaybe(rootColumnExpr)?.DbName ?? rootColumnExpr.Member.Name;

        var steps = new List<(SqlFunctionType type, object[]? args)>();

        void Collect(Expression expr)
        {
            switch (expr)
            {
                case MemberExpression me when me.Member.Name == "Length":
                    steps.Add((SqlFunctionType.StringLength, null));
                    if (me.Expression != null)
                        Collect(me.Expression);
                    break;
                case MethodCallExpression mc:
                    SqlFunctionType? f = mc.Method.Name switch
                    {
                        "Trim" => SqlFunctionType.StringTrim,
                        "ToUpper" => SqlFunctionType.StringToUpper,
                        "ToLower" => SqlFunctionType.StringToLower,
                        "Substring" => SqlFunctionType.StringSubstring,
                        _ => null
                    };
                    object[]? args = null;
                    if (f == SqlFunctionType.StringSubstring)
                    {
                        var startIndex = (int)GetConstant(mc.Arguments[0])! + 1; // SQL 1-indexed
                        var length = (int)GetConstant(mc.Arguments[1])!;
                        args = new object[] { startIndex, length };
                    }
                    if (f.HasValue)
                        steps.Add((f.Value, args));
                    if (mc.Object != null)
                        Collect(mc.Object);
                    break;
                case MemberExpression me2 when me2.Expression is QuerySourceReferenceExpression:
                    // base column reached
                    break;
                case MemberExpression me3:
                    if (me3.Expression != null)
                        Collect(me3.Expression);
                    break;
            }
        }

        Collect(outerMember);
        if (steps.Count == 0)
            return false;

        // steps currently ordered outer -> inner (e.g., Length, Trim). We need to apply inner -> outer.
        // Instead of reversing enumeration, build a nested expression manually: START from root applying inner-most last collected element first.
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            var (type, args) = steps[i];
            current = GetSqlForFunction(type, current, args);
        }

        sql = current;
        return true;
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
            // Recursive step: Method call chain (e.g., Trim()/ToUpper()/Substring())
            MethodCallExpression { Object: not null } methodCall => FindRootColumn(methodCall.Object),
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
        _ => throw new QueryTranslationException($"Expression type '{type}' is not supported for relation mapping.")
    };
}
