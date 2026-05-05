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

    private DatabaseCachePolicy(
        IReadOnlyList<(CacheLimitType limitType, long amount)> databaseCacheLimits,
        IReadOnlyList<(CacheCleanupType cleanupType, long amount)> cacheCleanup,
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> indexCache)
    {
        DatabaseCacheLimits = databaseCacheLimits;
        CacheCleanup = cacheCleanup;
        IndexCache = indexCache;
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
            GetIndexCache(metadata));
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
}
