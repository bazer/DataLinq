using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using DataLinq.Interfaces;

namespace DataLinq.Diagnostics;

internal sealed class DataLinqTableMetricsHandle
{
    private readonly DataLinqTableMetricsState state;

    internal DataLinqTableMetricsHandle(DataLinqTableMetricsState state)
    {
        this.state = state;
    }

    internal void RecordRowCacheHits(int count) => state.RecordRowCacheHits(count);
    internal void RecordRowCacheMisses(int count) => state.RecordRowCacheMisses(count);
    internal void RecordDatabaseRowsLoaded(int count) => state.RecordDatabaseRowsLoaded(count);
    internal void RecordRowMaterialization() => state.RecordRowMaterialization();
    internal void RecordRowCacheStore() => state.RecordRowCacheStore();

    internal void RecordRelationReferenceCacheHit() => state.RecordRelationReferenceCacheHit();
    internal void RecordRelationReferenceLoad() => state.RecordRelationReferenceLoad();
    internal void RecordRelationCollectionCacheHit() => state.RecordRelationCollectionCacheHit();
    internal void RecordRelationCollectionLoad() => state.RecordRelationCollectionLoad();

    internal void RecordCacheNotificationSubscribe(int approximateQueueDepth) => state.RecordCacheNotificationSubscribe(approximateQueueDepth);
    internal void RecordCacheNotificationNotifySweep(int snapshotEntries, int liveSubscribers, int currentQueueDepth)
        => state.RecordCacheNotificationNotifySweep(snapshotEntries, liveSubscribers, currentQueueDepth);
    internal void RecordCacheNotificationCleanSweep(int snapshotEntries, int requeuedSubscribers, int droppedSubscribers, int currentQueueDepth)
        => state.RecordCacheNotificationCleanSweep(snapshotEntries, requeuedSubscribers, droppedSubscribers, currentQueueDepth);
    internal void RecordCacheNotificationCleanBusySkip() => state.RecordCacheNotificationCleanBusySkip();
}

internal sealed class DataLinqProviderMetricsState
{
    private readonly ConcurrentDictionary<string, DataLinqTableMetricsState> tableStates = new(StringComparer.Ordinal);
    private long entityExecutions;
    private long scalarExecutions;
    private long readerCommandExecutions;
    private long scalarCommandExecutions;
    private long nonQueryCommandExecutions;
    private long commandFailures;
    private long commandDurationMicroseconds;
    private long transactionStarts;
    private long transactionCommits;
    private long transactionRollbacks;
    private long transactionFailures;
    private long transactionDurationMicroseconds;

    internal string ProviderInstanceId { get; }
    internal string ProviderTypeName { get; }
    internal string DatabaseName { get; }
    internal DatabaseType DatabaseType { get; }

    internal DataLinqProviderMetricsState(IDatabaseProvider databaseProvider)
        : this(DataLinqTelemetryContext.FromProvider(databaseProvider))
    {
    }

    internal DataLinqProviderMetricsState(DataLinqTelemetryContext telemetryContext)
    {
        ProviderInstanceId = telemetryContext.ProviderInstanceId;
        ProviderTypeName = telemetryContext.ProviderTypeName;
        DatabaseName = telemetryContext.DatabaseName;
        DatabaseType = telemetryContext.DatabaseType;
    }

    internal DataLinqTableMetricsHandle GetOrCreateTable(string tableName)
        => new(tableStates.GetOrAdd(tableName, name => new DataLinqTableMetricsState(name)));

    internal void RecordEntityQueryExecution() => Interlocked.Increment(ref entityExecutions);
    internal void RecordScalarQueryExecution() => Interlocked.Increment(ref scalarExecutions);

