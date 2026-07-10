using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class QueryPlanSqlSourceMap
{
    private readonly Dictionary<string, QueryPlanSourceSlot> sourcesById;

    public QueryPlanSqlSourceMap(QueryPlanTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        sourcesById = template.Sources.ToDictionary(static source => source.Id, StringComparer.Ordinal);
        RootSource = template.Sources.SingleOrDefault(static source => source.Kind == QueryPlanSourceKind.RootTable)
            ?? throw new QueryTranslationException("Query plan SQL rendering requires exactly one root table source slot.");

        if (template.Sources.Count(static source => source.Kind == QueryPlanSourceKind.RootTable) != 1)
            throw new QueryTranslationException("Query plan SQL rendering requires exactly one root table source slot.");
    }

    public QueryPlanSourceSlot RootSource { get; }

    public QueryPlanSourceSlot Get(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        return sourcesById.TryGetValue(sourceId, out var source)
            ? source
            : throw new QueryTranslationException($"Query plan source slot '{sourceId}' is not available to SQL rendering.");
    }

    public QueryPlanSourceSlot Get(QueryPlanSourceSlot source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var mapped = Get(source.Id);
        if (!ReferenceEquals(mapped, source) && mapped != source)
            throw new QueryTranslationException($"Query plan source slot '{source.Id}' does not match the SQL source map.");

        return mapped;
    }
}
