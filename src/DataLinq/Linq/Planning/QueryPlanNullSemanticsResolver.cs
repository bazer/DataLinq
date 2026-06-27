using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal static class QueryPlanNullSemanticsResolver
{
    public static QueryPlanNullSemantics GetComparisonNullSemantics(
        QueryPlanComparisonOperator comparisonOperator,
        QueryPlanValue left,
        QueryPlanValue right,
        IReadOnlyList<QueryPlanBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(bindings);

        return comparisonOperator == QueryPlanComparisonOperator.NotEqual && IsNullableColumnComparedWithNonNull(left, right, bindings)
            ? QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull
            : QueryPlanNullSemantics.Default;
    }

    private static bool IsNullableColumnComparedWithNonNull(QueryPlanValue left, QueryPlanValue right, IReadOnlyList<QueryPlanBinding> bindings)
        => IsNullableColumnAndNonNullValue(left, right, bindings) || IsNullableColumnAndNonNullValue(right, left, bindings);

    private static bool IsNullableColumnAndNonNullValue(QueryPlanValue columnCandidate, QueryPlanValue valueCandidate, IReadOnlyList<QueryPlanBinding> bindings)
        => columnCandidate is QueryPlanColumnValue column &&
           column.Column.ValueProperty.CsNullable &&
           !IsNullValue(valueCandidate, bindings);

    private static bool IsNullValue(QueryPlanValue value, IReadOnlyList<QueryPlanBinding> bindings)
        => value is QueryPlanConstantValue { Value: null } ||
           value is QueryPlanCapturedValue captured &&
           bindings.SingleOrDefault(binding => binding.Id == captured.BindingId) is { Kind: QueryPlanBindingKind.Scalar, Value: null };
}
