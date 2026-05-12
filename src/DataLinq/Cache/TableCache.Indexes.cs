using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

public partial class TableCache
{
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

    public (IndexCacheType, int? amount) GetIndexCachePolicy()
    {
        return DatabaseCache.GetIndexCachePolicy(Table);
    }

    public void ClearIndex()
    {
        foreach (var indexCache in GetLoadedIndexCaches())
            indexCache.Clear();

        RefreshOccupancyMetrics();
    }

    internal void TryRemoveRowFromAllIndices(DataLinqKey primaryKeys, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        foreach (var indexCache in GetLoadedIndexCaches())
        {
            if (indexCache.TryRemovePrimaryKey(primaryKeys, out var rowsRemoved))
                numRowsRemoved += rowsRemoved;
        }
    }

    internal bool TryRemoveForeignKeyIndex<TKey>(ColumnIndex columnIndex, TKey foreignKey, out int numRowsRemoved)
        where TKey : notnull
    {
        var indexCache = TryGetIndexCache(columnIndex);
        if (indexCache is null)
        {
            numRowsRemoved = 0;
            return true;
        }

        return indexCache.TryRemove(foreignKey, out numRowsRemoved);
    }

    internal bool TryRemovePrimaryKeyIndex(ColumnIndex columnIndex, DataLinqKey primaryKeys, out int numRowsRemoved)
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

    internal void PreloadIndex(DataLinqKey foreignKey, RelationProperty otherSide, int? limitRows = null)
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

    internal DataLinqKey[] GetKeys<TKey>(TKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        var index = otherSide.RelationPart.GetOtherSide().ColumnIndex;
        if (Table.PrimaryKeyColumns.SequenceEqual(index.Columns))
            return [ProviderKeyComponents.ToDataLinqKey(foreignKey)];

        if (dataSource is ReadOnlyAccess && indexCachePolicy.type != IndexCacheType.None)
        {
            if (TryGetIndexCache(index)?.TryGet(foreignKey, out var keys) == true)
                return keys!;
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
}
