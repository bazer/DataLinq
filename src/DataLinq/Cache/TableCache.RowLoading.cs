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
            if (GetCanonicalPrimaryKeySourceServices(dataSource) is { } sourceServices)
            {
                var canonicalKeys = keysToLoad
                    .Select(ProviderKeyComponents.ToDataLinqKey)
                    .Distinct()
                    .ToList();
                foreach (var loaded in LoadCanonicalRowsAfterKnownMiss(
                    canonicalKeys,
                    sourceServices))
                {
                    rowsByPrimaryKey.TryAdd(loaded.Key, loaded.Value);
                }
            }
            else
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
        // The index cache is shared committed state. A transaction can observe pending
        // inserts, updates, and deletes, so publishing its visible key set here would let
        // transaction-local state survive rollback and poison later read-only relation loads.
        var cachePrimaryKeys = dataSource is ReadOnlyAccess &&
            indexCachePolicy.type != IndexCacheType.None;
        var primaryKeyCount = 0;
        var singlePrimaryKey = default(DataLinqKey);
        List<DataLinqKey>? primaryKeys = null;
        var rowCacheHits = 0;
        var rowCacheMisses = 0;

        if (TryGetCanonicalIndexSourceServices(
            foreignKey,
            index,
            dataSource,
            out var sourceServices,
            out var canonicalProviderIndexKey))
        {
            var request = new SourceIndexRowRequest(
                Table,
                index,
                canonicalProviderIndexKey);
            var result = sourceServices.IndexRowLoader.Load(request);
            if (!ReferenceEquals(result.Request, request))
            {
                throw new InvalidOperationException(
                    $"Source index row loader returned a result for a different request than table '{Table.DbName}' index '{index.Name}'.");
            }

            foreach (var providerRow in result.Rows)
                AddCanonicalRow(providerRow, sourceServices);
        }
        else if (TryConvertScalarProviderColumnValue(foreignKey, index.Columns, dataSource, out var predicateColumn, out var predicateValue))
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

        void AddCanonicalRow(
            CanonicalProviderValueRow providerRow,
            IDataLinqIndexRowServices sourceServices)
        {
            if (!providerRow.TryCreateCanonicalPrimaryKey(out var primaryKey))
            {
                throw new InvalidOperationException(
                    $"Source index row for table '{Table.DbName}' did not contain a canonical primary key.");
            }

            AddPrimaryKey(primaryKey);

            if (GetRowFromCache(primaryKey, dataSource, out var cachedRow))
            {
                rowCacheHits++;
                AddLoadedRow(cachedRow!);
                return;
            }

            rowCacheMisses++;
            MetricsHandle.RecordDatabaseRowsLoaded(1);
            AddLoadedRow(sourceServices.MaterializationServices
                .MaterializeAfterKnownCacheMiss(providerRow));
        }

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

    private static bool TryGetCanonicalIndexSourceServices<TKey>(
        TKey foreignKey,
        ColumnIndex index,
        IDataSourceAccess dataSource,
        out IDataLinqIndexRowServices sourceServices,
        out DataLinqKey canonicalProviderIndexKey)
        where TKey : notnull
    {
        sourceServices = null!;
        canonicalProviderIndexKey = DataLinqKey.Null;

        // F6-B admits only exact, single-column integral canonical provider keys. The
        // bounded converter-backed extension admits only Int32 and Int64 and requires the
        // caller to have already supplied that exact canonical value; model wrappers must
        // still fail the exact-key check below so this boundary never converts or
        // double-converts them. String/CHAR collation, UUID codecs, other converted integral
        // types, and composite keys remain on the legacy SQL path.
        if (dataSource is not IDataLinqIndexRowServices availableServices ||
            index.Table.PrimaryKeyColumns.Count == 0 ||
            index.Columns.Count != 1 ||
            !SupportsCanonicalIndexSourceColumn(index.Columns[0]) ||
            !ProviderKeyComponents.HasOnlyIntegralCanonicalComponents(index.Columns) ||
            !ProviderKeyComponents.TryCreateExactCanonicalKey(
                foreignKey,
                index.Columns,
                out canonicalProviderIndexKey))
        {
            return false;
        }

        sourceServices = availableServices;
        return true;
    }

    private static bool SupportsCanonicalIndexSourceColumn(ColumnDefinition column)
    {
        if (!column.HasScalarConverter)
            return true;

        var providerType = column.ProviderClrType;
        if (providerType is null)
            return false;

        providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;
        return providerType == typeof(int) || providerType == typeof(long);
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
            if (GetCanonicalPrimaryKeySourceServices(dataSource) is { } sourceServices)
            {
                var canonicalKeys = keysToLoad
                    .Select(ProviderKeyComponents.ToDataLinqKey)
                    .Distinct()
                    .ToList();
                foreach (var loaded in LoadCanonicalRowsAfterKnownMiss(
                    canonicalKeys,
                    sourceServices))
                {
                    loadedRows.Add(loaded.Value);
                }
            }
            else
            {
                foreach (var split in keysToLoad.SplitList(500))
                {
                    foreach (var rowData in GetRowDataFromPrimaryKeyValues(split, dataSource, orderings))
                    {
                        MetricsHandle.RecordDatabaseRowsLoaded(1);
                        loadedRows.Add(AddRow(rowData, dataSource));
                    }
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

    private IReadOnlyDictionary<DataLinqKey, IImmutableInstance>
        LoadCanonicalRowsAfterKnownMiss(
            List<DataLinqKey> canonicalProviderKeys,
            IDataLinqSourceRowServices sourceServices)
    {
        var rows = new Dictionary<DataLinqKey, IImmutableInstance>(
            canonicalProviderKeys.Count);
        foreach (var split in canonicalProviderKeys.SplitList(500))
        {
            var request = new SourcePrimaryKeyRowRequest(Table, split);
            var result = sourceServices.RowLoader.Load(request);
            foreach (var providerRow in result.Rows)
            {
                if (!providerRow.TryCreateCanonicalPrimaryKey(out var key))
                {
                    throw new InvalidOperationException(
                        $"Source row for table '{Table.DbName}' did not contain a canonical primary key.");
                }

                var row = sourceServices.MaterializationServices
                    .MaterializeAfterKnownCacheMiss(providerRow);
                if (!rows.TryAdd(key, row))
                {
                    throw new InvalidOperationException(
                        $"Source row loader returned duplicate canonical primary key '{key}' for table '{Table.DbName}'.");
                }

                MetricsHandle.RecordDatabaseRowsLoaded(1);
            }
        }

        return rows;
    }

    private IDataLinqSourceRowServices? GetCanonicalPrimaryKeySourceServices(
        IDataSourceAccess dataSource)
    {
        if (dataSource is not IDataLinqSourceRowServices sourceServices)
            return null;

        // Source-row results validate requested keys with canonical CLR equality. Integral
        // components are provider-neutral. A scalar Guid is also exact only when this source
        // reports a supported concrete database type with resolved column storage metadata;
        // string/collation, composite UUID, and other provider-sensitive shapes remain on the
        // legacy path.
        return ProviderKeyComponents.SupportsNeutralSourceRowLoading(
            Table,
            dataSource.Provider.DatabaseType)
            ? sourceServices
            : null;
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
