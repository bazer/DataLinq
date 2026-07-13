using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal enum QueryPlanFeatureCategory
{
    SourceCount,
    SourceTopology,
    SourceKind,
    SourceCardinality,
    SourceNullability,
    Operation,
    PushdownShape,
    OrderingDirection,
    JoinKind,
    JoinRightSourceKind,
    Predicate,
    PredicatePolarity,
    RelationPart,
    ComparisonOperator,
    NullSemantics,
    ComparisonShape,
    Value,
    Intrinsic,
    Function,
    FunctionShape,
    GroupedAggregate,
    AggregateSelectorShape,
    PagingCountShape,
    Projection,
    ProjectionDisposition,
    ProjectionRecipe,
    ProjectionIntrinsic,
    ProjectionBinaryOperator,
    ProjectionSupportedMember,
    ProjectionFunction,
    Result,
    BindingKind,
    ScalarNullness,
    LocalSequenceShape
}

internal enum QueryPlanSourceCountKind
{
    Single,
    Multiple
}

internal enum QueryPlanSourceTopology
{
    ExactlyOneRoot,
    NoRoot,
    MultipleRoots
}

internal enum QueryPlanSourceNullability
{
    NonNullable,
    Nullable
}

internal enum QueryPlanPredicatePolarity
{
    Positive,
    Negated
}

internal enum QueryPlanComparisonShape
{
    DefaultNullSemantics,
    NullableNotEqualColumnAndNullValue,
    NullableNotEqualColumnAndNonNullValue,
    UnsupportedNullableNotEqual
}

internal enum QueryPlanFunctionShape
{
    Unary,
    StringPredicateWithPattern,
    SubstringWithStart,
    SubstringWithStartAndLength
}

internal enum QueryPlanAggregateSelectorShape
{
    DirectNumericColumn,
    ConverterBackedColumn,
    NonNumericColumn,
    NonColumn
}

internal enum QueryPlanPagingCountShape
{
    NonNegative,
    Negative,
    Null,
    Invalid
}

internal enum QueryPlanPushdownShape
{
    Simple,
    JoinedSqlRowDirectColumns,
    JoinedSqlRowNonColumn,
    JoinedNonSqlRow,
    RepeatedInScope
}

internal enum QueryPlanValueUse
{
    PredicateOperand,
    BooleanPredicateFunction,
    MembershipItem,
    MembershipSequence,
    Ordering,
    PagingCount,
    JoinKey,
    GroupingKey,
    ProjectionMember,
    GroupedProjectionMember,
    AggregateSelector,
    FunctionSource,
    ScalarFunctionArgument
}

internal enum QueryPlanLocalSequenceShapeKind
{
    Empty,
    NonEmptyWithoutNulls,
    NonEmptyWithNulls
}

