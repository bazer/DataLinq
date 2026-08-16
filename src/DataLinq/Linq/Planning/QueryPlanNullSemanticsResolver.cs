using System;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanNullSemanticsResolver
{
    public static QueryPlanNullSemantics GetComparisonNullSemantics(
        QueryPlanComparisonOperator comparisonOperator,
        QueryPlanValue left,
        QueryPlanValue right,
        IQueryPlanSpecializationLookup specialization,
        bool includeNullsForNegatedRelational = false)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(specialization);

        if (IsKnownNullValue(left, specialization) || IsKnownNullValue(right, specialization))
            return QueryPlanNullSemantics.Default;

        var leftIsNullableColumn = IsNullableColumn(left);
        var rightIsNullableColumn = IsNullableColumn(right);
        var requiresCSharpNullSemantics = comparisonOperator switch
        {
            QueryPlanComparisonOperator.Equal => leftIsNullableColumn && rightIsNullableColumn,
            QueryPlanComparisonOperator.NotEqual => leftIsNullableColumn || rightIsNullableColumn,
            QueryPlanComparisonOperator.GreaterThan or
            QueryPlanComparisonOperator.GreaterThanOrEqual or
            QueryPlanComparisonOperator.LessThan or
            QueryPlanComparisonOperator.LessThanOrEqual =>
                includeNullsForNegatedRelational && (leftIsNullableColumn || rightIsNullableColumn),
            _ => false
        };

        return requiresCSharpNullSemantics
            ? QueryPlanNullSemantics.CSharpNullableComparison
            : QueryPlanNullSemantics.Default;
    }

    internal static bool IsNullableColumn(QueryPlanValue value) => value switch
    {
        QueryPlanColumnValue column => column.Column.ValueProperty.CsNullable,
        QueryPlanConvertedValue converted => IsNullableColumn(converted.Value),
        QueryPlanGroupKeyValue groupKey => IsNullableColumn(groupKey.Key),
        _ => false
    };

    internal static bool IsKnownNullValue(
        QueryPlanValue value,
        IQueryPlanSpecializationLookup specialization)
    {
        if (value is QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.Null })
            return true;

        if (value is QueryPlanConvertedValue converted)
            return IsKnownNullValue(converted.Value, specialization);

        if (value is not QueryPlanScalarBindingReference scalar)
            return false;

        if (!specialization.TryGetSpecialization(scalar.BindingId, out var constraint))
            throw new InvalidOperationException(
                $"Scalar query plan binding '{scalar.BindingId}' has no explicit specialization.");

        if (constraint is not QueryPlanBindingSpecialization.ScalarNullness nullness)
            throw new InvalidOperationException(
                $"Query plan binding '{scalar.BindingId}' does not have scalar nullness specialization.");

        return nullness.Nullness == QueryPlanBindingNullness.Null;
    }
}