    internal void RecordCommandExecution(string commandKind, bool succeeded, TimeSpan duration)
    {
        switch (commandKind)
        {
            case "reader":
                Interlocked.Increment(ref readerCommandExecutions);
                break;
            case "scalar":
                Interlocked.Increment(ref scalarCommandExecutions);
                break;
            case "non_query":
                Interlocked.Increment(ref nonQueryCommandExecutions);
                break;
        }

        if (!succeeded)
            Interlocked.Increment(ref commandFailures);

        Interlocked.Add(ref commandDurationMicroseconds, ToMicroseconds(duration));
    }

    internal void RecordTransactionStarted() => Interlocked.Increment(ref transactionStarts);

    internal void RecordTransactionCompleted(DatabaseTransactionStatus outcome, bool succeeded, TimeSpan duration)
    {
        if (succeeded)
        {
            switch (outcome)
            {
                case DatabaseTransactionStatus.Committed:
                    Interlocked.Increment(ref transactionCommits);
                    break;
                case DatabaseTransactionStatus.RolledBack:
                    Interlocked.Increment(ref transactionRollbacks);
                    break;
            }
        }
        else
        {
            Interlocked.Increment(ref transactionFailures);
        }

        Interlocked.Add(ref transactionDurationMicroseconds, ToMicroseconds(duration));
    }

    internal void Reset()
    {
        Interlocked.Exchange(ref entityExecutions, 0);
        Interlocked.Exchange(ref scalarExecutions, 0);
        Interlocked.Exchange(ref readerCommandExecutions, 0);
        Interlocked.Exchange(ref scalarCommandExecutions, 0);
        Interlocked.Exchange(ref nonQueryCommandExecutions, 0);
        Interlocked.Exchange(ref commandFailures, 0);
        Interlocked.Exchange(ref commandDurationMicroseconds, 0);
        Interlocked.Exchange(ref transactionStarts, 0);
        Interlocked.Exchange(ref transactionCommits, 0);
        Interlocked.Exchange(ref transactionRollbacks, 0);
        Interlocked.Exchange(ref transactionFailures, 0);
        Interlocked.Exchange(ref transactionDurationMicroseconds, 0);

        foreach (var tableState in tableStates.Values)
            tableState.Reset();
    }

    internal bool HasActivity()
        => Interlocked.Read(ref entityExecutions) != 0 ||
           Interlocked.Read(ref scalarExecutions) != 0 ||
           Interlocked.Read(ref readerCommandExecutions) != 0 ||
           Interlocked.Read(ref scalarCommandExecutions) != 0 ||
           Interlocked.Read(ref nonQueryCommandExecutions) != 0 ||
           Interlocked.Read(ref commandFailures) != 0 ||
           Interlocked.Read(ref transactionStarts) != 0 ||
           Interlocked.Read(ref transactionCommits) != 0 ||
           Interlocked.Read(ref transactionRollbacks) != 0 ||
           Interlocked.Read(ref transactionFailures) != 0 ||
           tableStates.Values.Any(x => x.HasActivity());

    internal DataLinqProviderMetricsSnapshot Snapshot()
    {
        var tables = tableStates.Values
            .Where(x => x.HasActivity())
            .Select(x => x.Snapshot())
            .OrderBy(x => x.TableName, StringComparer.Ordinal)
            .ToArray();

        return new DataLinqProviderMetricsSnapshot(
            ProviderInstanceId: ProviderInstanceId,
            ProviderTypeName: ProviderTypeName,
            DatabaseName: DatabaseName,
            DatabaseType: DatabaseType,
            Queries: new QueryMetricsSnapshot(
                EntityExecutions: Interlocked.Read(ref entityExecutions),
                ScalarExecutions: Interlocked.Read(ref scalarExecutions)),
            Commands: new CommandMetricsSnapshot(
                ReaderExecutions: Interlocked.Read(ref readerCommandExecutions),
                ScalarExecutions: Interlocked.Read(ref scalarCommandExecutions),
                NonQueryExecutions: Interlocked.Read(ref nonQueryCommandExecutions),
                Failures: Interlocked.Read(ref commandFailures),
                TotalDurationMicroseconds: Interlocked.Read(ref commandDurationMicroseconds)),
            Transactions: new TransactionMetricsSnapshot(
                Starts: Interlocked.Read(ref transactionStarts),
                Commits: Interlocked.Read(ref transactionCommits),
                Rollbacks: Interlocked.Read(ref transactionRollbacks),
                Failures: Interlocked.Read(ref transactionFailures),
                TotalDurationMicroseconds: Interlocked.Read(ref transactionDurationMicroseconds)),
            Relations: RelationMetricsSnapshot.Sum(tables.Select(x => x.Relations)),
            RowCache: RowCacheMetricsSnapshot.Sum(tables.Select(x => x.RowCache)),
            CacheNotifications: CacheNotificationMetricsSnapshot.Sum(tables.Select(x => x.CacheNotifications)),
            Tables: tables);
    }

