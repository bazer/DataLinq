using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal sealed record QueryPlanRequirement(
    QueryPlanFeature Feature,
    string Location,
    string? SourceId = null,
    string? ColumnName = null,
    int? Count = null,
    int? NullCount = null);

internal sealed class QueryPlanRequirements
{
    private QueryPlanRequirements(
        IReadOnlyList<QueryPlanRequirement> structural,
        IReadOnlyList<QueryPlanRequirement> invocation)
    {
        Structural = structural;
        Invocation = invocation;
    }

    public IReadOnlyList<QueryPlanRequirement> Structural { get; }

    public IReadOnlyList<QueryPlanRequirement> Invocation { get; }

    public static QueryPlanRequirements Extract(QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new Extractor(invocation).Extract();
    }

    private sealed class Extractor(QueryPlanInvocation invocation)
    {
        private readonly List<QueryPlanRequirement> structural = [];
        private readonly List<QueryPlanRequirement> invocationRequirements = [];

        public QueryPlanRequirements Extract()
        {
            var template = invocation.Template;
            AddStructural(
                QueryPlanFeature.SourceCount(template.Sources.Count == 1
                    ? QueryPlanSourceCountKind.Single
                    : QueryPlanSourceCountKind.Multiple),
                "sources");
            var rootSourceCount = template.Sources.Count(static source => source.Kind == QueryPlanSourceKind.RootTable);
            AddStructural(
                QueryPlanFeature.SourceTopology(rootSourceCount switch
                {
                    0 => QueryPlanSourceTopology.NoRoot,
                    1 => QueryPlanSourceTopology.ExactlyOneRoot,
                    _ => QueryPlanSourceTopology.MultipleRoots
                }),
                "sources");

            for (var index = 0; index < template.Sources.Count; index++)
            {
                var source = template.Sources[index];
                var location = $"sources[{index}]";
                AddStructural(QueryPlanFeature.SourceKind(source.Kind), location, source.Id);
                AddStructural(QueryPlanFeature.SourceCardinality(source.Cardinality), location, source.Id);
                AddStructural(
                    QueryPlanFeature.SourceNullability(source.IsNullable
                        ? QueryPlanSourceNullability.Nullable
                        : QueryPlanSourceNullability.NonNullable),
                    location,
                    source.Id);
            }

            VisitOperations(
                template.Operations,
                "operations",
                template.Projection.Kind,
                HasDirectColumnSqlRowMembers(template.Projection),
                template.Sources[0].Id);

            VisitProjection(template.Projection, "projection");
            VisitResult(template.Result, "result", template.Sources[0].Id);

            for (var index = 0; index < template.BindingDeclarations.Count; index++)
            {
                var declaration = template.BindingDeclarations[index];
                AddStructural(QueryPlanFeature.BindingKind(declaration.Kind), $"bindings[{index}]");
                VisitInvocationValue(invocation.Values[index], $"invocation.bindings[{index}]");
            }

            return new QueryPlanRequirements(
                Array.AsReadOnly(structural.ToArray()),
                Array.AsReadOnly(invocationRequirements.ToArray()));
        }

        private void VisitOperations(
            IReadOnlyList<QueryPlanOperation> operations,
            string location,
            QueryPlanProjectionKind projectionKind,
            bool sqlRowHasDirectColumnMembers,
            string defaultSourceId)
        {
            var hasSeenPushdown = false;
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                VisitOperation(
                    operation,
                    $"{location}[{index}]",
                    projectionKind,
                    sqlRowHasDirectColumnMembers,
                    defaultSourceId,
                    hasSeenPushdown && operation is QueryPlanOperation.Pushdown);

                hasSeenPushdown |= operation is QueryPlanOperation.Pushdown;
            }
        }

