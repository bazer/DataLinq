using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Extensions.Helpers;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Cache;

public partial class TableCache
{
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
            GetIndexCache(index).TryAdd(foreignKey, primaryKeys.ToArray());

        RefreshOccupancyMetrics();

        return rows.ToArray();
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
}
