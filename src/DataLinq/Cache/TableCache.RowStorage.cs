using System;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Cache;

public partial class TableCache
{
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

    private bool TryRemoveProviderKeyFromCache<TKey>(RowCache cache, TKey key, out int numRowsRemoved)
        where TKey : notnull
    {
        if (key is DataLinqKey dataLinqKey)
            return TryRemoveRowFromCache(cache, dataLinqKey, out numRowsRemoved);

        return cache.TryRemoveProviderKey(key, out numRowsRemoved);
    }

    private bool GetRowFromCache<TKey>(TKey key, IDataSourceAccess dataSource, out IImmutableInstance? row)
        where TKey : notnull
    {
        if (key is DataLinqKey dataLinqKey)
            return GetRowFromCache(dataLinqKey, dataSource, out row);

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

        var added = (dataSource is ReadOnlyAccess && (!Table.UseCache || GetOrCreateRowCache().TryAddRow(primaryKey, rowData, row)))
            || (dataSource is Transaction transaction &&
                transactionRows is not null &&
                transactionRows.TryGetValue(transaction, out var transactionRowCache) &&
                transactionRowCache.TryAddRow(primaryKey, rowData, row));

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
                TableKeyComponentStoreKind.Int32 when value is int intKey => cache.TryAddRow(intKey, rowData, row),
                TableKeyComponentStoreKind.Int64 when value is long longKey => cache.TryAddRow(longKey, rowData, row),
                TableKeyComponentStoreKind.Guid when value is Guid guidKey => cache.TryAddRow(guidKey, rowData, row),
                TableKeyComponentStoreKind.String when value is string stringKey => cache.TryAddRow(stringKey, rowData, row),
                _ => false
            };
        }

        var keys = KeyFactory.GetKey(rowData, Table.PrimaryKeyColumns);
        return cache.TryAddRow(keys, rowData, row);
    }
}