    private static long ToMicroseconds(TimeSpan duration)
        => (long)Math.Round(duration.TotalMilliseconds * 1000d, MidpointRounding.AwayFromZero);
}

internal sealed class DataLinqTableMetricsState
{
    private long rowCacheHits;
    private long rowCacheMisses;
    private long databaseRowsLoaded;
    private long rowMaterializations;
    private long rowCacheStores;

    private long relationReferenceCacheHits;
    private long relationReferenceLoads;
    private long relationCollectionCacheHits;
    private long relationCollectionLoads;

    private long cacheNotificationSubscriptions;
    private long cacheNotificationApproximateCurrentQueueDepth;
    private long cacheNotificationLastNotifySnapshotEntries;
    private long cacheNotificationLastNotifyLiveSubscribers;
    private long cacheNotificationNotifySweeps;
    private long cacheNotificationNotifySnapshotEntries;
    private long cacheNotificationNotifyLiveSubscribers;
    private long cacheNotificationLastCleanSnapshotEntries;
    private long cacheNotificationLastCleanRequeuedSubscribers;
    private long cacheNotificationLastCleanDroppedSubscribers;
    private long cacheNotificationCleanSweeps;
    private long cacheNotificationCleanSnapshotEntries;
    private long cacheNotificationCleanRequeuedSubscribers;
    private long cacheNotificationCleanDroppedSubscribers;
    private long cacheNotificationCleanBusySkips;
    private long cacheNotificationApproximatePeakQueueDepth;

    internal string TableName { get; }

    internal DataLinqTableMetricsState(string tableName)
    {
        TableName = tableName;
    }

    internal void RecordRowCacheHits(int count) => Interlocked.Add(ref rowCacheHits, count);
    internal void RecordRowCacheMisses(int count) => Interlocked.Add(ref rowCacheMisses, count);
    internal void RecordDatabaseRowsLoaded(int count) => Interlocked.Add(ref databaseRowsLoaded, count);
    internal void RecordRowMaterialization() => Interlocked.Increment(ref rowMaterializations);
    internal void RecordRowCacheStore() => Interlocked.Increment(ref rowCacheStores);

    internal void RecordRelationReferenceCacheHit() => Interlocked.Increment(ref relationReferenceCacheHits);
    internal void RecordRelationReferenceLoad() => Interlocked.Increment(ref relationReferenceLoads);
    internal void RecordRelationCollectionCacheHit() => Interlocked.Increment(ref relationCollectionCacheHits);
    internal void RecordRelationCollectionLoad() => Interlocked.Increment(ref relationCollectionLoads);

    internal void RecordCacheNotificationSubscribe(int approximateQueueDepth)
    {
        Interlocked.Increment(ref cacheNotificationSubscriptions);
        Interlocked.Exchange(ref cacheNotificationApproximateCurrentQueueDepth, approximateQueueDepth);
        RecordCacheNotificationApproximatePeakQueueDepth(approximateQueueDepth);
    }

