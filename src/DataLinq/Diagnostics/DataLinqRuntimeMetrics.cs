using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using DataLinq.Interfaces;

namespace DataLinq.Diagnostics;

/// <summary>
/// Captures a process-wide snapshot of DataLinq runtime telemetry.
/// </summary>
/// <param name="EntityQueryExecutions">Total number of entity queries executed since the last reset.</param>
/// <param name="ScalarQueryExecutions">Total number of scalar queries executed since the last reset.</param>
/// <param name="RowCacheHits">Total number of row cache hits since the last reset.</param>
/// <param name="RowCacheMisses">Total number of row cache misses since the last reset.</param>
/// <param name="DatabaseRowsLoaded">Total number of rows loaded from the database since the last reset.</param>
/// <param name="RowMaterializations">Total number of rows materialized into model instances since the last reset.</param>
/// <param name="RowCacheStores">Total number of row cache store operations since the last reset.</param>
/// <param name="RelationReferenceCacheHits">Total number of relation reference cache hits since the last reset.</param>
/// <param name="RelationReferenceLoads">Total number of relation reference loads since the last reset.</param>
/// <param name="RelationCollectionCacheHits">Total number of relation collection cache hits since the last reset.</param>
/// <param name="RelationCollectionLoads">Total number of relation collection loads since the last reset.</param>
/// <param name="CacheNotificationSubscriptions">Total number of cache notification subscriptions across all providers and tables since the last reset.</param>
/// <param name="CacheNotificationApproximateCurrentQueueDepth">Approximate sum of all currently queued notification subscribers across all providers and tables.</param>
/// <param name="CacheNotificationLastNotifySnapshotEntries">Sum of each table's most recently recorded notify snapshot size.</param>
/// <param name="CacheNotificationLastNotifyLiveSubscribers">Sum of each table's most recently recorded live subscriber count during notify.</param>
/// <param name="CacheNotificationNotifySweeps">Total number of notification notify sweeps across all providers and tables since the last reset.</param>
/// <param name="CacheNotificationNotifySnapshotEntries">Total number of notification entries observed by notify sweeps since the last reset.</param>
/// <param name="CacheNotificationNotifyLiveSubscribers">Total number of live subscribers observed by notify sweeps since the last reset.</param>
/// <param name="CacheNotificationLastCleanSnapshotEntries">Sum of each table's most recently recorded clean snapshot size.</param>
/// <param name="CacheNotificationLastCleanRequeuedSubscribers">Sum of each table's most recently requeued subscriber count during clean.</param>
/// <param name="CacheNotificationLastCleanDroppedSubscribers">Sum of each table's most recently dropped subscriber count during clean.</param>
/// <param name="CacheNotificationCleanSweeps">Total number of notification clean sweeps across all providers and tables since the last reset.</param>
/// <param name="CacheNotificationCleanSnapshotEntries">Total number of notification entries inspected by clean sweeps since the last reset.</param>
/// <param name="CacheNotificationCleanRequeuedSubscribers">Total number of live subscribers requeued by clean sweeps since the last reset.</param>
/// <param name="CacheNotificationCleanDroppedSubscribers">Total number of dead subscribers dropped by clean sweeps since the last reset.</param>
/// <param name="CacheNotificationCleanBusySkips">Total number of clean sweeps skipped because notification maintenance was already busy.</param>
/// <param name="CacheNotificationApproximatePeakQueueDepth">Highest approximate queue depth observed for any single table since the last reset.</param>
/// <param name="CacheNotificationProviders">Database-provider-instance notification telemetry, each with a per-table breakdown.</param>
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
    long CacheNotificationApproximateCurrentQueueDepth,
    long CacheNotificationLastNotifySnapshotEntries,
    long CacheNotificationLastNotifyLiveSubscribers,
    long CacheNotificationNotifySweeps,
    long CacheNotificationNotifySnapshotEntries,
    long CacheNotificationNotifyLiveSubscribers,
    long CacheNotificationLastCleanSnapshotEntries,
    long CacheNotificationLastCleanRequeuedSubscribers,
    long CacheNotificationLastCleanDroppedSubscribers,
    long CacheNotificationCleanSweeps,
    long CacheNotificationCleanSnapshotEntries,
    long CacheNotificationCleanRequeuedSubscribers,
    long CacheNotificationCleanDroppedSubscribers,
    long CacheNotificationCleanBusySkips,
    long CacheNotificationApproximatePeakQueueDepth,
    DataLinqCacheNotificationProviderMetricsSnapshot[] CacheNotificationProviders)
{
    public override string ToString()
        => $"entity-queries={EntityQueryExecutions}, scalar-queries={ScalarQueryExecutions}, " +
           $"row-cache-hits={RowCacheHits}, row-cache-misses={RowCacheMisses}, database-rows={DatabaseRowsLoaded}, " +
           $"materializations={RowMaterializations}, row-cache-stores={RowCacheStores}, " +
           $"relation-ref-hits={RelationReferenceCacheHits}, relation-ref-loads={RelationReferenceLoads}, " +
           $"relation-collection-hits={RelationCollectionCacheHits}, relation-collection-loads={RelationCollectionLoads}, " +
           $"cache-notification-subscriptions={CacheNotificationSubscriptions}, " +
           $"cache-notification-approx-current-depth={CacheNotificationApproximateCurrentQueueDepth}, " +
           $"cache-notification-last-notify-snapshot-entries={CacheNotificationLastNotifySnapshotEntries}, " +
           $"cache-notification-last-notify-live={CacheNotificationLastNotifyLiveSubscribers}, " +
           $"cache-notification-notify-sweeps={CacheNotificationNotifySweeps}, " +
           $"cache-notification-notify-snapshot-entries={CacheNotificationNotifySnapshotEntries}, " +
           $"cache-notification-notify-live={CacheNotificationNotifyLiveSubscribers}, " +
           $"cache-notification-last-clean-snapshot-entries={CacheNotificationLastCleanSnapshotEntries}, " +
           $"cache-notification-last-clean-requeued={CacheNotificationLastCleanRequeuedSubscribers}, " +
           $"cache-notification-last-clean-dropped={CacheNotificationLastCleanDroppedSubscribers}, " +
           $"cache-notification-clean-sweeps={CacheNotificationCleanSweeps}, " +
           $"cache-notification-clean-snapshot-entries={CacheNotificationCleanSnapshotEntries}, " +
           $"cache-notification-clean-requeued={CacheNotificationCleanRequeuedSubscribers}, " +
           $"cache-notification-clean-dropped={CacheNotificationCleanDroppedSubscribers}, " +
           $"cache-notification-clean-busy-skips={CacheNotificationCleanBusySkips}, " +
           $"cache-notification-approx-peak-depth={CacheNotificationApproximatePeakQueueDepth}, " +
           $"cache-notification-providers={CacheNotificationProviders.Length}";
}

