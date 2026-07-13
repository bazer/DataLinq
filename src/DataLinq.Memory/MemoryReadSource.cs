using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;

namespace DataLinq.Memory;

internal sealed class MemoryReadSource :
    IDataLinqSourceRowServices,
    IDataLinqQueryPlanServices,
    ISourceRowLoader,
    IReadSourceMaterializationCache
{
    private readonly MemoryProviderStore store;
    private readonly IReadOnlyDictionary<TableDefinition, RowCache> materializedRows;
    private readonly IModelMaterializationServices materializationServices;
    private readonly IQueryPlanBackend queryPlanBackend;
    private long primaryKeyRequests;
    private long primaryKeyProbes;
    private long scanRowsVisited;
    private long cacheLookups;
    private long cacheHits;
    private long cacheMisses;
    private long materializations;
    private long cacheInsertions;

    internal MemoryReadSource(
        DatabaseDefinition metadata,
        MemoryProviderStore store)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        materializedRows = metadata.TableModels.ToDictionary(
            static tableModel => tableModel.Table,
            static _ => new RowCache());
        materializationServices = new ModelMaterializationServices(
            "memory",
            new ReadSourceModelMaterializationRuntime(this, this));
        queryPlanBackend = new MemoryQueryPlanBackend(this);
    }

    public DatabaseDefinition Metadata { get; }

    IModelMaterializationServices IDataLinqReadServices.MaterializationServices =>
        materializationServices;

    ISourceRowLoader IDataLinqSourceRowServices.RowLoader => this;

    IQueryPlanBackend IDataLinqQueryPlanServices.QueryPlanBackend => queryPlanBackend;

    public SourceRowLoadResult Load(SourcePrimaryKeyRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateOwnedTable(request.Table);
        request.ThrowIfCancellationRequested();
        Interlocked.Increment(ref primaryKeyRequests);

        var rows = new List<CanonicalProviderValueRow>(request.CanonicalProviderKeys.Length);
        var distinctKeys = new HashSet<DataLinqKey>();
        foreach (var key in request.CanonicalProviderKeys)
        {
            request.ThrowIfCancellationRequested();
            if (!distinctKeys.Add(key))
                continue;

            Interlocked.Increment(ref primaryKeyProbes);
            if (store.TryGet(request.Table, key, out var row) && row is not null)
                rows.Add(row);
        }

        return new SourceRowLoadResult(request, rows);
    }

    internal IImmutableInstance Materialize(CanonicalProviderValueRow row) =>
        materializationServices.GetOrMaterialize(row);

    internal IReadOnlyList<CanonicalProviderValueRow> GetRows(TableDefinition table) =>
        store.GetRows(table);

    internal void RecordScanRowVisited() =>
        Interlocked.Increment(ref scanRowsVisited);

    internal int GetMaterializedRowCount(TableDefinition table) =>
        GetMaterializedRowCache(table).Count;

    /// <summary>
    /// Test-only cache eviction hook. It is deliberately not a concurrent maintenance contract.
    /// </summary>
    internal void ClearMaterializedRowsForTest(TableDefinition table) =>
        GetMaterializedRowCache(table).ClearRows();

    internal MemoryDiagnostics GetDiagnostics() => new(
        PrimaryKeyRequests: Interlocked.Read(ref primaryKeyRequests),
        PrimaryKeyProbes: Interlocked.Read(ref primaryKeyProbes),
        ScanRowsVisited: Interlocked.Read(ref scanRowsVisited),
        CacheLookups: Interlocked.Read(ref cacheLookups),
        CacheHits: Interlocked.Read(ref cacheHits),
        CacheMisses: Interlocked.Read(ref cacheMisses),
        Materializations: Interlocked.Read(ref materializations),
        CacheInsertions: Interlocked.Read(ref cacheInsertions));

    public bool TryGetCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        out IImmutableInstance? instance)
    {
        if (!table.UseCache)
        {
            ValidateOwnedTable(table);
            instance = null;
            return false;
        }

        return GetMaterializedRowCache(table).TryGetValue(canonicalProviderKey, out instance);
    }

    public ModelCachePublicationResult PublishCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        RowData rowData,
        IImmutableInstance instance)
    {
        ArgumentNullException.ThrowIfNull(rowData);
        ArgumentNullException.ThrowIfNull(instance);
        var cache = GetMaterializedRowCache(table);

        if (!ReferenceEquals(rowData.Table, table))
        {
            throw new ArgumentException(
                "The materialized row metadata does not match the memory cache table.",
                nameof(rowData));
        }

        if (!ReferenceEquals(instance.GetReadSource(), this))
        {
            throw new ArgumentException(
                "The materialized instance belongs to another read source.",
                nameof(instance));
        }

        if (!table.UseCache)
            return ModelCachePublicationResult.NotCached();

        if (cache.TryAddRow(canonicalProviderKey, rowData, instance))
            return ModelCachePublicationResult.Inserted();

        if (cache.TryGetValue(canonicalProviderKey, out var existing) && existing is not null)
            return ModelCachePublicationResult.Existing(existing);

        throw new InvalidOperationException(
            $"Memory materialization cache failed to publish a row for table '{table.DbName}'.");
    }

    public void RecordCacheLookup(TableDefinition table, bool hit)
    {
        ValidateOwnedTable(table);
        Interlocked.Increment(ref cacheLookups);
        if (hit)
            Interlocked.Increment(ref cacheHits);
        else
            Interlocked.Increment(ref cacheMisses);
    }

    public void RecordMaterialization(TableDefinition table)
    {
        ValidateOwnedTable(table);
        Interlocked.Increment(ref materializations);
    }

    public void RecordCacheInsertion(TableDefinition table)
    {
        ValidateOwnedTable(table);
        Interlocked.Increment(ref cacheInsertions);
    }

    private RowCache GetMaterializedRowCache(TableDefinition table)
    {
        ValidateOwnedTable(table);
        return materializedRows.TryGetValue(table, out var cache)
            ? cache
            : throw new InvalidOperationException(
                $"Memory source has no materialization cache for table '{table.DbName}'.");
    }

    private void ValidateOwnedTable(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!ReferenceEquals(table.Database, Metadata))
        {
            throw new ArgumentException(
                $"Table '{table.DbName}' is not owned by memory source '{Metadata.DbName}'.",
                nameof(table));
        }
    }
}
