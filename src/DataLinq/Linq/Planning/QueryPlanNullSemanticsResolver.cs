using System;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanNullSemanticsResolver
{
    public static QueryPlanNullSemantics GetComparisonNullSemantics(
        QueryPlanComparisonOperator comparisonOperator,
        QueryPlanValue left,
        QueryPlanValue right,
        IQueryPlanSpecializationLookup specialization)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(specialization);

        return comparisonOperator == QueryPlanComparisonOperator.NotEqual && IsNullableColumnComparedWithNonNull(left, right, specialization)
            ? QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull
            : QueryPlanNullSemantics.Default;
    }

    private static bool IsNullableColumnComparedWithNonNull(
        QueryPlanValue left,
        QueryPlanValue right,
        IQueryPlanSpecializationLookup specialization)
        => IsNullableColumnAndNonNullValue(left, right, specialization) ||
           IsNullableColumnAndNonNullValue(right, left, specialization);

    private static bool IsNullableColumnAndNonNullValue(
        QueryPlanValue columnCandidate,
        QueryPlanValue valueCandidate,
        IQueryPlanSpecializationLookup specialization)
        => columnCandidate is QueryPlanColumnValue column &&
           column.Column.ValueProperty.CsNullable &&
           !IsNullValue(valueCandidate, specialization);

    private static bool IsNullValue(
        QueryPlanValue value,
        IQueryPlanSpecializationLookup specialization)
    {
        if (value is QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.Null })
            return true;

        if (value is QueryPlanConvertedValue converted)
            return IsNullValue(converted.Value, specialization);

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
