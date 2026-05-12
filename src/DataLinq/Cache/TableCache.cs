using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Utils;

namespace DataLinq.Cache;

public interface ICacheNotification
{
    void Clear();
}

public class TableCache
{
    internal sealed class CacheNotificationManager
    {
        private sealed record CacheNotificationSubscription(
            WeakReference<ICacheNotification> Subscriber,
            Transaction? Transaction);

        // Use ConcurrentQueue. Subscribe stays lock-free and O(1).
        // Notify self-clears by swapping the queue, and Clean compacts dead
        // weak references for read-heavy workloads that don't notify often.
        private readonly DataLinqTableMetricsHandle metricsHandle;
        private ConcurrentQueue<CacheNotificationSubscription> _subscribers = new();
        private int _maintenanceState = 0;
        private int _approximateSubscriberCount = 0;

        internal CacheNotificationManager(DataLinqTableMetricsHandle metricsHandle)
        {
            this.metricsHandle = metricsHandle;
        }

        internal void Subscribe(ICacheNotification subscriber) => Subscribe(subscriber, null);

        internal void Subscribe(ICacheNotification subscriber, Transaction? transaction)
        {
            // This is a fully thread-safe, lock-free, O(1) operation.
            _subscribers.Enqueue(new CacheNotificationSubscription(new WeakReference<ICacheNotification>(subscriber), transaction));
            var approximateQueueDepth = Interlocked.Increment(ref _approximateSubscriberCount);
            metricsHandle.RecordCacheNotificationSubscribe(approximateQueueDepth);
        }

        internal void Notify() => Notify(null);

        internal void Notify(Transaction? transaction)
        {
            // 1. Check if there's anything to do. This is a quick, lock-free check.
            if (_subscribers.IsEmpty)
            {
                return;
            }

            // 2. Serialize the queue swap with Clean() without blocking Subscribe().
            // We only hold this gate long enough to take a private snapshot.
            var spinWait = new SpinWait();
            while (Interlocked.CompareExchange(ref _maintenanceState, 1, 0) != 0)
                spinWait.SpinOnce();

            ConcurrentQueue<CacheNotificationSubscription>? subscribersToNotify = null;
            try
            {
                // Another maintenance operation may already have swapped the queue
                // while we were waiting, so re-check after acquiring the gate.
                if (_subscribers.IsEmpty)
                {
                    return;
                }

                // 3. Atomically swap the current queue with a new, empty one.
                // Any new calls to Subscribe() from other threads will now add
                // to the new queue without interfering with this notification pass.
                subscribersToNotify = Interlocked.Exchange(ref _subscribers, new ConcurrentQueue<CacheNotificationSubscription>());
                Interlocked.Exchange(ref _approximateSubscriberCount, 0);
            }
            finally
            {
                Volatile.Write(ref _maintenanceState, 0);
            }

            // 4. Iterate over our private snapshot outside the maintenance gate.
            var snapshotEntries = 0;
            var liveSubscribers = 0;
            var requeuedSubscribers = 0;
            foreach (var subscription in subscribersToNotify)
            {
                snapshotEntries++;
                if (subscription.Subscriber.TryGetTarget(out var subscriber))
                {
                    if (transaction == null || ReferenceEquals(subscription.Transaction, transaction))
                    {
                        liveSubscribers++;
                        subscriber.Clear();
                    }
                    else
                    {
                        _subscribers.Enqueue(subscription);
                        requeuedSubscribers++;
                    }
                }
            }

            var approximateQueueDepth = Interlocked.Add(ref _approximateSubscriberCount, requeuedSubscribers);
            metricsHandle.RecordCacheNotificationNotifySweep(snapshotEntries, liveSubscribers, approximateQueueDepth);
        }

        internal void Clean()
        {
            // Best-effort compaction. If Notify() is already in progress, skip this
            // cycle and let the next background sweep retry.
            if (_subscribers.IsEmpty || Interlocked.CompareExchange(ref _maintenanceState, 1, 0) != 0)
            {
                if (!_subscribers.IsEmpty)
                    metricsHandle.RecordCacheNotificationCleanBusySkip();
                return;
            }

            try
            {
                // Clean keeps the maintenance gate for the full compaction pass.
                // If Notify() were allowed to swap the queue between our Exchange()
                // and re-enqueue of live subscribers, it could miss an invalidation.
                if (_subscribers.IsEmpty)
                {
                    return;
                }

                var subscribersToKeep = Interlocked.Exchange(ref _subscribers, new ConcurrentQueue<CacheNotificationSubscription>());
                Interlocked.Exchange(ref _approximateSubscriberCount, 0);
                var snapshotEntries = 0;
                var requeuedSubscribers = 0;
                foreach (var subscription in subscribersToKeep)
                {
                    snapshotEntries++;
                    if (subscription.Subscriber.TryGetTarget(out _))
                    {
                        _subscribers.Enqueue(subscription);
                        requeuedSubscribers++;
                    }
                }

                var approximateQueueDepth = Interlocked.Add(ref _approximateSubscriberCount, requeuedSubscribers);
                metricsHandle.RecordCacheNotificationCleanSweep(
                    snapshotEntries,
                    requeuedSubscribers,
                    snapshotEntries - requeuedSubscribers,
                    approximateQueueDepth);
            }
            finally
            {
                Volatile.Write(ref _maintenanceState, 0);
            }
        }
    }

