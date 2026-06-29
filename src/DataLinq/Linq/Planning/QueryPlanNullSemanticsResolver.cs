using System;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanNullSemanticsResolver
{
    public static QueryPlanNullSemantics GetComparisonNullSemantics(
        QueryPlanComparisonOperator comparisonOperator,
        QueryPlanValue left,
        QueryPlanValue right,
        IQueryPlanBindingLookup bindings)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(bindings);

        return comparisonOperator == QueryPlanComparisonOperator.NotEqual && IsNullableColumnComparedWithNonNull(left, right, bindings)
            ? QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull
            : QueryPlanNullSemantics.Default;
    }

    private static bool IsNullableColumnComparedWithNonNull(QueryPlanValue left, QueryPlanValue right, IQueryPlanBindingLookup bindings)
        => IsNullableColumnAndNonNullValue(left, right, bindings) || IsNullableColumnAndNonNullValue(right, left, bindings);

    private static bool IsNullableColumnAndNonNullValue(QueryPlanValue columnCandidate, QueryPlanValue valueCandidate, IQueryPlanBindingLookup bindings)
        => columnCandidate is QueryPlanColumnValue column &&
           column.Column.ValueProperty.CsNullable &&
           !IsNullValue(valueCandidate, bindings);

    private static bool IsNullValue(QueryPlanValue value, IQueryPlanBindingLookup bindings)
    {
        if (value is QueryPlanConstantValue { Value: null })
            return true;

        if (value is not QueryPlanCapturedValue captured)
            return false;

        if (!bindings.TryGet(captured.BindingId, out var binding))
            throw new InvalidOperationException(
                $"Captured query plan value '{captured.BindingId}' is missing from the binding frame.");

        if (binding.Kind != QueryPlanBindingKind.Scalar)
            throw new InvalidOperationException(
                $"Captured query plan value '{captured.BindingId}' references a non-scalar binding.");

        return binding.Value is null;
    }
}
