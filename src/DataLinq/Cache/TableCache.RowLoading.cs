using System;
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

public partial class TableCache
{
    private IEnumerable<IImmutableInstance> LoadRowsFromDatabaseAndCache<TKey>(IReadOnlyList<TKey> primaryKeys, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        dataSource ??= DatabaseCache.Database.ReadOnlyAccess;

        var keysToLoad = new List<TKey>(primaryKeys.Count);
        var rowsByPrimaryKey = new Dictionary<DataLinqKey, IImmutableInstance>();
        foreach (var key in primaryKeys)
        {
            var normalizedKey = ProviderKeyComponents.ToDataLinqKey(key);
            if (GetRowFromCache(normalizedKey, dataSource, out var row))
                rowsByPrimaryKey.TryAdd(normalizedKey, row!);
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
                    var row = AddRow(rowData, dataSource);
                    rowsByPrimaryKey.TryAdd(CreatePrimaryKey(rowData), row);
                }
            }

            Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, keysToLoad.Count);
        }

        foreach (var key in primaryKeys)
        {
            var normalizedKey = ProviderKeyComponents.ToDataLinqKey(key);
            if (rowsByPrimaryKey.TryGetValue(normalizedKey, out var row))
                yield return row;
        }
    }

    private IImmutableInstance[] LoadRowsFromForeignKeyAndCache<TKey>(TKey foreignKey, ColumnIndex index, IDataSourceAccess dataSource)
        where TKey : notnull
    {
        var rowCount = 0;
        IImmutableInstance? singleRow = null;
        List<IImmutableInstance>? rows = null;
        var cachePrimaryKeys = indexCachePolicy.type != IndexCacheType.None;
        var primaryKeyCount = 0;
        var singlePrimaryKey = default(DataLinqKey);
        List<DataLinqKey>? primaryKeys = null;
        var rowCacheHits = 0;
        var rowCacheMisses = 0;

        if (TryConvertScalarProviderColumnValue(foreignKey, index.Columns, dataSource, out var predicateColumn, out var predicateValue))
        {
            DataSourceAccess.EnsureReadAllowed(dataSource, "load relation rows");
            var scalarQuery = new ScalarColumnRowsQuery(Table, dataSource, predicateColumn, predicateValue);
            using var command = scalarQuery.ToDbCommand();
            using var reader = dataSource.DatabaseAccess.ExecuteReader(command);

            while (reader.ReadNextRow())
            {
                var rowData = new RowData(reader, Table, Table.Columns, true);
                AddRowData(rowData);
            }
        }
        else
        {
            var q = new SqlQuery(Table, dataSource)
                .Where(index.Columns, foreignKey)
                .SelectQuery();

            foreach (var rowData in q.ReadRows())
                AddRowData(rowData);
        }

        MetricsHandle.RecordRowCacheHits(rowCacheHits);
        MetricsHandle.RecordRowCacheMisses(rowCacheMisses);
        Log.LoadRowsFromCache(loggingConfiguration.CacheLogger, Table, rowCacheHits);
        Log.LoadRowsFromDatabase(loggingConfiguration.CacheLogger, Table, rowCacheMisses);

        if (cachePrimaryKeys)
            GetIndexCache(index).TryAdd(foreignKey, GetPrimaryKeyArray());

        RefreshOccupancyMetrics();

        return GetRowArray();

        void AddRowData(RowData rowData)
        {
            var primaryKey = KeyFactory.GetKey(rowData, Table.PrimaryKeyColumns);
            AddPrimaryKey(primaryKey);

            if (GetRowFromCache(primaryKey, dataSource, out var cachedRow))
            {
                rowCacheHits++;
                AddLoadedRow(cachedRow!);
                return;
            }

            rowCacheMisses++;
            MetricsHandle.RecordDatabaseRowsLoaded(1);
            AddLoadedRow(AddRow(rowData, dataSource));
        }

        void AddLoadedRow(IImmutableInstance row)
        {
            if (rowCount == 0)
            {
                singleRow = row;
            }
            else
            {
                rows ??= new List<IImmutableInstance> { singleRow! };
                rows.Add(row);
            }

            rowCount++;
        }

        void AddPrimaryKey(DataLinqKey primaryKey)
        {
            if (!cachePrimaryKeys)
                return;

            if (primaryKeyCount == 0)
            {
                singlePrimaryKey = primaryKey;
            }
            else
            {
                primaryKeys ??= new List<DataLinqKey> { singlePrimaryKey };
                primaryKeys.Add(primaryKey);
            }

            primaryKeyCount++;
        }

        IImmutableInstance[] GetRowArray()
        {
            return rowCount switch
            {
                0 => [],
                1 => [singleRow!],
                _ => rows!.ToArray()
            };
        }

        DataLinqKey[] GetPrimaryKeyArray()
        {
            return primaryKeyCount switch
            {
                0 => [],
                1 => [singlePrimaryKey],
                _ => primaryKeys!.ToArray()
            };
        }
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
            var column = ordering.Column ?? throw new InvalidOperationException("Cached row ordering requires a column-backed ordering.");
            Func<IImmutableInstance, IComparable?> keySelector = x => (IComparable?)x.GetValues([column]).First().Value;

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

    private DataLinqKey CreatePrimaryKey(RowData rowData)
    {
        if (Table.Model.ProviderKeyRowStoreAccessor is IProviderKeyRowStoreAccessor providerKeyAccessor &&
            providerKeyAccessor.TryCreateKey(rowData, out var primaryKey))
        {
            return primaryKey;
        }

        return KeyFactory.GetKey(rowData, Table.PrimaryKeyColumns);
    }
}