        private void VisitOperation(
            QueryPlanOperation operation,
            string location,
            QueryPlanProjectionKind projectionKind,
            bool sqlRowHasDirectColumnMembers,
            string defaultSourceId,
            bool repeatedPushdown)
        {
            var sourceId = FindSourceId(operation) ?? defaultSourceId;
            AddStructural(QueryPlanFeature.Operation(operation.Kind), location, sourceId);
            switch (operation)
            {
                case QueryPlanOperation.Where where:
                    VisitPredicate(where.Predicate, $"{location}.predicate", sourceId);
                    break;
                case QueryPlanOperation.Having having:
                    VisitPredicate(having.Predicate, $"{location}.predicate", sourceId);
                    break;
                case QueryPlanOperation.OrderBy orderBy:
                    VisitOrderings(orderBy.Orderings, $"{location}.orderings", sourceId);
                    break;
                case QueryPlanOperation.Skip skip:
                    AddInvocation(
                        QueryPlanFeature.PagingCountShape(GetPagingCountShape(skip.Count)),
                        $"{location}.count.shape",
                        sourceId);
                    VisitValue(skip.Count, QueryPlanValueUse.PagingCount, $"{location}.count", sourceId);
                    break;
                case QueryPlanOperation.Take take:
                    AddInvocation(
                        QueryPlanFeature.PagingCountShape(GetPagingCountShape(take.Count)),
                        $"{location}.count.shape",
                        sourceId);
                    VisitValue(take.Count, QueryPlanValueUse.PagingCount, $"{location}.count", sourceId);
                    break;
                case QueryPlanOperation.Join join:
                    AddStructural(
                        QueryPlanFeature.JoinKind(join.JoinShape.Kind),
                        $"{location}.join.kind",
                        join.JoinShape.LeftSource.Id);
                    AddStructural(
                        QueryPlanFeature.JoinRightSourceKind(join.JoinShape.RightSource.Kind),
                        $"{location}.join.right-source-kind",
                        join.JoinShape.RightSource.Id);
                    VisitValue(
                        new QueryPlanColumnValue(join.JoinShape.LeftSource, join.JoinShape.LeftColumn),
                        QueryPlanValueUse.JoinKey,
                        $"{location}.join.left");
                    VisitValue(
                        new QueryPlanColumnValue(join.JoinShape.RightSource, join.JoinShape.RightColumn),
                        QueryPlanValueUse.JoinKey,
                        $"{location}.join.right");
                    break;
                case QueryPlanOperation.Pushdown pushdown:
                    var containsJoin = pushdown.Operations.Any(static inner => inner is QueryPlanOperation.Join);
                    var shape = repeatedPushdown
                        ? QueryPlanPushdownShape.RepeatedInScope
                        : containsJoin
                            ? projectionKind == QueryPlanProjectionKind.SqlRow
                                ? sqlRowHasDirectColumnMembers
                                    ? QueryPlanPushdownShape.JoinedSqlRowDirectColumns
                                    : QueryPlanPushdownShape.JoinedSqlRowNonColumn
                                : QueryPlanPushdownShape.JoinedNonSqlRow
                            : QueryPlanPushdownShape.Simple;
                    AddStructural(QueryPlanFeature.PushdownShape(shape), $"{location}.shape", sourceId);

                    var innerProjectionKind = containsJoin && projectionKind == QueryPlanProjectionKind.SqlRow
                        ? QueryPlanProjectionKind.SqlRow
                        : QueryPlanProjectionKind.Entity;
                    VisitOperations(
                        pushdown.Operations,
                        $"{location}.operations",
                        innerProjectionKind,
                        containsJoin && projectionKind == QueryPlanProjectionKind.SqlRow && sqlRowHasDirectColumnMembers,
                        sourceId);
                    VisitOrderings(pushdown.PreservedOrderings, $"{location}.preservedOrderings", sourceId);
                    break;
                case QueryPlanOperation.GroupBy groupBy:
                    for (var index = 0; index < groupBy.Keys.Count; index++)
                        VisitValue(groupBy.Keys[index], QueryPlanValueUse.GroupingKey, $"{location}.keys[{index}]", sourceId);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan operation '{operation.GetType().Name}'.", nameof(operation));
            }
        }

        private void VisitOrderings(
            IReadOnlyList<QueryPlanOrdering> orderings,
            string location,
            string defaultSourceId)
        {
            for (var index = 0; index < orderings.Count; index++)
            {
                var ordering = orderings[index];
                var sourceId = FindSourceId(ordering.Value) ?? defaultSourceId;
                AddStructural(
                    QueryPlanFeature.OrderingDirection(ordering.Direction),
                    $"{location}[{index}].direction",
                    sourceId);
                VisitValue(ordering.Value, QueryPlanValueUse.Ordering, $"{location}[{index}].value", sourceId);
            }
        }

