using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

public class RowChangeEventArgs : EventArgs
{
    // For now, we can keep this empty since a full clear is acceptable.
}

public class TableCache
{
    protected Dictionary<ColumnIndex, IndexCache> IndexCaches;
    protected KeyCache<IKey> PrimaryKeysCache = new();
    protected RowCache RowCache = new();
    protected ConcurrentDictionary<Transaction, RowCache> TransactionRows = new();

    protected int primaryKeyColumnsCount;
    protected List<ColumnIndex> indices;
    protected (IndexCacheType type, int? amount) indexCachePolicy;
    private readonly DataLinqLoggingConfiguration loggingConfiguration;

    public event EventHandler<RowChangeEventArgs>? RowChanged;

    public TableCache(TableDefinition table, DatabaseCache databaseCache, DataLinqLoggingConfiguration loggingConfiguration)
    {
        this.Table = table;
        this.DatabaseCache = databaseCache;
        this.loggingConfiguration = loggingConfiguration;
        this.primaryKeyColumnsCount = Table.PrimaryKeyColumns.Length;
        this.indices = Table.ColumnIndices;
        this.indexCachePolicy = GetIndexCachePolicy();

        IndexCaches = indices.ToDictionary(x => x, _ => new IndexCache());
    }

    public long? OldestTick => RowCache.OldestTick;
    public long? NewestTick => RowCache.NewestTick;
    public int RowCount => RowCache.Count;
    public long TotalBytes => RowCache.TotalBytes;
    public string TotalBytesFormatted => RowCache.TotalBytesFormatted;
    public int TransactionRowsCount => TransactionRows.Count;
    public IEnumerable<(string index, int count)> IndicesCount => indices.Select(x => (x.Name, IndexCaches[x].Count));

    public TableDefinition Table { get; }
    public DatabaseCache DatabaseCache { get; }

    public bool IsTransactionInCache(Transaction transaction) => TransactionRows.ContainsKey(transaction);
    public IEnumerable<IImmutableInstance> GetTransactionRows(Transaction transaction)
    {
        if (TransactionRows.TryGetValue(transaction, out var result))
            return result.Rows;

        return new List<IImmutableInstance>();
    }

    public int ApplyChanges(IEnumerable<StateChange> changes, Transaction? transaction = null)
    {
        var numRows = 0;

        foreach (var change in changes)
        {
            if (change.Table != Table)
                continue;

            if (change.Type == TransactionChangeType.Delete || change.Type == TransactionChangeType.Update)
            {
                if (transaction != null)
                {
                    if (TryRemoveTransactionRow(change.PrimaryKeys, transaction, out var transRows))
                        numRows += transRows;
                }

                RowCache.TryRemoveRow(change.PrimaryKeys, out var rows);
                numRows += rows;
            }


            TryRemoveRowFromAllIndices(change.PrimaryKeys, out var indexRows);
            numRows += indexRows;

            if (change.Type == TransactionChangeType.Update)
            {
                var changedValues = change.GetChanges().ToList();
                foreach (var columnIndex in change.Table.ColumnIndices.Where(x => changedValues.Any(y => x.Columns.Contains(y.Key))))
                    RemoveIndexOnBothSides(columnIndex, change.Model);
            }
            else
            {
                foreach (var columnIndex in change.Table.ColumnIndices)
                {
                    RemoveIndexOnBothSides(columnIndex, change.Model);
                }
            }
        }

        // At this point, all cache changes have been applied.
        // Raise the event to notify any observers that a change has occurred.
        OnRowChanged(new RowChangeEventArgs());

        return numRows;

        int RemoveIndexOnBothSides(ColumnIndex columnIndex, IModelInstance model)
        {
            var fk = KeyFactory.CreateKeyFromValues(model.GetValues(columnIndex.Columns).Select(x => x.Value));

            if (TryRemoveForeignKeyIndex(columnIndex, fk, out var indexRowsThisSide))
                numRows += indexRowsThisSide;

            foreach (var index in columnIndex.RelationParts.Select(x => x.GetOtherSide().ColumnIndex))
                if (DatabaseCache.GetTableCache(index.Table).TryRemoveForeignKeyIndex(index, fk, out var indexRowsOtherSide))
                    numRows += indexRowsOtherSide;

            return numRows;
        }
    }

    protected virtual void OnRowChanged(RowChangeEventArgs e)
    {
        RowChanged?.Invoke(this, e);
    }

    public (IndexCacheType, int? amount) GetIndexCachePolicy()
    {
        if (!Table.IndexCache.Any())
            return DatabaseCache.GetIndexCachePolicy();

        if (Table.IndexCache.Any(x => x.indexCacheType == IndexCacheType.None))
            return (IndexCacheType.None, 0);

        if (Table.IndexCache.Any(x => x.indexCacheType == IndexCacheType.MaxAmountRows))
            return (IndexCacheType.MaxAmountRows, Table.IndexCache.First(x => x.indexCacheType == IndexCacheType.MaxAmountRows).amount);

        if (Table.IndexCache.Any(x => x.indexCacheType == IndexCacheType.All))
            return (IndexCacheType.All, null);

        throw new NotImplementedException();
    }

