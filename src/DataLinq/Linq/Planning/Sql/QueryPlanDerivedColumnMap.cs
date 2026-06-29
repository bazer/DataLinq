using System;
using System.Collections.Generic;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class QueryPlanDerivedColumnMap
{
    private readonly Dictionary<QueryPlanDerivedColumnKey, string> aliasesByColumn;

    private QueryPlanDerivedColumnMap(string sourceAlias, Dictionary<QueryPlanDerivedColumnKey, string> aliasesByColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAlias);
        ArgumentNullException.ThrowIfNull(aliasesByColumn);

        SourceAlias = sourceAlias;
        this.aliasesByColumn = aliasesByColumn;
    }

    public string SourceAlias { get; }

    public static QueryPlanDerivedColumnMap FromSqlRowProjection(
        string sourceAlias,
        QueryPlanProjection.SqlRow projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var aliases = new Dictionary<QueryPlanDerivedColumnKey, string>();
        foreach (var member in projection.Members)
        {
            if (UnwrapConvertedValue(member.Value) is not QueryPlanColumnValue column)
            {
                throw new QueryTranslationException(
                    $"Joined pushdown projection member '{member.Name}' is not a direct source-slot column.");
            }

            aliases.TryAdd(new QueryPlanDerivedColumnKey(column.Source.Id, column.Column.DbName), member.Name);
        }

        return new QueryPlanDerivedColumnMap(sourceAlias, aliases);
    }

    public bool TryGetAlias(QueryPlanColumnValue column, out string alias)
    {
        ArgumentNullException.ThrowIfNull(column);

        return aliasesByColumn.TryGetValue(
            new QueryPlanDerivedColumnKey(column.Source.Id, column.Column.DbName),
            out alias!);
    }

    private static QueryPlanValue UnwrapConvertedValue(QueryPlanValue value)
    {
        while (value is QueryPlanConvertedValue converted)
            value = converted.Value;

        return value;
    }

    private readonly record struct QueryPlanDerivedColumnKey(string SourceId, string ColumnDbName);
}
