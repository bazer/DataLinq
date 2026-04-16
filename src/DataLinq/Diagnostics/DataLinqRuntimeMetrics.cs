using System.Threading;

namespace DataLinq.Diagnostics;

public readonly record struct DataLinqRuntimeMetricsSnapshot(
    long EntityQueryExecutions,
    long ScalarQueryExecutions,
    long RowCacheHits,
    long RowCacheMisses,
    long DatabaseRowsLoaded,
    long RowMaterializations,
    long RowCacheStores,
    long RelationReferenceCacheHits,
    long RelationReferenceLoads,
    long RelationCollectionCacheHits,
    long RelationCollectionLoads,
    long CacheNotificationSubscriptions,
    long CacheNotificationNotifySweeps,
    long CacheNotificationNotifySnapshotEntries,
    long CacheNotificationNotifyLiveSubscribers,
    long CacheNotificationCleanSweeps,
    long CacheNotificationCleanSnapshotEntries,
    long CacheNotificationCleanRequeuedSubscribers,
    long CacheNotificationCleanDroppedSubscribers,
    long CacheNotificationCleanBusySkips,
    long CacheNotificationApproximatePeakQueueDepth)
{
    public override string ToString()
        => $"entity-queries={EntityQueryExecutions}, scalar-queries={ScalarQueryExecutions}, " +
           $"row-cache-hits={RowCacheHits}, row-cache-misses={RowCacheMisses}, database-rows={DatabaseRowsLoaded}, " +
           $"materializations={RowMaterializations}, row-cache-stores={RowCacheStores}, " +
           $"relation-ref-hits={RelationReferenceCacheHits}, relation-ref-loads={RelationReferenceLoads}, " +
           $"relation-collection-hits={RelationCollectionCacheHits}, relation-collection-loads={RelationCollectionLoads}, " +
           $"cache-notification-subscriptions={CacheNotificationSubscriptions}, " +
           $"cache-notification-notify-sweeps={CacheNotificationNotifySweeps}, " +
           $"cache-notification-notify-snapshot-entries={CacheNotificationNotifySnapshotEntries}, " +
           $"cache-notification-notify-live={CacheNotificationNotifyLiveSubscribers}, " +
           $"cache-notification-clean-sweeps={CacheNotificationCleanSweeps}, " +
           $"cache-notification-clean-snapshot-entries={CacheNotificationCleanSnapshotEntries}, " +
           $"cache-notification-clean-requeued={CacheNotificationCleanRequeuedSubscribers}, " +
           $"cache-notification-clean-dropped={CacheNotificationCleanDroppedSubscribers}, " +
           $"cache-notification-clean-busy-skips={CacheNotificationCleanBusySkips}, " +
           $"cache-notification-approx-peak-depth={CacheNotificationApproximatePeakQueueDepth}";
}

public static class DataLinqRuntimeMetrics
{
    private static long _entityQueryExecutions;
    private static long _scalarQueryExecutions;
    private static long _rowCacheHits;
    private static long _rowCacheMisses;
    private static long _databaseRowsLoaded;
    private static long _rowMaterializations;
    private static long _rowCacheStores;
    private static long _relationReferenceCacheHits;
    private static long _relationReferenceLoads;
    private static long _relationCollectionCacheHits;
    private static long _relationCollectionLoads;
    private static long _cacheNotificationSubscriptions;
    private static long _cacheNotificationNotifySweeps;
    private static long _cacheNotificationNotifySnapshotEntries;
    private static long _cacheNotificationNotifyLiveSubscribers;
    private static long _cacheNotificationCleanSweeps;
    private static long _cacheNotificationCleanSnapshotEntries;
    private static long _cacheNotificationCleanRequeuedSubscribers;
    private static long _cacheNotificationCleanDroppedSubscribers;
    private static long _cacheNotificationCleanBusySkips;
    private static long _cacheNotificationApproximatePeakQueueDepth;

    public static DataLinqRuntimeMetricsSnapshot Snapshot()
        => new(
            EntityQueryExecutions: Interlocked.Read(ref _entityQueryExecutions),
            ScalarQueryExecutions: Interlocked.Read(ref _scalarQueryExecutions),
            RowCacheHits: Interlocked.Read(ref _rowCacheHits),
            RowCacheMisses: Interlocked.Read(ref _rowCacheMisses),
            DatabaseRowsLoaded: Interlocked.Read(ref _databaseRowsLoaded),
            RowMaterializations: Interlocked.Read(ref _rowMaterializations),
            RowCacheStores: Interlocked.Read(ref _rowCacheStores),
            RelationReferenceCacheHits: Interlocked.Read(ref _relationReferenceCacheHits),
            RelationReferenceLoads: Interlocked.Read(ref _relationReferenceLoads),
            RelationCollectionCacheHits: Interlocked.Read(ref _relationCollectionCacheHits),
            RelationCollectionLoads: Interlocked.Read(ref _relationCollectionLoads),
            CacheNotificationSubscriptions: Interlocked.Read(ref _cacheNotificationSubscriptions),
            CacheNotificationNotifySweeps: Interlocked.Read(ref _cacheNotificationNotifySweeps),
            CacheNotificationNotifySnapshotEntries: Interlocked.Read(ref _cacheNotificationNotifySnapshotEntries),
            CacheNotificationNotifyLiveSubscribers: Interlocked.Read(ref _cacheNotificationNotifyLiveSubscribers),
            CacheNotificationCleanSweeps: Interlocked.Read(ref _cacheNotificationCleanSweeps),
            CacheNotificationCleanSnapshotEntries: Interlocked.Read(ref _cacheNotificationCleanSnapshotEntries),
            CacheNotificationCleanRequeuedSubscribers: Interlocked.Read(ref _cacheNotificationCleanRequeuedSubscribers),
            CacheNotificationCleanDroppedSubscribers: Interlocked.Read(ref _cacheNotificationCleanDroppedSubscribers),
            CacheNotificationCleanBusySkips: Interlocked.Read(ref _cacheNotificationCleanBusySkips),
            CacheNotificationApproximatePeakQueueDepth: Interlocked.Read(ref _cacheNotificationApproximatePeakQueueDepth));

