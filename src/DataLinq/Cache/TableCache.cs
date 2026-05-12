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
    private Dictionary<ColumnIndex, IIndexCache>? indexCaches;
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

    private IIndexCache GetIndexCache(ColumnIndex index)
    {
        lock (indexCacheGate)
        {
            indexCaches ??= new Dictionary<ColumnIndex, IIndexCache>();

            if (!indexCaches.TryGetValue(index, out var cache))
            {
                cache = CreateIndexCache(index);
                indexCaches.Add(index, cache);
            }

            return cache;
        }
    }

    private IIndexCache? TryGetIndexCache(ColumnIndex index)
    {
        lock (indexCacheGate)
        {
            return indexCaches is not null && indexCaches.TryGetValue(index, out var cache)
                ? cache
                : null;
        }
    }

    private IIndexCache[] GetLoadedIndexCaches()
    {
        lock (indexCacheGate)
        {
            return indexCaches is null
                ? []
                : indexCaches.Values.ToArray();
        }
    }

    private static IIndexCache CreateIndexCache(ColumnIndex index)
    {
        if (index.Columns.Count == 1)
        {
            return TableKeyShape.GetProviderStoreKind(index.Columns[0]) switch
            {
                TableKeyComponentStoreKind.Int32 => new TypedIndexCache<int>(),
                TableKeyComponentStoreKind.Int64 => new TypedIndexCache<long>(),
                TableKeyComponentStoreKind.Guid => new TypedIndexCache<Guid>(),
                TableKeyComponentStoreKind.String => new TypedIndexCache<string>(),
                _ => new IndexCache()
            };
        }

        return new IndexCache();
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
            DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.TransactionStateChange, numRows, duration);
        }

        MetricsHandle.RecordCacheCleanup(0, duration);
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.TransactionStateChangeTable, 0, duration);

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
                if (rowCache is not null && TryRemoveRowFromCache(rowCache, change.PrimaryKeys, out var rows))
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
            DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.StateChangePrecise, numRows, duration);
        }

        MetricsHandle.RecordCacheCleanup(0, duration);
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.StateChangeTable, 0, duration);

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
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.Clear, rowsRemoved, duration);
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
        DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.AgeLimit, rowsRemoved, duration);
        MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
        return rowsRemoved;
    }


    public void TryRemoveRowFromAllIndices(DataLinqKey primaryKeys, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        foreach (var indexCache in GetLoadedIndexCaches())
        {
            if (indexCache.TryRemovePrimaryKey(primaryKeys, out var rowsRemoved))
                numRowsRemoved += rowsRemoved;
        }
    }

    public bool TryRemoveForeignKeyIndex(ColumnIndex columnIndex, DataLinqKey foreignKey, out int numRowsRemoved)
    {
        var indexCache = TryGetIndexCache(columnIndex);
        if (indexCache is null)
        {
            numRowsRemoved = 0;
            return true;
        }

        return indexCache.TryRemoveForeignKey(foreignKey, out numRowsRemoved);
    }

    public bool TryRemoveForeignKeyIndex<TKey>(ColumnIndex columnIndex, TKey foreignKey, out int numRowsRemoved)
        where TKey : notnull
    {
        var indexCache = TryGetIndexCache(columnIndex);
        if (indexCache is null)
        {
            numRowsRemoved = 0;
            return true;
        }

        return indexCache.TryRemoveProviderKey(foreignKey, out numRowsRemoved);
    }

    public bool TryRemovePrimaryKeyIndex(ColumnIndex columnIndex, DataLinqKey primaryKeys, out int numRowsRemoved)
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

    public bool TryRemoveTransactionRow(DataLinqKey primaryKeys, Transaction transaction, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        return transactionRows?.TryGetValue(transaction, out var transactionRowCache) == true &&
            TryRemoveRowFromCache(transactionRowCache, primaryKeys, out numRowsRemoved);
    }

    public bool TryRemoveProviderKey<TKey>(TKey primaryKey, IDataSourceAccess dataSource, out int numRowsRemoved)
        where TKey : notnull
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
                DataLinqTelemetry.RecordCacheMaintenance(telemetryContext, Table.DbName, CacheMaintenanceOperations.TransactionRemove, rowsRemoved, duration);
                MetricsHandle.RecordCacheCleanup(rowsRemoved, duration);
                return true;
            }

            return false;
        }

        return true;
    }

    public void PreloadIndex(DataLinqKey foreignKey, RelationProperty otherSide, int? limitRows = null)
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

    public DataLinqKey[] GetKeys(DataLinqKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
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

    public DataLinqKey[] GetKeys<TKey>(TKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        if (foreignKey is DataLinqKey dataLinqKey)
            return GetKeys(dataLinqKey, otherSide, dataSource);

        var index = otherSide.RelationPart.GetOtherSide().ColumnIndex;
        if (Table.PrimaryKeyColumns.SequenceEqual(index.Columns))
            return [DataLinqKey.FromValue(foreignKey)];

        if (dataSource is ReadOnlyAccess && indexCachePolicy.type != IndexCacheType.None)
        {
            if (TryGetIndexCache(index)?.TryGetProviderKey(foreignKey, out var keys) == true)
                return keys!;
        }

        var select = new SqlQuery(Table, dataSource ?? DatabaseCache.Database.ReadOnlyAccess)
            .What(Table.PrimaryKeyColumns)
            .Where(index.Columns, foreignKey)
            .SelectQuery();

        var newKeys = KeyFactory.GetKeys(select, Table.PrimaryKeyColumns).ToArray();

        if (indexCachePolicy.type != IndexCacheType.None)
            GetIndexCache(index).TryAddProviderKey(foreignKey, newKeys);

        RefreshOccupancyMetrics();

        return newKeys;
    }

    public IEnumerable<IImmutableInstance> GetRows(DataLinqKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
    {
        if (foreignKey.IsNull)
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

    public IEnumerable<IImmutableInstance> GetRows<TKey>(TKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        if (foreignKey is DataLinqKey dataLinqKey)
            return GetRows(dataLinqKey, otherSide, dataSource);

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
            TryGetIndexCache(index)?.TryGetProviderKey(foreignKey, out var keys) == true)
            return GetRows(keys!, dataSource);

        return LoadRowsFromForeignKeyAndCache(foreignKey, index, dataSource);
    }

    public IImmutableInstance? GetRow(DataLinqKey primaryKeys, IDataSourceAccess dataSource)
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

    public IImmutableInstance? GetRow<TKey>(
        TKey primaryKey,
        IDataSourceAccess dataSource)
        where TKey : notnull
    {
        if (primaryKey is null)
            return null;

        if (primaryKey is DataLinqKey dataLinqKey)
            return GetRow(dataLinqKey, dataSource);

        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (ShouldUseDynamicKeyLookup(primaryKey))
            return GetRow(DataLinqKey.FromValue(primaryKey), dataSource);

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
            return AddRow(rowData, dataSource, primaryKey);
        }

        Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, 1);
        return null;
    }

    private bool ShouldUseDynamicKeyLookup<TKey>(TKey primaryKey)
        where TKey : notnull
    {
        if (primaryKey is IProviderKey)
            return false;

        if (Table.Model.ProviderKeyRowStoreAccessor is IProviderKeyRowStoreAccessor)
            return false;

        if (!Table.PrimaryKeyShape.IsScalar)
            return false;

        return Table.PrimaryKeyShape[0].ProviderStoreKind switch
        {
            TableKeyComponentStoreKind.Int32 => primaryKey is not int,
            TableKeyComponentStoreKind.Int64 => primaryKey is not long,
            TableKeyComponentStoreKind.Guid => primaryKey is not Guid,
            TableKeyComponentStoreKind.String => primaryKey is not string,
            _ => true
        };
    }

    public IEnumerable<IImmutableInstance> GetRows(DataLinqKey[] primaryKeys, IDataSourceAccess dataSource, List<OrderBy>? orderings = null)
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

    internal bool TryGetRowsFromScalarPrimaryKeyQuery<T>(
        Select<T> select,
        IDataSourceAccess dataSource,
        List<OrderBy>? orderings,
        out IEnumerable<IImmutableInstance> rows)
    {
        if (!Table.PrimaryKeyShape.SupportsScalarProviderKeyStore)
        {
            rows = [];
            return false;
        }

        select.What(Table.PrimaryKeyColumns);
        var primaryKeyColumn = Table.PrimaryKeyColumns[0];

        rows = Table.PrimaryKeyShape[0].ProviderStoreKind switch
        {
            TableKeyComponentStoreKind.Int32 => GetRows(ReadScalarPrimaryKeys<T, int>(select, primaryKeyColumn), dataSource, orderings),
            TableKeyComponentStoreKind.Int64 => GetRows(ReadScalarPrimaryKeys<T, long>(select, primaryKeyColumn), dataSource, orderings),
            TableKeyComponentStoreKind.Guid => GetRows(ReadScalarPrimaryKeys<T, Guid>(select, primaryKeyColumn), dataSource, orderings),
            TableKeyComponentStoreKind.String => GetRows(ReadScalarPrimaryKeys<T, string>(select, primaryKeyColumn), dataSource, orderings),
            _ => []
        };

        return true;
    }

    internal IEnumerable<IImmutableInstance> GetRows<TKey>(IReadOnlyList<TKey> primaryKeys, IDataSourceAccess dataSource, List<OrderBy>? orderings = null)
        where TKey : notnull
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (primaryKeys.Count == 0)
            return [];

        if (orderings == null || orderings.Count == 0)
        {
            if (primaryKeys.Count == 1)
            {
                var row = GetRow(primaryKeys[0], dataSource);
                return row is null ? [] : [row];
            }

            return LoadRowsFromDatabaseAndCache(primaryKeys, dataSource);
        }

        return LoadOrderedRowsFromDatabaseAndCache(primaryKeys, dataSource, orderings);
    }

    internal bool TryGetRowFromProviderKeyValue(object? primaryKey, IDataSourceAccess dataSource, out IImmutableInstance? row)
    {
        row = null;
        if (primaryKey is null || !Table.PrimaryKeyShape.SupportsScalarProviderKeyStore)
            return false;

        switch (Table.PrimaryKeyShape[0].ProviderStoreKind)
        {
            case TableKeyComponentStoreKind.Int32 when primaryKey is int intKey:
                row = GetRow(intKey, dataSource);
                return true;
            case TableKeyComponentStoreKind.Int64 when primaryKey is long longKey:
                row = GetRow(longKey, dataSource);
                return true;
            case TableKeyComponentStoreKind.Guid when primaryKey is Guid guidKey:
                row = GetRow(guidKey, dataSource);
                return true;
            case TableKeyComponentStoreKind.String when primaryKey is string stringKey:
                row = GetRow(stringKey, dataSource);
                return true;
            default:
                return false;
        }
    }

    internal IImmutableInstance? GetRow(
        IDataLinqDataReader reader,
        IReadOnlyList<int> primaryKeyOrdinals,
        IDataSourceAccess dataSource)
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (Table.Model.ProviderKeyRowStoreAccessor is IProviderKeyDataReaderRowStoreAccessor providerKeyAccessor &&
            providerKeyAccessor.TryGetRow(this, reader, primaryKeyOrdinals, dataSource, out var row))
            return row;

        if (TryReadScalarPrimaryKeyValue(reader, primaryKeyOrdinals, out var primaryKey) &&
            TryGetRowFromProviderKeyValue(primaryKey, dataSource, out row))
            return row;

        return GetRow(ReadPrimaryKey(reader, primaryKeyOrdinals), dataSource);
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

    private IEnumerable<IImmutableInstance> LoadRowsFromDatabaseAndCache(DataLinqKey[] primaryKeys, IDataSourceAccess dataSource)
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;

        var keysToLoad = new List<DataLinqKey>(primaryKeys.Length);
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

    private IEnumerable<IImmutableInstance> LoadRowsFromDatabaseAndCache<TKey>(IReadOnlyList<TKey> primaryKeys, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;

        var keysToLoad = new List<TKey>(primaryKeys.Count);
        foreach (var key in primaryKeys)
        {
            if (GetRowFromCache(key, dataSource, out var row))
                yield return row!;
            else
                keysToLoad.Add(key);
        }

        MetricsHandle.RecordRowCacheHits(primaryKeys.Count - keysToLoad.Count);
        MetricsHandle.RecordRowCacheMisses(keysToLoad.Count);

        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, primaryKeys.Count - keysToLoad.Count);

        if (keysToLoad.Count != 0)
        {
            foreach (var split in keysToLoad.SplitList(500))
            {
                foreach (var rowData in GetRowDataFromPrimaryKeyValues(split, dataSource))
                {
                    MetricsHandle.RecordDatabaseRowsLoaded(1);
                    yield return AddRow(rowData, dataSource);
                }
            }

            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, keysToLoad.Count);
        }
    }

    private IImmutableInstance[] LoadRowsFromForeignKeyAndCache(DataLinqKey foreignKey, ColumnIndex index, IDataSourceAccess dataSource)
    {
        var q = new SqlQuery(Table, dataSource)
            .Where(index.Columns, foreignKey)
            .SelectQuery();

        var rows = new List<IImmutableInstance>();
        var primaryKeys = indexCachePolicy.type == IndexCacheType.None ? null : new List<DataLinqKey>();
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

    private IImmutableInstance[] LoadRowsFromForeignKeyAndCache<TKey>(TKey foreignKey, ColumnIndex index, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        var q = new SqlQuery(Table, dataSource)
            .Where(index.Columns, foreignKey)
            .SelectQuery();

        var rows = new List<IImmutableInstance>();
        var primaryKeys = indexCachePolicy.type == IndexCacheType.None ? null : new List<DataLinqKey>();
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
            GetIndexCache(index).TryAddProviderKey(foreignKey, primaryKeys.ToArray());

        RefreshOccupancyMetrics();

        return rows.ToArray();
    }

    private IEnumerable<IImmutableInstance> LoadOrderedRowsFromDatabaseAndCache(DataLinqKey[] primaryKeys, IDataSourceAccess dataSource, List<OrderBy> orderings)
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;

        var keysToLoad = new List<DataLinqKey>(primaryKeys.Length);
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

    private IEnumerable<IImmutableInstance> LoadOrderedRowsFromDatabaseAndCache<TKey>(IReadOnlyList<TKey> primaryKeys, IDataSourceAccess dataSource, List<OrderBy> orderings)
        where TKey : notnull
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;

        var keysToLoad = new List<TKey>(primaryKeys.Count);
        var loadedRows = new List<IImmutableInstance>(primaryKeys.Count);

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
                foreach (var rowData in GetRowDataFromPrimaryKeyValues(split, dataSource, orderings))
                {
                    MetricsHandle.RecordDatabaseRowsLoaded(1);
                    loadedRows.Add(AddRow(rowData, dataSource));
                }
            }

            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, keysToLoad.Count);
        }

        return ApplyOrderings(loadedRows, orderings);
    }

    private static IEnumerable<IImmutableInstance> ApplyOrderings(
        IEnumerable<IImmutableInstance> rows,
        List<OrderBy> orderings)
    {
        IOrderedEnumerable<IImmutableInstance>? orderedRows = null;

        foreach (var ordering in orderings)
        {
            Func<IImmutableInstance, IComparable?> keySelector = x => (IComparable?)x.GetValues([ordering.Column]).First().Value;

            if (orderedRows == null)
            {
                orderedRows = ordering.Ascending
                    ? rows.OrderBy(keySelector)
                    : rows.OrderByDescending(keySelector);
            }
            else
            {
                orderedRows = ordering.Ascending
                    ? orderedRows.ThenBy(keySelector)
                    : orderedRows.ThenByDescending(keySelector);
            }
        }

        return orderedRows ?? rows;
    }

    private IEnumerable<RowData> GetRowDataFromPrimaryKeys(IEnumerable<DataLinqKey> keys, IDataSourceAccess dataSource, List<OrderBy>? orderings = null)
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

    private IEnumerable<RowData> GetRowDataFromPrimaryKeyValues<TKey>(IEnumerable<TKey> keys, IDataSourceAccess dataSource, List<OrderBy>? orderings = null)
        where TKey : notnull
    {
        var keyArray = keys as TKey[] ?? keys.ToArray();
        if (keyArray.Length == 0)
            return [];

        var q = new SqlQuery(Table, dataSource);

        if (Table.PrimaryKeyColumns.Length == 1)
        {
            var pkColumn = Table.PrimaryKeyColumns[0];

            q.Where(pkColumn.DbName)
             .In(keyArray.Select(key => dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, key)));
        }
        else
        {
            var first = true;
            foreach (var key in keyArray)
            {
                if (key is not IProviderKey providerKey)
                    throw new InvalidOperationException(
                        $"Provider key for table '{Table.DbName}' must expose components for composite lookup.");

                if (providerKey.ValueCount != primaryKeyColumnsCount)
                    throw new InvalidOperationException(
                        $"Provider key for table '{Table.DbName}' has {providerKey.ValueCount} components, expected {primaryKeyColumnsCount}.");

                var keySpecificAndGroup = q.AddWhereGroup(first ? BooleanType.And : BooleanType.Or);
                first = false;

                for (var i = 0; i < primaryKeyColumnsCount; i++)
                {
                    var pkColumn = Table.PrimaryKeyColumns[i];
                    keySpecificAndGroup.Where(pkColumn.DbName)
                        .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, providerKey.GetValue(i)));
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

    private static List<TKey> ReadScalarPrimaryKeys<TSelect, TKey>(Select<TSelect> select, ColumnDefinition column)
        where TKey : notnull
    {
        var keys = new List<TKey>();
        foreach (var reader in select.ReadReader())
        {
            if (reader.GetValue<TKey>(column, 0) is TKey key)
                keys.Add(key);
        }

        return keys;
    }

    private DataLinqKey ReadPrimaryKey(IDataLinqDataReader reader, IReadOnlyList<int> primaryKeyOrdinals)
    {
        if (primaryKeyColumnsCount == 1)
            return DataLinqKey.FromValue(reader.GetValue<object>(Table.PrimaryKeyColumns[0], primaryKeyOrdinals[0]));

        var values = new object?[primaryKeyColumnsCount];
        for (var i = 0; i < values.Length; i++)
            values[i] = reader.GetValue<object>(Table.PrimaryKeyColumns[i], primaryKeyOrdinals[i]);

        return DataLinqKey.FromValues(values);
    }

    private bool TryReadScalarPrimaryKeyValue(
        IDataLinqDataReader reader,
        IReadOnlyList<int> primaryKeyOrdinals,
        out object? primaryKey)
    {
        primaryKey = null;
        if (!Table.PrimaryKeyShape.SupportsScalarProviderKeyStore || primaryKeyOrdinals.Count != 1)
            return false;

        var column = Table.PrimaryKeyColumns[0];
        primaryKey = Table.PrimaryKeyShape[0].ProviderStoreKind switch
        {
            TableKeyComponentStoreKind.Int32 => reader.GetValue<int>(column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.Int64 => reader.GetValue<long>(column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.Guid => reader.GetValue<Guid>(column, primaryKeyOrdinals[0]),
            TableKeyComponentStoreKind.String => reader.GetValue<string>(column, primaryKeyOrdinals[0]),
            _ => null
        };

        return primaryKey is not null;
    }

    private RowData? GetRowDataFromPrimaryKey(DataLinqKey key, IDataSourceAccess dataSource)
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
        where TKey : notnull
    {
        var q = new SqlQuery(Table, dataSource);

        if (key is IProviderKey providerKey)
        {
            if (providerKey.ValueCount != primaryKeyColumnsCount)
                throw new InvalidOperationException(
                    $"Provider key for table '{Table.DbName}' has {providerKey.ValueCount} components, expected {primaryKeyColumnsCount}.");

            for (var i = 0; i < primaryKeyColumnsCount; i++)
            {
                var pkColumn = Table.PrimaryKeyColumns[i];
                q.Where(pkColumn.DbName)
                 .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, providerKey.GetValue(i)));
            }
        }
        else
        {
            var pkColumn = Table.PrimaryKeyColumns[0];
            q.Where(pkColumn.DbName)
             .EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(pkColumn, key));
        }

        return q
            .SelectQuery()
            .ReadFirstRow();
    }

    private bool GetRowFromCache(DataLinqKey key, IDataSourceAccess dataSource, out IImmutableInstance? row)
    {
        if (dataSource is ReadOnlyAccess && rowCache is not null && TryGetRowFromCache(rowCache, key, out row))
            return true;
        else if (dataSource is Transaction transaction &&
            transactionRows is not null &&
            transactionRows.TryGetValue(transaction, out var transactionRowCache) &&
            TryGetRowFromCache(transactionRowCache, key, out row))
            return true;

        row = null;
        return false;
    }

    private bool TryGetRowFromCache(RowCache cache, DataLinqKey key, out IImmutableInstance? row)
    {
        if (Table.Model.ProviderKeyRowStoreAccessor is IProviderKeyRowStoreAccessor providerKeyAccessor &&
            providerKeyAccessor.TryGetRow(cache, key, out row))
            return row is not null;

        return cache.TryGetValue(key, out row);
    }

    private bool TryRemoveRowFromCache(RowCache cache, DataLinqKey key, out int numRowsRemoved)
    {
        if (Table.Model.ProviderKeyRowStoreAccessor is IProviderKeyRowStoreAccessor providerKeyAccessor &&
            providerKeyAccessor.TryRemoveRow(cache, key, out numRowsRemoved))
            return true;

        return cache.TryRemoveRow(key, out numRowsRemoved);
    }

    private bool GetRowFromCache<TKey>(TKey key, IDataSourceAccess dataSource, out IImmutableInstance? row)
        where TKey : notnull
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

    private IImmutableInstance AddRow<TKey>(
        RowData rowData,
        IDataSourceAccess transaction,
        TKey primaryKey)
        where TKey : notnull
    {
        TryAddRow(rowData, transaction, primaryKey, out var row);
        return row;
    }

    private bool TryAddRow(RowData rowData, IDataSourceAccess dataSource, out IImmutableInstance row)
    {
        row = InstanceFactory.NewImmutableRow(rowData, dataSource);

        var added = (dataSource is ReadOnlyAccess && (!Table.UseCache || TryAddRowToCache(GetOrCreateRowCache(), rowData, row)))
            || (dataSource is Transaction transaction &&
                transactionRows is not null &&
                transactionRows.TryGetValue(transaction, out var transactionRowCache) &&
                TryAddRowToCache(transactionRowCache, rowData, row));

        if (added)
        {
            MetricsHandle.RecordRowCacheStore();
            RefreshOccupancyMetrics();
        }

        return added;
    }

    private bool TryAddRow<TKey>(
        RowData rowData,
        IDataSourceAccess dataSource,
        TKey primaryKey,
        out IImmutableInstance row)
        where TKey : notnull
    {
        row = InstanceFactory.NewImmutableRow(rowData, dataSource);

        var added = (dataSource is ReadOnlyAccess && (!Table.UseCache || GetOrCreateRowCache().TryAddRow(primaryKey, rowData.Size, row)))
            || (dataSource is Transaction transaction &&
                transactionRows is not null &&
                transactionRows.TryGetValue(transaction, out var transactionRowCache) &&
                transactionRowCache.TryAddRow(primaryKey, rowData.Size, row));

        if (added)
        {
            MetricsHandle.RecordRowCacheStore();
            RefreshOccupancyMetrics();
        }

        return added;
    }

    private bool TryAddRowToCache(RowCache cache, RowData rowData, IImmutableInstance row)
    {
        if (Table.Model.ProviderKeyRowStoreAccessor is IProviderKeyRowStoreAccessor providerKeyAccessor)
            return providerKeyAccessor.TryAddRow(cache, rowData, row);

        if (Table.PrimaryKeyShape.IsScalar)
        {
            var column = Table.PrimaryKeyColumns[0];
            var value = rowData.GetValue(column);
            if (value is null)
                return false;

            return Table.PrimaryKeyShape[0].ProviderStoreKind switch
            {
                TableKeyComponentStoreKind.Int32 when value is int intKey => cache.TryAddRow(intKey, rowData.Size, row),
                TableKeyComponentStoreKind.Int64 when value is long longKey => cache.TryAddRow(longKey, rowData.Size, row),
                TableKeyComponentStoreKind.Guid when value is Guid guidKey => cache.TryAddRow(guidKey, rowData.Size, row),
                TableKeyComponentStoreKind.String when value is string stringKey => cache.TryAddRow(stringKey, rowData.Size, row),
                _ => false
            };
        }

        var keys = KeyFactory.GetKey(rowData, Table.PrimaryKeyColumns);
        return cache.TryAddRow(keys, rowData, row);
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
            CacheLimitType.Rows => CacheMaintenanceOperations.RowLimit,
            CacheLimitType.Bytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Kilobytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Megabytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Gigabytes => CacheMaintenanceOperations.SizeLimit,
            CacheLimitType.Seconds => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Minutes => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Hours => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Days => CacheMaintenanceOperations.AgeLimit,
            CacheLimitType.Ticks => CacheMaintenanceOperations.AgeLimit,
            _ => CacheMaintenanceOperations.Limit
        };
}
