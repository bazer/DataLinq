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
                var canonicalKeys = CreateDistinctCanonicalProviderKeys(keysToLoad);
                LoadCanonicalRowsAfterKnownMiss(
                    canonicalKeys,
                    sourceServices,
                    rowsByPrimaryKey: rowsByPrimaryKey);
            }
            else
            {
                for (var offset = 0; offset < keysToLoad.Count; offset += 500)
                {
                    var count = Math.Min(500, keysToLoad.Count - offset);
                    var generation = CaptureReadGeneration();
                    foreach (var rowData in GetRowDataFromPrimaryKeyValues(
                        keysToLoad,
                        offset,
                        count,
                        dataSource))
                    {
                        MetricsHandle.RecordDatabaseRowsLoaded(1);
                        var row = AddRow(rowData, dataSource, generation);
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
        var generation = CaptureReadGeneration();
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

            foreach (var loadedRow in result.Rows)
                AddCanonicalRow(loadedRow, sourceServices);
        }
        else if (TryConvertScalarProviderColumnValue(foreignKey, index.Columns, dataSource, out var predicateColumn, out var predicateValue))
        {
            DataSourceAccess.EnsureReadAllowed(dataSource, "load relation rows");
            var scalarQuery = new ScalarColumnRowsQuery(Table, dataSource, predicateColumn, predicateValue);
            using var command = scalarQuery.ToDbCommand();
            using var reader = dataSource.DatabaseAccess.ExecuteReader(command);

            while (reader.ReadNextRow())
            {
                var rowData = new RowData(
                    reader,
                    Table,
                    Table.Columns,
                    true,
                    $"sql:{dataSource.Provider.DatabaseType}:relation-cache-row");
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
        {
            lock (publicationGate)
                if (ReferenceEquals(generation, readGeneration))
                    GetIndexCache(index).TryAdd(foreignKey, GetPrimaryKeyArray());
        }

        RefreshOccupancyMetrics();

        return GetRowArray();

        void AddCanonicalRow(
            LoadedCanonicalRow loadedRow,
            IDataLinqIndexRowServices sourceServices)
        {
            var primaryKey = loadedRow.CanonicalProviderKey;

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
                .MaterializeAfterKnownCacheMiss(loadedRow with { ReadGeneration = generation }));
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
            AddLoadedRow(AddRow(rowData, dataSource, generation));
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

        // F6-B admits exact, single-column canonical provider keys only. Integral keys retain
        // their existing rule: converter-free columns admit every integral CLR type, while the
        // bounded converter-backed extension admits only Int32 and Int64. A scalar Guid is
        // admitted only for a concrete built-in provider with resolved active-provider storage.
        // The caller must already supply the exact canonical value; model wrappers still fail
        // the exact-key check below, so this boundary never converts or double-converts them.
        // String/CHAR collation, unresolved UUID layouts, other converted integral types, and
        // composite keys remain on the legacy SQL path.
        if (dataSource is not IDataLinqIndexRowServices availableServices ||
            index.Table.PrimaryKeyColumns.Count == 0 ||
            index.Columns.Count != 1)
        {
            return false;
        }

        var indexColumn = index.Columns[0];
        var supportsIntegralKey = SupportsCanonicalIntegralIndexSourceColumn(indexColumn) &&
            ProviderKeyComponents.HasOnlyIntegralCanonicalComponents(index.Columns);
        var supportsResolvedGuidKey =
            ProviderKeyComponents.SupportsResolvedCanonicalGuidColumn(
                indexColumn,
                dataSource.Provider.DatabaseType);
        if ((!supportsIntegralKey && !supportsResolvedGuidKey) ||
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

    private static bool SupportsCanonicalIntegralIndexSourceColumn(ColumnDefinition column)
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
                var canonicalKeys = CreateDistinctCanonicalProviderKeys(keysToLoad);
                LoadCanonicalRowsAfterKnownMiss(
                    canonicalKeys,
                    sourceServices,
                    loadedRows: loadedRows);
            }
            else
            {
                for (var offset = 0; offset < keysToLoad.Count; offset += 500)
                {
                    var count = Math.Min(500, keysToLoad.Count - offset);
                    var generation = CaptureReadGeneration();
                    foreach (var rowData in GetRowDataFromPrimaryKeyValues(
                        keysToLoad,
                        offset,
                        count,
                        dataSource,
                        orderings))
                    {
                        MetricsHandle.RecordDatabaseRowsLoaded(1);
                        loadedRows.Add(AddRow(rowData, dataSource, generation));
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

    private void LoadCanonicalRowsAfterKnownMiss(
        List<DataLinqKey> canonicalProviderKeys,
        IDataLinqSourceRowServices sourceServices,
        Dictionary<DataLinqKey, IImmutableInstance>? rowsByPrimaryKey = null,
        List<IImmutableInstance>? loadedRows = null)
    {
        if ((rowsByPrimaryKey is null) == (loadedRows is null))
        {
            throw new ArgumentException(
                "Canonical row loading requires exactly one result destination.");
        }

        for (var offset = 0; offset < canonicalProviderKeys.Count; offset += 500)
        {
            var count = Math.Min(500, canonicalProviderKeys.Count - offset);
            var request = SourcePrimaryKeyRowRequest.Borrow(
                Table,
                canonicalProviderKeys,
                offset,
                count);
            var generation = CaptureReadGeneration();
            var result = sourceServices.RowLoader.Load(request);
            if (!ReferenceEquals(result.Request, request))
            {
                throw new InvalidOperationException(
                    $"Source row loader returned a result for a different request than table '{Table.DbName}'.");
            }

            foreach (var loadedRow in result.Rows)
            {
                var key = loadedRow.CanonicalProviderKey;
                var row = sourceServices.MaterializationServices
                    .MaterializeAfterKnownCacheMiss(loadedRow with { ReadGeneration = generation });
                if (rowsByPrimaryKey is not null)
                    rowsByPrimaryKey.TryAdd(key, row);
                else
                    loadedRows!.Add(row);

                MetricsHandle.RecordDatabaseRowsLoaded(1);
            }
        }
    }

    private static List<DataLinqKey> CreateDistinctCanonicalProviderKeys<TKey>(
        List<TKey> keys)
        where TKey : notnull
    {
        var canonicalKeys = new List<DataLinqKey>(keys.Count);
        HashSet<DataLinqKey>? seen = keys.Count > SourceRowLoadResult.LinearValidationThreshold
            ? new HashSet<DataLinqKey>()
            : null;

        foreach (var key in keys)
        {
            var canonicalKey = ProviderKeyComponents.ToDataLinqKey(key);
            var duplicate = seen is not null
                ? !seen.Add(canonicalKey)
                : ContainsKey(canonicalKeys, canonicalKey);
            if (!duplicate)
                canonicalKeys.Add(canonicalKey);
        }

        return canonicalKeys;
    }

    private static bool ContainsKey(List<DataLinqKey> keys, DataLinqKey candidate)
    {
        foreach (var key in keys)
        {
            if (key.Equals(candidate))
                return true;
        }

        return false;
    }

    private IImmutableInstance? LoadCanonicalRowAfterKnownMiss(
        DataLinqKey canonicalProviderKey,
        IDataLinqSourceRowServices sourceServices)
    {
        var generation = CaptureReadGeneration();
        var providerRow = sourceServices.RowLoader.LoadSingle(
            Table,
            in canonicalProviderKey);
        if (providerRow is null)
            return null;

        SourceRowLoadingValidation.ValidateSingleResult(
            Table,
            in canonicalProviderKey,
            providerRow,
            "Source row loader");

        var row = sourceServices.MaterializationServices
            .MaterializeAfterKnownCacheMiss(
                new LoadedCanonicalRow(providerRow, canonicalProviderKey) { ReadGeneration = generation });
        MetricsHandle.RecordDatabaseRowsLoaded(1);
        return row;
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