    internal void RecordCacheNotificationNotifySweep(int snapshotEntries, int liveSubscribers, int currentQueueDepth)
    {
        Interlocked.Increment(ref cacheNotificationNotifySweeps);
        Interlocked.Exchange(ref cacheNotificationApproximateCurrentQueueDepth, currentQueueDepth);
        Interlocked.Exchange(ref cacheNotificationLastNotifySnapshotEntries, snapshotEntries);
        Interlocked.Exchange(ref cacheNotificationLastNotifyLiveSubscribers, liveSubscribers);
        Interlocked.Add(ref cacheNotificationNotifySnapshotEntries, snapshotEntries);
        Interlocked.Add(ref cacheNotificationNotifyLiveSubscribers, liveSubscribers);
    }

    internal void RecordCacheNotificationCleanSweep(int snapshotEntries, int requeuedSubscribers, int droppedSubscribers, int currentQueueDepth)
    {
        Interlocked.Increment(ref cacheNotificationCleanSweeps);
        Interlocked.Exchange(ref cacheNotificationApproximateCurrentQueueDepth, currentQueueDepth);
        Interlocked.Exchange(ref cacheNotificationLastCleanSnapshotEntries, snapshotEntries);
        Interlocked.Exchange(ref cacheNotificationLastCleanRequeuedSubscribers, requeuedSubscribers);
        Interlocked.Exchange(ref cacheNotificationLastCleanDroppedSubscribers, droppedSubscribers);
        Interlocked.Add(ref cacheNotificationCleanSnapshotEntries, snapshotEntries);
        Interlocked.Add(ref cacheNotificationCleanRequeuedSubscribers, requeuedSubscribers);
        Interlocked.Add(ref cacheNotificationCleanDroppedSubscribers, droppedSubscribers);
    }

    internal void RecordCacheNotificationCleanBusySkip() => Interlocked.Increment(ref cacheNotificationCleanBusySkips);

    internal void Reset()
    {
        Interlocked.Exchange(ref rowCacheHits, 0);
        Interlocked.Exchange(ref rowCacheMisses, 0);
        Interlocked.Exchange(ref databaseRowsLoaded, 0);
        Interlocked.Exchange(ref rowMaterializations, 0);
        Interlocked.Exchange(ref rowCacheStores, 0);

        Interlocked.Exchange(ref relationReferenceCacheHits, 0);
        Interlocked.Exchange(ref relationReferenceLoads, 0);
        Interlocked.Exchange(ref relationCollectionCacheHits, 0);
        Interlocked.Exchange(ref relationCollectionLoads, 0);

        Interlocked.Exchange(ref cacheNotificationSubscriptions, 0);
        Interlocked.Exchange(ref cacheNotificationApproximateCurrentQueueDepth, 0);
        Interlocked.Exchange(ref cacheNotificationLastNotifySnapshotEntries, 0);
        Interlocked.Exchange(ref cacheNotificationLastNotifyLiveSubscribers, 0);
        Interlocked.Exchange(ref cacheNotificationNotifySweeps, 0);
        Interlocked.Exchange(ref cacheNotificationNotifySnapshotEntries, 0);
        Interlocked.Exchange(ref cacheNotificationNotifyLiveSubscribers, 0);
        Interlocked.Exchange(ref cacheNotificationLastCleanSnapshotEntries, 0);
        Interlocked.Exchange(ref cacheNotificationLastCleanRequeuedSubscribers, 0);
        Interlocked.Exchange(ref cacheNotificationLastCleanDroppedSubscribers, 0);
        Interlocked.Exchange(ref cacheNotificationCleanSweeps, 0);
        Interlocked.Exchange(ref cacheNotificationCleanSnapshotEntries, 0);
        Interlocked.Exchange(ref cacheNotificationCleanRequeuedSubscribers, 0);
        Interlocked.Exchange(ref cacheNotificationCleanDroppedSubscribers, 0);
        Interlocked.Exchange(ref cacheNotificationCleanBusySkips, 0);
        Interlocked.Exchange(ref cacheNotificationApproximatePeakQueueDepth, 0);
    }