    public static void Reset()
    {
        Interlocked.Exchange(ref _entityQueryExecutions, 0);
        Interlocked.Exchange(ref _scalarQueryExecutions, 0);
        Interlocked.Exchange(ref _rowCacheHits, 0);
        Interlocked.Exchange(ref _rowCacheMisses, 0);
        Interlocked.Exchange(ref _databaseRowsLoaded, 0);
        Interlocked.Exchange(ref _rowMaterializations, 0);
        Interlocked.Exchange(ref _rowCacheStores, 0);
        Interlocked.Exchange(ref _relationReferenceCacheHits, 0);
        Interlocked.Exchange(ref _relationReferenceLoads, 0);
        Interlocked.Exchange(ref _relationCollectionCacheHits, 0);
        Interlocked.Exchange(ref _relationCollectionLoads, 0);
        Interlocked.Exchange(ref _cacheNotificationSubscriptions, 0);
        Interlocked.Exchange(ref _cacheNotificationNotifySweeps, 0);
        Interlocked.Exchange(ref _cacheNotificationNotifySnapshotEntries, 0);
        Interlocked.Exchange(ref _cacheNotificationNotifyLiveSubscribers, 0);
        Interlocked.Exchange(ref _cacheNotificationCleanSweeps, 0);
        Interlocked.Exchange(ref _cacheNotificationCleanSnapshotEntries, 0);
        Interlocked.Exchange(ref _cacheNotificationCleanRequeuedSubscribers, 0);
        Interlocked.Exchange(ref _cacheNotificationCleanDroppedSubscribers, 0);
        Interlocked.Exchange(ref _cacheNotificationCleanBusySkips, 0);
        Interlocked.Exchange(ref _cacheNotificationApproximatePeakQueueDepth, 0);
    }

    internal static void RecordEntityQueryExecution() => Interlocked.Increment(ref _entityQueryExecutions);
    internal static void RecordScalarQueryExecution() => Interlocked.Increment(ref _scalarQueryExecutions);
    internal static void RecordRowCacheHits(int count) => Interlocked.Add(ref _rowCacheHits, count);
    internal static void RecordRowCacheMisses(int count) => Interlocked.Add(ref _rowCacheMisses, count);
    internal static void RecordDatabaseRowsLoaded(int count) => Interlocked.Add(ref _databaseRowsLoaded, count);
    internal static void RecordRowMaterialization() => Interlocked.Increment(ref _rowMaterializations);
    internal static void RecordRowCacheStore() => Interlocked.Increment(ref _rowCacheStores);
    internal static void RecordRelationReferenceCacheHit() => Interlocked.Increment(ref _relationReferenceCacheHits);
    internal static void RecordRelationReferenceLoad() => Interlocked.Increment(ref _relationReferenceLoads);
    internal static void RecordRelationCollectionCacheHit() => Interlocked.Increment(ref _relationCollectionCacheHits);
    internal static void RecordRelationCollectionLoad() => Interlocked.Increment(ref _relationCollectionLoads);
    internal static void RecordCacheNotificationSubscribe(int approximateQueueDepth)
    {
        Interlocked.Increment(ref _cacheNotificationSubscriptions);
        RecordCacheNotificationApproximatePeakQueueDepth(approximateQueueDepth);
    }
    internal static void RecordCacheNotificationNotifySweep(int snapshotEntries, int liveSubscribers)
    {
        Interlocked.Increment(ref _cacheNotificationNotifySweeps);
        Interlocked.Add(ref _cacheNotificationNotifySnapshotEntries, snapshotEntries);
        Interlocked.Add(ref _cacheNotificationNotifyLiveSubscribers, liveSubscribers);
    }
    internal static void RecordCacheNotificationCleanSweep(int snapshotEntries, int requeuedSubscribers, int droppedSubscribers)
    {
        Interlocked.Increment(ref _cacheNotificationCleanSweeps);
        Interlocked.Add(ref _cacheNotificationCleanSnapshotEntries, snapshotEntries);
        Interlocked.Add(ref _cacheNotificationCleanRequeuedSubscribers, requeuedSubscribers);
        Interlocked.Add(ref _cacheNotificationCleanDroppedSubscribers, droppedSubscribers);
    }
    internal static void RecordCacheNotificationCleanBusySkip() => Interlocked.Increment(ref _cacheNotificationCleanBusySkips);

    private static void RecordCacheNotificationApproximatePeakQueueDepth(int approximateQueueDepth)
    {
        long currentPeak;
        do
        {
            currentPeak = Interlocked.Read(ref _cacheNotificationApproximatePeakQueueDepth);
            if (approximateQueueDepth <= currentPeak)
                return;
        }
        while (Interlocked.CompareExchange(ref _cacheNotificationApproximatePeakQueueDepth, approximateQueueDepth, currentPeak) != currentPeak);
    }
}
