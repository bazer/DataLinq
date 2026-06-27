using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Query plan collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
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
    JoinedRowLocal
}
