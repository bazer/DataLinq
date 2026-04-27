using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using DataLinq.Metadata;

namespace DataLinq.Query;

internal sealed record SelectSqlTemplate(string Text, string[] ParameterNames)
{
    public Sql Bind(IReadOnlyList<object?> values)
    {
        var sql = new Sql(Text);
        for (var i = 0; i < ParameterNames.Length; i++)
            sql.AddParameter(ParameterNames[i], values[i]);

        return sql;
    }
}

internal readonly record struct SelectSqlTemplateKey(
    Type ProviderType,
    DatabaseType DatabaseType,
    string DatabaseName,
    string TableName,
    string EscapeCharacter,
    string ParameterPrefix,
    string? Alias,
    string? Selector,
    int WhereCount,
    string? WhereColumn1,
    string? WhereAlias1,
    string? WhereColumn2,
    string? WhereAlias2,
    string? WhereColumn3,
    string? WhereAlias3,
    string? WhereColumn4,
    string? WhereAlias4,
    string? OrderByColumn,
    string? OrderByAlias,
    bool OrderByAscending,
    int? Limit,
    int? Offset);

internal static class SelectSqlTemplateCache
{
    private const int MaxEntries = 128;
    private static readonly ConcurrentDictionary<SelectSqlTemplateKey, SelectSqlTemplate> Cache = new();
    private static int entryCount;

    public static bool TryGet(SelectSqlTemplateKey key, out SelectSqlTemplate template)
        => Cache.TryGetValue(key, out template!);

    public static void TryAdd(SelectSqlTemplateKey key, SelectSqlTemplate template)
    {
        if (Cache.TryAdd(key, template) && Interlocked.Increment(ref entryCount) > MaxEntries)
        {
            Cache.Clear();
            Interlocked.Exchange(ref entryCount, 0);
        }
    }
}