/// <summary>
/// Captures cache notification telemetry for a single database provider instance.
/// </summary>
/// <param name="ProviderInstanceId">Stable identifier for this database provider instance within the current process lifetime.</param>
/// <param name="ProviderTypeName">Runtime type name of the database provider instance.</param>
/// <param name="DatabaseName">Logical database name reported by the provider instance.</param>
/// <param name="DatabaseTypeName">Database type reported by the provider instance.</param>
/// <param name="Subscriptions">Total number of notification subscriptions recorded for this provider instance since the last reset.</param>
/// <param name="ApproximateCurrentQueueDepth">Approximate sum of all currently queued notification subscribers across this provider instance's tables.</param>
/// <param name="LastNotifySnapshotEntries">Sum of the most recently recorded notify snapshot sizes across this provider instance's tables.</param>
/// <param name="LastNotifyLiveSubscribers">Sum of the most recently recorded live notify subscribers across this provider instance's tables.</param>
/// <param name="NotifySweeps">Total number of notify sweeps recorded for this provider instance since the last reset.</param>
/// <param name="NotifySnapshotEntries">Total number of notify snapshot entries recorded for this provider instance since the last reset.</param>
/// <param name="NotifyLiveSubscribers">Total number of live subscribers observed during notify sweeps for this provider instance since the last reset.</param>
/// <param name="LastCleanSnapshotEntries">Sum of the most recently recorded clean snapshot sizes across this provider instance's tables.</param>
/// <param name="LastCleanRequeuedSubscribers">Sum of the most recently requeued subscriber counts across this provider instance's tables.</param>
/// <param name="LastCleanDroppedSubscribers">Sum of the most recently dropped subscriber counts across this provider instance's tables.</param>
/// <param name="CleanSweeps">Total number of clean sweeps recorded for this provider instance since the last reset.</param>
/// <param name="CleanSnapshotEntries">Total number of clean snapshot entries recorded for this provider instance since the last reset.</param>
/// <param name="CleanRequeuedSubscribers">Total number of live subscribers requeued during clean sweeps for this provider instance since the last reset.</param>
/// <param name="CleanDroppedSubscribers">Total number of dead subscribers dropped during clean sweeps for this provider instance since the last reset.</param>
/// <param name="CleanBusySkips">Total number of clean sweeps skipped for this provider instance because notification maintenance was already busy.</param>
/// <param name="ApproximatePeakQueueDepth">Highest approximate queue depth observed for any single table within this provider instance since the last reset.</param>
/// <param name="Tables">Per-table notification telemetry for this provider instance.</param>
public readonly record struct DataLinqCacheNotificationProviderMetricsSnapshot(
    string ProviderInstanceId,
    string ProviderTypeName,
    string DatabaseName,
    string DatabaseTypeName,
    long Subscriptions,
    long ApproximateCurrentQueueDepth,
    long LastNotifySnapshotEntries,
    long LastNotifyLiveSubscribers,
    long NotifySweeps,
    long NotifySnapshotEntries,
    long NotifyLiveSubscribers,
    long LastCleanSnapshotEntries,
    long LastCleanRequeuedSubscribers,
    long LastCleanDroppedSubscribers,
    long CleanSweeps,
    long CleanSnapshotEntries,
    long CleanRequeuedSubscribers,
    long CleanDroppedSubscribers,
    long CleanBusySkips,
    long ApproximatePeakQueueDepth,
    DataLinqCacheNotificationTableMetricsSnapshot[] Tables);