    internal bool HasActivity()
        => Interlocked.Read(ref rowCacheHits) != 0 ||
           Interlocked.Read(ref rowCacheMisses) != 0 ||
           Interlocked.Read(ref databaseRowsLoaded) != 0 ||
           Interlocked.Read(ref rowMaterializations) != 0 ||
           Interlocked.Read(ref rowCacheStores) != 0 ||
           Interlocked.Read(ref relationReferenceCacheHits) != 0 ||
           Interlocked.Read(ref relationReferenceLoads) != 0 ||
           Interlocked.Read(ref relationCollectionCacheHits) != 0 ||
           Interlocked.Read(ref relationCollectionLoads) != 0 ||
           Interlocked.Read(ref cacheNotificationSubscriptions) != 0 ||
           Interlocked.Read(ref cacheNotificationApproximateCurrentQueueDepth) != 0 ||
           Interlocked.Read(ref cacheNotificationNotifySweeps) != 0 ||
           Interlocked.Read(ref cacheNotificationCleanSweeps) != 0 ||
           Interlocked.Read(ref cacheNotificationCleanBusySkips) != 0;

    internal DataLinqTableMetricsSnapshot Snapshot()
        => new(
            TableName: TableName,
            Relations: new RelationMetricsSnapshot(
                ReferenceCacheHits: Interlocked.Read(ref relationReferenceCacheHits),
                ReferenceLoads: Interlocked.Read(ref relationReferenceLoads),
                CollectionCacheHits: Interlocked.Read(ref relationCollectionCacheHits),
                CollectionLoads: Interlocked.Read(ref relationCollectionLoads)),
            RowCache: new RowCacheMetricsSnapshot(
                Hits: Interlocked.Read(ref rowCacheHits),
                Misses: Interlocked.Read(ref rowCacheMisses),
                DatabaseRowsLoaded: Interlocked.Read(ref databaseRowsLoaded),
                Materializations: Interlocked.Read(ref rowMaterializations),
                Stores: Interlocked.Read(ref rowCacheStores)),
            CacheNotifications: new CacheNotificationMetricsSnapshot(
                Subscriptions: Interlocked.Read(ref cacheNotificationSubscriptions),
                ApproximateCurrentQueueDepth: Interlocked.Read(ref cacheNotificationApproximateCurrentQueueDepth),
                LastNotifySnapshotEntries: Interlocked.Read(ref cacheNotificationLastNotifySnapshotEntries),
                LastNotifyLiveSubscribers: Interlocked.Read(ref cacheNotificationLastNotifyLiveSubscribers),
                NotifySweeps: Interlocked.Read(ref cacheNotificationNotifySweeps),
                NotifySnapshotEntries: Interlocked.Read(ref cacheNotificationNotifySnapshotEntries),
                NotifyLiveSubscribers: Interlocked.Read(ref cacheNotificationNotifyLiveSubscribers),
                LastCleanSnapshotEntries: Interlocked.Read(ref cacheNotificationLastCleanSnapshotEntries),
                LastCleanRequeuedSubscribers: Interlocked.Read(ref cacheNotificationLastCleanRequeuedSubscribers),
                LastCleanDroppedSubscribers: Interlocked.Read(ref cacheNotificationLastCleanDroppedSubscribers),
                CleanSweeps: Interlocked.Read(ref cacheNotificationCleanSweeps),
                CleanSnapshotEntries: Interlocked.Read(ref cacheNotificationCleanSnapshotEntries),
                CleanRequeuedSubscribers: Interlocked.Read(ref cacheNotificationCleanRequeuedSubscribers),
                CleanDroppedSubscribers: Interlocked.Read(ref cacheNotificationCleanDroppedSubscribers),
                CleanBusySkips: Interlocked.Read(ref cacheNotificationCleanBusySkips),
                ApproximatePeakQueueDepth: Interlocked.Read(ref cacheNotificationApproximatePeakQueueDepth)));

