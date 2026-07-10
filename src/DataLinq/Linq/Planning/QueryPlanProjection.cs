using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal abstract record QueryPlanProjection
{
    protected QueryPlanProjection(
        QueryPlanProjectionKind kind,
        Type resultType,
        QueryPlanProjectionDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        Kind = kind;
        ResultType = resultType;
        Disposition = disposition;
    }

    public QueryPlanProjectionKind Kind { get; }

    public Type ResultType { get; }

    public QueryPlanProjectionDisposition Disposition { get; }

    public sealed record Entity(QueryPlanSourceSlot Source)
        : QueryPlanProjection(
            QueryPlanProjectionKind.Entity,
            Source.ElementType,
            QueryPlanProjectionDisposition.Direct)
    ;

    public sealed record ScalarMember(QueryPlanSourceSlot Source, ColumnDefinition Column, Type ResultType)
        : QueryPlanProjection(
            QueryPlanProjectionKind.ScalarMember,
            ResultType,
            QueryPlanProjectionDisposition.Direct)
    {
        public ScalarMember(QueryPlanSourceSlot source, ColumnDefinition column)
            : this(source, column, column.ValueProperty?.CsType.Type ?? typeof(object))
        {
        }
    }

    public sealed record Anonymous : QueryPlanProjection
    {
        public Anonymous(
            Type anonymousType,
            IEnumerable<QueryPlanProjectionMember> members,
            IEnumerable<QueryPlanSourceSlot> sources,
            QueryPlanProjectionRecipe recipe)
            : base(
                QueryPlanProjectionKind.Anonymous,
                anonymousType,
                QueryPlanProjectionDisposition.SqlOnlyCompatibility)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            AnonymousType = anonymousType;
            Members = Freeze(members, nameof(members));
            Sources = Freeze(sources, nameof(sources));
            Recipe = recipe;
            ValidateMembers(Members);
        }

        public Type AnonymousType { get; }

        public IReadOnlyList<QueryPlanProjectionMember> Members { get; }

        public IReadOnlyList<QueryPlanSourceSlot> Sources { get; }

        public QueryPlanProjectionRecipe Recipe { get; }
    }

    public sealed record ComputedRowLocal : QueryPlanProjection
    {
        public ComputedRowLocal(
            Type computedType,
            QueryPlanProjectionRecipe recipe,
            IEnumerable<QueryPlanSourceSlot> sources)
            : base(
                QueryPlanProjectionKind.ComputedRowLocalExpression,
                computedType,
                RecipeDisposition(recipe))
        {
            ComputedType = computedType;
            Recipe = recipe;
            Sources = Freeze(sources, nameof(sources));
        }

        public Type ComputedType { get; }

        public QueryPlanProjectionRecipe Recipe { get; }

        public IReadOnlyList<QueryPlanSourceSlot> Sources { get; }
    }

    public sealed record JoinedRowLocal : QueryPlanProjection
    {
        public JoinedRowLocal(
            Type joinedType,
            IEnumerable<QueryPlanProjectionMember> members,
            IEnumerable<QueryPlanSourceSlot> sources,
            QueryPlanProjectionRecipe recipe)
            : base(
                QueryPlanProjectionKind.JoinedRowLocal,
                joinedType,
                QueryPlanProjectionDisposition.SqlOnlyCompatibility)
        {
            ArgumentNullException.ThrowIfNull(recipe);
            JoinedType = joinedType;
            Members = Freeze(members, nameof(members));
            Sources = Freeze(sources, nameof(sources));
            Recipe = recipe;
        }

        public Type JoinedType { get; }

        public IReadOnlyList<QueryPlanProjectionMember> Members { get; }

        public IReadOnlyList<QueryPlanSourceSlot> Sources { get; }

        public QueryPlanProjectionRecipe Recipe { get; }
    }

    public sealed record SqlRow : QueryPlanProjection
    {
        public SqlRow(Type rowType, IEnumerable<QueryPlanProjectionMember> members, ConstructorInfo constructor)
            : base(
                QueryPlanProjectionKind.SqlRow,
                rowType,
                QueryPlanProjectionDisposition.SqlOnlyCompatibility)
        {
            ArgumentNullException.ThrowIfNull(constructor);
            RowType = rowType;
            Members = Freeze(members, nameof(members));
            Constructor = constructor;
        }

        public Type RowType { get; }

        public IReadOnlyList<QueryPlanProjectionMember> Members { get; }

        public ConstructorInfo Constructor { get; }
    }

    public sealed record TransparentIdentifier : QueryPlanProjection
    {
        public TransparentIdentifier(Type transparentType, IEnumerable<KeyValuePair<string, QueryPlanSourceSlot>> sourcesByMember)
            : base(
                QueryPlanProjectionKind.TransparentIdentifier,
                transparentType,
                QueryPlanProjectionDisposition.Unsupported)
        {
            TransparentType = transparentType;
            SourcesByMember = FreezeDictionary(sourcesByMember, nameof(sourcesByMember));
        }

        public Type TransparentType { get; }

        public IReadOnlyDictionary<string, QueryPlanSourceSlot> SourcesByMember { get; }
    }

    public sealed record GroupedAggregate : QueryPlanProjection
    {
        public GroupedAggregate(
            Type aggregateRowType,
            IEnumerable<QueryPlanProjectionMember> members,
            QueryPlanSourceSlot source,
            ConstructorInfo constructor)
            : base(
                QueryPlanProjectionKind.GroupedAggregate,
                aggregateRowType,
                QueryPlanProjectionDisposition.SqlOnlyCompatibility)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(constructor);
            AggregateRowType = aggregateRowType;
            Members = Freeze(members, nameof(members));
            Source = source;
            Constructor = constructor;
        }

        public Type AggregateRowType { get; }

        public IReadOnlyList<QueryPlanProjectionMember> Members { get; }

        public QueryPlanSourceSlot Source { get; }

        public ConstructorInfo Constructor { get; }
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Query plan collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
    }

    private static ReadOnlyDictionary<string, QueryPlanSourceSlot> FreezeDictionary(
        IEnumerable<KeyValuePair<string, QueryPlanSourceSlot>> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var dictionary = new Dictionary<string, QueryPlanSourceSlot>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key, parameterName);
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!dictionary.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException($"Projection source member '{pair.Key}' is duplicated.", parameterName);
        }

        if (dictionary.Count == 0)
            throw new ArgumentException("Transparent identifier projections must contain at least one source member.", parameterName);

        return new ReadOnlyDictionary<string, QueryPlanSourceSlot>(dictionary);
    }

    private static void ValidateMembers(IReadOnlyList<QueryPlanProjectionMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0)
            throw new ArgumentException("Structured projections must contain at least one member.", nameof(members));
    }

    private static void ValidateSources(IReadOnlyList<QueryPlanSourceSlot> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
            throw new ArgumentException("Projections must reference at least one source slot.", nameof(sources));
    }

    private static QueryPlanProjectionDisposition RecipeDisposition(QueryPlanProjectionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe.Disposition;
    }
}

internal sealed record QueryPlanProjectionMember(string Name, QueryPlanValue Value)
;

internal enum QueryPlanProjectionKind
{
    Entity,
    ScalarMember,
    Anonymous,
    ComputedRowLocalExpression,
    JoinedRowLocal,
    SqlRow,
    TransparentIdentifier,
    GroupedAggregate
}

internal enum QueryPlanProjectionDisposition
{
    Direct,
    AotSafe,
    SqlOnlyCompatibility,
    Unsupported
}