/// <summary>
/// Captures cache notification telemetry for a single table within a provider.
/// </summary>
/// <param name="TableName">The table name inside the provider.</param>
/// <param name="Subscriptions">Total number of notification subscriptions recorded for this table since the last reset.</param>
/// <param name="ApproximateCurrentQueueDepth">Approximate number of currently queued notification subscribers for this table.</param>
/// <param name="LastNotifySnapshotEntries">Number of entries seen in this table's most recent notify sweep.</param>
/// <param name="LastNotifyLiveSubscribers">Number of live subscribers seen in this table's most recent notify sweep.</param>
/// <param name="NotifySweeps">Total number of notify sweeps recorded for this table since the last reset.</param>
/// <param name="NotifySnapshotEntries">Total number of notify snapshot entries recorded for this table since the last reset.</param>
/// <param name="NotifyLiveSubscribers">Total number of live subscribers observed during notify sweeps for this table since the last reset.</param>
/// <param name="LastCleanSnapshotEntries">Number of entries seen in this table's most recent clean sweep.</param>
/// <param name="LastCleanRequeuedSubscribers">Number of live subscribers requeued in this table's most recent clean sweep.</param>
/// <param name="LastCleanDroppedSubscribers">Number of dead subscribers dropped in this table's most recent clean sweep.</param>
/// <param name="CleanSweeps">Total number of clean sweeps recorded for this table since the last reset.</param>
/// <param name="CleanSnapshotEntries">Total number of clean snapshot entries recorded for this table since the last reset.</param>
/// <param name="CleanRequeuedSubscribers">Total number of live subscribers requeued during clean sweeps for this table since the last reset.</param>
/// <param name="CleanDroppedSubscribers">Total number of dead subscribers dropped during clean sweeps for this table since the last reset.</param>
/// <param name="CleanBusySkips">Total number of clean sweeps skipped for this table because notification maintenance was already busy.</param>
/// <param name="ApproximatePeakQueueDepth">Highest approximate queue depth observed for this table since the last reset.</param>
public readonly record struct DataLinqCacheNotificationTableMetricsSnapshot(
    string TableName,
    long Subscriptions,
    long ApproximateCurrentQueueDepth,
    long LastNotifySnapshotEntries,
    long LastNotifyLiveSubscribers,
    long NotifySweeps,
    long NotifySnapshotEntries,
    long NotifyLiveSubscribers,
    long LastCleanSnapshotEntries,
    long LastCleanRequeuedSubscribers,
    long LastCleanDroppedSubscribers,
    long CleanSweeps,
    long CleanSnapshotEntries,
    long CleanRequeuedSubscribers,
    long CleanDroppedSubscribers,
    long CleanBusySkips,
    long ApproximatePeakQueueDepth);

