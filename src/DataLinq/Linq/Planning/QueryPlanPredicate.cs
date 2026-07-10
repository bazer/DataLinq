using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning;

internal abstract record QueryPlanPredicate(QueryPlanPredicateKind Kind)
{
    public sealed record Fixed(bool Value) : QueryPlanPredicate(Value ? QueryPlanPredicateKind.FixedTrue : QueryPlanPredicateKind.FixedFalse);

    public sealed record And : QueryPlanPredicate
    {
        public And(IEnumerable<QueryPlanPredicate> terms) : base(QueryPlanPredicateKind.And)
        {
            Terms = Freeze(terms, nameof(terms));
            ValidateTerms(Terms, nameof(Terms));
        }

        public IReadOnlyList<QueryPlanPredicate> Terms { get; }
    }

    public sealed record Or : QueryPlanPredicate
    {
        public Or(IEnumerable<QueryPlanPredicate> terms) : base(QueryPlanPredicateKind.Or)
        {
            Terms = Freeze(terms, nameof(terms));
            ValidateTerms(Terms, nameof(Terms));
        }

        public IReadOnlyList<QueryPlanPredicate> Terms { get; }
    }

    public sealed record Not : QueryPlanPredicate
    {
        public Not(QueryPlanPredicate Predicate) : base(QueryPlanPredicateKind.Not)
        {
            ArgumentNullException.ThrowIfNull(Predicate);
            this.Predicate = Predicate;
        }

        public QueryPlanPredicate Predicate { get; }
    }

    public sealed record Compare : QueryPlanPredicate
    {
        public Compare(
            QueryPlanValue Left,
            QueryPlanComparisonOperator Operator,
            QueryPlanValue Right,
            QueryPlanNullSemantics NullSemantics = QueryPlanNullSemantics.Default) : base(QueryPlanPredicateKind.Compare)
        {
            ArgumentNullException.ThrowIfNull(Left);
            ArgumentNullException.ThrowIfNull(Right);
            this.Left = Left;
            this.Operator = Operator;
            this.Right = Right;
            this.NullSemantics = NullSemantics;
        }

        public QueryPlanValue Left { get; }

        public QueryPlanComparisonOperator Operator { get; }

        public QueryPlanValue Right { get; }

        public QueryPlanNullSemantics NullSemantics { get; }
    }

    public sealed record In : QueryPlanPredicate
    {
        public In(
            QueryPlanValue Item,
            QueryPlanLocalSequenceBindingReference Sequence,
            bool IsNegated) : base(QueryPlanPredicateKind.In)
        {
            ArgumentNullException.ThrowIfNull(Item);
            ArgumentNullException.ThrowIfNull(Sequence);

            this.Item = Item;
            this.Sequence = Sequence;
            this.IsNegated = IsNegated;
        }

        public QueryPlanValue Item { get; }

        public QueryPlanLocalSequenceBindingReference Sequence { get; }

        public bool IsNegated { get; }
    }

    public sealed record Exists : QueryPlanPredicate
    {
        public Exists(
            RelationProperty Relation,
            QueryPlanSourceSlot ParentSource,
            QueryPlanSourceSlot ChildSource,
            QueryPlanPredicate? Predicate,
            bool IsNegated) : base(QueryPlanPredicateKind.Exists)
        {
            ArgumentNullException.ThrowIfNull(Relation);
            ArgumentNullException.ThrowIfNull(ParentSource);
            ArgumentNullException.ThrowIfNull(ChildSource);
            this.Relation = Relation;
            this.ParentSource = ParentSource;
            this.ChildSource = ChildSource;
            this.Predicate = Predicate;
            this.IsNegated = IsNegated;
        }

        public RelationProperty Relation { get; }

        public QueryPlanSourceSlot ParentSource { get; }

        public QueryPlanSourceSlot ChildSource { get; }

        public QueryPlanPredicate? Predicate { get; }

        public bool IsNegated { get; }
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Query plan collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
    }

    private static void ValidateTerms(IReadOnlyList<QueryPlanPredicate> terms, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (terms.Count < 2)
            throw new ArgumentException("Compound predicates must contain at least two terms.", parameterName);
    }
}

internal enum QueryPlanPredicateKind
{
    And,
    Or,
    Not,
    Compare,
    In,
    Exists,
    FixedTrue,
    FixedFalse
}

internal enum QueryPlanComparisonOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

internal enum QueryPlanNullSemantics
{
    Default,
    CSharpNullableNotEqualIncludesNull
}
