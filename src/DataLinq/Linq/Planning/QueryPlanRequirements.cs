using System;
using System.Collections.Generic;
using System.Globalization;

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
    private readonly QueryPlanInvocation invocation;
    private readonly QueryPlanFeature[] invocationFeatures;
    private IReadOnlyList<QueryPlanRequirement>? structuralDiagnostics;
    private IReadOnlyList<QueryPlanRequirement>? invocationDiagnostics;

    private QueryPlanRequirements(QueryPlanInvocation invocation)
    {
        this.invocation = invocation;
        invocationFeatures = InvocationRequirementExtractor.ExtractFeatures(invocation);
    }

    public int StructuralCount => invocation.Template.StructuralRequirementFeatures.Length;

    public int InvocationCount => invocationFeatures.Length;

    // Successful validation consumes the compact feature spans below. Detailed paths are
    // reconstructed only for diagnostics or explicit requirement introspection.
    public IReadOnlyList<QueryPlanRequirement> Structural =>
        structuralDiagnostics ??= StructuralExtractor.ExtractDiagnostics(invocation.Template);

    public IReadOnlyList<QueryPlanRequirement> Invocation =>
        invocationDiagnostics ??= InvocationRequirementExtractor.ExtractDiagnostics(invocation);

    internal ReadOnlySpan<QueryPlanFeature> StructuralFeatures =>
        invocation.Template.StructuralRequirementFeatures;

    internal ReadOnlySpan<QueryPlanFeature> InvocationFeatures => invocationFeatures;

    public static QueryPlanRequirements Extract(QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new QueryPlanRequirements(invocation);
    }

    internal static QueryPlanFeature[] ExtractStructuralFeatures(QueryPlanTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return StructuralExtractor.ExtractFeatures(template);
    }

    internal static IReadOnlyList<QueryPlanRequirement> ExtractStructuralDiagnostics(
        QueryPlanTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return StructuralExtractor.ExtractDiagnostics(template);
    }

    internal static int FindFirstUnsupportedInvocationFeature(
        QueryPlanInvocation invocation,
        QueryBackendCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(capabilities);
        return InvocationRequirementExtractor.FindFirstUnsupportedFeature(invocation, capabilities);
    }

    internal static IReadOnlyList<QueryPlanRequirement> ExtractInvocationDiagnostics(
        QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return InvocationRequirementExtractor.ExtractDiagnostics(invocation);
    }

    private sealed class StructuralExtractor
    {
        private readonly QueryPlanTemplate template;
        private readonly List<QueryPlanFeature>? features;
        private readonly List<QueryPlanRequirement>? diagnostics;

        private StructuralExtractor(QueryPlanTemplate template, bool includeDiagnostics)
        {
            this.template = template;
            if (includeDiagnostics)
                diagnostics = [];
            else
                features = [];
        }

        public static QueryPlanFeature[] ExtractFeatures(QueryPlanTemplate template)
        {
            var extractor = new StructuralExtractor(template, includeDiagnostics: false);
            extractor.Extract();
            return extractor.features!.ToArray();
        }

        public static IReadOnlyList<QueryPlanRequirement> ExtractDiagnostics(QueryPlanTemplate template)
        {
            var extractor = new StructuralExtractor(template, includeDiagnostics: true);
            extractor.Extract();
            return Array.AsReadOnly(extractor.diagnostics!.ToArray());
        }

        private void Extract()
        {
            AddStructural(
                QueryPlanFeature.SourceCount(template.Sources.Count == 1
                    ? QueryPlanSourceCountKind.Single
                    : QueryPlanSourceCountKind.Multiple),
                Root("sources"));
            var rootSourceCount = 0;
            for (var index = 0; index < template.Sources.Count; index++)
            {
                if (template.Sources[index].Kind == QueryPlanSourceKind.RootTable)
                    rootSourceCount++;
            }
            AddStructural(
                QueryPlanFeature.SourceTopology(rootSourceCount switch
                {
                    0 => QueryPlanSourceTopology.NoRoot,
                    1 => QueryPlanSourceTopology.ExactlyOneRoot,
                    _ => QueryPlanSourceTopology.MultipleRoots
                }),
                Root("sources"));

            for (var index = 0; index < template.Sources.Count; index++)
            {
                var source = template.Sources[index];
                var location = Indexed(Root("sources"), index);
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
                Root("operations"),
                template.Projection.Kind,
                HasDirectColumnSqlRowMembers(template.Projection),
                template.Sources[0].Id);

            VisitProjection(template.Projection, template.Sources, Root("projection"));
            VisitResult(
                template.Result,
                template.Operations,
                Root("result"),
                template.Sources[0].Id);

            for (var index = 0; index < template.BindingDeclarations.Count; index++)
            {
                var declaration = template.BindingDeclarations[index];
                AddStructural(
                    QueryPlanFeature.BindingKind(declaration.Kind),
                    Indexed(Root("bindings"), index));
            }
        }

        private void VisitOperations(
            IReadOnlyList<QueryPlanOperation> operations,
            string? location,
            QueryPlanProjectionKind projectionKind,
            bool sqlRowHasDirectColumnMembers,
            string defaultSourceId)
        {
            QueryPlanOperation.OrderBy? firstOrderBy = null;
            var hasPaging = false;
            for (var index = 0; index < operations.Count; index++)
            {
                firstOrderBy ??= operations[index] as QueryPlanOperation.OrderBy;
                hasPaging |= operations[index] is QueryPlanOperation.Skip or QueryPlanOperation.Take;
            }

            if (firstOrderBy is not null)
            {
                var firstOrdering = firstOrderBy.Orderings[0];
                AddStructural(
                    QueryPlanFeature.OrderingShape(QueryPlanOrderingShapeFacts.Classify(operations, defaultSourceId)),
                    Child(location, ".ordering.shape"),
                    FindSourceId(firstOrdering.Value) ?? defaultSourceId,
                    firstOrdering.Value is QueryPlanColumnValue column ? column.Column.DbName : null);
            }

            if (hasPaging)
            {
                AddStructural(
                    QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShapeFacts.Classify(operations)),
                    Child(location, ".pagingComposition.shape"),
                    defaultSourceId);
            }

            var hasSeenPushdown = false;
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                VisitOperation(
                    operation,
                    Indexed(location, index),
                    projectionKind,
                    sqlRowHasDirectColumnMembers,
                    defaultSourceId,
                    hasSeenPushdown && operation is QueryPlanOperation.Pushdown);

                hasSeenPushdown |= operation is QueryPlanOperation.Pushdown;
            }
        }

        private void VisitOperation(
            QueryPlanOperation operation,
            string? location,
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
                    VisitPredicate(where.Predicate, Child(location, ".predicate"), sourceId);
                    break;
                case QueryPlanOperation.Having having:
                    VisitPredicate(having.Predicate, Child(location, ".predicate"), sourceId);
                    break;
                case QueryPlanOperation.OrderBy orderBy:
                    VisitOrderings(orderBy.Orderings, Child(location, ".orderings"), sourceId);
                    break;
                case QueryPlanOperation.Skip skip:
                    VisitValue(skip.Count, QueryPlanValueUse.PagingCount, Child(location, ".count"), sourceId);
                    break;
                case QueryPlanOperation.Take take:
                    VisitValue(take.Count, QueryPlanValueUse.PagingCount, Child(location, ".count"), sourceId);
                    break;
                case QueryPlanOperation.Join join:
                    AddStructural(
                        QueryPlanFeature.JoinKind(join.JoinShape.Kind),
                        Child(location, ".join.kind"),
                        join.JoinShape.LeftSource.Id);
                    AddStructural(
                        QueryPlanFeature.JoinRightSourceKind(join.JoinShape.RightSource.Kind),
                        Child(location, ".join.right-source-kind"),
                        join.JoinShape.RightSource.Id);
                    VisitValue(
                        new QueryPlanColumnValue(join.JoinShape.LeftSource, join.JoinShape.LeftColumn),
                        QueryPlanValueUse.JoinKey,
                        Child(location, ".join.left"));
                    VisitValue(
                        new QueryPlanColumnValue(join.JoinShape.RightSource, join.JoinShape.RightColumn),
                        QueryPlanValueUse.JoinKey,
                        Child(location, ".join.right"));
                    break;
                case QueryPlanOperation.Pushdown pushdown:
                    var containsJoin = false;
                    for (var index = 0; index < pushdown.Operations.Count; index++)
                        containsJoin |= pushdown.Operations[index] is QueryPlanOperation.Join;
                    var shape = repeatedPushdown
                        ? QueryPlanPushdownShape.RepeatedInScope
                        : containsJoin
                            ? projectionKind == QueryPlanProjectionKind.SqlRow
                                ? sqlRowHasDirectColumnMembers
                                    ? QueryPlanPushdownShape.JoinedSqlRowDirectColumns
                                    : QueryPlanPushdownShape.JoinedSqlRowNonColumn
                                : QueryPlanPushdownShape.JoinedNonSqlRow
                            : QueryPlanPushdownShape.Simple;
                    AddStructural(QueryPlanFeature.PushdownShape(shape), Child(location, ".shape"), sourceId);

                    var innerProjectionKind = containsJoin && projectionKind == QueryPlanProjectionKind.SqlRow
                        ? QueryPlanProjectionKind.SqlRow
                        : QueryPlanProjectionKind.Entity;
                    VisitOperations(
                        pushdown.Operations,
                        Child(location, ".operations"),
                        innerProjectionKind,
                        containsJoin && projectionKind == QueryPlanProjectionKind.SqlRow && sqlRowHasDirectColumnMembers,
                        sourceId);
                    VisitOrderings(pushdown.PreservedOrderings, Child(location, ".preservedOrderings"), sourceId);
                    break;
                case QueryPlanOperation.GroupBy groupBy:
                    for (var index = 0; index < groupBy.Keys.Count; index++)
                        VisitValue(groupBy.Keys[index], QueryPlanValueUse.GroupingKey, Indexed(Child(location, ".keys"), index), sourceId);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan operation '{operation.GetType().Name}'.", nameof(operation));
            }
        }

        private void VisitOrderings(
            IReadOnlyList<QueryPlanOrdering> orderings,
            string? location,
            string defaultSourceId)
        {
            for (var index = 0; index < orderings.Count; index++)
            {
                var ordering = orderings[index];
                var sourceId = FindSourceId(ordering.Value) ?? defaultSourceId;
                AddStructural(
                    QueryPlanFeature.OrderingDirection(ordering.Direction),
                    Indexed(location, index, ".direction"),
                    sourceId);
                VisitValue(ordering.Value, QueryPlanValueUse.Ordering, Indexed(location, index, ".value"), sourceId);
            }
        }

        private void VisitPredicate(
            QueryPlanPredicate predicate,
            string? location,
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
                        VisitPredicate(and.Terms[index], Indexed(Child(location, ".terms"), index), sourceId);
                    break;
                case QueryPlanPredicate.Or or:
                    for (var index = 0; index < or.Terms.Count; index++)
                        VisitPredicate(or.Terms[index], Indexed(Child(location, ".terms"), index), sourceId);
                    break;
                case QueryPlanPredicate.Not not:
                    VisitPredicate(not.Predicate, Child(location, ".predicate"), sourceId);
                    break;
                case QueryPlanPredicate.Compare compare:
                    AddStructural(QueryPlanFeature.ComparisonOperator(compare.Operator), Child(location, ".operator"), sourceId);
                    AddStructural(QueryPlanFeature.NullSemantics(compare.NullSemantics), Child(location, ".nullSemantics"), sourceId);
                    AddStructural(
                        QueryPlanFeature.ComparisonShape(GetComparisonShape(compare)),
                        Child(location, ".shape"),
                        sourceId);
                    VisitValue(
                        compare.Left,
                        GetComparisonValueUse(compare, compare.Left, compare.Right),
                        Child(location, ".left"),
                        sourceId);
                    VisitValue(
                        compare.Right,
                        GetComparisonValueUse(compare, compare.Right, compare.Left),
                        Child(location, ".right"),
                        sourceId);
                    break;
                case QueryPlanPredicate.In inPredicate:
                    AddStructural(
                        QueryPlanFeature.PredicatePolarity(inPredicate.IsNegated
                            ? QueryPlanPredicatePolarity.Negated
                            : QueryPlanPredicatePolarity.Positive),
                        Child(location, ".polarity"),
                        sourceId);
                    AddStructural(
                        QueryPlanFeature.MembershipShape(
                            QueryPlanMembershipShapeFacts.IsDirectNonNullableInt32ColumnAndLocalSequence(
                                inPredicate.Item,
                                inPredicate.Sequence,
                                template.BindingDeclarations)
                                ? QueryPlanMembershipShape.DirectNonNullableInt32ColumnAndLocalSequence
                                : QueryPlanMembershipShape.Other),
                        Child(location, ".shape"),
                        sourceId,
                        inPredicate.Item is QueryPlanColumnValue membershipColumn
                            ? membershipColumn.Column.DbName
                            : null);
                    VisitValue(inPredicate.Item, QueryPlanValueUse.MembershipItem, Child(location, ".item"), sourceId);
                    VisitValue(inPredicate.Sequence, QueryPlanValueUse.MembershipSequence, Child(location, ".sequence"), sourceId);
                    break;
                case QueryPlanPredicate.Exists exists:
                    AddStructural(
                        QueryPlanFeature.PredicatePolarity(exists.IsNegated
                            ? QueryPlanPredicatePolarity.Negated
                            : QueryPlanPredicatePolarity.Positive),
                        Child(location, ".polarity"),
                        sourceId);
                    AddStructural(
                        QueryPlanFeature.RelationPart(exists.Relation.RelationPart.Type),
                        Child(location, ".relation"),
                        exists.ParentSource.Id);
                    if (exists.Predicate is not null)
                        VisitPredicate(exists.Predicate, Child(location, ".predicate"), exists.ChildSource.Id);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan predicate '{predicate.GetType().Name}'.", nameof(predicate));
            }
        }

        private void VisitValue(
            QueryPlanValue value,
            QueryPlanValueUse use,
            string? location,
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
                        Child(location, ".shape"),
                        sourceId);
                    VisitFunctionArguments(function, location, sourceId);
                    break;
                case QueryPlanConvertedValue converted:
                    VisitValue(converted.Value, use, Child(location, ".value"), sourceId);
                    break;
                case QueryPlanGroupKeyValue groupKey:
                    VisitValue(groupKey.Key, QueryPlanValueUse.GroupingKey, Child(location, ".key"), sourceId);
                    break;
                case QueryPlanGroupedAggregateValue aggregate:
                    AddStructural(QueryPlanFeature.GroupedAggregate(aggregate.Aggregate, use), location, sourceId);
                    if (aggregate.Selector is not null)
                        VisitAggregateSelector(aggregate.Selector, Child(location, ".selector"), sourceId);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan value '{value.GetType().Name}'.", nameof(value));
            }
        }

        private void VisitFunctionArguments(
            QueryPlanFunctionValue function,
            string? location,
            string? defaultSourceId)
        {
            for (var index = 0; index < function.Arguments.Count; index++)
            {
                var use = IsScalarFunctionArgument(function.Function, index)
                    ? QueryPlanValueUse.ScalarFunctionArgument
                    : QueryPlanValueUse.FunctionSource;
                VisitValue(function.Arguments[index], use, Indexed(Child(location, ".arguments"), index), defaultSourceId);
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

        private void VisitProjection(
            QueryPlanProjection projection,
            IReadOnlyList<QueryPlanSourceSlot> sources,
            string? location)
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
                Child(location, ".disposition"),
                sourceId,
                columnName);

            switch (projection)
            {
                case QueryPlanProjection.Entity:
                    break;
                case QueryPlanProjection.ScalarMember scalar:
                    AddStructural(
                        QueryPlanFeature.ScalarProjectionShape(
                            QueryPlanScalarProjectionShapeFacts.Classify(scalar, sources)),
                        Child(location, ".scalar.shape"),
                        scalar.Source.Id,
                        scalar.Column.DbName);
                    VisitValue(new QueryPlanColumnValue(scalar.Source, scalar.Column, scalar.ResultType), QueryPlanValueUse.ProjectionMember, Child(location, ".member"));
                    break;
                case QueryPlanProjection.Anonymous anonymous:
                    VisitProjectionMembers(anonymous.Members, Child(location, ".members"));
                    VisitRecipe(anonymous.Recipe, Child(location, ".recipe"));
                    break;
                case QueryPlanProjection.ComputedRowLocal computed:
                    VisitRecipe(computed.Recipe, Child(location, ".recipe"));
                    break;
                case QueryPlanProjection.JoinedRowLocal joined:
                    VisitProjectionMembers(joined.Members, Child(location, ".members"));
                    VisitRecipe(joined.Recipe, Child(location, ".recipe"));
                    break;
                case QueryPlanProjection.SqlRow sqlRow:
                    VisitProjectionMembers(sqlRow.Members, Child(location, ".members"));
                    break;
                case QueryPlanProjection.TransparentIdentifier:
                    break;
                case QueryPlanProjection.GroupedAggregate grouped:
                    VisitProjectionMembers(
                        grouped.Members,
                        QueryPlanValueUse.GroupedProjectionMember,
                        Child(location, ".members"),
                        grouped.Source.Id);
                    break;
                default:
                    throw new ArgumentException($"Unknown query plan projection '{projection.GetType().Name}'.", nameof(projection));
            }
        }

        private void VisitProjectionMembers(IReadOnlyList<QueryPlanProjectionMember> members, string? location)
            => VisitProjectionMembers(members, QueryPlanValueUse.ProjectionMember, location, defaultSourceId: null);

        private void VisitProjectionMembers(
            IReadOnlyList<QueryPlanProjectionMember> members,
            QueryPlanValueUse use,
            string? location,
            string? defaultSourceId)
        {
            for (var index = 0; index < members.Count; index++)
                VisitValue(members[index].Value, use, Indexed(location, index, ".value"), defaultSourceId);
        }

        private void VisitRecipe(QueryPlanProjectionRecipe recipe, string? location)
        {
            var sourceId = FindSourceId(recipe);
            var columnName = recipe is QueryPlanProjectionRecipe.SourceColumn sourceColumnRecipe
                ? sourceColumnRecipe.Column.DbName
                : null;

            AddStructural(QueryPlanFeature.ProjectionRecipe(recipe.Kind), location, sourceId, columnName);
            AddStructural(
                QueryPlanFeature.ProjectionDisposition(recipe.Disposition),
                Child(location, ".disposition"),
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
                    VisitRecipe(convert.Operand, Child(location, ".operand"));
                    break;
                case QueryPlanProjectionRecipe.Not not:
                    VisitRecipe(not.Operand, Child(location, ".operand"));
                    break;
                case QueryPlanProjectionRecipe.Binary binary:
                    AddStructural(QueryPlanFeature.ProjectionBinaryOperator(binary.Operator), Child(location, ".operator"), sourceId);
                    VisitRecipe(binary.Left, Child(location, ".left"));
                    VisitRecipe(binary.Right, Child(location, ".right"));
                    break;
                case QueryPlanProjectionRecipe.SupportedMember member:
                    AddStructural(QueryPlanFeature.ProjectionSupportedMember(member.Member), Child(location, ".member"), sourceId);
                    VisitRecipe(member.Instance, Child(location, ".instance"));
                    break;
                case QueryPlanProjectionRecipe.Function function:
                    AddStructural(QueryPlanFeature.ProjectionFunction(function.FunctionKind), Child(location, ".function"), sourceId);
                    VisitRecipes(function.Arguments, Child(location, ".arguments"));
                    break;
                case QueryPlanProjectionRecipe.Conditional conditional:
                    VisitRecipe(conditional.Test, Child(location, ".test"));
                    VisitRecipe(conditional.IfTrue, Child(location, ".ifTrue"));
                    VisitRecipe(conditional.IfFalse, Child(location, ".ifFalse"));
                    break;
                case QueryPlanProjectionRecipe.NewArray newArray:
                    VisitRecipes(newArray.Elements, Child(location, ".elements"));
                    break;
                case QueryPlanProjectionRecipe.CompatibilityConstructor constructor:
                    VisitRecipes(constructor.Arguments, Child(location, ".arguments"));
                    break;
                case QueryPlanProjectionRecipe.CompatibilityMember member when member.Instance is not null:
                    VisitRecipe(member.Instance, Child(location, ".instance"));
                    break;
                case QueryPlanProjectionRecipe.CompatibilityMember:
                    break;
                default:
                    throw new ArgumentException($"Unknown projection recipe '{recipe.GetType().Name}'.", nameof(recipe));
            }
        }

        private void VisitRecipes(IReadOnlyList<QueryPlanProjectionRecipe> recipes, string? location)
        {
            for (var index = 0; index < recipes.Count; index++)
                VisitRecipe(recipes[index], Indexed(location, index));
        }

        private void VisitResult(
            QueryPlanResult result,
            IReadOnlyList<QueryPlanOperation> operations,
            string? location,
            string defaultSourceId)
        {
            if (result.Kind is QueryPlanResultKind.First or QueryPlanResultKind.FirstOrDefault)
            {
                AddStructural(
                    QueryPlanFeature.ResultCompositionShape(
                        QueryPlanResultCompositionShapeFacts.Classify(operations)),
                    Child(location, ".composition.shape"),
                    defaultSourceId);
            }

            AddStructural(
                QueryPlanFeature.Result(result.Kind),
                location,
                result.AggregateSelector is null
                    ? defaultSourceId
                    : FindSourceId(result.AggregateSelector) ?? defaultSourceId);
            if (result.AggregateSelector is not null)
                VisitAggregateSelector(result.AggregateSelector, Child(location, ".selector"), defaultSourceId);
        }

        private void VisitAggregateSelector(
            QueryPlanValue selector,
            string? location,
            string? defaultSourceId)
        {
            var unwrapped = UnwrapConvertedValue(selector);
            var sourceId = FindSourceId(unwrapped) ?? defaultSourceId;
            var columnName = unwrapped is QueryPlanColumnValue column ? column.Column.DbName : null;
            AddStructural(
                QueryPlanFeature.AggregateSelectorShape(GetAggregateSelectorShape(unwrapped)),
                Child(location, ".shape"),
                sourceId,
                columnName);
            VisitValue(selector, QueryPlanValueUse.AggregateSelector, location, sourceId);
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
            {
                if (QueryPlanComparisonShapeFacts.IsDirectNonNullableInt32ColumnAndScalar(
                        comparison.Left,
                        comparison.Right,
                        template.BindingDeclarations))
                {
                    return QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar;
                }

                if (comparison.Operator is (
                        QueryPlanComparisonOperator.Equal or
                        QueryPlanComparisonOperator.NotEqual) &&
                    QueryPlanComparisonShapeFacts.IsNonNullableCanonicalGuidColumnAndScalar(
                        comparison.Left,
                        comparison.Right,
                        template.BindingDeclarations))
                {
                    return QueryPlanComparisonShape.NonNullableCanonicalGuidColumnAndScalar;
                }

                return QueryPlanComparisonShape.DefaultNullSemantics;
            }

            if (comparison.NullSemantics == QueryPlanNullSemantics.CSharpNullableComparison)
            {
                if (TryGetColumn(comparison.Left, out var leftColumn) &&
                    TryGetColumn(comparison.Right, out var rightColumn) &&
                    IsSupportedCSharpNullableOperator(comparison.Operator, leftColumn, rightColumn))
                {
                    return QueryPlanComparisonShape.CSharpNullableColumnToColumn;
                }

                if ((TryGetColumn(comparison.Left, out var column) && IsScalarValue(comparison.Right) ||
                     TryGetColumn(comparison.Right, out column) && IsScalarValue(comparison.Left)) &&
                    IsSupportedCSharpNullableOperator(comparison.Operator, column, rightColumn: null))
                {
                    return QueryPlanComparisonShape.CSharpNullableColumnAndScalar;
                }
            }

            return QueryPlanComparisonShape.UnsupportedCSharpNullableComparison;
        }

        private static bool TryGetColumn(QueryPlanValue value, out QueryPlanColumnValue column)
        {
            switch (value)
            {
                case QueryPlanColumnValue direct:
                    column = direct;
                    return true;
                case QueryPlanConvertedValue converted:
                    return TryGetColumn(converted.Value, out column);
                case QueryPlanGroupKeyValue groupKey:
                    return TryGetColumn(groupKey.Key, out column);
                default:
                    column = null!;
                    return false;
            }
        }

        private static bool IsScalarValue(QueryPlanValue value) => value switch
        {
            QueryPlanIntrinsicValue => true,
            QueryPlanScalarBindingReference => true,
            QueryPlanConvertedValue converted => IsScalarValue(converted.Value),
            _ => false
        };

        private static bool IsSupportedCSharpNullableOperator(
            QueryPlanComparisonOperator comparisonOperator,
            QueryPlanColumnValue leftColumn,
            QueryPlanColumnValue? rightColumn)
        {
            var leftAllowsNull = leftColumn.Column.ValueProperty.CsNullable;
            var rightAllowsNull = rightColumn?.Column.ValueProperty.CsNullable == true;
            return comparisonOperator switch
            {
                QueryPlanComparisonOperator.Equal => leftAllowsNull && rightAllowsNull,
                QueryPlanComparisonOperator.NotEqual or
                QueryPlanComparisonOperator.GreaterThan or
                QueryPlanComparisonOperator.GreaterThanOrEqual or
                QueryPlanComparisonOperator.LessThan or
                QueryPlanComparisonOperator.LessThanOrEqual => leftAllowsNull || rightAllowsNull,
                _ => false
            };
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

        private static bool HasDirectColumnSqlRowMembers(QueryPlanProjection projection)
        {
            if (projection is not QueryPlanProjection.SqlRow sqlRow)
                return false;

            for (var index = 0; index < sqlRow.Members.Count; index++)
            {
                if (UnwrapConvertedValue(sqlRow.Members[index].Value) is not QueryPlanColumnValue)
                    return false;
            }

            return true;
        }

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

        private static string? FindSourceId(IReadOnlyList<QueryPlanOperation> operations)
        {
            for (var index = 0; index < operations.Count; index++)
            {
                if (FindSourceId(operations[index]) is { } sourceId)
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

        private static string? FindSourceId(IReadOnlyList<QueryPlanValue> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (FindSourceId(values[index]) is { } sourceId)
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
            string? location,
            string? sourceId = null,
            string? columnName = null)
        {
            if (features is not null)
            {
                features.Add(feature);
                return;
            }

            diagnostics!.Add(new QueryPlanRequirement(
                feature,
                location ?? throw new InvalidOperationException("Structural diagnostic location was not captured."),
                sourceId,
                columnName));
        }

        private string? Root(string value) => diagnostics is null ? null : value;

        private static string? Child(string? location, string suffix) =>
            location is null ? null : string.Concat(location, suffix);

        private static string? Indexed(string? location, int index, string suffix = "") =>
            location is null
                ? null
                : string.Concat(
                    location,
                    "[",
                    index.ToString(CultureInfo.InvariantCulture),
                    "]",
                    suffix);
    }

    private struct InvocationRequirementExtractor
    {
        private readonly QueryPlanInvocation invocation;
        private readonly QueryPlanFeature[]? features;
        private readonly List<QueryPlanRequirement>? diagnostics;
        private readonly QueryBackendCapabilities? validationCapabilities;
        private int nextFeature;
        private int firstUnsupportedFeature;

        private InvocationRequirementExtractor(QueryPlanInvocation invocation, bool includeDiagnostics)
        {
            this.invocation = invocation;
            features = null;
            diagnostics = null;
            validationCapabilities = null;
            nextFeature = 0;
            firstUnsupportedFeature = -1;
            if (includeDiagnostics)
                diagnostics = [];
            else
            {
                var count = Count(invocation);
                features = count == 0
                    ? Array.Empty<QueryPlanFeature>()
                    : new QueryPlanFeature[count];
            }
        }

        private InvocationRequirementExtractor(
            QueryPlanInvocation invocation,
            QueryBackendCapabilities validationCapabilities)
        {
            this.invocation = invocation;
            features = null;
            diagnostics = null;
            this.validationCapabilities = validationCapabilities;
            nextFeature = 0;
            firstUnsupportedFeature = -1;
        }

        public static QueryPlanFeature[] ExtractFeatures(QueryPlanInvocation invocation)
        {
            var extractor = new InvocationRequirementExtractor(invocation, includeDiagnostics: false);
            extractor.Extract();
            return extractor.features!;
        }

        public static IReadOnlyList<QueryPlanRequirement> ExtractDiagnostics(QueryPlanInvocation invocation)
        {
            var extractor = new InvocationRequirementExtractor(invocation, includeDiagnostics: true);
            extractor.Extract();
            return Array.AsReadOnly(extractor.diagnostics!.ToArray());
        }

        public static int FindFirstUnsupportedFeature(
            QueryPlanInvocation invocation,
            QueryBackendCapabilities capabilities)
        {
            var extractor = new InvocationRequirementExtractor(invocation, capabilities);
            extractor.Extract();
            return extractor.firstUnsupportedFeature;
        }

        private void Extract()
        {
            VisitOperations(
                invocation.Template.Operations,
                Root("operations"),
                invocation.Template.Sources[0].Id);

            for (var index = 0; index < invocation.Values.Count; index++)
            {
                VisitInvocationValue(
                    invocation.Values[index],
                    Indexed(Root("invocation.bindings"), index));
            }

            if (features is not null && nextFeature != features.Length)
            {
                throw new InvalidOperationException(
                    $"Invocation requirement extraction expected {features.Length} features but captured {nextFeature}.");
            }
        }

        private void VisitOperations(
            IReadOnlyList<QueryPlanOperation> operations,
            string? location,
            string defaultSourceId)
        {
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index];
                var operationLocation = Indexed(location, index);
                var sourceId = FindSourceId(operation) ?? defaultSourceId;
                switch (operation)
                {
                    case QueryPlanOperation.Skip skip:
                        Add(
                            QueryPlanFeature.PagingCountShape(GetPagingCountShape(skip.Count)),
                            Child(operationLocation, ".count.shape"),
                            sourceId);
                        break;
                    case QueryPlanOperation.Take take:
                        Add(
                            QueryPlanFeature.PagingCountShape(GetPagingCountShape(take.Count)),
                            Child(operationLocation, ".count.shape"),
                            sourceId);
                        break;
                    case QueryPlanOperation.Pushdown pushdown:
                        VisitOperations(
                            pushdown.Operations,
                            Child(operationLocation, ".operations"),
                            sourceId);
                        break;
                }
            }
        }

        private void VisitInvocationValue(QueryPlanInvocationValue value, string? location)
        {
            switch (value)
            {
                case QueryPlanInvocationValue.Scalar scalar:
                    Add(
                        QueryPlanFeature.ScalarNullness(
                            scalar.Value is null
                                ? QueryPlanBindingNullness.Null
                                : QueryPlanBindingNullness.NonNull),
                        location);
                    break;
                case QueryPlanInvocationValue.LocalSequence sequence:
                    var nullCount = 0;
                    for (var index = 0; index < sequence.Values.Count; index++)
                    {
                        if (sequence.Values[index] is null)
                            nullCount++;
                    }

                    var shape = sequence.Values.Count == 0
                        ? QueryPlanLocalSequenceShapeKind.Empty
                        : nullCount == 0
                            ? QueryPlanLocalSequenceShapeKind.NonEmptyWithoutNulls
                            : QueryPlanLocalSequenceShapeKind.NonEmptyWithNulls;
                    Add(
                        QueryPlanFeature.LocalSequenceShape(shape),
                        location,
                        count: sequence.Values.Count,
                        nullCount: nullCount);
                    break;
                default:
                    throw new ArgumentException($"Unknown invocation value '{value.GetType().Name}'.", nameof(value));
            }
        }

        private QueryPlanPagingCountShape GetPagingCountShape(QueryPlanValue count)
        {
            try
            {
                if (!TryGetScalarValue(count, out var value))
                    return QueryPlanPagingCountShape.Invalid;
                if (value is null)
                    return QueryPlanPagingCountShape.Null;

                if (value is int int32Value)
                {
                    if (int32Value < 0)
                        return QueryPlanPagingCountShape.Negative;

                    return QueryPlanExactInt32ValueShapeFacts.IsDirectNonNullableInt32ScalarBinding(
                        count,
                        invocation.Template.BindingDeclarations)
                            ? QueryPlanPagingCountShape.NonNegativeInt32ScalarBinding
                            : QueryPlanPagingCountShape.NonNegative;
                }

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

        private void Add(
            QueryPlanFeature feature,
            string? location,
            string? sourceId = null,
            int? count = null,
            int? nullCount = null)
        {
            if (features is not null)
            {
                features[nextFeature++] = feature;
                return;
            }

            if (diagnostics is null)
            {
                var featureIndex = nextFeature++;
                if (firstUnsupportedFeature < 0 &&
                    validationCapabilities!.GetDisposition(feature) !=
                        QueryBackendCapabilityDisposition.Supported)
                {
                    firstUnsupportedFeature = featureIndex;
                }

                return;
            }

            diagnostics.Add(new QueryPlanRequirement(
                feature,
                location ?? throw new InvalidOperationException("Invocation diagnostic location was not captured."),
                sourceId,
                Count: count,
                NullCount: nullCount));
        }

        private static int Count(QueryPlanInvocation invocation) =>
            invocation.Values.Count + CountPaging(invocation.Template.Operations);

        private static int CountPaging(IReadOnlyList<QueryPlanOperation> operations)
        {
            var count = 0;
            for (var index = 0; index < operations.Count; index++)
            {
                count += operations[index] switch
                {
                    QueryPlanOperation.Skip or QueryPlanOperation.Take => 1,
                    QueryPlanOperation.Pushdown pushdown => CountPaging(pushdown.Operations),
                    _ => 0
                };
            }

            return count;
        }

        private static string? FindSourceId(QueryPlanOperation operation) => operation switch
        {
            QueryPlanOperation.Skip skip => FindSourceId(skip.Count),
            QueryPlanOperation.Take take => FindSourceId(take.Count),
            QueryPlanOperation.Pushdown pushdown => FindSourceId(pushdown.Operations),
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

        private static string? FindSourceId(IReadOnlyList<QueryPlanOperation> operations)
        {
            for (var index = 0; index < operations.Count; index++)
            {
                if (FindSourceId(operations[index]) is { } sourceId)
                    return sourceId;
            }

            return null;
        }

        private static string? FindSourceId(IReadOnlyList<QueryPlanValue> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                if (FindSourceId(values[index]) is { } sourceId)
                    return sourceId;
            }

            return null;
        }

        private string? Root(string value) => diagnostics is null ? null : value;

        private static string? Child(string? location, string suffix) =>
            location is null ? null : string.Concat(location, suffix);

        private static string? Indexed(string? location, int index) =>
            location is null
                ? null
                : string.Concat(
                    location,
                    "[",
                    index.ToString(CultureInfo.InvariantCulture),
                    "]");
    }
}