internal sealed class DataLinqCacheNotificationTableMetricsHandle
{
    private readonly DataLinqCacheNotificationTableMetricsState state;

    internal DataLinqCacheNotificationTableMetricsHandle(DataLinqCacheNotificationTableMetricsState state)
    {
        this.state = state;
    }

    internal void RecordSubscribe(int approximateQueueDepth) => state.RecordSubscribe(approximateQueueDepth);
    internal void RecordNotifySweep(int snapshotEntries, int liveSubscribers, int currentQueueDepth)
        => state.RecordNotifySweep(snapshotEntries, liveSubscribers, currentQueueDepth);
    internal void RecordCleanSweep(int snapshotEntries, int requeuedSubscribers, int droppedSubscribers, int currentQueueDepth)
        => state.RecordCleanSweep(snapshotEntries, requeuedSubscribers, droppedSubscribers, currentQueueDepth);
    internal void RecordCleanBusySkip() => state.RecordCleanBusySkip();
}

internal sealed class DataLinqCacheNotificationProviderMetricsState
{
    private readonly ConcurrentDictionary<string, DataLinqCacheNotificationTableMetricsState> tableStates = new(StringComparer.Ordinal);

    internal string ProviderInstanceId { get; }
    internal string ProviderTypeName { get; }
    internal string DatabaseName { get; }
    internal string DatabaseTypeName { get; }

    internal DataLinqCacheNotificationProviderMetricsState(IDatabaseProvider databaseProvider)
    {
        ProviderInstanceId = databaseProvider.TelemetryInstanceId;
        ProviderTypeName = databaseProvider.GetType().Name;
        DatabaseName = databaseProvider.DatabaseName;
        DatabaseTypeName = databaseProvider.DatabaseType.ToString();
    }

    internal DataLinqCacheNotificationTableMetricsHandle GetOrCreateTable(string tableName)
        => new(tableStates.GetOrAdd(tableName, name => new DataLinqCacheNotificationTableMetricsState(name)));

    internal void Reset()
    {
        foreach (var tableState in tableStates.Values)
            tableState.Reset();
    }

    internal bool HasActivity()
        => tableStates.Values.Any(x => x.HasActivity());

