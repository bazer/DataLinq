using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Cache;

public partial class TableCache
{
    internal IEnumerable<IImmutableInstance> GetRows<TKey>(TKey foreignKey, RelationProperty otherSide, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        if (ProviderKeyComponents.IsNull(foreignKey))
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
            TryGetIndexCache(index)?.TryGet(foreignKey, out var keys) == true)
            return GetRows(keys!, dataSource);

        return LoadRowsFromForeignKeyAndCache(foreignKey, index, dataSource);
    }

    private IImmutableInstance? GetRowByDynamicKey(DataLinqKey primaryKeys, IDataSourceAccess dataSource)
    {
        return GetRowCore(primaryKeys, dataSource, static (cache, rowData, source, _) =>
            cache.AddRow(rowData, source));
    }

    public IImmutableInstance? GetRow<TKey>(
        TKey primaryKey,
        IDataSourceAccess dataSource)
        where TKey : notnull
    {
        if (primaryKey is null)
            return null;

        if (primaryKey is DataLinqKey dataLinqKey)
            return GetRowByDynamicKey(dataLinqKey, dataSource);

        if (ShouldUseDynamicKeyLookup(primaryKey))
            return GetRowByDynamicKey(DataLinqKey.FromValue(primaryKey), dataSource);

        return GetRowCore(primaryKey, dataSource, static (cache, rowData, source, key) =>
            cache.AddRow(rowData, source, key));
    }

    private IImmutableInstance? GetRowCore<TKey>(
        TKey primaryKey,
        IDataSourceAccess dataSource,
        Func<TableCache, RowData, IDataSourceAccess, TKey, IImmutableInstance> addRow)
        where TKey : notnull
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;
        EnsureTransactionRowCache(dataSource);

        if (GetRowFromCache(primaryKey, dataSource, out var row))
        {
            RecordSingleRowCacheLookup(hit: true);
            return row;
        }

        RecordSingleRowCacheLookup(hit: false);

        var rowData = GetRowDataFromPrimaryKeyValue(primaryKey, dataSource);
        if (rowData is not null)
            return RecordDatabaseRowLoaded(addRow(this, rowData, dataSource, primaryKey));

        Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, 1);
        return null;
    }

    private void RecordSingleRowCacheLookup(bool hit)
    {
        MetricsHandle.RecordRowCacheHits(hit ? 1 : 0);
        MetricsHandle.RecordRowCacheMisses(hit ? 0 : 1);
        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, hit ? 1 : 0);
    }

    private IImmutableInstance RecordDatabaseRowLoaded(IImmutableInstance row)
    {
        MetricsHandle.RecordDatabaseRowsLoaded(1);
        Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, 1);
        return row;
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
}
