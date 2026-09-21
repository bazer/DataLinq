using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Workers;

namespace DataLinq.Cache;

public class DatabaseCache : IDisposable
{
    internal static Func<bool> IsBrowserRuntime { get; set; } = static () => OperatingSystem.IsBrowser();
    internal static Func<TimeProvider> TimeProviderFactory { get; set; } = static () => TimeProvider.System;
    internal static Func<IMemoryPressureReader> MemoryPressureReaderFactory { get; set; } =
        static () => IsBrowserRuntime() ? UnsupportedMemoryPressureReader.Instance : GcMemoryPressureReader.Instance;

    public IDatabaseProvider Database { get; set; }
    internal DatabaseCachePolicy Policy { get; }
    private readonly DataLinqLoggingConfiguration loggingConfiguration;
    private readonly string? providerInstanceId;
    private OwnedRootDisposal? disposal;
    public Dictionary<TableDefinition, TableCache> TableCaches { get; }

    public CleanCacheWorker? CleanCacheWorker => null;
    public CacheCleanupScheduler? CleanupScheduler { get; }
    public CacheMemoryPressureCleanupPolicy MemoryPressureCleanupPolicy { get; private set; } = CacheMemoryPressureCleanupPolicy.Disabled;

    public CacheHistory History { get; } = new();

    public DatabaseCache(IDatabaseProvider database, DataLinqLoggingConfiguration loggingConfiguration)
        : this(database, loggingConfiguration, static cache => IsBrowserRuntime() ? null
            : new CacheCleanupScheduler(cache, cache.Policy.CacheCleanup, TimeProviderFactory(), MemoryPressureReaderFactory()))
    {
    }

    internal DatabaseCache(IDatabaseProvider database, DataLinqLoggingConfiguration loggingConfiguration,
        Func<DatabaseCache, CacheCleanupScheduler?> createScheduler)
    {
        ArgumentNullException.ThrowIfNull(createScheduler);
        this.Database = database;
        this.providerInstanceId = database.TelemetryInstanceId;
        this.loggingConfiguration = loggingConfiguration;
        this.Policy = DatabaseCachePolicy.FromMetadata(database.Metadata);
        this.TableCaches = new Dictionary<TableDefinition, TableCache>(this.Database.Metadata.TableModels.Count);
        for (var i = 0; i < this.Database.Metadata.TableModels.Count; i++)
        {
            var table = this.Database.Metadata.TableModels[i].Table;
            this.TableCaches.Add(table, new TableCache(table, this, loggingConfiguration));
        }

        CleanupScheduler = createScheduler(this);
        CleanupScheduler?.Start();
    }

    //public TableCache GetTableCache(string tableName)
    //{
    //    return TableCaches.Single(x => x.Table.DbName == tableName);
    //}

    public TableCache GetTableCache(TableDefinition table)
    {
        return TableCaches[table];
    }

    public DatabaseCacheSnapshot GetLatestSnapshot()
    {
        return History.GetLatest() ?? MakeSnapshot();
    }

    public DatabaseCacheSnapshot MakeSnapshot()
    {
        var snapshot = new DatabaseCacheSnapshot(DateTime.UtcNow, TableCaches.Values.Select(x => x.MakeSnapshot()).ToArray());
        History.Add(snapshot);

        return snapshot;
    }

    internal CacheMemoryEstimate GetMemoryEstimate() =>
        CacheMemoryEstimate.Sum(TableCaches.Values.Select(x => x.GetMemoryEstimate())) + History.GetMemoryEstimate();

    public void ConfigureMemoryPressureCleanup(CacheMemoryPressureCleanupPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        MemoryPressureCleanupPolicy = policy.Normalize();

        if (!IsBrowserRuntime())
            CleanupScheduler?.Restart();
    }

    public (IndexCacheType, int? amount) GetIndexCachePolicy()
        => GetIndexCachePolicy(Policy.IndexCache);

    internal (IndexCacheType, int? amount) GetIndexCachePolicy(TableDefinition table)
        => GetIndexCachePolicy(Policy.GetTableIndexCache(table));