    internal DataLinqCacheNotificationProviderMetricsSnapshot Snapshot()
    {
        var tables = tableStates.Values
            .Where(x => x.HasActivity())
            .Select(x => x.Snapshot())
            .OrderBy(x => x.TableName, StringComparer.Ordinal)
            .ToArray();

        return new DataLinqCacheNotificationProviderMetricsSnapshot(
            ProviderInstanceId: ProviderInstanceId,
            ProviderTypeName: ProviderTypeName,
            DatabaseName: DatabaseName,
            DatabaseTypeName: DatabaseTypeName,
            Subscriptions: tables.Sum(x => x.Subscriptions),
            ApproximateCurrentQueueDepth: tables.Sum(x => x.ApproximateCurrentQueueDepth),
            LastNotifySnapshotEntries: tables.Sum(x => x.LastNotifySnapshotEntries),
            LastNotifyLiveSubscribers: tables.Sum(x => x.LastNotifyLiveSubscribers),
            NotifySweeps: tables.Sum(x => x.NotifySweeps),
            NotifySnapshotEntries: tables.Sum(x => x.NotifySnapshotEntries),
            NotifyLiveSubscribers: tables.Sum(x => x.NotifyLiveSubscribers),
            LastCleanSnapshotEntries: tables.Sum(x => x.LastCleanSnapshotEntries),
            LastCleanRequeuedSubscribers: tables.Sum(x => x.LastCleanRequeuedSubscribers),
            LastCleanDroppedSubscribers: tables.Sum(x => x.LastCleanDroppedSubscribers),
            CleanSweeps: tables.Sum(x => x.CleanSweeps),
            CleanSnapshotEntries: tables.Sum(x => x.CleanSnapshotEntries),
            CleanRequeuedSubscribers: tables.Sum(x => x.CleanRequeuedSubscribers),
            CleanDroppedSubscribers: tables.Sum(x => x.CleanDroppedSubscribers),
            CleanBusySkips: tables.Sum(x => x.CleanBusySkips),
            ApproximatePeakQueueDepth: tables.Length == 0 ? 0 : tables.Max(x => x.ApproximatePeakQueueDepth),
            Tables: tables);
    }
}

internal sealed class DataLinqCacheNotificationTableMetricsState
{
    private long subscriptions;
    private long approximateCurrentQueueDepth;
    private long lastNotifySnapshotEntries;
    private long lastNotifyLiveSubscribers;
    private long notifySweeps;
    private long notifySnapshotEntries;
    private long notifyLiveSubscribers;
    private long lastCleanSnapshotEntries;
    private long lastCleanRequeuedSubscribers;
    private long lastCleanDroppedSubscribers;
    private long cleanSweeps;
    private long cleanSnapshotEntries;
    private long cleanRequeuedSubscribers;
    private long cleanDroppedSubscribers;
    private long cleanBusySkips;
    private long approximatePeakQueueDepth;

    internal string TableName { get; }

    internal DataLinqCacheNotificationTableMetricsState(string tableName)
    {
        TableName = tableName;
    }

    internal void RecordSubscribe(int approximateQueueDepth)
    {
        Interlocked.Increment(ref subscriptions);
        Interlocked.Exchange(ref approximateCurrentQueueDepth, approximateQueueDepth);
        RecordApproximatePeakQueueDepth(approximateQueueDepth);
    }

    internal void RecordNotifySweep(int snapshotEntries, int liveSubscribers, int currentQueueDepth)
    {
        Interlocked.Increment(ref notifySweeps);
        Interlocked.Exchange(ref approximateCurrentQueueDepth, currentQueueDepth);
        Interlocked.Exchange(ref lastNotifySnapshotEntries, snapshotEntries);
        Interlocked.Exchange(ref lastNotifyLiveSubscribers, liveSubscribers);
        Interlocked.Add(ref notifySnapshotEntries, snapshotEntries);
        Interlocked.Add(ref notifyLiveSubscribers, liveSubscribers);
    }

    internal void RecordCleanSweep(int snapshotEntries, int requeuedSubscribers, int droppedSubscribers, int currentQueueDepth)
    {
        Interlocked.Increment(ref cleanSweeps);
        Interlocked.Exchange(ref approximateCurrentQueueDepth, currentQueueDepth);
        Interlocked.Exchange(ref lastCleanSnapshotEntries, snapshotEntries);
        Interlocked.Exchange(ref lastCleanRequeuedSubscribers, requeuedSubscribers);
        Interlocked.Exchange(ref lastCleanDroppedSubscribers, droppedSubscribers);
        Interlocked.Add(ref cleanSnapshotEntries, snapshotEntries);
        Interlocked.Add(ref cleanRequeuedSubscribers, requeuedSubscribers);
        Interlocked.Add(ref cleanDroppedSubscribers, droppedSubscribers);
    }

