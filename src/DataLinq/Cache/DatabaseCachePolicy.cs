using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Cache;

internal sealed class DatabaseCachePolicy
{
    private static readonly (CacheLimitType limitType, long amount)[] EnabledDefaultCacheLimits =
    [
        (CacheLimitType.Megabytes, 256L),
        (CacheLimitType.Minutes, 30L),
    ];

    private static readonly (CacheCleanupType cleanupType, long amount)[] EnabledDefaultCacheCleanup =
    [
        (CacheCleanupType.Minutes, 10L),
    ];

    private static readonly (CacheCleanupType cleanupType, long amount)[] DisabledDefaultCacheCleanup =
    [
        (CacheCleanupType.Minutes, 5L),
    ];

    private static readonly (IndexCacheType indexCacheType, int? amount)[] EnabledDefaultIndexCache =
    [
        (IndexCacheType.MaxAmountRows, 1000000),
    ];

    private static readonly (CacheLimitType limitType, long amount)[] EmptyCacheLimits = [];
    private readonly Dictionary<TableDefinition, IReadOnlyList<(CacheLimitType limitType, long amount)>> tableCacheLimits;
    private readonly Dictionary<TableDefinition, IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>> tableIndexCache;
    private readonly Dictionary<TableDefinition, IReadOnlyList<(CacheLimitType limitType, long amount)>> tableCacheLimitOverrides = new();
    private readonly object overrideGate = new();

    private DatabaseCachePolicy(
        IReadOnlyList<(CacheLimitType limitType, long amount)> databaseCacheLimits,
        IReadOnlyList<(CacheCleanupType cleanupType, long amount)> cacheCleanup,
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> indexCache,
        Dictionary<TableDefinition, IReadOnlyList<(CacheLimitType limitType, long amount)>> tableCacheLimits,
        Dictionary<TableDefinition, IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>> tableIndexCache)
    {
        DatabaseCacheLimits = databaseCacheLimits;
        CacheCleanup = cacheCleanup;
        IndexCache = indexCache;
        this.tableCacheLimits = tableCacheLimits;
        this.tableIndexCache = tableIndexCache;
    }

    public IReadOnlyList<(CacheLimitType limitType, long amount)> DatabaseCacheLimits { get; }
    public IReadOnlyList<(CacheCleanupType cleanupType, long amount)> CacheCleanup { get; }
    public IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> IndexCache { get; }

    public static DatabaseCachePolicy FromMetadata(DatabaseDefinition metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new DatabaseCachePolicy(
            GetDatabaseCacheLimits(metadata),
            GetCacheCleanup(metadata),
            GetIndexCache(metadata),
            GetTableCacheLimits(metadata),
            GetTableIndexCache(metadata));
    }

    public IReadOnlyList<(CacheLimitType limitType, long amount)> GetTableCacheLimits(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);

        lock (overrideGate)
        {
            if (tableCacheLimitOverrides.TryGetValue(table, out var overrideLimits))
                return overrideLimits;
        }

        return tableCacheLimits.TryGetValue(table, out var limits)
            ? limits
            : EmptyCacheLimits;
    }

    public IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> GetTableIndexCache(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return tableIndexCache.TryGetValue(table, out var policy)
            ? policy
            : IndexCache;
    }

    internal IDisposable OverrideTableCacheLimitsForTesting(
        TableDefinition table,
        IReadOnlyList<(CacheLimitType limitType, long amount)> cacheLimits)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(cacheLimits);

        var snapshot = cacheLimits.ToArray();
        bool hadPrevious;
        IReadOnlyList<(CacheLimitType limitType, long amount)>? previous;

        lock (overrideGate)
        {
            hadPrevious = tableCacheLimitOverrides.TryGetValue(table, out previous);
            tableCacheLimitOverrides[table] = snapshot;
        }

        return new PolicyOverride(() =>
        {
            lock (overrideGate)
            {
                if (hadPrevious)
                    tableCacheLimitOverrides[table] = previous!;
                else
                    tableCacheLimitOverrides.Remove(table);
            }
        });
    }

    private static IReadOnlyList<(CacheLimitType limitType, long amount)> GetDatabaseCacheLimits(DatabaseDefinition metadata)
    {
        if (metadata.CacheLimits.Any())
            return metadata.CacheLimits.ToArray();

        return metadata.UseCache
            ? EnabledDefaultCacheLimits
            : Array.Empty<(CacheLimitType limitType, long amount)>();
    }

    private static IReadOnlyList<(CacheCleanupType cleanupType, long amount)> GetCacheCleanup(DatabaseDefinition metadata)
    {
        if (metadata.CacheCleanup.Any())
            return metadata.CacheCleanup.ToArray();

        return metadata.UseCache
            ? EnabledDefaultCacheCleanup
            : DisabledDefaultCacheCleanup;
    }

    private static IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> GetIndexCache(DatabaseDefinition metadata)
    {
        if (metadata.IndexCache.Any())
            return metadata.IndexCache.ToArray();

        return metadata.UseCache
            ? EnabledDefaultIndexCache
            : Array.Empty<(IndexCacheType indexCacheType, int? amount)>();
    }

    private static Dictionary<TableDefinition, IReadOnlyList<(CacheLimitType limitType, long amount)>> GetTableCacheLimits(
        DatabaseDefinition metadata) =>
        metadata.TableModels
            .Select(tableModel => tableModel.Table)
            .Where(table => table.CacheLimits.Any())
            .ToDictionary(
                table => table,
                table => (IReadOnlyList<(CacheLimitType limitType, long amount)>)table.CacheLimits.ToArray());

    private static Dictionary<TableDefinition, IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>> GetTableIndexCache(
        DatabaseDefinition metadata) =>
        metadata.TableModels
            .Select(tableModel => tableModel.Table)
            .Where(table => table.IndexCache.Any())
            .ToDictionary(
                table => table,
                table => (IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>)table.IndexCache.ToArray());

    private sealed class PolicyOverride(Action dispose) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            dispose();
        }
    }
}