        private void VisitPredicate(
            QueryPlanPredicate predicate,
            string location,
            string defaultSourceId)
        {
            var sourceId = FindSourceId(predicate) ?? defaultSourceId;
            AddStructural(QueryPlanFeature.Predicate(predicate.Kind), location, sourceId);
            switch (predicate)
            {
                case QueryPlanPredicate.Fixed:
                    break;
                case QueryPlanPredicate.And and:
                    for (var index = 0; index < and.Terms.Count; index++)
                        VisitPredicate(and.Terms[index], $"{location}.terms[{index}]", sourceId);
                    break;
                case QueryPlanPredicate.Or or:
                    for (var index = 0; index < or.Terms.Count; index++)
                        VisitPredicate(or.Terms[index], $"{location}.terms[{index}]", sourceId);
                    break;
                case QueryPlanPredicate.Not not:
                    VisitPredicate(not.Predicate, $"{location}.predicate", sourceId);
                    break;
                case QueryPlanPredicate.Compare compare:
                    AddStructural(QueryPlanFeature.ComparisonOperator(compare.Operator), $"{location}.operator", sourceId);
                    AddStructural(QueryPlanFeature.NullSemantics(compare.NullSemantics), $"{location}.nullSemantics", sourceId);
                    AddStructural(
                        QueryPlanFeature.ComparisonShape(GetComparisonShape(compare)),
                        $"{location}.shape",
                        sourceId);
                    VisitValue(
                        compare.Left,
                        GetComparisonValueUse(compare, compare.Left, compare.Right),
                        $"{location}.left",
                        sourceId);
                    VisitValue(
                        compare.Right,
                        GetComparisonValueUse(compare, compare.Right, compare.Left),
                        $"{location}.right",
                        sourceId);
                    break;
                case QueryPlanPredicate.In inPredicate:
                    AddStructural(
                        QueryPlanFeature.PredicatePolarity(inPredicate.IsNegated
                            ? QueryPlanPredicatePolarity.Negated
                            : QueryPlanPredicatePolarity.Positive),
                        $"{location}.polarity",
                        sourceId);
                    VisitValue(inPredicate.Item, QueryPlanValueUse.MembershipItem, $"{location}.item", sourceId);
                    VisitValue(inPredicate.Sequence, QueryPlanValueUse.MembershipSequence, $"{location}.sequence", sourceId);
                    break;
                case QueryPlanPredicate.Exists exists:
                    AddStructural(
                        QueryPlanFeature.PredicatePolarity(exists.IsNegated
                            ? QueryPlanPredicatePolarity.Negated
                            : QueryPlanPredicatePolarity.Positive),
                        $"{location}.polarity",
                        sourceId);
                    AddStructural(
                        QueryPlanFeature.RelationPart(exists.Relation.RelationPart.Type),
                        $"{location}.relation",
                        exists.ParentSource.Id);
                    if (exists.Predicate is not null)
                        VisitPredicate(exists.Predicate, $"{location}.predicate", exists.ChildSource.Id);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan predicate '{predicate.GetType().Name}'.", nameof(predicate));
            }
        }

        private void VisitValue(
            QueryPlanValue value,
            QueryPlanValueUse use,
            string location,
            string? defaultSourceId = null)
        {
            var sourceId = FindSourceId(value) ?? defaultSourceId;
            var columnName = value is QueryPlanColumnValue columnValue ? columnValue.Column.DbName : null;
            AddStructural(QueryPlanFeature.ValueKind(value.Kind, use), location, sourceId, columnName);

            switch (value)
            {
                case QueryPlanColumnValue:
                case QueryPlanScalarBindingReference:
                case QueryPlanLocalSequenceBindingReference:
                    break;
                case QueryPlanIntrinsicValue intrinsic:
                    AddStructural(QueryPlanFeature.Intrinsic(intrinsic.Intrinsic, use), location, sourceId);
                    break;
                case QueryPlanFunctionValue function:
                    AddStructural(QueryPlanFeature.Function(function.Function, use), location, sourceId);
                    AddStructural(
                        QueryPlanFeature.FunctionShape(GetFunctionShape(function)),
                        $"{location}.shape",
                        sourceId);
                    VisitFunctionArguments(function, location, sourceId);
                    break;
                case QueryPlanConvertedValue converted:
                    VisitValue(converted.Value, use, $"{location}.value", sourceId);
                    break;
                case QueryPlanGroupKeyValue groupKey:
                    VisitValue(groupKey.Key, QueryPlanValueUse.GroupingKey, $"{location}.key", sourceId);
                    break;
                case QueryPlanGroupedAggregateValue aggregate:
                    AddStructural(QueryPlanFeature.GroupedAggregate(aggregate.Aggregate, use), location, sourceId);
                    if (aggregate.Selector is not null)
                        VisitAggregateSelector(aggregate.Selector, $"{location}.selector", sourceId);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan value '{value.GetType().Name}'.", nameof(value));
            }
        }

        private void VisitFunctionArguments(
            QueryPlanFunctionValue function,
            string location,
            string? defaultSourceId)
        {
            for (var index = 0; index < function.Arguments.Count; index++)
            {
                var use = IsScalarFunctionArgument(function.Function, index)
                    ? QueryPlanValueUse.ScalarFunctionArgument
                    : QueryPlanValueUse.FunctionSource;
                VisitValue(function.Arguments[index], use, $"{location}.arguments[{index}]", defaultSourceId);
            }
        }