    private void RecordCacheNotificationApproximatePeakQueueDepth(int approximateQueueDepth)
    {
        long currentPeak;
        do
        {
            currentPeak = Interlocked.Read(ref cacheNotificationApproximatePeakQueueDepth);
            if (approximateQueueDepth <= currentPeak)
                return;
        }
        while (Interlocked.CompareExchange(ref cacheNotificationApproximatePeakQueueDepth, approximateQueueDepth, currentPeak) != currentPeak);
    }
}

public static class DataLinqMetrics
{
    private static readonly ConcurrentDictionary<string, DataLinqProviderMetricsState> providers = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a process-wide snapshot of the current DataLinq metrics.
    /// </summary>
    public static DataLinqMetricsSnapshot Snapshot()
    {
        var providerSnapshots = providers.Values
            .Where(x => x.HasActivity())
            .Select(x => x.Snapshot())
            .OrderBy(x => x.DatabaseName, StringComparer.Ordinal)
            .ThenBy(x => x.ProviderTypeName, StringComparer.Ordinal)
            .ThenBy(x => x.ProviderInstanceId, StringComparer.Ordinal)
            .ToArray();

        return new DataLinqMetricsSnapshot(
            Queries: QueryMetricsSnapshot.Sum(providerSnapshots.Select(x => x.Queries)),
            Commands: CommandMetricsSnapshot.Sum(providerSnapshots.Select(x => x.Commands)),
            Transactions: TransactionMetricsSnapshot.Sum(providerSnapshots.Select(x => x.Transactions)),
            Relations: RelationMetricsSnapshot.Sum(providerSnapshots.Select(x => x.Relations)),
            RowCache: RowCacheMetricsSnapshot.Sum(providerSnapshots.Select(x => x.RowCache)),
            CacheNotifications: CacheNotificationMetricsSnapshot.Sum(providerSnapshots.Select(x => x.CacheNotifications)),
            Providers: providerSnapshots);
    }

    /// <summary>
    /// Resets all metrics currently tracked by this DataLinq runtime instance.
    /// </summary>
    public static void Reset()
    {
        foreach (var providerState in providers.Values)
            providerState.Reset();
    }

    internal static DataLinqTableMetricsHandle RegisterTable(IDatabaseProvider databaseProvider, string tableName)
        => GetOrCreateProvider(databaseProvider).GetOrCreateTable(tableName);

    internal static void RecordEntityQueryExecution(IDatabaseProvider databaseProvider)
        => GetOrCreateProvider(databaseProvider).RecordEntityQueryExecution();

    internal static void RecordScalarQueryExecution(IDatabaseProvider databaseProvider)
        => GetOrCreateProvider(databaseProvider).RecordScalarQueryExecution();

    internal static void RecordCommandExecution(DataLinqTelemetryContext telemetryContext, string commandKind, bool succeeded, TimeSpan duration)
        => GetOrCreateProvider(telemetryContext).RecordCommandExecution(commandKind, succeeded, duration);

    internal static void RecordTransactionStarted(DataLinqTelemetryContext telemetryContext)
        => GetOrCreateProvider(telemetryContext).RecordTransactionStarted();

    internal static void RecordTransactionCompleted(DataLinqTelemetryContext telemetryContext, DatabaseTransactionStatus outcome, bool succeeded, TimeSpan duration)
        => GetOrCreateProvider(telemetryContext).RecordTransactionCompleted(outcome, succeeded, duration);

    private static DataLinqProviderMetricsState GetOrCreateProvider(IDatabaseProvider databaseProvider)
        => providers.GetOrAdd(databaseProvider.TelemetryInstanceId, _ => new DataLinqProviderMetricsState(databaseProvider));

    private static DataLinqProviderMetricsState GetOrCreateProvider(DataLinqTelemetryContext telemetryContext)
        => providers.GetOrAdd(telemetryContext.ProviderInstanceId, _ => new DataLinqProviderMetricsState(telemetryContext));
}
