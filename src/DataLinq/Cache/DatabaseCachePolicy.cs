using System;
using System.Collections.Generic;
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

        var snapshot = new (CacheLimitType limitType, long amount)[cacheLimits.Count];
        for (var i = 0; i < cacheLimits.Count; i++)
            snapshot[i] = cacheLimits[i];

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
        if (metadata.CacheLimits.Count != 0)
            return metadata.CacheLimits;

        return metadata.UseCache
            ? EnabledDefaultCacheLimits
            : Array.Empty<(CacheLimitType limitType, long amount)>();
    }

    private static IReadOnlyList<(CacheCleanupType cleanupType, long amount)> GetCacheCleanup(DatabaseDefinition metadata)
    {
        if (metadata.CacheCleanup.Count != 0)
            return metadata.CacheCleanup;

        return metadata.UseCache
            ? EnabledDefaultCacheCleanup
            : DisabledDefaultCacheCleanup;
    }

    private static IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> GetIndexCache(DatabaseDefinition metadata)
    {
        if (metadata.IndexCache.Count != 0)
            return metadata.IndexCache;

        return metadata.UseCache
            ? EnabledDefaultIndexCache
            : Array.Empty<(IndexCacheType indexCacheType, int? amount)>();
    }

    private static Dictionary<TableDefinition, IReadOnlyList<(CacheLimitType limitType, long amount)>> GetTableCacheLimits(
        DatabaseDefinition metadata)
    {
        var result = new Dictionary<TableDefinition, IReadOnlyList<(CacheLimitType limitType, long amount)>>();

        for (var i = 0; i < metadata.TableModels.Count; i++)
        {
            var table = metadata.TableModels[i].Table;
            if (table.CacheLimits.Count != 0)
                result.Add(table, table.CacheLimits);
        }

        return result;
    }

    private static Dictionary<TableDefinition, IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>> GetTableIndexCache(
        DatabaseDefinition metadata)
    {
        var result = new Dictionary<TableDefinition, IReadOnlyList<(IndexCacheType indexCacheType, int? amount)>>();

        for (var i = 0; i < metadata.TableModels.Count; i++)
        {
            var table = metadata.TableModels[i].Table;
            if (table.IndexCache.Count != 0)
                result.Add(table, table.IndexCache);
        }

        return result;
    }

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
