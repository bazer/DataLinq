using System;

namespace DataLinq.Linq.Planning;

internal sealed record QueryPlanResult
{
    public QueryPlanResult(
        QueryPlanResultKind Kind,
        Type ResultType,
        QueryPlanValue? AggregateSelector = null)
    {
        ArgumentNullException.ThrowIfNull(ResultType);

        var isAggregate = Kind is QueryPlanResultKind.Sum or
            QueryPlanResultKind.Min or
            QueryPlanResultKind.Max or
            QueryPlanResultKind.Average;
        if (isAggregate && AggregateSelector is null)
        {
            throw new ArgumentException("Aggregate results must record their selector shape.", nameof(AggregateSelector));
        }

        if (!isAggregate && AggregateSelector is not null)
        {
            throw new ArgumentException(
                $"Query result '{Kind}' must not record an aggregate selector.",
                nameof(AggregateSelector));
        }

        this.Kind = Kind;
        this.ResultType = ResultType;
        this.AggregateSelector = AggregateSelector;
    }

    public QueryPlanResultKind Kind { get; }

    public Type ResultType { get; }

    public QueryPlanValue? AggregateSelector { get; }

    public bool IsScalarResult =>
        Kind is QueryPlanResultKind.Count or
            QueryPlanResultKind.Any or
            QueryPlanResultKind.Sum or
            QueryPlanResultKind.Min or
            QueryPlanResultKind.Max or
            QueryPlanResultKind.Average;

    public static QueryPlanResult Sequence(Type resultType) => new(QueryPlanResultKind.Sequence, resultType);
}

internal enum QueryPlanResultKind
{
    Sequence,
    Single,
    SingleOrDefault,
    First,
    FirstOrDefault,
    Last,
    LastOrDefault,
    Count,
    Any,
    Sum,
    Min,
    Max,
    Average
}