    internal void RecordCleanBusySkip()
        => Interlocked.Increment(ref cleanBusySkips);

    internal void Reset()
    {
        Interlocked.Exchange(ref subscriptions, 0);
        Interlocked.Exchange(ref approximateCurrentQueueDepth, 0);
        Interlocked.Exchange(ref lastNotifySnapshotEntries, 0);
        Interlocked.Exchange(ref lastNotifyLiveSubscribers, 0);
        Interlocked.Exchange(ref notifySweeps, 0);
        Interlocked.Exchange(ref notifySnapshotEntries, 0);
        Interlocked.Exchange(ref notifyLiveSubscribers, 0);
        Interlocked.Exchange(ref lastCleanSnapshotEntries, 0);
        Interlocked.Exchange(ref lastCleanRequeuedSubscribers, 0);
        Interlocked.Exchange(ref lastCleanDroppedSubscribers, 0);
        Interlocked.Exchange(ref cleanSweeps, 0);
        Interlocked.Exchange(ref cleanSnapshotEntries, 0);
        Interlocked.Exchange(ref cleanRequeuedSubscribers, 0);
        Interlocked.Exchange(ref cleanDroppedSubscribers, 0);
        Interlocked.Exchange(ref cleanBusySkips, 0);
        Interlocked.Exchange(ref approximatePeakQueueDepth, 0);
    }

    internal bool HasActivity()
        => Interlocked.Read(ref subscriptions) != 0 ||
           Interlocked.Read(ref approximateCurrentQueueDepth) != 0 ||
           Interlocked.Read(ref notifySweeps) != 0 ||
           Interlocked.Read(ref cleanSweeps) != 0 ||
           Interlocked.Read(ref cleanBusySkips) != 0;

    internal DataLinqCacheNotificationTableMetricsSnapshot Snapshot()
        => new(
            TableName: TableName,
            Subscriptions: Interlocked.Read(ref subscriptions),
            ApproximateCurrentQueueDepth: Interlocked.Read(ref approximateCurrentQueueDepth),
            LastNotifySnapshotEntries: Interlocked.Read(ref lastNotifySnapshotEntries),
            LastNotifyLiveSubscribers: Interlocked.Read(ref lastNotifyLiveSubscribers),
            NotifySweeps: Interlocked.Read(ref notifySweeps),
            NotifySnapshotEntries: Interlocked.Read(ref notifySnapshotEntries),
            NotifyLiveSubscribers: Interlocked.Read(ref notifyLiveSubscribers),
            LastCleanSnapshotEntries: Interlocked.Read(ref lastCleanSnapshotEntries),
            LastCleanRequeuedSubscribers: Interlocked.Read(ref lastCleanRequeuedSubscribers),
            LastCleanDroppedSubscribers: Interlocked.Read(ref lastCleanDroppedSubscribers),
            CleanSweeps: Interlocked.Read(ref cleanSweeps),
            CleanSnapshotEntries: Interlocked.Read(ref cleanSnapshotEntries),
            CleanRequeuedSubscribers: Interlocked.Read(ref cleanRequeuedSubscribers),
            CleanDroppedSubscribers: Interlocked.Read(ref cleanDroppedSubscribers),
            CleanBusySkips: Interlocked.Read(ref cleanBusySkips),
            ApproximatePeakQueueDepth: Interlocked.Read(ref approximatePeakQueueDepth));

    private void RecordApproximatePeakQueueDepth(int approximateQueueDepth)
    {
        long currentPeak;
        do
        {
            currentPeak = Interlocked.Read(ref approximatePeakQueueDepth);
            if (approximateQueueDepth <= currentPeak)
                return;
        }
        while (Interlocked.CompareExchange(ref approximatePeakQueueDepth, approximateQueueDepth, currentPeak) != currentPeak);
    }
}