    private readonly object indexCacheGate = new();
    private Dictionary<ColumnIndex, IndexCache>? indexCaches;
    private RowCache? rowCache;
    private ConcurrentDictionary<Transaction, RowCache>? transactionRows;

    protected int primaryKeyColumnsCount;
    protected IReadOnlyList<ColumnIndex> indices;
    protected (IndexCacheType type, int? amount) indexCachePolicy;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;
    private readonly DataLinqTelemetryContext telemetryContext;
    internal DataLinqTableMetricsHandle MetricsHandle { get; }

    // This table weakly maps a relation object to its subscription manager.
    private CacheNotificationManager? notificationManager;

    public void SubscribeToChanges(ICacheNotification subscriber, Transaction? transaction = null)
    {
        (notificationManager ??= new CacheNotificationManager(MetricsHandle)).Subscribe(subscriber, transaction);
    }

    public TableCache(TableDefinition table, DatabaseCache databaseCache, DataLinqLoggingConfiguration loggingConfiguration)
    {
        this.Table = table;
        this.DatabaseCache = databaseCache;
        this.loggingConfiguration = loggingConfiguration;
        this.telemetryContext = DataLinqTelemetryContext.FromProvider(databaseCache.Database);
        this.primaryKeyColumnsCount = Table.PrimaryKeyColumns.Length;
        this.indices = Table.ColumnIndices;
        this.indexCachePolicy = GetIndexCachePolicy();
        MetricsHandle = DataLinqMetrics.RegisterTable(databaseCache.Database, table.DbName);
        DataLinqTelemetry.RegisterTableCache(
            telemetryContext,
            table.DbName,
            GetOccupancySnapshot,
            MetricsHandle.GetCacheNotificationSnapshot);

        RefreshOccupancyMetrics();
    }

    public long? OldestTick => rowCache?.OldestTick;
    public long? NewestTick => rowCache?.NewestTick;
    public int RowCount => rowCache?.Count ?? 0;
    public long TotalBytes => rowCache?.TotalBytes ?? 0;
    public string TotalBytesFormatted => TotalBytes.ToFileSize();
    public int TransactionRowsCount => transactionRows?.Count ?? 0;
    public IEnumerable<(string index, int count)> IndicesCount => indices.Select(x => (x.Name, TryGetIndexCache(x)?.Count ?? 0));

    public TableDefinition Table { get; }
    public DatabaseCache DatabaseCache { get; }

    private RowCache GetOrCreateRowCache()
    {
        var cache = rowCache;
        if (cache is not null)
            return cache;

        cache = new RowCache();
        var existing = Interlocked.CompareExchange(ref rowCache, cache, null);
        return existing ?? cache;
    }

    private ConcurrentDictionary<Transaction, RowCache> GetOrCreateTransactionRows()
    {
        var rows = transactionRows;
        if (rows is not null)
            return rows;

        rows = new ConcurrentDictionary<Transaction, RowCache>();
        var existing = Interlocked.CompareExchange(ref transactionRows, rows, null);
        return existing ?? rows;
    }

    private IndexCache GetIndexCache(ColumnIndex index)
    {
        lock (indexCacheGate)
        {
            indexCaches ??= new Dictionary<ColumnIndex, IndexCache>();

            if (!indexCaches.TryGetValue(index, out var cache))
            {
                cache = new IndexCache();
                indexCaches.Add(index, cache);
            }

            return cache;
        }
    }

    private IndexCache? TryGetIndexCache(ColumnIndex index)
    {
        lock (indexCacheGate)
        {
            return indexCaches is not null && indexCaches.TryGetValue(index, out var cache)
                ? cache
                : null;
        }
    }

    private IndexCache[] GetLoadedIndexCaches()
    {
        lock (indexCacheGate)
        {
            return indexCaches is null
                ? []
                : indexCaches.Values.ToArray();
        }
    }

    public bool IsTransactionInCache(Transaction transaction) => transactionRows?.ContainsKey(transaction) == true;
    public IEnumerable<IImmutableInstance> GetTransactionRows(Transaction transaction)
    {
        if (transactionRows?.TryGetValue(transaction, out var result) == true)
            return result.Rows;

        return [];
    }

