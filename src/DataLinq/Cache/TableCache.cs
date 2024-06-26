﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

public class TableCache
{
    protected Dictionary<ColumnIndex, IndexCache> IndexCaches;
    protected RowCache RowCache = new();
    protected ConcurrentDictionary<Transaction, RowCache> TransactionRows = new();

    protected int primaryKeyColumnsCount;
    protected List<ColumnIndex> indices;
    protected (IndexCacheType type, int? amount) indexCachePolicy;

    public TableCache(TableMetadata table, DatabaseCache databaseCache)
    {
        this.Table = table;
        this.DatabaseCache = databaseCache;
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

    public TableMetadata Table { get; }
    public DatabaseCache DatabaseCache { get; }

    public bool IsTransactionInCache(Transaction transaction) => TransactionRows.ContainsKey(transaction);
    public IEnumerable<ImmutableInstanceBase> GetTransactionRows(Transaction transaction)
    {
        if (TransactionRows.TryGetValue(transaction, out var result))
            return result.Rows;

        return new List<ImmutableInstanceBase>();
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

        return numRows;

        int RemoveIndexOnBothSides(ColumnIndex columnIndex, IModel model)
        {
            var data = model.GetValues(columnIndex.Columns).Select(x => x.Value).ToArray();

            if (TryRemoveIndex(columnIndex, new ForeignKey(columnIndex, data), out var indexRowsThisSide))
                numRows += indexRowsThisSide;

            foreach (var index in columnIndex.RelationParts.Select(x => x.GetOtherSide().ColumnIndex))
                if (DatabaseCache.GetTableCache(index.Table).TryRemoveIndex(index, new ForeignKey(index, data), out var indexRowsOtherSide))
                    numRows += indexRowsOtherSide;

            return numRows;
        }
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


    public void TryRemoveRowFromAllIndices(PrimaryKeys primaryKeys, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        for (var i = 0; i < indices.Count; i++)
        {
            if (IndexCaches[indices[i]].TryRemove(primaryKeys, out var rowsRemoved))
                numRowsRemoved += rowsRemoved;
        }
    }

    public bool TryRemoveIndex(ColumnIndex columnIndex, ForeignKey foreignKey, out int numRowsRemoved) =>
        IndexCaches[columnIndex].TryRemove(foreignKey, out numRowsRemoved);

    public bool TryRemoveIndex(ColumnIndex columnIndex, PrimaryKeys primaryKeys, out int numRowsRemoved) =>
        IndexCaches[columnIndex].TryRemove(primaryKeys, out numRowsRemoved);

    public int RemoveAllIndicesInsertedBeforeTick(long tick) =>
        IndexCaches.Select(x => x.Value.RemoveInsertedBeforeTick(tick)).Sum();

    public bool TryRemoveTransactionRow(PrimaryKeys primaryKeys, Transaction transaction, out int numRowsRemoved)
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

    public void PreloadIndex(ForeignKey foreignKey, RelationProperty otherSide, Transaction? transaction = null, int? limitRows = null)
    {
        transaction ??= DatabaseCache.Database.StartTransaction(TransactionType.ReadOnly);

        var select = new SqlQuery(Table, transaction)
            .What(Table.PrimaryKeyColumns.Concat(foreignKey.Index.Columns).Distinct())
            .WhereNot(foreignKey.Index.Columns.Select(y => (y.DbName, null as object)));

        var query = select
            .OrderByDesc(foreignKey.Index.Columns[0].DbName);

        if (limitRows.HasValue)
            query.Limit(limitRows.Value);

        foreach (var (fk, pk) in query.SelectQuery().ReadPrimaryAndForeignKeys(foreignKey.Index))
            IndexCaches[foreignKey.Index].TryAdd(fk, pk);


        var otherColumns = otherSide.RelationPart.ColumnIndex.Columns;
        select = new SqlQuery(otherSide.Model.Table, transaction, "other")
            .What(otherSide.Model.Table.PrimaryKeyColumns)
            .LeftJoin(Table.DbName, "this")
                .On(foreignKey.Index.Columns[0].DbName, "this")
                .EqualToColumn(otherColumns[0].DbName, "other");

        if (foreignKey.Index.Columns.Count > 1)
        {
            for (var i = 1; i < foreignKey.Index.Columns.Count; i++)
                select.And(foreignKey.Index.Columns[i].DbName, "this").EqualToColumn(otherColumns[i].DbName, "other");
        }

        query = select
            .Where(foreignKey.Index.Columns.Select(y => (y.DbName, null as object)), BooleanType.And, "this")
            .OrderByDesc(otherColumns[0].DbName, "other");

        if (limitRows.HasValue)
            query.Limit(limitRows.Value);

        foreach (var pk in query.SelectQuery().ReadKeys()) // .ReadForeignKeys(otherSide.RelationPart.ColumnIndex))
            IndexCaches[foreignKey.Index].TryAdd(new ForeignKey(foreignKey.Index, pk.Data!), []);
    }

    public PrimaryKeys[] GetKeys(ForeignKey foreignKey, RelationProperty otherSide, Transaction? transaction = null)
    {
        if (Table.PrimaryKeyColumns.Length == foreignKey.Index.Columns.Count() && Table.PrimaryKeyColumns.All(x => foreignKey.Index.Columns.Contains(x)))
            return [new PrimaryKeys(foreignKey.Data)];

        if ((transaction == null || transaction.Type == TransactionType.ReadOnly) && indexCachePolicy.type != IndexCacheType.None)
        {
            if (IndexCaches[foreignKey.Index].TryGetValue(foreignKey, out var keys))
                return keys!;

            if (IndexCaches[foreignKey.Index].Count == 0)
            {
                PreloadIndex(foreignKey, otherSide, transaction, indexCachePolicy.type == IndexCacheType.MaxAmountRows ? indexCachePolicy.amount : null);
                GetRows(IndexCaches[foreignKey.Index].Values.SelectMany(x => x).Take(1000).ToArray(), transaction).ToList();

                if (IndexCaches[foreignKey.Index].TryGetValue(foreignKey, out var retryKeys))
                    return retryKeys!;
            }
        }

        var select = new SqlQuery(Table, transaction ?? DatabaseCache.Database.StartTransaction(TransactionType.ReadOnly))
            .What(Table.PrimaryKeyColumns)
            .Where(foreignKey.GetData())
            .SelectQuery();

        var newKeys = select
            .ReadKeys()
            .ToArray();

        if (indexCachePolicy.type != IndexCacheType.None)
            IndexCaches[foreignKey.Index].TryAdd(foreignKey, newKeys);

        return newKeys;
    }

    public IEnumerable<object> GetRows(ForeignKey foreignKey, RelationProperty otherSide, Transaction? transaction = null)
    {
        if (foreignKey.Data == null)
            return [];

        return GetRows(GetKeys(foreignKey, otherSide, transaction), transaction);
    }

    public object? GetRow(PrimaryKeys primaryKeys, Transaction transaction) =>
        GetRows([primaryKeys], transaction).SingleOrDefault();

    public IEnumerable<object> GetRows(PrimaryKeys[] primaryKeys, Transaction? transaction = null, List<OrderBy>? orderings = null)
    {
        if (transaction != null && transaction.Type != TransactionType.ReadOnly && !TransactionRows.ContainsKey(transaction))
            TransactionRows.TryAdd(transaction, new RowCache());

        if (orderings == null)
            return LoadRowsFromDatabaseAndCache(primaryKeys, transaction);
        else
            return LoadOrderedRowsFromDatabaseAndCache(transaction, primaryKeys, orderings);
    }

    private IEnumerable<object> LoadRowsFromDatabaseAndCache(PrimaryKeys[] primaryKeys, Transaction? transaction = null)
    {
        var keysToLoad = new List<PrimaryKeys>(primaryKeys.Length);
        foreach (var key in primaryKeys)
        {
            if (GetRowFromCache(key, transaction, out var row))
                yield return row!;
            else
                keysToLoad.Add(key);
        }

        if (keysToLoad.Count != 0)
        {
            transaction ??= DatabaseCache.Database.StartTransaction(TransactionType.ReadOnly);

            foreach (var split in keysToLoad.SplitList(500))
            {
                foreach (var rowData in GetRowDataFromPrimaryKeys(split, transaction))
                {
                    yield return AddRow(rowData, transaction);
                }
            }
        }
    }

    private IEnumerable<ImmutableInstanceBase> LoadOrderedRowsFromDatabaseAndCache(Transaction? transaction, PrimaryKeys[] primaryKeys, List<OrderBy> orderings)
    {
        var keysToLoad = new List<PrimaryKeys>(primaryKeys.Length);
        var loadedRows = new List<ImmutableInstanceBase>(primaryKeys.Length);

        foreach (var key in primaryKeys)
        {
            if (GetRowFromCache(key, transaction, out var row))
                loadedRows.Add(row!);
            else
                keysToLoad.Add(key);
        }

        if (keysToLoad.Count != 0)
        {
            transaction ??= DatabaseCache.Database.StartTransaction(TransactionType.ReadOnly);

            foreach (var split in keysToLoad.SplitList(500))
            {
                foreach (var rowData in GetRowDataFromPrimaryKeys(split, transaction, orderings))
                {
                    loadedRows.Add(AddRow(rowData, transaction));
                }
            }
        }

        IOrderedEnumerable<ImmutableInstanceBase>? orderedRows = null;

        foreach (var ordering in orderings)
        {
            Func<ImmutableInstanceBase, IComparable> keySelector = x => (IComparable)x.GetValues([ordering.Column]).First().Value;

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

    private IEnumerable<RowData> GetRowDataFromPrimaryKeys(IEnumerable<PrimaryKeys> keys, Transaction transaction, List<OrderBy>? orderings = null)
    {
        var q = new SqlQuery(Table.DbName, transaction);

        foreach (var key in keys)
        {
            var where = q.AddWhereGroup(BooleanType.Or);
            for (var i = 0; i < primaryKeyColumnsCount; i++)
                where.And(Table.PrimaryKeyColumns[i].DbName).EqualTo(transaction.Provider.GetWriter().ConvertColumnValue(Table.PrimaryKeyColumns[i], key.Data[i]));
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

    private bool GetRowFromCache(PrimaryKeys key, Transaction? transaction, out ImmutableInstanceBase? row)
    {
        if ((transaction == null || transaction.Type == TransactionType.ReadOnly) && RowCache.TryGetValue(key, out row))
            return true;
        else if (transaction != null && transaction.Type != TransactionType.ReadOnly && TransactionRows.TryGetValue(transaction, out var transactionRows) && transactionRows.TryGetValue(key, out row))
            return true;

        row = null;
        return false;
    }

    private ImmutableInstanceBase AddRow(RowData rowData, Transaction transaction)
    {
        TryAddRow(rowData, transaction, out var row);
        return row;
    }

    private bool TryAddRow(RowData rowData, Transaction transaction, out ImmutableInstanceBase row)
    {
        row = InstanceFactory.NewImmutableRow(rowData, transaction.Provider, transaction);
        var keys = rowData.GetKeys();

        return (transaction.Type == TransactionType.ReadOnly && (!Table.UseCache || RowCache.TryAddRow(keys, rowData, row)))
            || (transaction.Type != TransactionType.ReadOnly && TransactionRows.TryGetValue(transaction, out var rowCache) && rowCache.TryAddRow(keys, rowData, row));
    }

    public TableCacheSnapshot MakeSnapshot()
    {
        return new(Table.DbName, RowCount, TotalBytes, NewestTick, OldestTick, IndicesCount.ToArray());
    }
}