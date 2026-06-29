using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal abstract record QueryPlanProjection
{
    protected QueryPlanProjection(QueryPlanProjectionKind kind, Type resultType)
    {
        ArgumentNullException.ThrowIfNull(resultType);
        Kind = kind;
        ResultType = resultType;
    }

    public QueryPlanProjectionKind Kind { get; }

    public Type ResultType { get; }

    public sealed record Entity(QueryPlanSourceSlot Source)
        : QueryPlanProjection(QueryPlanProjectionKind.Entity, Source.ElementType)
    ;

    public sealed record ScalarMember(QueryPlanSourceSlot Source, ColumnDefinition Column, Type ResultType)
        : QueryPlanProjection(QueryPlanProjectionKind.ScalarMember, ResultType)
    {
        public ScalarMember(QueryPlanSourceSlot source, ColumnDefinition column)
            : this(source, column, column.ValueProperty?.CsType.Type ?? typeof(object))
        {
        }
    }

    public sealed record Anonymous(Type AnonymousType, IReadOnlyList<QueryPlanProjectionMember> Members, IReadOnlyList<QueryPlanSourceSlot> Sources)
        : QueryPlanProjection(QueryPlanProjectionKind.Anonymous, AnonymousType)
    {
        public Anonymous(Type anonymousType, IEnumerable<QueryPlanProjectionMember> members, IEnumerable<QueryPlanSourceSlot> sources)
            : this(anonymousType, Freeze(members, nameof(members)), Freeze(sources, nameof(sources)))
        {
        }
    }

    public sealed record ComputedRowLocal(Type ComputedType, string ExpressionShape, IReadOnlyList<QueryPlanSourceSlot> Sources)
        : QueryPlanProjection(QueryPlanProjectionKind.ComputedRowLocalExpression, ComputedType)
    {
        public ComputedRowLocal(Type computedType, string expressionShape, IEnumerable<QueryPlanSourceSlot> sources)
            : this(computedType, expressionShape, Freeze(sources, nameof(sources)))
        {
        }
    }

    public sealed record JoinedRowLocal(Type JoinedType, IReadOnlyList<QueryPlanProjectionMember> Members, IReadOnlyList<QueryPlanSourceSlot> Sources)
        : QueryPlanProjection(QueryPlanProjectionKind.JoinedRowLocal, JoinedType)
    {
        public JoinedRowLocal(Type joinedType, IEnumerable<QueryPlanProjectionMember> members, IEnumerable<QueryPlanSourceSlot> sources)
            : this(joinedType, Freeze(members, nameof(members)), Freeze(sources, nameof(sources)))
        {
        }
    }

    public sealed record SqlRow(
        Type RowType,
        IReadOnlyList<QueryPlanProjectionMember> Members,
        ConstructorInfo Constructor)
        : QueryPlanProjection(QueryPlanProjectionKind.SqlRow, RowType)
    {
        public SqlRow(Type rowType, IEnumerable<QueryPlanProjectionMember> members, ConstructorInfo constructor)
            : this(rowType, Freeze(members, nameof(members)), constructor)
        {
            ArgumentNullException.ThrowIfNull(constructor);
        }
    }

    public sealed record TransparentIdentifier(
        Type TransparentType,
        IReadOnlyDictionary<string, QueryPlanSourceSlot> SourcesByMember)
        : QueryPlanProjection(QueryPlanProjectionKind.TransparentIdentifier, TransparentType)
    {
        public TransparentIdentifier(Type transparentType, IEnumerable<KeyValuePair<string, QueryPlanSourceSlot>> sourcesByMember)
            : this(transparentType, FreezeDictionary(sourcesByMember, nameof(sourcesByMember)))
        {
        }
    }

    public sealed record GroupedAggregate(
        Type AggregateRowType,
        IReadOnlyList<QueryPlanProjectionMember> Members,
        QueryPlanSourceSlot Source,
        ConstructorInfo Constructor)
        : QueryPlanProjection(QueryPlanProjectionKind.GroupedAggregate, AggregateRowType)
    {
        public GroupedAggregate(
            Type aggregateRowType,
            IEnumerable<QueryPlanProjectionMember> members,
            QueryPlanSourceSlot source,
            ConstructorInfo constructor)
            : this(aggregateRowType, Freeze(members, nameof(members)), source, constructor)
        {
            ArgumentNullException.ThrowIfNull(constructor);
        }
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