internal readonly record struct QueryPlanFeature(
    QueryPlanFeatureCategory Category,
    int Value,
    QueryPlanValueUse? ValueUse = null)
{
    public static QueryPlanFeature SourceCount(QueryPlanSourceCountKind value) =>
        new(QueryPlanFeatureCategory.SourceCount, (int)value);

    public static QueryPlanFeature SourceTopology(QueryPlanSourceTopology value) =>
        new(QueryPlanFeatureCategory.SourceTopology, (int)value);

    public static QueryPlanFeature SourceKind(QueryPlanSourceKind value) =>
        new(QueryPlanFeatureCategory.SourceKind, (int)value);

    public static QueryPlanFeature SourceCardinality(QueryPlanSourceCardinality value) =>
        new(QueryPlanFeatureCategory.SourceCardinality, (int)value);

    public static QueryPlanFeature SourceNullability(QueryPlanSourceNullability value) =>
        new(QueryPlanFeatureCategory.SourceNullability, (int)value);

    public static QueryPlanFeature Operation(QueryPlanOperationKind value) =>
        new(QueryPlanFeatureCategory.Operation, (int)value);

    public static QueryPlanFeature PushdownShape(QueryPlanPushdownShape value) =>
        new(QueryPlanFeatureCategory.PushdownShape, (int)value);

    public static QueryPlanFeature OrderingDirection(QueryPlanOrderingDirection value) =>
        new(QueryPlanFeatureCategory.OrderingDirection, (int)value);

    public static QueryPlanFeature JoinKind(QueryPlanJoinKind value) =>
        new(QueryPlanFeatureCategory.JoinKind, (int)value);

    public static QueryPlanFeature JoinRightSourceKind(QueryPlanSourceKind value) =>
        new(QueryPlanFeatureCategory.JoinRightSourceKind, (int)value);

    public static QueryPlanFeature Predicate(QueryPlanPredicateKind value) =>
        new(QueryPlanFeatureCategory.Predicate, (int)value);

    public static QueryPlanFeature PredicatePolarity(QueryPlanPredicatePolarity value) =>
        new(QueryPlanFeatureCategory.PredicatePolarity, (int)value);

    public static QueryPlanFeature RelationPart(DataLinq.Metadata.RelationPartType value) =>
        new(QueryPlanFeatureCategory.RelationPart, (int)value);

    public static QueryPlanFeature ComparisonOperator(QueryPlanComparisonOperator value) =>
        new(QueryPlanFeatureCategory.ComparisonOperator, (int)value);

    public static QueryPlanFeature NullSemantics(QueryPlanNullSemantics value) =>
        new(QueryPlanFeatureCategory.NullSemantics, (int)value);

    public static QueryPlanFeature ComparisonShape(QueryPlanComparisonShape value) =>
        new(QueryPlanFeatureCategory.ComparisonShape, (int)value);

    public static QueryPlanFeature ValueKind(QueryPlanValueKind value, QueryPlanValueUse use) =>
        new(QueryPlanFeatureCategory.Value, (int)value, use);

    public static QueryPlanFeature Intrinsic(QueryPlanIntrinsicKind value, QueryPlanValueUse use) =>
        new(QueryPlanFeatureCategory.Intrinsic, (int)value, use);

    public static QueryPlanFeature Function(QueryPlanFunctionKind value, QueryPlanValueUse use) =>
        new(QueryPlanFeatureCategory.Function, (int)value, use);

    public static QueryPlanFeature FunctionShape(QueryPlanFunctionShape value) =>
        new(QueryPlanFeatureCategory.FunctionShape, (int)value);

    public static QueryPlanFeature GroupedAggregate(QueryPlanGroupedAggregateKind value, QueryPlanValueUse use) =>
        new(QueryPlanFeatureCategory.GroupedAggregate, (int)value, use);

    public static QueryPlanFeature AggregateSelectorShape(QueryPlanAggregateSelectorShape value) =>
        new(QueryPlanFeatureCategory.AggregateSelectorShape, (int)value);

    public static QueryPlanFeature PagingCountShape(QueryPlanPagingCountShape value) =>
        new(QueryPlanFeatureCategory.PagingCountShape, (int)value);

    public static QueryPlanFeature Projection(QueryPlanProjectionKind value) =>
        new(QueryPlanFeatureCategory.Projection, (int)value);

    public static QueryPlanFeature ProjectionDisposition(QueryPlanProjectionDisposition value) =>
        new(QueryPlanFeatureCategory.ProjectionDisposition, (int)value);

    public static QueryPlanFeature ProjectionRecipe(QueryPlanProjectionRecipeKind value) =>
        new(QueryPlanFeatureCategory.ProjectionRecipe, (int)value);

    public static QueryPlanFeature ProjectionIntrinsic(QueryPlanProjectionIntrinsicKind value) =>
        new(QueryPlanFeatureCategory.ProjectionIntrinsic, (int)value);

    public static QueryPlanFeature ProjectionBinaryOperator(QueryPlanProjectionBinaryOperator value) =>
        new(QueryPlanFeatureCategory.ProjectionBinaryOperator, (int)value);

    public static QueryPlanFeature ProjectionSupportedMember(QueryPlanProjectionSupportedMemberKind value) =>
        new(QueryPlanFeatureCategory.ProjectionSupportedMember, (int)value);

    public static QueryPlanFeature ProjectionFunction(QueryPlanProjectionFunctionKind value) =>
        new(QueryPlanFeatureCategory.ProjectionFunction, (int)value);

    public static QueryPlanFeature Result(QueryPlanResultKind value) =>
        new(QueryPlanFeatureCategory.Result, (int)value);

    public static QueryPlanFeature BindingKind(QueryPlanBindingKind value) =>
        new(QueryPlanFeatureCategory.BindingKind, (int)value);

    public static QueryPlanFeature ScalarNullness(QueryPlanBindingNullness value) =>
        new(QueryPlanFeatureCategory.ScalarNullness, (int)value);

    public static QueryPlanFeature LocalSequenceShape(QueryPlanLocalSequenceShapeKind value) =>
        new(QueryPlanFeatureCategory.LocalSequenceShape, (int)value);

    public string Token => ValueUse is null
        ? $"{Category}:{GetValueName()}"
        : $"{Category}:{GetValueName()}@{ValueUse}";

    private string GetValueName() => Category switch
    {
        QueryPlanFeatureCategory.SourceCount => Name<QueryPlanSourceCountKind>(),
        QueryPlanFeatureCategory.SourceTopology => Name<QueryPlanSourceTopology>(),
        QueryPlanFeatureCategory.SourceKind => Name<QueryPlanSourceKind>(),
        QueryPlanFeatureCategory.SourceCardinality => Name<QueryPlanSourceCardinality>(),
        QueryPlanFeatureCategory.SourceNullability => Name<QueryPlanSourceNullability>(),
        QueryPlanFeatureCategory.Operation => Name<QueryPlanOperationKind>(),
        QueryPlanFeatureCategory.PushdownShape => Name<QueryPlanPushdownShape>(),
        QueryPlanFeatureCategory.OrderingDirection => Name<QueryPlanOrderingDirection>(),
        QueryPlanFeatureCategory.JoinKind => Name<QueryPlanJoinKind>(),
        QueryPlanFeatureCategory.JoinRightSourceKind => Name<QueryPlanSourceKind>(),
        QueryPlanFeatureCategory.Predicate => Name<QueryPlanPredicateKind>(),
        QueryPlanFeatureCategory.PredicatePolarity => Name<QueryPlanPredicatePolarity>(),
        QueryPlanFeatureCategory.RelationPart => Name<DataLinq.Metadata.RelationPartType>(),
        QueryPlanFeatureCategory.ComparisonOperator => Name<QueryPlanComparisonOperator>(),
        QueryPlanFeatureCategory.NullSemantics => Name<QueryPlanNullSemantics>(),
        QueryPlanFeatureCategory.ComparisonShape => Name<QueryPlanComparisonShape>(),
        QueryPlanFeatureCategory.Value => Name<QueryPlanValueKind>(),
        QueryPlanFeatureCategory.Intrinsic => Name<QueryPlanIntrinsicKind>(),
        QueryPlanFeatureCategory.Function => Name<QueryPlanFunctionKind>(),
        QueryPlanFeatureCategory.FunctionShape => Name<QueryPlanFunctionShape>(),
        QueryPlanFeatureCategory.GroupedAggregate => Name<QueryPlanGroupedAggregateKind>(),
        QueryPlanFeatureCategory.AggregateSelectorShape => Name<QueryPlanAggregateSelectorShape>(),
        QueryPlanFeatureCategory.PagingCountShape => Name<QueryPlanPagingCountShape>(),
        QueryPlanFeatureCategory.Projection => Name<QueryPlanProjectionKind>(),
        QueryPlanFeatureCategory.ProjectionDisposition => Name<QueryPlanProjectionDisposition>(),
        QueryPlanFeatureCategory.ProjectionRecipe => Name<QueryPlanProjectionRecipeKind>(),
        QueryPlanFeatureCategory.ProjectionIntrinsic => Name<QueryPlanProjectionIntrinsicKind>(),
        QueryPlanFeatureCategory.ProjectionBinaryOperator => Name<QueryPlanProjectionBinaryOperator>(),
        QueryPlanFeatureCategory.ProjectionSupportedMember => Name<QueryPlanProjectionSupportedMemberKind>(),
        QueryPlanFeatureCategory.ProjectionFunction => Name<QueryPlanProjectionFunctionKind>(),
        QueryPlanFeatureCategory.Result => Name<QueryPlanResultKind>(),
        QueryPlanFeatureCategory.BindingKind => Name<QueryPlanBindingKind>(),
        QueryPlanFeatureCategory.ScalarNullness => Name<QueryPlanBindingNullness>(),
        QueryPlanFeatureCategory.LocalSequenceShape => Name<QueryPlanLocalSequenceShapeKind>(),
        _ => Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private string Name<TEnum>() where TEnum : struct, Enum =>
        Enum.GetName(typeof(TEnum), Value) ?? Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal static class QueryPlanFeatureCatalog
{
    public static IReadOnlyList<QueryPlanFeature> All { get; } = Create();

    private static IReadOnlyList<QueryPlanFeature> Create()
    {
        var features = new List<QueryPlanFeature>();

        Add(features, Enum.GetValues<QueryPlanSourceCountKind>(), QueryPlanFeature.SourceCount);
        Add(features, Enum.GetValues<QueryPlanSourceTopology>(), QueryPlanFeature.SourceTopology);
        Add(features, Enum.GetValues<QueryPlanSourceKind>(), QueryPlanFeature.SourceKind);
        Add(features, Enum.GetValues<QueryPlanSourceCardinality>(), QueryPlanFeature.SourceCardinality);
        Add(features, Enum.GetValues<QueryPlanSourceNullability>(), QueryPlanFeature.SourceNullability);
        Add(features, Enum.GetValues<QueryPlanOperationKind>(), QueryPlanFeature.Operation);
        Add(features, Enum.GetValues<QueryPlanPushdownShape>(), QueryPlanFeature.PushdownShape);
        Add(features, Enum.GetValues<QueryPlanOrderingDirection>(), QueryPlanFeature.OrderingDirection);
        Add(features, Enum.GetValues<QueryPlanJoinKind>(), QueryPlanFeature.JoinKind);
        Add(features, Enum.GetValues<QueryPlanSourceKind>(), QueryPlanFeature.JoinRightSourceKind);
        Add(features, Enum.GetValues<QueryPlanPredicateKind>(), QueryPlanFeature.Predicate);
        Add(features, Enum.GetValues<QueryPlanPredicatePolarity>(), QueryPlanFeature.PredicatePolarity);
        Add(features, Enum.GetValues<DataLinq.Metadata.RelationPartType>(), QueryPlanFeature.RelationPart);
        Add(features, Enum.GetValues<QueryPlanComparisonOperator>(), QueryPlanFeature.ComparisonOperator);
        Add(features, Enum.GetValues<QueryPlanNullSemantics>(), QueryPlanFeature.NullSemantics);
        Add(features, Enum.GetValues<QueryPlanComparisonShape>(), QueryPlanFeature.ComparisonShape);

        foreach (var use in Enum.GetValues<QueryPlanValueUse>())
        {
            Add(features, Enum.GetValues<QueryPlanValueKind>(), value => QueryPlanFeature.ValueKind(value, use));
            Add(features, Enum.GetValues<QueryPlanIntrinsicKind>(), value => QueryPlanFeature.Intrinsic(value, use));
            Add(features, Enum.GetValues<QueryPlanFunctionKind>(), value => QueryPlanFeature.Function(value, use));
            Add(features, Enum.GetValues<QueryPlanGroupedAggregateKind>(), value => QueryPlanFeature.GroupedAggregate(value, use));
        }

        Add(features, Enum.GetValues<QueryPlanFunctionShape>(), QueryPlanFeature.FunctionShape);

        Add(features, Enum.GetValues<QueryPlanProjectionKind>(), QueryPlanFeature.Projection);
        Add(features, Enum.GetValues<QueryPlanAggregateSelectorShape>(), QueryPlanFeature.AggregateSelectorShape);
        Add(features, Enum.GetValues<QueryPlanPagingCountShape>(), QueryPlanFeature.PagingCountShape);
        Add(features, Enum.GetValues<QueryPlanProjectionDisposition>(), QueryPlanFeature.ProjectionDisposition);
        Add(features, Enum.GetValues<QueryPlanProjectionRecipeKind>(), QueryPlanFeature.ProjectionRecipe);
        Add(features, Enum.GetValues<QueryPlanProjectionIntrinsicKind>(), QueryPlanFeature.ProjectionIntrinsic);
        Add(features, Enum.GetValues<QueryPlanProjectionBinaryOperator>(), QueryPlanFeature.ProjectionBinaryOperator);
        Add(features, Enum.GetValues<QueryPlanProjectionSupportedMemberKind>(), QueryPlanFeature.ProjectionSupportedMember);
        Add(features, Enum.GetValues<QueryPlanProjectionFunctionKind>(), QueryPlanFeature.ProjectionFunction);
        Add(features, Enum.GetValues<QueryPlanResultKind>(), QueryPlanFeature.Result);
        Add(features, Enum.GetValues<QueryPlanBindingKind>(), QueryPlanFeature.BindingKind);
        Add(features, Enum.GetValues<QueryPlanBindingNullness>(), QueryPlanFeature.ScalarNullness);
        Add(features, Enum.GetValues<QueryPlanLocalSequenceShapeKind>(), QueryPlanFeature.LocalSequenceShape);

        return Array.AsReadOnly(features.ToArray());
    }

    private static void Add<T>(
        ICollection<QueryPlanFeature> target,
        IEnumerable<T> values,
        Func<T, QueryPlanFeature> factory)
    {
        foreach (var value in values)
            target.Add(factory(value));
    }
}

internal enum QueryBackendCapabilityDisposition
{
    Supported,
    Unsupported
}

internal sealed class QueryBackendCapabilities
{
    private static readonly QueryBackendCapabilities sql = CreateSql();
    private readonly IReadOnlyDictionary<QueryPlanFeature, QueryBackendCapabilityDisposition> dispositions;

    internal QueryBackendCapabilities(
        string backendName,
        IEnumerable<KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>> dispositions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendName);
        ArgumentNullException.ThrowIfNull(dispositions);

        BackendName = backendName;
        var source = dispositions.ToArray();
        var byFeature = new Dictionary<QueryPlanFeature, QueryBackendCapabilityDisposition>(source.Length);
        foreach (var item in source)
        {
            if (!Enum.IsDefined(item.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dispositions),
                    item.Value,
                    $"Capability feature '{item.Key.Token}' has an unknown disposition.");
            }

            if (!byFeature.TryAdd(item.Key, item.Value))
                throw new ArgumentException($"Capability feature '{item.Key.Token}' is duplicated.", nameof(dispositions));
        }

        var knownFeatures = QueryPlanFeatureCatalog.All.ToHashSet();
        var unknown = byFeature.Keys
            .Where(feature => !knownFeatures.Contains(feature))
            .OrderBy(static feature => feature.Token, StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length != 0)
        {
            throw new ArgumentException(
                $"Capability profile '{backendName}' contains unknown features: {string.Join(", ", unknown.Select(static feature => feature.Token))}.",
                nameof(dispositions));
        }

        var missing = QueryPlanFeatureCatalog.All
            .Where(feature => !byFeature.ContainsKey(feature))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new ArgumentException(
                $"Capability profile '{backendName}' has no disposition for: {string.Join(", ", missing.Select(static feature => feature.Token))}.",
                nameof(dispositions));
        }

        this.dispositions = new ReadOnlyDictionary<QueryPlanFeature, QueryBackendCapabilityDisposition>(byFeature);
    }

    public string BackendName { get; }

    public static QueryBackendCapabilities Sql => sql;

    public QueryBackendCapabilityDisposition GetDisposition(QueryPlanFeature feature) =>
        dispositions.TryGetValue(feature, out var disposition)
            ? disposition
            : QueryBackendCapabilityDisposition.Unsupported;

    private static QueryBackendCapabilities CreateSql() => new(
        "sql",
        QueryPlanFeatureCatalog.All.Select(static feature =>
            new KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>(
                feature,
                GetSqlDisposition(feature))));

    private static QueryBackendCapabilityDisposition GetSqlDisposition(QueryPlanFeature feature) =>
        feature.Category switch
        {
            QueryPlanFeatureCategory.Projection =>
                (QueryPlanProjectionKind)feature.Value == QueryPlanProjectionKind.TransparentIdentifier
                ? QueryBackendCapabilityDisposition.Unsupported
                : QueryBackendCapabilityDisposition.Supported,
            QueryPlanFeatureCategory.ProjectionDisposition =>
                (QueryPlanProjectionDisposition)feature.Value == QueryPlanProjectionDisposition.Unsupported
                ? QueryBackendCapabilityDisposition.Unsupported
                : QueryBackendCapabilityDisposition.Supported,
            QueryPlanFeatureCategory.Value =>
                IsSqlValueSupported((QueryPlanValueKind)feature.Value, feature.ValueUse!.Value)
                ? QueryBackendCapabilityDisposition.Supported
                : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.PushdownShape =>
                (QueryPlanPushdownShape)feature.Value is QueryPlanPushdownShape.Simple or QueryPlanPushdownShape.JoinedSqlRowDirectColumns
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.SourceTopology =>
                (QueryPlanSourceTopology)feature.Value == QueryPlanSourceTopology.ExactlyOneRoot
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.RelationPart =>
                (DataLinq.Metadata.RelationPartType)feature.Value == DataLinq.Metadata.RelationPartType.CandidateKey
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.JoinRightSourceKind =>
                (QueryPlanSourceKind)feature.Value is QueryPlanSourceKind.ExplicitJoin or QueryPlanSourceKind.ImplicitJoin
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.ComparisonShape =>
                (QueryPlanComparisonShape)feature.Value != QueryPlanComparisonShape.UnsupportedNullableNotEqual
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.AggregateSelectorShape =>
                (QueryPlanAggregateSelectorShape)feature.Value == QueryPlanAggregateSelectorShape.DirectNumericColumn
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.PagingCountShape =>
                (QueryPlanPagingCountShape)feature.Value == QueryPlanPagingCountShape.NonNegative
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.Function =>
                IsSqlFunctionSupported((QueryPlanFunctionKind)feature.Value, feature.ValueUse!.Value)
                ? QueryBackendCapabilityDisposition.Supported
                : QueryBackendCapabilityDisposition.Unsupported,
            QueryPlanFeatureCategory.FunctionShape =>
                (QueryPlanFunctionShape)feature.Value == QueryPlanFunctionShape.SubstringWithStart
                    ? QueryBackendCapabilityDisposition.Unsupported
                    : QueryBackendCapabilityDisposition.Supported,
            QueryPlanFeatureCategory.SourceCount or
            QueryPlanFeatureCategory.SourceKind or
            QueryPlanFeatureCategory.SourceCardinality or
            QueryPlanFeatureCategory.SourceNullability or
            QueryPlanFeatureCategory.Operation or
            QueryPlanFeatureCategory.OrderingDirection or
            QueryPlanFeatureCategory.JoinKind or
            QueryPlanFeatureCategory.Predicate or
            QueryPlanFeatureCategory.PredicatePolarity or
            QueryPlanFeatureCategory.ComparisonOperator or
            QueryPlanFeatureCategory.NullSemantics or
            QueryPlanFeatureCategory.Intrinsic or
            QueryPlanFeatureCategory.GroupedAggregate or
            QueryPlanFeatureCategory.ProjectionRecipe or
            QueryPlanFeatureCategory.ProjectionIntrinsic or
            QueryPlanFeatureCategory.ProjectionBinaryOperator or
            QueryPlanFeatureCategory.ProjectionSupportedMember or
            QueryPlanFeatureCategory.ProjectionFunction or
            QueryPlanFeatureCategory.Result or
            QueryPlanFeatureCategory.BindingKind or
            QueryPlanFeatureCategory.ScalarNullness or
            QueryPlanFeatureCategory.LocalSequenceShape => QueryBackendCapabilityDisposition.Supported,
            _ => throw new InvalidOperationException(
                $"Query plan feature category '{feature.Category}' has no explicit SQL disposition rule.")
        };

    private static bool IsSqlValueSupported(QueryPlanValueKind kind, QueryPlanValueUse use) => use switch
    {
        QueryPlanValueUse.PredicateOperand or QueryPlanValueUse.MembershipItem =>
            kind != QueryPlanValueKind.LocalSequenceBinding,
        QueryPlanValueUse.BooleanPredicateFunction => kind == QueryPlanValueKind.Function,
        QueryPlanValueUse.MembershipSequence => kind == QueryPlanValueKind.LocalSequenceBinding,
        QueryPlanValueUse.Ordering => kind is QueryPlanValueKind.Column or
            QueryPlanValueKind.GroupKey or QueryPlanValueKind.GroupedAggregate,
        QueryPlanValueUse.PagingCount or QueryPlanValueUse.ScalarFunctionArgument =>
            kind is QueryPlanValueKind.Intrinsic or QueryPlanValueKind.ScalarBinding or QueryPlanValueKind.Converted,
        QueryPlanValueUse.JoinKey => kind == QueryPlanValueKind.Column,
        QueryPlanValueUse.GroupingKey or QueryPlanValueUse.ProjectionMember =>
            kind is QueryPlanValueKind.Column or QueryPlanValueKind.Function or QueryPlanValueKind.Converted,
        QueryPlanValueUse.GroupedProjectionMember =>
            kind is QueryPlanValueKind.GroupKey or QueryPlanValueKind.GroupedAggregate,
        QueryPlanValueUse.AggregateSelector =>
            kind is QueryPlanValueKind.Column or QueryPlanValueKind.Converted,
        QueryPlanValueUse.FunctionSource =>
            kind is QueryPlanValueKind.Column or QueryPlanValueKind.Function or QueryPlanValueKind.Converted,
        _ => false
    };

    private static bool IsSqlFunctionSupported(QueryPlanFunctionKind kind, QueryPlanValueUse use)
    {
        var booleanFunction = kind is QueryPlanFunctionKind.StringStartsWith or
            QueryPlanFunctionKind.StringEndsWith or
            QueryPlanFunctionKind.StringContains or
            QueryPlanFunctionKind.StringIsNullOrEmpty or
            QueryPlanFunctionKind.StringIsNullOrWhiteSpace;

        if (booleanFunction)
            return use == QueryPlanValueUse.BooleanPredicateFunction;

        return use == QueryPlanValueUse.PredicateOperand ||
            use is QueryPlanValueUse.MembershipItem or
                QueryPlanValueUse.GroupingKey or
                QueryPlanValueUse.ProjectionMember or
                QueryPlanValueUse.FunctionSource;
    }
}