    public int ApplyChanges(IEnumerable<StateChange> changes, Transaction? transaction = null)
    {
        var relevantChanges = changes.Where(x => x.Table == Table).ToList();
        if (relevantChanges.Count == 0)
            return 0;

        return transaction == null
            ? ApplyCommittedChanges(relevantChanges)
            : ApplyTransactionChanges(relevantChanges, transaction);
    }

    private int ApplyTransactionChanges(IReadOnlyList<StateChange> changes, Transaction transaction)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var numRows = 0;

        foreach (var change in changes)
        {
            if (change.Type == TransactionChangeType.Delete || change.Type == TransactionChangeType.Update)
            {
                if (TryRemoveTransactionRow(change.PrimaryKeys, transaction, out var transRows))
                    numRows += transRows;
            }
        }

        var duration = Stopwatch.GetElapsedTime(startedAt);
        if (numRows > 0)
        {
            MetricsHandle.RecordCacheCleanup(numRows, duration);
            DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, "transaction_state_change", numRows, duration);
        }

        MetricsHandle.RecordCacheCleanup(0, duration);
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, "transaction_state_change_table", 0, duration);

        RefreshOccupancyMetrics();
        OnRowChanged(transaction);

        return numRows;
    }

    private int ApplyCommittedChanges(IReadOnlyList<StateChange> changes)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var numRows = 0;

        foreach (var change in changes)
        {
            if (change.Type == TransactionChangeType.Delete || change.Type == TransactionChangeType.Update)
            {
                if (rowCache?.TryRemoveRow(change.PrimaryKeys, out var rows) == true)
                    numRows += rows;
            }

            TryRemoveRowFromAllIndices(change.PrimaryKeys, out var indexRows);
            numRows += indexRows;

            if (change.Type == TransactionChangeType.Update)
            {
                var invalidatedIndices = new HashSet<ColumnIndex>();
                foreach (var changedValue in change.GetChanges())
                {
                    var columnIndices = change.Table.GetColumnIndices(changedValue.Key);
                    for (var i = 0; i < columnIndices.Count; i++)
                    {
                        var columnIndex = columnIndices[i];
                        if (invalidatedIndices.Add(columnIndex))
                            RemoveIndexOnBothSides(columnIndex, change.Model);
                    }
                }
            }
            else
            {
                foreach (var columnIndex in change.Table.ColumnIndices)
                {
                    RemoveIndexOnBothSides(columnIndex, change.Model);
                }
            }
        }

        var duration = Stopwatch.GetElapsedTime(startedAt);
        if (numRows > 0)
        {
            MetricsHandle.RecordCacheCleanup(numRows, duration);
            DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, "state_change_precise", numRows, duration);
        }

        MetricsHandle.RecordCacheCleanup(0, duration);
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, "state_change_table", 0, duration);

        RefreshOccupancyMetrics();
        OnRowChanged();

        return numRows;

        int RemoveIndexOnBothSides(ColumnIndex columnIndex, IModelInstance model)
        {
            var fk = KeyFactory.GetKey(model, columnIndex.Columns);

            if (TryRemoveForeignKeyIndex(columnIndex, fk, out var indexRowsThisSide))
                numRows += indexRowsThisSide;

            foreach (var index in columnIndex.RelationParts.Select(x => x.GetOtherSide().ColumnIndex))
                if (DatabaseCache.GetTableCache(index.Table).TryRemoveForeignKeyIndex(index, fk, out var indexRowsOtherSide))
                    numRows += indexRowsOtherSide;

            return numRows;
        }
    }

    protected virtual void OnRowChanged(Transaction? transaction = null)
    {
        notificationManager?.Notify(transaction);
    }

    public (IndexCacheType, int? amount) GetIndexCachePolicy()
    {
        return DatabaseCache.GetIndexCachePolicy(Table);
    }

    public void ClearCache()
    {
        ClearRows();
        ClearIndex();
    }

    public void ClearRows()
    {
        var rowsRemoved = RowCount;
        var startedAt = Stopwatch.GetTimestamp();
        rowCache?.ClearRows();
        var duration = Stopwatch.GetElapsedTime(startedAt);
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, "clear", rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        OnRowChanged();
    }

    public void ClearIndex()
    {
        foreach (var indexCache in GetLoadedIndexCaches())
            indexCache.Clear();

        RefreshOccupancyMetrics();
    }

    public void CleanRelationNotifications()
    {
        notificationManager?.Clean();
    }

    public int RemoveRowsByLimit(CacheLimitType limitType, long amount)
    {
        if (limitType is CacheLimitType.Seconds or CacheLimitType.Minutes or CacheLimitType.Hours or CacheLimitType.Days or CacheLimitType.Ticks)
        {
            var cutoffTick = limitType switch
            {
                CacheLimitType.Seconds => DateTime.Now.Subtract(TimeSpan.FromSeconds(amount)).Ticks,
                CacheLimitType.Minutes => DateTime.Now.Subtract(TimeSpan.FromMinutes(amount)).Ticks,
                CacheLimitType.Hours => DateTime.Now.Subtract(TimeSpan.FromHours(amount)).Ticks,
                CacheLimitType.Days => DateTime.Now.Subtract(TimeSpan.FromDays(amount)).Ticks,
                CacheLimitType.Ticks => DateTime.Now.Subtract(TimeSpan.FromTicks(amount)).Ticks,
                _ => throw new NotImplementedException($"CacheLimitType '{limitType}' is not implemented.")
            };

            return RemoveRowsInsertedBeforeTick(cutoffTick);
        }

        var startedAt = Stopwatch.GetTimestamp();
        var rowsRemoved = limitType switch
        {
            CacheLimitType.Rows => rowCache?.RemoveRowsOverRowLimit((int)amount) ?? 0,
            CacheLimitType.Bytes => rowCache?.RemoveRowsOverSizeLimit(amount) ?? 0,
            CacheLimitType.Kilobytes => rowCache?.RemoveRowsOverSizeLimit(amount * 1024) ?? 0,
            CacheLimitType.Megabytes => rowCache?.RemoveRowsOverSizeLimit(amount * 1024 * 1024) ?? 0,
            CacheLimitType.Gigabytes => rowCache?.RemoveRowsOverSizeLimit(amount * 1024 * 1024 * 1024) ?? 0,
            _ => throw new NotImplementedException($"CacheLimitType '{limitType}' is not implemented.")
        };

        var duration = Stopwatch.GetElapsedTime(startedAt);
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, GetCacheLimitOperationName(limitType), rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        return rowsRemoved;
    }

    public int RemoveRowsInsertedBeforeTick(long tick)
    {
        var startedAt = Stopwatch.GetTimestamp();
        RemoveAllIndicesInsertedBeforeTick(tick);
        var rowsRemoved = rowCache?.RemoveRowsInsertedBeforeTick(tick) ?? 0;
        var duration = Stopwatch.GetElapsedTime(startedAt);
        RefreshOccupancyMetrics();
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, "age_limit", rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        return rowsRemoved;
    }


    public void TryRemoveRowFromAllIndices(IKey primaryKeys, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        foreach (var indexCache in GetLoadedIndexCaches())
        {
            if (indexCache.TryRemovePrimaryKey(primaryKeys, out var rowsRemoved))
                numRowsRemoved += rowsRemoved;
        }
    }

    public bool TryRemoveForeignKeyIndex(ColumnIndex columnIndex, IKey foreignKey, out int numRowsRemoved)
    {
        var indexCache = TryGetIndexCache(columnIndex);
        if (indexCache is null)
        {
            numRowsRemoved = 0;
            return true;
        }

        return indexCache.TryRemoveForeignKey(foreignKey, out numRowsRemoved);
    }

    public bool TryRemovePrimaryKeyIndex(ColumnIndex columnIndex, IKey primaryKeys, out int numRowsRemoved)
    {
        var indexCache = TryGetIndexCache(columnIndex);
        if (indexCache is null)
        {
            numRowsRemoved = 0;
            return true;
        }

        return indexCache.TryRemovePrimaryKey(primaryKeys, out numRowsRemoved);
    }

    public int RemoveAllIndicesInsertedBeforeTick(long tick) =>
        GetLoadedIndexCaches().Sum(x => x.RemoveInsertedBeforeTick(tick));

    public bool TryRemoveTransactionRow(IKey primaryKeys, Transaction transaction, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        return transactionRows?.TryGetValue(transaction, out var transactionRowCache) == true &&
            transactionRowCache.TryRemoveRow(primaryKeys, out numRowsRemoved);
    }

    public bool TryRemoveProviderKey<TKey>(TKey primaryKey, IDataSourceAccess dataSource, out int numRowsRemoved)
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (dataSource is Transaction transaction && transaction.Type != TransactionType.ReadOnly)
        {
            numRowsRemoved = 0;
            return transactionRows?.TryGetValue(transaction, out var transactionRowCache) == true &&
                transactionRowCache.TryRemoveProviderKey(primaryKey, out numRowsRemoved);
        }

        return rowCache?.TryRemoveProviderKey(primaryKey, out numRowsRemoved) ?? RemoveNoRows(out numRowsRemoved);
    }

    public bool TryRemoveTransaction(Transaction transaction)
    {
        var rowsByTransaction = transactionRows;
        if (rowsByTransaction?.ContainsKey(transaction) == true)
        {
            var startedAt = Stopwatch.GetTimestamp();
            if (rowsByTransaction.TryRemove(transaction, out var rows))
            {
                var rowsRemoved = rows.Count;
                var duration = Stopwatch.GetElapsedTime(startedAt);
                RefreshOccupancyMetrics();
                DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, "transaction_remove", rowsRemoved, duration);
                MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
                return true;
            }

            return false;
        }

        return true;
    }

    public void PreloadIndex(IKey foreignKey, RelationProperty otherSide, int? limitRows = null)
    {
        var index = otherSide.RelationPart.GetOtherSide().ColumnIndex;

        var select = new SqlQuery(Table, DatabaseCache.Database.ReadOnlyAccess)
            .What(Table.PrimaryKeyColumns.Concat(index.Columns).Distinct())
            .WhereNot(index.Columns.Select(y => (y.DbName, null as object)));

        var query = select
            .OrderByDesc(index.Columns[0].DbName);

        if (limitRows.HasValue)
            query.Limit(limitRows.Value);

        foreach (var (fk, pk) in query.SelectQuery().ReadPrimaryAndForeignKeys(index))
            GetIndexCache(index).TryAdd(fk, pk);


        var otherColumns = otherSide.RelationPart.ColumnIndex.Columns;
        query = new SqlQuery(otherSide.Model.Table, DatabaseCache.Database.ReadOnlyAccess, "other")
            .What(otherSide.Model.Table.PrimaryKeyColumns)
            .LeftJoin(Table.DbName, "this")
                .On(on =>
                {
                    for (var i = 0; i < index.Columns.Count; i++)
                        on.And(index.Columns[i].DbName, "this")
                          .EqualToColumn(otherColumns[i].DbName, "other");
                })
            .Where(index.Columns.Select(y => (y.DbName, null as object)), BooleanType.And, "this")
            .OrderByDesc(otherColumns[0].DbName, "other");

        if (limitRows.HasValue)
            query.Limit(limitRows.Value);

        foreach (var pk in query.SelectQuery().ReadKeys())
            GetIndexCache(index).TryAdd(pk, []);

        RefreshOccupancyMetrics();
    }

    public IKey[] GetKeys(IKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
    {
        var index = otherSide.RelationPart.GetOtherSide().ColumnIndex;
        if (Table.PrimaryKeyColumns.SequenceEqual(index.Columns))
            return [foreignKey];

        if (dataSource is ReadOnlyAccess && indexCachePolicy.type != IndexCacheType.None)
        {
            if (TryGetIndexCache(index)?.TryGetValue(foreignKey, out var keys) == true)
                return keys!;

            //if (IndexCaches[index].Count == 0)
            //{
            //    PreloadIndex(foreignKey, otherSide, indexCachePolicy.type == IndexCacheType.MaxAmountRows ? indexCachePolicy.amount : null);
            //    Log.IndexCachePreload(loggingConfiguration.CacheLogger, index, IndexCaches[index].Count);

            //    var rowCount = GetRows(IndexCaches[index].Values.SelectMany(x => x).Take(1000).ToArray(), dataSource).Count();
            //    Log.RowCachePreload(loggingConfiguration.CacheLogger, Table, rowCount);

            //    if (IndexCaches[index].TryGetValue(foreignKey, out var retryKeys))
            //        return retryKeys!;
            //}
        }

        var select = new SqlQuery(Table, dataSource ?? DatabaseCache.Database.ReadOnlyAccess)
            .What(Table.PrimaryKeyColumns)
            .Where(index.Columns, foreignKey)
            .SelectQuery();

        var newKeys = KeyFactory.GetKeys(select, Table.PrimaryKeyColumns).ToArray();

        if (indexCachePolicy.type != IndexCacheType.None)
            GetIndexCache(index).TryAdd(foreignKey, newKeys);

        RefreshOccupancyMetrics();

        return newKeys;
    }

    public IEnumerable<IImmutableInstance> GetRows(IKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
    {
        if (foreignKey is NullKey)
            return [];

        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        var index = otherSide.RelationPart.GetOtherSide().ColumnIndex;
        if (Table.PrimaryKeyColumns.SequenceEqual(index.Columns))
        {
            var row = GetRow(foreignKey, dataSource);
            return row is null ? [] : [row];
        }

        if (dataSource is ReadOnlyAccess &&
            indexCachePolicy.type != IndexCacheType.None &&
            TryGetIndexCache(index)?.TryGetValue(foreignKey, out var keys) == true)
            return GetRows(keys!, dataSource);

        return LoadRowsFromForeignKeyAndCache(foreignKey, index, dataSource);
    }

    public IImmutableInstance? GetRow(IKey primaryKeys, IDataSourceAccess dataSource)
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (GetRowFromCache(primaryKeys, dataSource, out var row))
        {
            MetricsHandle.RecordRowCacheHits(1);
            MetricsHandle.RecordRowCacheMisses(0);
            Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, 1);
            return row;
        }

        MetricsHandle.RecordRowCacheHits(0);
        MetricsHandle.RecordRowCacheMisses(1);
        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, 0);

        var rowData = GetRowDataFromPrimaryKey(primaryKeys, dataSource);
        if (rowData is not null)
        {
            MetricsHandle.RecordDatabaseRowsLoaded(1);
            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, 1);
            return AddRow(rowData, dataSource);
        }

        Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, 1);
        return null;
    }

    public IImmutableInstance? GetRow<TKey>(TKey primaryKey, IDataSourceAccess dataSource)
    {
        if (!CanUseScalarProviderKey(primaryKey))
            return GetRow(KeyFactory.CreateKeyFromValue(primaryKey), dataSource);

        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (GetRowFromCache(primaryKey, dataSource, out var row))
        {
            MetricsHandle.RecordRowCacheHits(1);
            MetricsHandle.RecordRowCacheMisses(0);
            Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, 1);
            return row;
        }

        MetricsHandle.RecordRowCacheHits(0);
        MetricsHandle.RecordRowCacheMisses(1);
        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, 0);

        var rowData = GetRowDataFromPrimaryKeyValue(primaryKey, dataSource);
        if (rowData is not null)
        {
            MetricsHandle.RecordDatabaseRowsLoaded(1);
            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, 1);
            return AddRow(rowData, dataSource);
        }

        Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, 1);
        return null;
    }

    public IEnumerable<IImmutableInstance> GetRows(IKey[] primaryKeys, IDataSourceAccess dataSource, List<OrderBy>? orderings = null)
    {
        EnsureTransactionRowCache(dataSource);

        if (primaryKeys.Length == 0)
            return [];

        if (orderings == null || orderings.Count == 0)
        {
            if (primaryKeys.Length == 1)
            {
                var row = GetRow(primaryKeys[0], dataSource);
                return row is null ? [] : [row];
            }

            return LoadRowsFromDatabaseAndCache(primaryKeys, dataSource);
        }

        return LoadOrderedRowsFromDatabaseAndCache(primaryKeys, dataSource, orderings);
    }

    private void EnsureTransactionRowCache(IDataSourceAccess dataSource)
    {
        if (dataSource is Transaction transaction && transaction.Type != TransactionType.ReadOnly)
        {
            var rowsByTransaction = GetOrCreateTransactionRows();
            if (!rowsByTransaction.ContainsKey(transaction))
            {
                rowsByTransaction.TryAdd(transaction, new RowCache());
                RefreshOccupancyMetrics();
            }
        }
    }

    private IEnumerable<IImmutableInstance> LoadRowsFromDatabaseAndCache(IKey[] primaryKeys, IDataSourceAccess dataSource)
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;

        var keysToLoad = new List<IKey>(primaryKeys.Length);
        foreach (var key in primaryKeys)
        {
            if (GetRowFromCache(key, dataSource, out var row))
                yield return row!;
            else
                keysToLoad.Add(key);
        }

        MetricsHandle.RecordRowCacheHits(primaryKeys.Length - keysToLoad.Count);
        MetricsHandle.RecordRowCacheMisses(keysToLoad.Count);

        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, primaryKeys.Length - keysToLoad.Count);

        if (keysToLoad.Count != 0)
        {
            foreach (var split in keysToLoad.SplitList(500))
            {
                foreach (var rowData in GetRowDataFromPrimaryKeys(split, dataSource))
                {
                    MetricsHandle.RecordDatabaseRowsLoaded(1);
                    yield return AddRow(rowData, dataSource);
                }
            }

            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, keysToLoad.Count);
        }
    }

    private IImmutableInstance[] LoadRowsFromForeignKeyAndCache(IKey foreignKey, ColumnIndex index, IDataSourceAccess dataSource)
    {
        var q = new SqlQuery(Table, dataSource)
            .Where(index.Columns, foreignKey)
            .SelectQuery();

        var rows = new List<IImmutableInstance>();
        var primaryKeys = indexCachePolicy.type == IndexCacheType.None ? null : new List<IKey>();
        var rowCacheHits = 0;
        var rowCacheMisses = 0;

        foreach (var rowData in q.ReadRows())
        {
            var primaryKey = KeyFactory.GetKey(rowData, Table.PrimaryKeyColumns);
            primaryKeys?.Add(primaryKey);

            if (GetRowFromCache(primaryKey, dataSource, out var cachedRow))
            {
                rowCacheHits++;
                rows.Add(cachedRow!);
                continue;
            }

            rowCacheMisses++;
            MetricsHandle.RecordDatabaseRowsLoaded(1);
            rows.Add(AddRow(rowData, dataSource));
        }

        MetricsHandle.RecordRowCacheHits(rowCacheHits);
        MetricsHandle.RecordRowCacheMisses(rowCacheMisses);
        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, rowCacheHits);
        Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, rowCacheMisses);

        if (primaryKeys is not null)
            GetIndexCache(index).TryAdd(foreignKey, primaryKeys.ToArray());

        RefreshOccupancyMetrics();

        return rows.ToArray();
    }

    private IEnumerable<IImmutableInstance> LoadOrderedRowsFromDatabaseAndCache(IKey[] primaryKeys, IDataSourceAccess dataSource, List<OrderBy> orderings)
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;

        var keysToLoad = new List<IKey>(primaryKeys.Length);
        var loadedRows = new List<IImmutableInstance>(primaryKeys.Length);

        foreach (var key in primaryKeys)
        {
            if (GetRowFromCache(key, dataSource, out var row))
                loadedRows.Add(row!);
            else
                keysToLoad.Add(key);
        }

        MetricsHandle.RecordRowCacheHits(loadedRows.Count);
        MetricsHandle.RecordRowCacheMisses(keysToLoad.Count);

        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, loadedRows.Count);

        if (keysToLoad.Count != 0)
        {
            foreach (var split in keysToLoad.SplitList(500))
            {
                foreach (var rowData in GetRowDataFromPrimaryKeys(split, dataSource, orderings))
                {
                    MetricsHandle.RecordDatabaseRowsLoaded(1);
                    loadedRows.Add(AddRow(rowData, dataSource));
                }
            }

            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, keysToLoad.Count);
        }

        IOrderedEnumerable<IImmutableInstance>? orderedRows = null;

        foreach (var ordering in orderings)
        {
            Func<IImmutableInstance, IComparable?> keySelector = x => (IComparable?)x.GetValues([ordering.Column]).First().Value;

            if (orderedRows == null)
            {
                orderedRows = ordering.Ascending
                    ? loadedRows.OrderBy(keySelector)
                    : loadedRows.OrderByDescending(keySelector);
            }
            else
            {
                orderedRows = ordering.Ascending
                    ? orderedRows.ThenBy(keySelector)
                    : orderedRows.ThenByDescending(keySelector);
            }
        }

        return orderedRows == null
            ? loadedRows
            : orderedRows;
    }

    private IEnumerable<RowData> GetRowDataFromPrimaryKeys(IEnumerable<IKey> keys, IDataSourceAccess dataSource, List<OrderBy>? orderings = null)
    {
        var q = new SqlQuery(Table, dataSource);

        if (!keys.Any()) // Optimization: if no keys, return empty
            return [];

        if (Table.PrimaryKeyColumns.Length == 1)
        {
            var pkColumn = Table.PrimaryKeyColumns[0];

            q.Where(pkColumn.DbName)
             .In(keys.Select(x => dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, x.GetValue(0))));
        }
        else
        {
            foreach (var key in keys)
            {
                // Each key's set of PK conditions forms an AND group.
                // This AND group is ORed with other key's AND groups.
                // How it connects to the *previous* AND group. The first one is effectively standalone.
                var connectionToPreviousKeyGroup = (key == keys.First()) ? BooleanType.And : BooleanType.Or;

                var keySpecificAndGroup = q.AddWhereGroup(connectionToPreviousKeyGroup); // This creates a new subgroup for ANDs, connected by OR to previous

                for (var i = 0; i < primaryKeyColumnsCount; i++)
                {
                    var pkColumn = Table.PrimaryKeyColumns[i];
                    // All conditions for a single key are ANDed together *within* keySpecificAndGroup.
                    // The AddWhere on keySpecificAndGroup will use its InternalJoinType (which is AND by default for new groups from SqlQuery.AddWhereGroup)
                    keySpecificAndGroup.Where(pkColumn.DbName)
                                       .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, key.GetValue(i)));
                }
            }
        }

        if (orderings != null)
        {
            foreach (var order in orderings)
                q.OrderBy(order.Column, order.Alias, order.Ascending);
        }

        return q
            .SelectQuery()
            .ReadRows();
    }

    private RowData? GetRowDataFromPrimaryKey(IKey key, IDataSourceAccess dataSource)
    {
        var q = new SqlQuery(Table, dataSource);

        if (Table.PrimaryKeyColumns.Length == 1)
        {
            var pkColumn = Table.PrimaryKeyColumns[0];
            q.Where(pkColumn.DbName)
             .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, key.GetValue(0)));
        }
        else
        {
            for (var i = 0; i < primaryKeyColumnsCount; i++)
            {
                var pkColumn = Table.PrimaryKeyColumns[i];
                q.Where(pkColumn.DbName)
                 .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, key.GetValue(i)));
            }
        }

        return q
            .SelectQuery()
            .ReadFirstRow();
    }

    private RowData? GetRowDataFromPrimaryKeyValue<TKey>(TKey key, IDataSourceAccess dataSource)
    {
        var pkColumn = Table.PrimaryKeyColumns[0];
        var q = new SqlQuery(Table, dataSource);
        q.Where(pkColumn.DbName)
         .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, key));

        return q
            .SelectQuery()
            .ReadFirstRow();
    }

    private bool GetRowFromCache(IKey key, IDataSourceAccess dataSource, out IImmutableInstance? row)
    {
        if (dataSource is ReadOnlyAccess && rowCache is not null && rowCache.TryGetValue(key, out row))
            return true;
        else if (dataSource is Transaction transaction &&
            transactionRows is not null &&
            transactionRows.TryGetValue(transaction, out var transactionRowCache) &&
            transactionRowCache.TryGetValue(key, out row))
            return true;

        row = null;
        return false;
    }

    private bool GetRowFromCache<TKey>(TKey key, IDataSourceAccess dataSource, out IImmutableInstance? row)
    {
        if (dataSource is ReadOnlyAccess && rowCache is not null && rowCache.TryGetValue(key, out row))
            return true;
        else if (dataSource is Transaction transaction &&
            transactionRows is not null &&
            transactionRows.TryGetValue(transaction, out var transactionRowCache) &&
            transactionRowCache.TryGetValue(key, out row))
            return true;

        row = null;
        return false;
    }

    private bool CanUseScalarProviderKey<TKey>(TKey primaryKey)
    {
        return primaryKey is not null &&
            Table.PrimaryKeyShape.SupportsScalarProviderKey(primaryKey.GetType());
    }

    private static bool RemoveNoRows(out int numRowsRemoved)
    {
        numRowsRemoved = 0;
        return true;
    }

    private IImmutableInstance AddRow(RowData rowData, IDataSourceAccess transaction)
    {
        TryAddRow(rowData, transaction, out var row);
        return row;
    }

    private bool TryAddRow(RowData rowData, IDataSourceAccess dataSource, out IImmutableInstance row)
    {
        row = InstanceFactory.NewImmutableRow(rowData, dataSource);
        var keys = KeyFactory.GetKey(rowData, Table.PrimaryKeyColumns);

        var added = (dataSource is ReadOnlyAccess && (!Table.UseCache || GetOrCreateRowCache().TryAddRow(keys, rowData, row)))
            || (dataSource is Transaction transaction &&
                transactionRows is not null &&
                transactionRows.TryGetValue(transaction, out var transactionRowCache) &&
                transactionRowCache.TryAddRow(keys, rowData, row));

        if (added)
        {
            MetricsHandle.RecordRowCacheStore();
            RefreshOccupancyMetrics();
        }

        return added;
    }

    public TableCacheSnapshot MakeSnapshot()
    {
        return new(Table.DbName, RowCount, TotalBytes, NewestTick, OldestTick, IndicesCount.ToArray());
    }

    internal void UnregisterTelemetry()
    {
        DataLinqTelemetry.UnregisterTableCache(telemetryContext, Table.DbName);
    }

    private CacheOccupancyMetricsSnapshot GetOccupancySnapshot()
        => new(
            Rows: RowCount,
            TransactionRows: transactionRows?.Values.Sum(x => (long)x.Count) ?? 0,
            Bytes: TotalBytes,
            IndexEntries: GetLoadedIndexCaches().Sum(x => (long)x.Count));

    private void RefreshOccupancyMetrics()
    {
        MetricsHandle.UpdateCacheOccupancy(GetOccupancySnapshot());
    }

    private static string GetCacheLimitOperationName(CacheLimitType limitType)
        => limitType switch
        {
            CacheLimitType.Rows => "row_limit",
            CacheLimitType.Bytes => "size_limit",
            CacheLimitType.Kilobytes => "size_limit",
            CacheLimitType.Megabytes => "size_limit",
            CacheLimitType.Gigabytes => "size_limit",
            CacheLimitType.Seconds => "age_limit",
            CacheLimitType.Minutes => "age_limit",
            CacheLimitType.Hours => "age_limit",
            CacheLimitType.Days => "age_limit",
            CacheLimitType.Ticks => "age_limit",
            _ => "limit"
        };
}