    private static (IndexCacheType, int? amount) GetIndexCachePolicy(
        IReadOnlyList<(IndexCacheType indexCacheType, int? amount)> indexCache)
    {
        if (indexCache.Count == 0)
            return (IndexCacheType.None, 0);

        (IndexCacheType indexCacheType, int? amount)? maxRowsPolicy = null;
        var hasAllPolicy = false;

        for (var i = 0; i < indexCache.Count; i++)
        {
            var policy = indexCache[i];
            if (policy.indexCacheType == IndexCacheType.None)
                return (IndexCacheType.None, 0);

            if (policy.indexCacheType == IndexCacheType.MaxAmountRows)
                maxRowsPolicy = policy;
            else if (policy.indexCacheType == IndexCacheType.All)
                hasAllPolicy = true;
        }

        if (maxRowsPolicy.HasValue)
            return maxRowsPolicy.Value;

        if (hasAllPolicy)
            return (IndexCacheType.All, null);

        throw new NotImplementedException();
    }

    internal IDisposable OverrideTableCacheLimitsForTesting(
        TableDefinition table,
        IReadOnlyList<(CacheLimitType limitType, long amount)> cacheLimits) =>
        Policy.OverrideTableCacheLimitsForTesting(table, cacheLimits);

    public void ApplyChanges(IEnumerable<StateChange> changes, Transaction? transaction = null)
    {
        foreach (var change in changes.GroupBy(x => x.Table))
        {
            TableCaches[change.Key].ApplyChanges(change, transaction);
        }
    }

    public void RemoveTransaction(Transaction transaction)
    {
        var failures = RemoveTransactionBestEffort(transaction);
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        if (failures.Count > 1)
        {
            throw new AggregateException(
                $"Multiple failures occurred while removing transaction {transaction.TransactionID} from provider cache state.",
                failures);
        }
    }

    internal IReadOnlyList<Exception> RemoveTransactionBestEffort(Transaction transaction, Action<Exception>? observeFailure = null)
    {
        List<Exception>? failures = null;

        foreach (var table in TableCaches.Values)
        {
            using (ExecutionFailureScope.Call? observation = observeFailure is null ? null : ExecutionFailureScope.Begin())
            {
                try
                {
                    if (!table.TryRemoveTransaction(transaction) &&
                        table.IsTransactionInCache(transaction))
                    {
                        RecordRecoveryFailure(ref failures, new InvalidOperationException(
                            $"Transaction {transaction.TransactionID} remained in cache for table '{table.Table.DbName}' after best-effort removal."), observeFailure);
                    }
                }
                catch (Exception exception)
                {
                    RecordRecoveryFailure(ref failures, exception, observeFailure);
                }
            }

            using (ExecutionFailureScope.Call? observation = observeFailure is null ? null : ExecutionFailureScope.Begin())
            {
                try
                {
                    table.DiscardTransactionNotifications(transaction);
                }
                catch (Exception exception)
                {
                    RecordRecoveryFailure(ref failures, exception, observeFailure);
                }
            }
        }

        return failures is null ? Array.Empty<Exception>() : failures;
    }

