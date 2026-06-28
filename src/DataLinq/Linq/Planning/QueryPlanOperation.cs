using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal abstract record QueryPlanOperation(QueryPlanOperationKind Kind)
{
    public sealed record Where : QueryPlanOperation
    {
        public Where(QueryPlanPredicate Predicate) : base(QueryPlanOperationKind.Where)
        {
            ArgumentNullException.ThrowIfNull(Predicate);
            this.Predicate = Predicate;
        }

        public QueryPlanPredicate Predicate { get; }
    }

    public sealed record OrderBy : QueryPlanOperation
    {
        public OrderBy(IEnumerable<QueryPlanOrdering> orderings) : base(QueryPlanOperationKind.OrderBy)
        {
            Orderings = Freeze(orderings, nameof(orderings));
            if (Orderings.Count == 0)
                throw new ArgumentException("OrderBy operations must contain at least one ordering.", nameof(Orderings));
        }

        public IReadOnlyList<QueryPlanOrdering> Orderings { get; }
    }

    public sealed record Skip : QueryPlanOperation
    {
        public Skip(QueryPlanValue Count) : base(QueryPlanOperationKind.Skip)
        {
            ArgumentNullException.ThrowIfNull(Count);
            this.Count = Count;
        }

        public QueryPlanValue Count { get; }
    }

    public sealed record Take : QueryPlanOperation
    {
        public Take(QueryPlanValue Count) : base(QueryPlanOperationKind.Take)
        {
            ArgumentNullException.ThrowIfNull(Count);
            this.Count = Count;
        }

        public QueryPlanValue Count { get; }
    }

    public sealed record Join : QueryPlanOperation
    {
        public Join(QueryPlanJoin JoinShape) : base(QueryPlanOperationKind.Join)
        {
            ArgumentNullException.ThrowIfNull(JoinShape);
            this.JoinShape = JoinShape;
        }

        public QueryPlanJoin JoinShape { get; }
    }

    public sealed record Pushdown : QueryPlanOperation
    {
        public Pushdown(
            IEnumerable<QueryPlanOperation> operations,
            IEnumerable<QueryPlanOrdering> preservedOrderings)
            : base(QueryPlanOperationKind.Pushdown)
        {
            Operations = Freeze(operations, nameof(operations));
            PreservedOrderings = Freeze(preservedOrderings, nameof(preservedOrderings));

            if (Operations.Count == 0)
                throw new ArgumentException("Pushdown operations must contain at least one inner operation.", nameof(operations));
        }

        public IReadOnlyList<QueryPlanOperation> Operations { get; }

        public IReadOnlyList<QueryPlanOrdering> PreservedOrderings { get; }
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Query plan collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
    }
}

internal sealed record QueryPlanOrdering(QueryPlanValue Value, QueryPlanOrderingDirection Direction)
;

internal sealed record QueryPlanJoin(
    QueryPlanJoinKind Kind,
    QueryPlanSourceSlot LeftSource,
    ColumnDefinition LeftColumn,
    QueryPlanSourceSlot RightSource,
    ColumnDefinition RightColumn);

internal enum QueryPlanOperationKind
{
    Where,
    OrderBy,
    Skip,
    Take,
    Join,
    Pushdown
}

internal enum QueryPlanOrderingDirection
{
    Ascending,
    Descending
}

internal enum QueryPlanJoinKind
{
    Inner
}
