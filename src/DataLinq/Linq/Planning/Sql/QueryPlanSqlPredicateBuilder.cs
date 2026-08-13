using System;
using System.Collections.Generic;
using DataLinq.Exceptions;
using DataLinq.Query;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class QueryPlanSqlPredicateBuilder<T>(
    SqlQuery<T> query,
    QueryPlanSqlSourceMap sourceMap,
    QueryPlanSqlValueRenderer valueRenderer)
{
    public void Apply(QueryPlanPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        Apply(predicate, query.GetBaseWhereGroup(), BooleanType.And);
    }

    public void ApplyHaving(QueryPlanPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        Apply(predicate, query.GetBaseHavingGroup(), BooleanType.And);
    }

    private void Apply(QueryPlanPredicate predicate, WhereGroup<T> group, BooleanType connectionType)
    {
        switch (predicate)
        {
            case QueryPlanPredicate.Fixed fixedPredicate:
                group.AddFixedCondition(fixedPredicate.Value ? Operator.AlwaysTrue : Operator.AlwaysFalse, connectionType);
                break;

            case QueryPlanPredicate.And and:
                ApplyCompound(and.Terms, group, BooleanType.And, connectionType);
                break;

            case QueryPlanPredicate.Or or:
                ApplyCompound(or.Terms, group, BooleanType.Or, connectionType);
                break;

            case QueryPlanPredicate.Not not:
                var negated = new WhereGroup<T>(query, BooleanType.And, isNegated: true);
                group.AddSubGroup(negated, connectionType);
                Apply(not.Predicate, negated, BooleanType.And);
                break;

            case QueryPlanPredicate.Compare compare:
                ApplyCompare(compare, group, connectionType);
                break;

            case QueryPlanPredicate.In inPredicate:
                ApplyIn(inPredicate, group, connectionType);
                break;

            case QueryPlanPredicate.Exists exists:
                ApplyExists(exists, group, connectionType);
                break;

            default:
                throw new QueryTranslationException($"Query plan predicate '{predicate.Kind}' is not supported by SQL rendering.");
        }
    }

    private void ApplyCompound(
        IReadOnlyList<QueryPlanPredicate> terms,
        WhereGroup<T> group,
        BooleanType joinType,
        BooleanType connectionType)
    {
        var subgroup = new WhereGroup<T>(query, joinType);
        group.AddSubGroup(subgroup, connectionType);

        for (var index = 0; index < terms.Count; index++)
            Apply(terms[index], subgroup, index == 0 ? BooleanType.And : joinType);
    }

    private void ApplyCompare(QueryPlanPredicate.Compare compare, WhereGroup<T> group, BooleanType connectionType)
    {
        if (TryApplyBooleanFunctionComparison(compare, group, connectionType))
            return;

        var left = valueRenderer.RenderOperand(compare.Left);
        var right = valueRenderer.RenderOperand(compare.Right);
        (left, right) = valueRenderer.NormalizeComparisonOperands(compare.Operator, left, right);

        if (TryApplyNullComparison(compare.Operator, left, right, group, connectionType))
            return;

        var sqlOperator = ToSqlOperator(compare.Operator);

        if (compare.NullSemantics == QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull &&
            TryGetNullableColumnAndNonNullValue(left, right, out var nullableColumn, out var value) ||
            compare.NullSemantics == QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull &&
            TryGetNullableColumnAndNonNullValue(right, left, out nullableColumn, out value))
        {
            var orGroup = new WhereGroup<T>(query, BooleanType.Or);
            group.AddSubGroup(orGroup, connectionType);
            orGroup.AddWhere(new Comparison(nullableColumn, Operator.NotEqual, value), BooleanType.And);
            AddNullComparison(orGroup, nullableColumn, isNotNull: false, BooleanType.Or);
            return;
        }

        group.AddWhere(new Comparison(left, sqlOperator, right), connectionType);
    }

    private static bool TryApplyNullComparison(
        QueryPlanComparisonOperator comparisonOperator,
        Operand left,
        Operand right,
        WhereGroup<T> group,
        BooleanType connectionType)
    {
        var leftIsNull = left is ValueOperand { IsNull: true };
        var rightIsNull = right is ValueOperand { IsNull: true };
        if (!leftIsNull && !rightIsNull)
            return false;

        var operand = leftIsNull && !rightIsNull
            ? right
            : leftIsNull
                ? Operand.RawSql("NULL")
                : left;
        var sqlOperator = comparisonOperator switch
        {
            QueryPlanComparisonOperator.Equal => Operator.EqualNull,
            QueryPlanComparisonOperator.NotEqual => Operator.NotEqualNull,
            _ => ToSqlOperator(comparisonOperator)
        };

        group.AddWhere(
            new Comparison(operand, sqlOperator, Operand.RawSql("NULL")),
            connectionType);
        return true;
    }

    private void ApplyIn(QueryPlanPredicate.In inPredicate, WhereGroup<T> group, BooleanType connectionType)
    {
        var sourceValues = valueRenderer.GetLocalSequenceValues(inPredicate.Sequence);
        if (sourceValues.Count == 0)
        {
            group.AddFixedCondition(
                inPredicate.IsNegated ? Operator.AlwaysTrue : Operator.AlwaysFalse,
                connectionType);
            return;
        }

        var item = valueRenderer.RenderOperand(inPredicate.Item);
        var itemAllowsNull = item is ColumnOperandWithDefinition itemColumn
            ? itemColumn.ColumnDefinition.ValueProperty.CsNullable
            : CanBeNull(inPredicate.Item.ClrType);
        var nonNullValues = new object?[sourceValues.Count];
        var nonNullCount = 0;
        for (var index = 0; index < sourceValues.Count; index++)
        {
            if (sourceValues[index] is not { } value)
                continue;

            nonNullValues[nonNullCount++] = value;
        }

        var containsNull = nonNullCount != sourceValues.Count;
        if (nonNullCount == 0)
        {
            if (itemAllowsNull)
                AddNullComparison(group, item, inPredicate.IsNegated, connectionType);
            else
                group.AddFixedCondition(inPredicate.IsNegated ? Operator.AlwaysTrue : Operator.AlwaysFalse, connectionType);

            return;
        }

        if (nonNullCount != nonNullValues.Length)
            Array.Resize(ref nonNullValues, nonNullCount);

        ValueOperand values;
        if (item is ColumnOperandWithDefinition column)
        {
            values = valueRenderer.NormalizeLocalSequenceValues(column.ColumnDefinition, nonNullValues);
        }
        else
        {
            values = Operand.Value(nonNullValues);
        }

        var membership = new Comparison(item, inPredicate.IsNegated ? Operator.NotIn : Operator.In, values);
        if (!itemAllowsNull)
        {
            group.AddWhere(membership, connectionType);
            return;
        }

        var joinType = inPredicate.IsNegated == containsNull
            ? BooleanType.And
            : BooleanType.Or;
        var compound = new WhereGroup<T>(query, joinType);
        group.AddSubGroup(compound, connectionType);
        compound.AddWhere(membership, BooleanType.And);
        AddNullComparison(compound, item, isNotNull: inPredicate.IsNegated == containsNull, joinType);
    }

    private static void AddNullComparison(
        WhereGroup<T> group,
        Operand operand,
        bool isNotNull,
        BooleanType connectionType)
        => group.AddWhere(
            new Comparison(
                operand,
                isNotNull ? Operator.NotEqualNull : Operator.EqualNull,
                Operand.RawSql("NULL")),
            connectionType);

    private static bool CanBeNull(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

    private void ApplyExists(QueryPlanPredicate.Exists exists, WhereGroup<T> group, BooleanType connectionType)
    {
        var relationPart = exists.Relation.RelationPart;
        if (relationPart.Type != DataLinq.Metadata.RelationPartType.CandidateKey)
        {
            throw new QueryTranslationException(
                $"Relation property '{exists.Relation.PropertyName}' is not supported in query plan relation exists rendering. " +
                "Only collection relations from the candidate-key side are supported.");
        }

        var childPart = relationPart.GetOtherSide();
        var parentColumns = relationPart.ColumnIndex.Columns;
        var childColumns = childPart.ColumnIndex.Columns;
        if (parentColumns.Count != childColumns.Count)
            throw new QueryTranslationException($"Relation property '{exists.Relation.PropertyName}' has mismatched relation column counts.");

        var childSource = sourceMap.Get(exists.ChildSource);
        var childQuery = new SqlQuery<object>(childSource.Table, query.DataSource, childSource.Alias);
        var childGroup = childQuery.GetBaseWhereGroup();

        for (var index = 0; index < parentColumns.Count; index++)
        {
            childGroup
                .AddWhere(childColumns[index].DbName, childSource.Alias, BooleanType.And)
                .EqualToRaw(valueRenderer.RenderColumnSql(new QueryPlanColumnValue(exists.ParentSource, parentColumns[index])));
        }

        if (exists.Predicate is not null)
        {
            var childPredicateBuilder = new QueryPlanSqlPredicateBuilder<object>(childQuery, sourceMap, valueRenderer);
            childPredicateBuilder.Apply(exists.Predicate, childGroup, BooleanType.And);
        }

        group.AddExists(childQuery, connectionType, exists.IsNegated);
    }

    private bool TryApplyBooleanFunctionComparison(QueryPlanPredicate.Compare compare, WhereGroup<T> group, BooleanType connectionType)
    {
        if (!TryGetBooleanFunctionComparison(compare, out var function, out var shouldMatch))
            return false;

        if (shouldMatch)
        {
            ApplyBooleanFunction(function, group, connectionType);
            return true;
        }

        var negated = new WhereGroup<T>(query, BooleanType.And, isNegated: true);
        group.AddSubGroup(negated, connectionType);
        ApplyBooleanFunction(function, negated, BooleanType.And);
        return true;
    }

    private bool TryGetBooleanFunctionComparison(
        QueryPlanPredicate.Compare compare,
        out QueryPlanFunctionValue function,
        out bool shouldMatch)
    {
        function = null!;
        shouldMatch = false;

        if (compare.Operator is not (QueryPlanComparisonOperator.Equal or QueryPlanComparisonOperator.NotEqual))
            return false;

        if (compare.Left is QueryPlanFunctionValue leftFunction && TryGetBooleanValue(compare.Right, out var rightValue))
        {
            function = leftFunction;
            shouldMatch = compare.Operator == QueryPlanComparisonOperator.Equal ? rightValue : !rightValue;
            return IsBooleanPredicateFunction(function);
        }

        if (compare.Right is QueryPlanFunctionValue rightFunction && TryGetBooleanValue(compare.Left, out var leftValue))
        {
            function = rightFunction;
            shouldMatch = compare.Operator == QueryPlanComparisonOperator.Equal ? leftValue : !leftValue;
            return IsBooleanPredicateFunction(function);
        }

        return false;
    }

    private bool TryGetBooleanValue(QueryPlanValue value, out bool result)
    {
        if (value.ClrType != typeof(bool))
        {
            result = false;
            return false;
        }

        var scalar = valueRenderer.GetScalarValue(value);
        if (scalar is bool boolValue)
        {
            result = boolValue;
            return true;
        }

        result = false;
        return false;
    }

    private static bool IsBooleanPredicateFunction(QueryPlanFunctionValue function)
        => function.Function is QueryPlanFunctionKind.StringStartsWith or
            QueryPlanFunctionKind.StringEndsWith or
            QueryPlanFunctionKind.StringContains or
            QueryPlanFunctionKind.StringIsNullOrEmpty or
            QueryPlanFunctionKind.StringIsNullOrWhiteSpace;

    private void ApplyBooleanFunction(QueryPlanFunctionValue function, WhereGroup<T> group, BooleanType connectionType)
    {
        switch (function.Function)
        {
            case QueryPlanFunctionKind.StringStartsWith:
            case QueryPlanFunctionKind.StringEndsWith:
            case QueryPlanFunctionKind.StringContains:
                ApplyStringLikeFunction(function, group, connectionType);
                break;

            case QueryPlanFunctionKind.StringIsNullOrEmpty:
            case QueryPlanFunctionKind.StringIsNullOrWhiteSpace:
                ApplyStringNullOrEmptyFunction(function, group, connectionType);
                break;

            default:
                throw new QueryTranslationException($"Query plan function '{function.Function}' is not a boolean SQL predicate function.");
        }
    }

    private void ApplyStringLikeFunction(QueryPlanFunctionValue function, WhereGroup<T> group, BooleanType connectionType)
    {
        if (function.Arguments.Count != 2)
            throw new QueryTranslationException($"Query plan function '{function.Function}' expects source and pattern arguments.");

        var source = valueRenderer.RenderOperand(function.Arguments[0]);
        var value = valueRenderer.GetScalarValue(function.Arguments[1]);
        var pattern = function.Function switch
        {
            QueryPlanFunctionKind.StringStartsWith => string.Concat(value, "%"),
            QueryPlanFunctionKind.StringEndsWith => string.Concat("%", value),
            QueryPlanFunctionKind.StringContains => string.Concat("%", value, "%"),
            _ => throw new QueryTranslationException($"Query plan function '{function.Function}' is not a LIKE predicate.")
        };

        group.AddWhere(new Comparison(source, Operator.Like, Operand.Value(pattern)), connectionType);
    }

    private void ApplyStringNullOrEmptyFunction(QueryPlanFunctionValue function, WhereGroup<T> group, BooleanType connectionType)
    {
        if (function.Arguments.Count != 1)
            throw new QueryTranslationException($"Query plan function '{function.Function}' expects one source argument.");

        var source = valueRenderer.RenderOperand(function.Arguments[0]);
        var checkFunction = function.Function == QueryPlanFunctionKind.StringIsNullOrEmpty
            ? QueryPlanFunctionKind.StringLength
            : QueryPlanFunctionKind.StringTrim;
        object checkValue = function.Function == QueryPlanFunctionKind.StringIsNullOrEmpty
            ? 0
            : string.Empty;

        var functionOperand = valueRenderer.RenderOperand(new QueryPlanFunctionValue(checkFunction, [function.Arguments[0]], checkValue.GetType()));
        var orGroup = new WhereGroup<T>(query, BooleanType.Or);
        group.AddSubGroup(orGroup, connectionType);
        orGroup.AddWhere(new Comparison(source, Operator.Equal, Operand.Value((object?)null)), BooleanType.And);
        orGroup.AddWhere(new Comparison(functionOperand, Operator.Equal, Operand.Value(checkValue)), BooleanType.Or);
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

    private static Operator ToSqlOperator(QueryPlanComparisonOperator comparisonOperator) => comparisonOperator switch
    {
        QueryPlanComparisonOperator.Equal => Operator.Equal,
        QueryPlanComparisonOperator.NotEqual => Operator.NotEqual,
        QueryPlanComparisonOperator.GreaterThan => Operator.GreaterThan,
        QueryPlanComparisonOperator.GreaterThanOrEqual => Operator.GreaterThanOrEqual,
        QueryPlanComparisonOperator.LessThan => Operator.LessThan,
        QueryPlanComparisonOperator.LessThanOrEqual => Operator.LessThanOrEqual,
        _ => throw new QueryTranslationException($"Query plan comparison operator '{comparisonOperator}' is not supported by SQL rendering.")
    };
}