    internal IReadOnlyList<Exception> ClearForRecovery(Action<Exception>? observeFailure = null)
    {
        List<Exception>? failures = null;
        var tables = TableCaches.Values.ToArray();

        // Finish structural cleanup for every table before notifying any subscribers.
        // Recovery callers cannot safely expose a mixture of cleared and stale tables
        // to relation callbacks after a commit succeeded or may have reached the database.
        foreach (var table in tables)
        {
            using (ExecutionFailureScope.Call? observation = observeFailure is null ? null : ExecutionFailureScope.Begin())
            {
                try
                {
                    table.ClearRowsWithoutNotification();
                }
                catch (Exception exception)
                {
                    RecordRecoveryFailure(ref failures, exception, observeFailure);
                }
            }

            using (ExecutionFailureScope.Call? observation = observeFailure is null ? null : ExecutionFailureScope.Begin())
            {
                try
                {
                    table.ClearIndex();
                }
                catch (Exception exception)
                {
                    RecordRecoveryFailure(ref failures, exception, observeFailure);
                }
            }
        }

        foreach (var table in tables)
        {
            using (ExecutionFailureScope.Call? observation = observeFailure is null ? null : ExecutionFailureScope.Begin())
            {
                try
                {
                    table.NotifyRecoveryClear();
                }
                catch (Exception exception)
                {
                    RecordRecoveryFailure(ref failures, exception, observeFailure);
                }
            }

            // A recovery notification may itself subscribe more relation objects.
            // Once recovery requires fresh materialization, none of those callbacks
            // are safe to retain for a later clear or provider disposal.
            using (ExecutionFailureScope.Call? observation = observeFailure is null ? null : ExecutionFailureScope.Begin())
            {
                try
                {
                    table.DiscardRecoveryNotifications();
                }
                catch (Exception exception)
                {
                    RecordRecoveryFailure(ref failures, exception, observeFailure);
                }
            }
        }

        return failures is null ? Array.Empty<Exception>() : failures;
    }

    internal IReadOnlyList<Exception> DiscardRecoveryNotifications(Action<Exception>? observeFailure = null)
    {
        List<Exception>? failures = null;
        foreach (var table in TableCaches.Values)
        {
            using (ExecutionFailureScope.Call? observation = observeFailure is null ? null : ExecutionFailureScope.Begin())
            {
                try
                {
                    table.DiscardRecoveryNotifications();
                }
                catch (Exception exception)
                {
                    RecordRecoveryFailure(ref failures, exception, observeFailure);
                }
            }
        }

        return failures is null ? Array.Empty<Exception>() : failures;
    }

    // The private completion owner captures an occurrence before a subsequent
    // cache notification can replace the same exception object's direct lookup.
    private static void RecordRecoveryFailure(ref List<Exception>? failures, Exception exception, Action<Exception>? observeFailure)
    {
        (failures ??= []).Add(exception);
        observeFailure?.Invoke(exception);
    }

    public void CleanRelationNotifications()
    {
        foreach (var table in TableCaches.Values)
        {
            table.CleanRelationNotifications();
        }
    }


    public IEnumerable<(TableCache table, int numRows)> RemoveRowsBySettings(string cleanupTrigger = CacheMaintenanceTriggers.Manual)
    {
        foreach (var table in TableCaches.Values)
        {
            foreach (var (limitType, amount) in Policy.GetTableCacheLimits(table.Table))
            {
                var numRows = table.RemoveRowsByLimit(limitType, amount, cleanupTrigger);

                if (numRows > 0)
                    yield return (table, numRows);
            }
        }

        foreach (var (limitType, amount) in Policy.DatabaseCacheLimits)
        {
            foreach (var rows in RemoveRowsByLimit(limitType, amount, cleanupTrigger))
                yield return rows;
        }
    }

    public IEnumerable<(TableCache table, int numRows)> RemoveRowsByLimit(
        CacheLimitType limitType,
        long amount,
        string cleanupTrigger = CacheMaintenanceTriggers.Manual)
    {
        if (TableCache.IsByteCacheLimit(limitType))
        {
            foreach (var rows in RemoveRowsByDatabaseByteLimit(TableCache.ConvertByteLimitToBytes(limitType, amount), cleanupTrigger))
                yield return rows;

            yield break;
        }

        foreach (var table in TableCaches.Values)
        {
            var numRows = table.RemoveRowsByLimit(limitType, amount, cleanupTrigger);

            if (numRows > 0)
                yield return (table, numRows);
        }
    }