public static class DataLinqRuntimeMetrics
{
    private static long entityQueryExecutions;
    private static long scalarQueryExecutions;
    private static long rowCacheHits;
    private static long rowCacheMisses;
    private static long databaseRowsLoaded;
    private static long rowMaterializations;
    private static long rowCacheStores;
    private static long relationReferenceCacheHits;
    private static long relationReferenceLoads;
    private static long relationCollectionCacheHits;
    private static long relationCollectionLoads;

    private static readonly ConcurrentDictionary<string, DataLinqCacheNotificationProviderMetricsState> cacheNotificationProviders
        = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a process-wide snapshot of the current DataLinq runtime telemetry.
    /// </summary>
    public static DataLinqRuntimeMetricsSnapshot Snapshot()
    {
        var providerSnapshots = cacheNotificationProviders.Values
            .Where(x => x.HasActivity())
            .Select(x => x.Snapshot())
            .OrderBy(x => x.DatabaseName, StringComparer.Ordinal)
            .ThenBy(x => x.ProviderTypeName, StringComparer.Ordinal)
            .ThenBy(x => x.ProviderInstanceId, StringComparer.Ordinal)
            .ToArray();

        return new DataLinqRuntimeMetricsSnapshot(
            EntityQueryExecutions: Interlocked.Read(ref entityQueryExecutions),
            ScalarQueryExecutions: Interlocked.Read(ref scalarQueryExecutions),
            RowCacheHits: Interlocked.Read(ref rowCacheHits),
            RowCacheMisses: Interlocked.Read(ref rowCacheMisses),
            DatabaseRowsLoaded: Interlocked.Read(ref databaseRowsLoaded),
            RowMaterializations: Interlocked.Read(ref rowMaterializations),
            RowCacheStores: Interlocked.Read(ref rowCacheStores),
            RelationReferenceCacheHits: Interlocked.Read(ref relationReferenceCacheHits),
            RelationReferenceLoads: Interlocked.Read(ref relationReferenceLoads),
            RelationCollectionCacheHits: Interlocked.Read(ref relationCollectionCacheHits),
            RelationCollectionLoads: Interlocked.Read(ref relationCollectionLoads),
            CacheNotificationSubscriptions: providerSnapshots.Sum(x => x.Subscriptions),
            CacheNotificationApproximateCurrentQueueDepth: providerSnapshots.Sum(x => x.ApproximateCurrentQueueDepth),
            CacheNotificationLastNotifySnapshotEntries: providerSnapshots.Sum(x => x.LastNotifySnapshotEntries),
            CacheNotificationLastNotifyLiveSubscribers: providerSnapshots.Sum(x => x.LastNotifyLiveSubscribers),
            CacheNotificationNotifySweeps: providerSnapshots.Sum(x => x.NotifySweeps),
            CacheNotificationNotifySnapshotEntries: providerSnapshots.Sum(x => x.NotifySnapshotEntries),
            CacheNotificationNotifyLiveSubscribers: providerSnapshots.Sum(x => x.NotifyLiveSubscribers),
            CacheNotificationLastCleanSnapshotEntries: providerSnapshots.Sum(x => x.LastCleanSnapshotEntries),
            CacheNotificationLastCleanRequeuedSubscribers: providerSnapshots.Sum(x => x.LastCleanRequeuedSubscribers),
            CacheNotificationLastCleanDroppedSubscribers: providerSnapshots.Sum(x => x.LastCleanDroppedSubscribers),
            CacheNotificationCleanSweeps: providerSnapshots.Sum(x => x.CleanSweeps),
            CacheNotificationCleanSnapshotEntries: providerSnapshots.Sum(x => x.CleanSnapshotEntries),
            CacheNotificationCleanRequeuedSubscribers: providerSnapshots.Sum(x => x.CleanRequeuedSubscribers),
            CacheNotificationCleanDroppedSubscribers: providerSnapshots.Sum(x => x.CleanDroppedSubscribers),
            CacheNotificationCleanBusySkips: providerSnapshots.Sum(x => x.CleanBusySkips),
            CacheNotificationApproximatePeakQueueDepth: providerSnapshots.Length == 0 ? 0 : providerSnapshots.Max(x => x.ApproximatePeakQueueDepth),
            CacheNotificationProviders: providerSnapshots);
    }