        private static bool IsScalarFunctionArgument(QueryPlanFunctionKind function, int index) =>
            index == 1 && function is (
                QueryPlanFunctionKind.StringStartsWith or
                QueryPlanFunctionKind.StringEndsWith or
                QueryPlanFunctionKind.StringContains) ||
            index > 0 && function == QueryPlanFunctionKind.StringSubstring;

        private static QueryPlanFunctionShape GetFunctionShape(QueryPlanFunctionValue function) =>
            function.Function switch
            {
                QueryPlanFunctionKind.StringStartsWith or
                QueryPlanFunctionKind.StringEndsWith or
                QueryPlanFunctionKind.StringContains => QueryPlanFunctionShape.StringPredicateWithPattern,
                QueryPlanFunctionKind.StringSubstring when function.Arguments.Count == 2 =>
                    QueryPlanFunctionShape.SubstringWithStart,
                QueryPlanFunctionKind.StringSubstring => QueryPlanFunctionShape.SubstringWithStartAndLength,
                QueryPlanFunctionKind.StringIsNullOrEmpty or
                QueryPlanFunctionKind.StringIsNullOrWhiteSpace or
                QueryPlanFunctionKind.StringLength or
                QueryPlanFunctionKind.StringTrim or
                QueryPlanFunctionKind.StringToUpper or
                QueryPlanFunctionKind.StringToLower or
                QueryPlanFunctionKind.DatePartYear or
                QueryPlanFunctionKind.DatePartMonth or
                QueryPlanFunctionKind.DatePartDay or
                QueryPlanFunctionKind.DatePartDayOfYear or
                QueryPlanFunctionKind.DatePartDayOfWeek or
                QueryPlanFunctionKind.TimePartHour or
                QueryPlanFunctionKind.TimePartMinute or
                QueryPlanFunctionKind.TimePartSecond or
                QueryPlanFunctionKind.TimePartMillisecond => QueryPlanFunctionShape.Unary,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(function),
                    function.Function,
                    "Unknown query plan function shape.")
            };

        private void VisitProjection(QueryPlanProjection projection, string location)
        {
            var sourceId = projection switch
            {
                QueryPlanProjection.Entity entity => entity.Source.Id,
                QueryPlanProjection.ScalarMember scalar => scalar.Source.Id,
                QueryPlanProjection.GroupedAggregate grouped => grouped.Source.Id,
                _ => null
            };
            var columnName = projection is QueryPlanProjection.ScalarMember scalarMember
                ? scalarMember.Column.DbName
                : null;

            AddStructural(QueryPlanFeature.Projection(projection.Kind), location, sourceId, columnName);
            AddStructural(
                QueryPlanFeature.ProjectionDisposition(projection.Disposition),
                $"{location}.disposition",
                sourceId,
                columnName);

            switch (projection)
            {
                case QueryPlanProjection.Entity:
                    break;
                case QueryPlanProjection.ScalarMember scalar:
                    VisitValue(new QueryPlanColumnValue(scalar.Source, scalar.Column, scalar.ResultType), QueryPlanValueUse.ProjectionMember, $"{location}.member");
                    break;
                case QueryPlanProjection.Anonymous anonymous:
                    VisitProjectionMembers(anonymous.Members, $"{location}.members");
                    VisitRecipe(anonymous.Recipe, $"{location}.recipe");
                    break;
                case QueryPlanProjection.ComputedRowLocal computed:
                    VisitRecipe(computed.Recipe, $"{location}.recipe");
                    break;
                case QueryPlanProjection.JoinedRowLocal joined:
                    VisitProjectionMembers(joined.Members, $"{location}.members");
                    VisitRecipe(joined.Recipe, $"{location}.recipe");
                    break;
                case QueryPlanProjection.SqlRow sqlRow:
                    VisitProjectionMembers(sqlRow.Members, $"{location}.members");
                    break;
                case QueryPlanProjection.TransparentIdentifier:
                    break;
                case QueryPlanProjection.GroupedAggregate grouped:
                    VisitProjectionMembers(
                        grouped.Members,
                        QueryPlanValueUse.GroupedProjectionMember,
                        $"{location}.members",
                        grouped.Source.Id);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan projection '{projection.GetType().Name}'.", nameof(projection));
            }
        }

        private void VisitProjectionMembers(IReadOnlyList<QueryPlanProjectionMember> members, string location)
            => VisitProjectionMembers(members, QueryPlanValueUse.ProjectionMember, location, defaultSourceId: null);

        private void VisitProjectionMembers(
            IReadOnlyList<QueryPlanProjectionMember> members,
            QueryPlanValueUse use,
            string location,
            string? defaultSourceId)
        {
            for (var index = 0; index < members.Count; index++)
                VisitValue(members[index].Value, use, $"{location}[{index}].value", defaultSourceId);
        }

        private void VisitRecipe(QueryPlanProjectionRecipe recipe, string location)
        {
            var sourceId = FindSourceId(recipe);
            var columnName = recipe is QueryPlanProjectionRecipe.SourceColumn sourceColumnRecipe
                ? sourceColumnRecipe.Column.DbName
                : null;

            AddStructural(QueryPlanFeature.ProjectionRecipe(recipe.Kind), location, sourceId, columnName);
            AddStructural(
                QueryPlanFeature.ProjectionDisposition(recipe.Disposition),
                $"{location}.disposition",
                sourceId,
                columnName);
            switch (recipe)
            {
                case QueryPlanProjectionRecipe.Source:
                case QueryPlanProjectionRecipe.SourceColumn:
                case QueryPlanProjectionRecipe.ScalarBinding:
                    break;
                case QueryPlanProjectionRecipe.Intrinsic intrinsic:
                    AddStructural(QueryPlanFeature.ProjectionIntrinsic(intrinsic.IntrinsicKind), location, sourceId);
                    break;
                case QueryPlanProjectionRecipe.Convert convert:
                    VisitRecipe(convert.Operand, $"{location}.operand");
                    break;
                case QueryPlanProjectionRecipe.Not not:
                    VisitRecipe(not.Operand, $"{location}.operand");
                    break;
                case QueryPlanProjectionRecipe.Binary binary:
                    AddStructural(QueryPlanFeature.ProjectionBinaryOperator(binary.Operator), $"{location}.operator", sourceId);
                    VisitRecipe(binary.Left, $"{location}.left");
                    VisitRecipe(binary.Right, $"{location}.right");
                    break;
                case QueryPlanProjectionRecipe.SupportedMember member:
                    AddStructural(QueryPlanFeature.ProjectionSupportedMember(member.Member), $"{location}.member", sourceId);
                    VisitRecipe(member.Instance, $"{location}.instance");
                    break;
                case QueryPlanProjectionRecipe.Function function:
                    AddStructural(QueryPlanFeature.ProjectionFunction(function.FunctionKind), $"{location}.function", sourceId);
                    VisitRecipes(function.Arguments, $"{location}.arguments");
                    break;
                case QueryPlanProjectionRecipe.Conditional conditional:
                    VisitRecipe(conditional.Test, $"{location}.test");
                    VisitRecipe(conditional.IfTrue, $"{location}.ifTrue");
                    VisitRecipe(conditional.IfFalse, $"{location}.ifFalse");
                    break;
                case QueryPlanProjectionRecipe.NewArray newArray:
                    VisitRecipes(newArray.Elements, $"{location}.elements");
                    break;
                case QueryPlanProjectionRecipe.CompatibilityConstructor constructor:
                    VisitRecipes(constructor.Arguments, $"{location}.arguments");
                    break;
                case QueryPlanProjectionRecipe.CompatibilityMember member when member.Instance is not null:
                    VisitRecipe(member.Instance, $"{location}.instance");
                    break;
                case QueryPlanProjectionRecipe.CompatibilityMember:
                    break;
                default:
                    throw new ArgumentException($"Unknown projection recipe '{recipe.GetType().Name}'.", nameof(recipe));
            }
        }

        private void VisitRecipes(IReadOnlyList<QueryPlanProjectionRecipe> recipes, string location)
        {
            for (var index = 0; index < recipes.Count; index++)
                VisitRecipe(recipes[index], $"{location}[{index}]");
        }

        private void VisitResult(QueryPlanResult result, string location, string defaultSourceId)
        {
            AddStructural(
                QueryPlanFeature.Result(result.Kind),
                location,
                result.AggregateSelector is null
                    ? defaultSourceId
                    : FindSourceId(result.AggregateSelector) ?? defaultSourceId);
            if (result.AggregateSelector is not null)
                VisitAggregateSelector(result.AggregateSelector, $"{location}.selector", defaultSourceId);
        }

        private void VisitAggregateSelector(
            QueryPlanValue selector,
            string location,
            string? defaultSourceId)
        {
            var unwrapped = UnwrapConvertedValue(selector);
            var sourceId = FindSourceId(unwrapped) ?? defaultSourceId;
            var columnName = unwrapped is QueryPlanColumnValue column ? column.Column.DbName : null;
            AddStructural(
                QueryPlanFeature.AggregateSelectorShape(GetAggregateSelectorShape(unwrapped)),
                $"{location}.shape",
                sourceId,
                columnName);
            VisitValue(selector, QueryPlanValueUse.AggregateSelector, location, sourceId);
        }

        private void VisitInvocationValue(QueryPlanInvocationValue value, string location)
        {
            switch (value)
            {
                case QueryPlanInvocationValue.Scalar scalar:
                    var nullness = scalar.Value is null ? QueryPlanBindingNullness.Null : QueryPlanBindingNullness.NonNull;
                    invocationRequirements.Add(new QueryPlanRequirement(QueryPlanFeature.ScalarNullness(nullness), location));
                    break;
                case QueryPlanInvocationValue.LocalSequence sequence:
                    var nullCount = sequence.Values.Count(static value => value is null);
                    var shape = sequence.Values.Count == 0
                        ? QueryPlanLocalSequenceShapeKind.Empty
                        : nullCount == 0
                            ? QueryPlanLocalSequenceShapeKind.NonEmptyWithoutNulls
                            : QueryPlanLocalSequenceShapeKind.NonEmptyWithNulls;
                    invocationRequirements.Add(new QueryPlanRequirement(
                        QueryPlanFeature.LocalSequenceShape(shape),
                        location,
                        Count: sequence.Values.Count,
                        NullCount: nullCount));
                    break;
                default:
                    throw new ArgumentException($"Unknown invocation value '{value.GetType().Name}'.", nameof(value));
            }
        }

        private static QueryPlanValueUse GetComparisonValueUse(
            QueryPlanPredicate.Compare comparison,
            QueryPlanValue value,
            QueryPlanValue counterpart)
        {
            if (value is not QueryPlanFunctionValue function ||
                !IsBooleanPredicateFunction(function.Function) ||
                comparison.Operator is not (QueryPlanComparisonOperator.Equal or QueryPlanComparisonOperator.NotEqual) ||
                !IsScalarBooleanValue(counterpart))
            {
                return QueryPlanValueUse.PredicateOperand;
            }

            return QueryPlanValueUse.BooleanPredicateFunction;
        }

        private QueryPlanComparisonShape GetComparisonShape(QueryPlanPredicate.Compare comparison)
        {
            if (comparison.NullSemantics == QueryPlanNullSemantics.Default)
                return QueryPlanComparisonShape.DefaultNullSemantics;

            if (comparison.NullSemantics == QueryPlanNullSemantics.CSharpNullableNotEqualIncludesNull &&
                comparison.Operator == QueryPlanComparisonOperator.NotEqual)
            {
                if (TryGetNullableColumnAndScalarNullness(comparison.Left, comparison.Right, out var isNull) ||
                    TryGetNullableColumnAndScalarNullness(comparison.Right, comparison.Left, out isNull))
                {
                    return isNull
                        ? QueryPlanComparisonShape.NullableNotEqualColumnAndNullValue
                        : QueryPlanComparisonShape.NullableNotEqualColumnAndNonNullValue;
                }
            }

            return QueryPlanComparisonShape.UnsupportedNullableNotEqual;
        }

        private bool TryGetNullableColumnAndScalarNullness(
            QueryPlanValue columnValue,
            QueryPlanValue scalarValue,
            out bool isNull)
        {
            if (columnValue is QueryPlanColumnValue { Column.ValueProperty.CsNullable: true } &&
                TryGetScalarNullness(scalarValue, out isNull))
            {
                return true;
            }

            isNull = false;
            return false;
        }

        private bool TryGetScalarNullness(QueryPlanValue value, out bool isNull)
        {
            switch (value)
            {
                case QueryPlanIntrinsicValue intrinsic:
                    isNull = intrinsic.Intrinsic == QueryPlanIntrinsicKind.Null;
                    return true;
                case QueryPlanScalarBindingReference scalar
                    when invocation.Template.Specialization.TryGet(scalar.BindingId, out var specialization) &&
                         specialization is QueryPlanBindingSpecialization.ScalarNullness scalarNullness:
                    isNull = scalarNullness.Nullness == QueryPlanBindingNullness.Null;
                    return true;
                case QueryPlanConvertedValue converted:
                    return TryGetScalarNullness(converted.Value, out isNull);
                default:
                    isNull = false;
                    return false;
            }
        }

        private static QueryPlanAggregateSelectorShape GetAggregateSelectorShape(QueryPlanValue selector)
        {
            if (selector is not QueryPlanColumnValue column)
                return QueryPlanAggregateSelectorShape.NonColumn;

            if (column.Column.HasScalarConverter)
                return QueryPlanAggregateSelectorShape.ConverterBackedColumn;

            return IsNumericType(column.ClrType)
                ? QueryPlanAggregateSelectorShape.DirectNumericColumn
                : QueryPlanAggregateSelectorShape.NonNumericColumn;
        }

        private QueryPlanPagingCountShape GetPagingCountShape(QueryPlanValue count)
        {
            try
            {
                if (!TryGetScalarValue(count, out var value))
                    return QueryPlanPagingCountShape.Invalid;
                if (value is null)
                    return QueryPlanPagingCountShape.Null;

                return Convert.ToInt32(value, CultureInfo.InvariantCulture) < 0
                    ? QueryPlanPagingCountShape.Negative
                    : QueryPlanPagingCountShape.NonNegative;
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                FormatException or
                InvalidCastException or
                OverflowException)
            {
                return QueryPlanPagingCountShape.Invalid;
            }
        }

        private bool TryGetScalarValue(QueryPlanValue value, out object? scalarValue)
        {
            switch (value)
            {
                case QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.Null }:
                    scalarValue = null;
                    return true;
                case QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.BooleanTrue }:
                    scalarValue = true;
                    return true;
                case QueryPlanIntrinsicValue { Intrinsic: QueryPlanIntrinsicKind.BooleanFalse }:
                    scalarValue = false;
                    return true;
                case QueryPlanScalarBindingReference scalar
                    when invocation.Values.TryGet(scalar.BindingId, out var binding) &&
                         binding is QueryPlanInvocationValue.Scalar invocationScalar:
                    scalarValue = invocationScalar.Value;
                    return true;
                case QueryPlanConvertedValue converted when TryGetScalarValue(converted.Value, out var sourceValue):
                    if (sourceValue is null)
                    {
                        scalarValue = null;
                        return true;
                    }

                    var targetType = Nullable.GetUnderlyingType(converted.TargetType) ?? converted.TargetType;
                    scalarValue = targetType.IsInstanceOfType(sourceValue)
                        ? sourceValue
                        : Convert.ChangeType(sourceValue, targetType, CultureInfo.InvariantCulture);
                    return true;
                default:
                    scalarValue = null;
                    return false;
            }
        }

        private static bool IsNumericType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsEnum)
                return false;

            return Type.GetTypeCode(type) is
                TypeCode.Byte or
                TypeCode.SByte or
                TypeCode.Int16 or
                TypeCode.UInt16 or
                TypeCode.Int32 or
                TypeCode.UInt32 or
                TypeCode.Int64 or
                TypeCode.UInt64 or
                TypeCode.Single or
                TypeCode.Double or
                TypeCode.Decimal;
        }

        private static bool IsBooleanPredicateFunction(QueryPlanFunctionKind function) =>
            function is QueryPlanFunctionKind.StringStartsWith or
                QueryPlanFunctionKind.StringEndsWith or
                QueryPlanFunctionKind.StringContains or
                QueryPlanFunctionKind.StringIsNullOrEmpty or
                QueryPlanFunctionKind.StringIsNullOrWhiteSpace;

        private static bool IsScalarBooleanValue(QueryPlanValue value)
        {
            if (value.ClrType != typeof(bool))
                return false;

            return value switch
            {
                QueryPlanIntrinsicValue
                {
                    Intrinsic: QueryPlanIntrinsicKind.BooleanTrue or QueryPlanIntrinsicKind.BooleanFalse
                } => true,
                QueryPlanScalarBindingReference => true,
                QueryPlanConvertedValue converted => IsScalarSqlValue(converted.Value),
                _ => false
            };
        }

        private static bool IsScalarSqlValue(QueryPlanValue value) => value switch
        {
            QueryPlanIntrinsicValue => true,
            QueryPlanScalarBindingReference => true,
            QueryPlanConvertedValue converted => IsScalarSqlValue(converted.Value),
            _ => false
        };

        private static bool HasDirectColumnSqlRowMembers(QueryPlanProjection projection) =>
            projection is QueryPlanProjection.SqlRow sqlRow &&
            sqlRow.Members.All(static member => UnwrapConvertedValue(member.Value) is QueryPlanColumnValue);

        private static QueryPlanValue UnwrapConvertedValue(QueryPlanValue value)
        {
            while (value is QueryPlanConvertedValue converted)
                value = converted.Value;

            return value;
        }

        private static string? FindSourceId(QueryPlanOperation operation) => operation switch
        {
            QueryPlanOperation.Where where => FindSourceId(where.Predicate),
            QueryPlanOperation.Having having => FindSourceId(having.Predicate),
            QueryPlanOperation.OrderBy orderBy => FindSourceId(orderBy.Orderings),
            QueryPlanOperation.Skip skip => FindSourceId(skip.Count),
            QueryPlanOperation.Take take => FindSourceId(take.Count),
            QueryPlanOperation.Join join => join.JoinShape.LeftSource.Id,
            QueryPlanOperation.Pushdown pushdown =>
                FindSourceId(pushdown.Operations) ?? FindSourceId(pushdown.PreservedOrderings),
            QueryPlanOperation.GroupBy groupBy => FindSourceId(groupBy.Keys),
            _ => null
        };

        private static string? FindSourceId(QueryPlanPredicate predicate) => predicate switch
        {
            QueryPlanPredicate.And and => FindSourceId(and.Terms),
            QueryPlanPredicate.Or or => FindSourceId(or.Terms),
            QueryPlanPredicate.Not not => FindSourceId(not.Predicate),
            QueryPlanPredicate.Compare compare => FindSourceId(compare.Left) ?? FindSourceId(compare.Right),
            QueryPlanPredicate.In inPredicate => FindSourceId(inPredicate.Item),
            QueryPlanPredicate.Exists exists => exists.ParentSource.Id,
            _ => null
        };

        private static string? FindSourceId(QueryPlanValue value) => value switch
        {
            QueryPlanColumnValue column => column.Source.Id,
            QueryPlanFunctionValue function => FindSourceId(function.Arguments),
            QueryPlanConvertedValue converted => FindSourceId(converted.Value),
            QueryPlanGroupKeyValue groupKey => FindSourceId(groupKey.Key),
            QueryPlanGroupedAggregateValue { Selector: not null } aggregate => FindSourceId(aggregate.Selector),
            _ => null
        };

        private static string? FindSourceId(QueryPlanProjectionRecipe recipe) => recipe switch
        {
            QueryPlanProjectionRecipe.Source source => source.SourceSlot.Id,
            QueryPlanProjectionRecipe.SourceColumn sourceColumn => sourceColumn.SourceSlot.Id,
            QueryPlanProjectionRecipe.Convert convert => FindSourceId(convert.Operand),
            QueryPlanProjectionRecipe.Not not => FindSourceId(not.Operand),
            QueryPlanProjectionRecipe.Binary binary => FindSourceId(binary.Left) ?? FindSourceId(binary.Right),
            QueryPlanProjectionRecipe.SupportedMember member => FindSourceId(member.Instance),
            QueryPlanProjectionRecipe.Function function => FindSourceId(function.Arguments),
            QueryPlanProjectionRecipe.Conditional conditional =>
                FindSourceId(conditional.Test) ??
                FindSourceId(conditional.IfTrue) ??
                FindSourceId(conditional.IfFalse),
            QueryPlanProjectionRecipe.NewArray newArray => FindSourceId(newArray.Elements),
            QueryPlanProjectionRecipe.CompatibilityConstructor constructor => FindSourceId(constructor.Arguments),
            QueryPlanProjectionRecipe.CompatibilityMember { Instance: not null } member => FindSourceId(member.Instance),
            _ => null
        };

        private static string? FindSourceId(IEnumerable<QueryPlanOperation> operations)
        {
            foreach (var operation in operations)
            {
                if (FindSourceId(operation) is { } sourceId)
                    return sourceId;
            }

            return null;
        }

        private static string? FindSourceId(IEnumerable<QueryPlanPredicate> predicates)
        {
            foreach (var predicate in predicates)
            {
                if (FindSourceId(predicate) is { } sourceId)
                    return sourceId;
            }

            return null;
        }

        private static string? FindSourceId(IEnumerable<QueryPlanOrdering> orderings)
        {
            foreach (var ordering in orderings)
            {
                if (FindSourceId(ordering.Value) is { } sourceId)
                    return sourceId;
            }

            return null;
        }

        private static string? FindSourceId(IEnumerable<QueryPlanValue> values)
        {
            foreach (var value in values)
            {
                if (FindSourceId(value) is { } sourceId)
                    return sourceId;
            }

            return null;
        }

        private static string? FindSourceId(IEnumerable<QueryPlanProjectionRecipe> recipes)
        {
            foreach (var recipe in recipes)
            {
                if (FindSourceId(recipe) is { } sourceId)
                    return sourceId;
            }

            return null;
        }

        private void AddStructural(
            QueryPlanFeature feature,
            string location,
            string? sourceId = null,
            string? columnName = null) =>
            structural.Add(new QueryPlanRequirement(feature, location, sourceId, columnName));

        private void AddInvocation(
            QueryPlanFeature feature,
            string location,
            string? sourceId = null) =>
            invocationRequirements.Add(new QueryPlanRequirement(feature, location, sourceId));
    }
}