    private IEnumerable<(TableCache table, int numRows)> RemoveRowsByDatabaseByteLimit(long maxBytes, string cleanupTrigger)
    {
        while (GetMemoryEstimate().EstimatedCacheBytes > maxBytes)
        {
            var tableEstimate = TableCaches.Values
                .Where(x => x.RowCount > 0)
                .Select(x => (table: x, estimatedBytes: x.GetMemoryEstimate().EstimatedCacheBytes))
                .Where(x => x.estimatedBytes > 0)
                .OrderByDescending(x => x.estimatedBytes)
                .FirstOrDefault();

            if (tableEstimate.table is null)
                yield break;

            var overflowBytes = GetMemoryEstimate().EstimatedCacheBytes - maxBytes;
            var tableTargetBytes = Math.Max(0, tableEstimate.estimatedBytes - overflowBytes);
            var numRows = tableEstimate.table.RemoveRowsByEstimatedCacheByteLimit(tableTargetBytes, cleanupTrigger);

            if (numRows <= 0)
                yield break;

            yield return (tableEstimate.table, numRows);
        }
    }

    internal CacheCleanupPassResult RemoveRowsForMemoryPressure(long targetEstimatedBytes, int maxRows)
    {
        var beforeEstimate = GetMemoryEstimate().EstimatedCacheBytes;
        var rowsRemoved = 0;

        while (GetMemoryEstimate().EstimatedCacheBytes > targetEstimatedBytes && rowsRemoved < maxRows)
        {
            var tableEstimate = TableCaches.Values
                .Where(x => x.RowCount > 0)
                .Select(x => (table: x, estimatedBytes: x.GetMemoryEstimate().EstimatedCacheBytes))
                .Where(x => x.estimatedBytes > 0)
                .OrderByDescending(x => x.estimatedBytes)
                .FirstOrDefault();

            if (tableEstimate.table is null)
                break;

            var overflowBytes = GetMemoryEstimate().EstimatedCacheBytes - targetEstimatedBytes;
            var tableTargetBytes = Math.Max(0, tableEstimate.estimatedBytes - overflowBytes);
            var rowsForTable = tableEstimate.table.RemoveRowsForMemoryPressure(
                tableTargetBytes,
                maxRows - rowsRemoved,
                tableTargetBytes);

            if (rowsForTable <= 0)
                break;

            rowsRemoved += rowsForTable;
        }

        return new CacheCleanupPassResult(
            CacheMaintenanceReasons.MemoryPressure,
            CacheMaintenanceTriggers.MemoryPressure,
            CacheMaintenanceBases.EstimatedCacheBytes,
            rowsRemoved,
            beforeEstimate,
            GetMemoryEstimate().EstimatedCacheBytes,
            targetEstimatedBytes);
    }

    public IEnumerable<(TableCache table, int numRows)> RemoveRowsInsertedBeforeTick(long tick)
    {
        foreach (var table in TableCaches.Values)
        {
            var numRows = table.RemoveRowsInsertedBeforeTick(tick);

            if (numRows > 0)
                yield return (table, numRows);
        }
    }

    public void ClearCache()
    {
        foreach (var table in TableCaches.Values)
        {
            table.ClearCache();
        }
    }

    public void Dispose()
    {
        GetDisposal().Dispose();
    }

    internal ValueTask DisposeAsyncCore() => GetDisposal().DisposeAsync();

    internal void ValidateDisposal() => CleanupScheduler?.ValidateStop();

    private OwnedRootDisposal GetDisposal() => LazyInitializer.EnsureInitialized(ref disposal,
        () => new OwnedRootDisposal(CaptureCleanup, providerInstanceId));

    private RootCleanupStep[] CaptureCleanup()
    {
        var steps = new List<RootCleanupStep>();
        if (CleanupScheduler is { } scheduler)
            steps.Add(RootCleanupStep.Resource(scheduler.Dispose, scheduler.DisposeAsyncCore, scheduler.ValidateStop));
        // Every table gets both independent cleanup attempts even if an earlier
        // observer or telemetry callback failed. No user transaction is disposed.
        foreach (var table in TableCaches.Values)
            steps.Add(RootCleanupStep.Local(table.UnregisterTelemetry));
        foreach (var table in TableCaches.Values)
        {
            steps.Add(RootCleanupStep.Local(table.ClearRowsWithoutNotification));
            steps.Add(RootCleanupStep.Local(table.ClearIndex));
            steps.Add(RootCleanupStep.Local(table.NotifyRecoveryClear));
        }
        return steps.ToArray();
    }
}
