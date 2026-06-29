using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DataLinq.Linq.Planning;

internal sealed class DataLinqQueryPlan
{
    public DataLinqQueryPlan(
        IEnumerable<QueryPlanSourceSlot> sources,
        IEnumerable<QueryPlanOperation> operations,
        QueryPlanProjection projection,
        QueryPlanResult result,
        QueryPlanBindingFrame bindings)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bindings);

        Sources = Freeze(sources, nameof(sources));
        Operations = Freeze(operations, nameof(operations));
        Projection = projection;
        Result = result;
        Bindings = bindings;

        ValidateSourceIds(Sources);
    }

    public IReadOnlyList<QueryPlanSourceSlot> Sources { get; }

    public IReadOnlyList<QueryPlanOperation> Operations { get; }

    public QueryPlanProjection Projection { get; }

    public QueryPlanResult Result { get; }

    public QueryPlanBindingFrame Bindings { get; }

    public QueryPlanSourceSlot GetSource(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return Sources.FirstOrDefault(source => source.Id == id)
            ?? throw new InvalidOperationException($"Query plan source slot '{id}' does not exist.");
    }

    private static ReadOnlyCollection<T> Freeze<T>(IEnumerable<T> values, string parameterName)
    {
        var array = values.ToArray();
        if (array.Any(static value => value is null))
            throw new ArgumentException("Query plan collections cannot contain null entries.", parameterName);

        return Array.AsReadOnly(array);
    }

    private static void ValidateSourceIds(IReadOnlyList<QueryPlanSourceSlot> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("A query plan must contain at least one source slot.", nameof(sources));

        var duplicate = sources
            .GroupBy(static source => source.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"Query plan source slot id '{duplicate.Key}' is duplicated.", nameof(sources));
    }
}
