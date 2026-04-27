using System;
using System.Collections.Concurrent;
using System.Threading;
using DataLinq.Metadata;

namespace DataLinq.Query;

internal sealed record SelectSqlTemplate(string Text, string ParameterName)
{
    public Sql Bind(object? value)
        => new Sql(Text).AddParameter(ParameterName, value);
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
    string WhereColumn,
    string? WhereAlias,
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