    public void ClearCache()
    {
        ClearRows();
        ClearIndex();
    }

    public void ClearRows()
    {
        RowCache.ClearRows();
    }

    public void ClearIndex()
    {
        for (var i = 0; i < indices.Count; i++)
            IndexCaches[indices[i]].Clear();
    }

    public int RemoveRowsByLimit(CacheLimitType limitType, long amount)
    {
        if (limitType == CacheLimitType.Seconds)
            return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromSeconds(amount)).Ticks);

        if (limitType == CacheLimitType.Minutes)
            return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromMinutes(amount)).Ticks);

        if (limitType == CacheLimitType.Hours)
            return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromHours(amount)).Ticks);

        if (limitType == CacheLimitType.Days)
            return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromDays(amount)).Ticks);

        if (limitType == CacheLimitType.Ticks)
            return RemoveRowsInsertedBeforeTick(DateTime.Now.Subtract(TimeSpan.FromTicks(amount)).Ticks);

        if (limitType == CacheLimitType.Rows)
            return RowCache.RemoveRowsOverRowLimit((int)amount);

        if (limitType == CacheLimitType.Bytes)
            return RowCache.RemoveRowsOverSizeLimit(amount);

        if (limitType == CacheLimitType.Kilobytes)
            return RowCache.RemoveRowsOverSizeLimit(amount * 1024);

        if (limitType == CacheLimitType.Megabytes)
            return RowCache.RemoveRowsOverSizeLimit(amount * 1024 * 1024);

        if (limitType == CacheLimitType.Gigabytes)
            return RowCache.RemoveRowsOverSizeLimit(amount * 1024 * 1024 * 1024);

        throw new NotImplementedException();
    }

    public int RemoveRowsInsertedBeforeTick(long tick)
    {
        RemoveAllIndicesInsertedBeforeTick(tick);
        return RowCache.RemoveRowsInsertedBeforeTick(tick);
    }


    public void TryRemoveRowFromAllIndices(IKey primaryKeys, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        for (var i = 0; i < indices.Count; i++)
        {
            if (IndexCaches[indices[i]].TryRemovePrimaryKey(primaryKeys, out var rowsRemoved))
                numRowsRemoved += rowsRemoved;
        }
    }

    public bool TryRemoveForeignKeyIndex(ColumnIndex columnIndex, IKey foreignKey, out int numRowsRemoved) =>
        IndexCaches[columnIndex].TryRemoveForeignKey(foreignKey, out numRowsRemoved);

    public bool TryRemovePrimaryKeyIndex(ColumnIndex columnIndex, IKey primaryKeys, out int numRowsRemoved) =>
        IndexCaches[columnIndex].TryRemovePrimaryKey(primaryKeys, out numRowsRemoved);

    public int RemoveAllIndicesInsertedBeforeTick(long tick) =>
        IndexCaches.Select(x => x.Value.RemoveInsertedBeforeTick(tick)).Sum();

    public bool TryRemoveTransactionRow(IKey primaryKeys, Transaction transaction, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        return TransactionRows.TryGetValue(transaction, out RowCache? rowCache) && rowCache.TryRemoveRow(primaryKeys, out numRowsRemoved);
    }

    public bool TryRemoveTransaction(Transaction transaction)
    {
        if (TransactionRows.ContainsKey(transaction))
            return TransactionRows.TryRemove(transaction, out var _);

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
            IndexCaches[index].TryAdd(fk, pk);


        var otherColumns = otherSide.RelationPart.ColumnIndex.Columns;
        select = new SqlQuery(otherSide.Model.Table, DatabaseCache.Database.ReadOnlyAccess, "other")
            .What(otherSide.Model.Table.PrimaryKeyColumns)
            .LeftJoin(Table.DbName, "this")
                .On(index.Columns[0].DbName, "this")
                .EqualToColumn(otherColumns[0].DbName, "other");

        if (index.Columns.Count > 1)
        {
            for (var i = 1; i < index.Columns.Count; i++)
                select.And(index.Columns[i].DbName, "this").EqualToColumn(otherColumns[i].DbName, "other");
        }

        query = select
            .Where(index.Columns.Select(y => (y.DbName, null as object)), BooleanType.And, "this")
            .OrderByDesc(otherColumns[0].DbName, "other");

        if (limitRows.HasValue)
            query.Limit(limitRows.Value);

        foreach (var pk in query.SelectQuery().ReadKeys())
            IndexCaches[index].TryAdd(pk, []);
    }

    public IKey[] GetKeys(IKey foreignKey, RelationProperty otherSide, DataSourceAccess dataSource)
    {
        var index = otherSide.RelationPart.GetOtherSide().ColumnIndex;
        if (Table.PrimaryKeyColumns.SequenceEqual(index.Columns))
            return [foreignKey];

        if (dataSource is ReadOnlyAccess && indexCachePolicy.type != IndexCacheType.None)
        {
            if (IndexCaches[index].TryGetValue(foreignKey, out var keys))
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
            .Where(index.Columns.Select((x, i) => (x.DbName, foreignKey.Values[i])))
            .SelectQuery();

        var newKeys = KeyFactory.GetKeys(select, Table.PrimaryKeyColumns).ToArray();

        if (indexCachePolicy.type != IndexCacheType.None)
            IndexCaches[index].TryAdd(foreignKey, newKeys);

        return newKeys;
    }

    public IEnumerable<IImmutableInstance> GetRows(IKey foreignKey, RelationProperty otherSide, DataSourceAccess dataSource)
    {
        if (foreignKey is NullKey)
            return [];

        return GetRows(GetKeys(foreignKey, otherSide, dataSource), dataSource);
    }

    public IImmutableInstance? GetRow(IKey primaryKeys, DataSourceAccess dataSource) =>
        GetRows([primaryKeys], dataSource).SingleOrDefault();

    public IEnumerable<IImmutableInstance> GetRows(IKey[] primaryKeys, DataSourceAccess dataSource, List<OrderBy>? orderings = null)
    {
        if (dataSource is Transaction transaction && transaction.Type != TransactionType.ReadOnly && !TransactionRows.ContainsKey(transaction))
            TransactionRows.TryAdd(transaction, new RowCache());

        if (orderings == null || orderings.Count == 0)
            return LoadRowsFromDatabaseAndCache(primaryKeys, dataSource);
        else
            return LoadOrderedRowsFromDatabaseAndCache(primaryKeys, dataSource, orderings);
    }

    private IEnumerable<IImmutableInstance> LoadRowsFromDatabaseAndCache(IKey[] primaryKeys, DataSourceAccess dataSource)
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

        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, primaryKeys.Length - keysToLoad.Count);

        if (keysToLoad.Count != 0)
        {
            foreach (var split in keysToLoad.SplitList(500))
            {
                foreach (var rowData in GetRowDataFromPrimaryKeys(split, dataSource))
                {
                    yield return AddRow(rowData, dataSource);
                }
            }

            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, keysToLoad.Count);
        }
    }

    private IEnumerable<IImmutableInstance> LoadOrderedRowsFromDatabaseAndCache(IKey[] primaryKeys, DataSourceAccess dataSource, List<OrderBy> orderings)
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

        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, loadedRows.Count);

        if (keysToLoad.Count != 0)
        {
            foreach (var split in keysToLoad.SplitList(500))
            {
                foreach (var rowData in GetRowDataFromPrimaryKeys(split, dataSource, orderings))
                {
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

    private IEnumerable<RowData> GetRowDataFromPrimaryKeys(IEnumerable<IKey> keys, DataSourceAccess dataSource, List<OrderBy>? orderings = null)
    {
        var q = new SqlQuery(Table.DbName, dataSource);

        if (Table.PrimaryKeyColumns.Length == 1)
        {
            q.Where(Table.PrimaryKeyColumns[0].DbName).In(keys.Select(x => dataSource.Provider.GetWriter().ConvertColumnValue(Table.PrimaryKeyColumns[0], x.Values[0])));
        }
        else
        {
            foreach (var key in keys)
            {
                var where = q.AddWhereGroup(BooleanType.Or);
                for (var i = 0; i < primaryKeyColumnsCount; i++)
                    where.And(Table.PrimaryKeyColumns[i].DbName).EqualTo(dataSource.Provider.GetWriter().ConvertColumnValue(Table.PrimaryKeyColumns[i], key.Values[i]));
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

    private bool GetRowFromCache(IKey key, DataSourceAccess dataSource, out IImmutableInstance? row)
    {
        if (dataSource is ReadOnlyAccess && RowCache.TryGetValue(key, out row))
            return true;
        else if (dataSource is Transaction transaction && TransactionRows.TryGetValue(transaction, out var transactionRows) && transactionRows.TryGetValue(key, out row))
            return true;

        row = null;
        return false;
    }

    private IImmutableInstance AddRow(RowData rowData, DataSourceAccess transaction)
    {
        TryAddRow(rowData, transaction, out var row);
        return row;
    }

    private bool TryAddRow(RowData rowData, DataSourceAccess dataSource, out IImmutableInstance row)
    {
        row = InstanceFactory.NewImmutableRow(rowData, dataSource.Provider, dataSource);
        var keys = KeyFactory.GetKey(rowData, Table.PrimaryKeyColumns);

        return (dataSource is ReadOnlyAccess && (!Table.UseCache || RowCache.TryAddRow(keys, rowData, row)))
            || (dataSource is Transaction transaction && TransactionRows.TryGetValue(transaction, out var rowCache) && rowCache.TryAddRow(keys, rowData, row));
    }

    public TableCacheSnapshot MakeSnapshot()
    {
        return new(Table.DbName, RowCount, TotalBytes, NewestTick, OldestTick, IndicesCount.ToArray());
    }
}