    /// <summary>
    /// Resets all runtime telemetry counters currently tracked by this process.
    /// </summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref entityQueryExecutions, 0);
        Interlocked.Exchange(ref scalarQueryExecutions, 0);
        Interlocked.Exchange(ref rowCacheHits, 0);
        Interlocked.Exchange(ref rowCacheMisses, 0);
        Interlocked.Exchange(ref databaseRowsLoaded, 0);
        Interlocked.Exchange(ref rowMaterializations, 0);
        Interlocked.Exchange(ref rowCacheStores, 0);
        Interlocked.Exchange(ref relationReferenceCacheHits, 0);
        Interlocked.Exchange(ref relationReferenceLoads, 0);
        Interlocked.Exchange(ref relationCollectionCacheHits, 0);
        Interlocked.Exchange(ref relationCollectionLoads, 0);

        foreach (var providerState in cacheNotificationProviders.Values)
            providerState.Reset();
    }

    internal static void RecordEntityQueryExecution() => Interlocked.Increment(ref entityQueryExecutions);
    internal static void RecordScalarQueryExecution() => Interlocked.Increment(ref scalarQueryExecutions);
    internal static void RecordRowCacheHits(int count) => Interlocked.Add(ref rowCacheHits, count);
    internal static void RecordRowCacheMisses(int count) => Interlocked.Add(ref rowCacheMisses, count);
    internal static void RecordDatabaseRowsLoaded(int count) => Interlocked.Add(ref databaseRowsLoaded, count);
    internal static void RecordRowMaterialization() => Interlocked.Increment(ref rowMaterializations);
    internal static void RecordRowCacheStore() => Interlocked.Increment(ref rowCacheStores);
    internal static void RecordRelationReferenceCacheHit() => Interlocked.Increment(ref relationReferenceCacheHits);
    internal static void RecordRelationReferenceLoad() => Interlocked.Increment(ref relationReferenceLoads);
    internal static void RecordRelationCollectionCacheHit() => Interlocked.Increment(ref relationCollectionCacheHits);
    internal static void RecordRelationCollectionLoad() => Interlocked.Increment(ref relationCollectionLoads);

    internal static DataLinqCacheNotificationTableMetricsHandle RegisterCacheNotificationTable(IDatabaseProvider databaseProvider, string tableName)
        => cacheNotificationProviders
            .GetOrAdd(databaseProvider.TelemetryInstanceId, _ => new DataLinqCacheNotificationProviderMetricsState(databaseProvider))
            .GetOrCreateTable(tableName);

    internal static void RecordCacheNotificationSubscribe(DataLinqCacheNotificationTableMetricsHandle metricsHandle, int approximateQueueDepth)
        => metricsHandle.RecordSubscribe(approximateQueueDepth);

    internal static void RecordCacheNotificationNotifySweep(
        DataLinqCacheNotificationTableMetricsHandle metricsHandle,
        int snapshotEntries,
        int liveSubscribers,
        int currentQueueDepth)
        => metricsHandle.RecordNotifySweep(snapshotEntries, liveSubscribers, currentQueueDepth);

    internal static void RecordCacheNotificationCleanSweep(
        DataLinqCacheNotificationTableMetricsHandle metricsHandle,
        int snapshotEntries,
        int requeuedSubscribers,
        int droppedSubscribers,
        int currentQueueDepth)
        => metricsHandle.RecordCleanSweep(snapshotEntries, requeuedSubscribers, droppedSubscribers, currentQueueDepth);

    internal static void RecordCacheNotificationCleanBusySkip(DataLinqCacheNotificationTableMetricsHandle metricsHandle)
        => metricsHandle.RecordCleanBusySkip();
}
