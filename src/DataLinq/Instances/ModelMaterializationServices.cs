using System;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Source-bound orchestration for canonical identity, row conversion, immutable construction,
/// cache participation, and successful materialization metrics.
/// </summary>
internal interface IModelMaterializationServices
{
    IImmutableInstance GetOrMaterialize(CanonicalProviderValueRow providerRow);
}

/// <summary>
/// Backend adapter consumed by <see cref="ModelMaterializationServices"/>. Implementations bind the
/// correct cache scope, generated immutable factory, and metrics sink without exposing those details
/// to the neutral orchestration algorithm.
/// </summary>
internal interface IModelMaterializationRuntime
{
    /// <summary>
    /// Probes the source's correctly scoped identity cache without recording a logical cache metric.
    /// The caller records exactly one hit or miss for the initial probe.
    /// </summary>
    bool TryGetCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        out IImmutableInstance? instance);

    /// <summary>
    /// Invokes the generated immutable factory without recording materialization metrics.
    /// </summary>
    IImmutableInstance CreateImmutable(RowData rowData);

    /// <summary>
    /// Publishes an immutable instance to the correctly scoped identity cache. Cache-disabled rows
    /// return <see cref="ModelCachePublication.NotCached"/>; a concurrent winner is returned as
    /// <see cref="ModelCachePublication.Existing"/>. <see cref="ModelCachePublication.Inserted"/>
    /// means this call physically inserted the supplied instance.
    /// </summary>
    ModelCachePublicationResult PublishCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        RowData rowData,
        IImmutableInstance instance);

    void RecordCacheLookup(TableDefinition table, bool hit);
    void RecordMaterialization(TableDefinition table);

    /// <summary>
    /// Records an actual cache insertion and refreshes cache occupancy accounting.
    /// </summary>
    void RecordCacheInsertion(TableDefinition table);
}

/// <summary>
/// Source-owned identity-cache and metrics boundary used by neutral materialization. Implementations
/// retain committed versus transaction-local scope without exposing provider-specific state.
/// </summary>
internal interface IReadSourceMaterializationCache
{
    bool TryGetCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        out IImmutableInstance? instance);

    ModelCachePublicationResult PublishCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        RowData rowData,
        IImmutableInstance instance);

    void RecordCacheLookup(TableDefinition table, bool hit);
    void RecordMaterialization(TableDefinition table);
    void RecordCacheInsertion(TableDefinition table);
}

/// <summary>
/// Binds the shared materialization algorithm to one read source and its correctly scoped identity
/// cache. Immutable construction stays source-neutral; cache ownership remains with the source.
/// </summary>
internal sealed class ReadSourceModelMaterializationRuntime : IModelMaterializationRuntime
{
    private readonly IDataLinqReadSource readSource;
    private readonly IReadSourceMaterializationCache cache;

    internal ReadSourceModelMaterializationRuntime(
        IDataLinqReadSource readSource,
        IReadSourceMaterializationCache cache)
    {
        this.readSource = readSource ?? throw new ArgumentNullException(nameof(readSource));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public bool TryGetCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        out IImmutableInstance? instance) =>
        cache.TryGetCached(table, canonicalProviderKey, out instance);

    public IImmutableInstance CreateImmutable(RowData rowData) =>
        InstanceFactory.NewReadSourceImmutableRow(rowData, readSource);

    public ModelCachePublicationResult PublishCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        RowData rowData,
        IImmutableInstance instance) =>
        cache.PublishCached(table, canonicalProviderKey, rowData, instance);

    public void RecordCacheLookup(TableDefinition table, bool hit) =>
        cache.RecordCacheLookup(table, hit);

    public void RecordMaterialization(TableDefinition table) =>
        cache.RecordMaterialization(table);

    public void RecordCacheInsertion(TableDefinition table) =>
        cache.RecordCacheInsertion(table);
}

internal enum ModelCachePublication
{
    Invalid,
    NotCached,
    Inserted,
    Existing
}

internal readonly record struct ModelCachePublicationResult
{
    private ModelCachePublicationResult(
        ModelCachePublication publication,
        IImmutableInstance? existingInstance)
    {
        Publication = publication;
        ExistingInstance = existingInstance;
    }

    internal ModelCachePublication Publication { get; }
    internal IImmutableInstance? ExistingInstance { get; }

    internal static ModelCachePublicationResult NotCached() =>
        new(ModelCachePublication.NotCached, existingInstance: null);

    internal static ModelCachePublicationResult Inserted() =>
        new(ModelCachePublication.Inserted, existingInstance: null);

    internal static ModelCachePublicationResult Existing(IImmutableInstance instance) =>
        new(
            ModelCachePublication.Existing,
            instance ?? throw new ArgumentNullException(nameof(instance)));
}

internal sealed class ModelMaterializationServices : IModelMaterializationServices
{
    private readonly string sourceName;
    private readonly IModelMaterializationRuntime runtime;

    internal ModelMaterializationServices(string sourceName, IModelMaterializationRuntime runtime)
    {
        ProviderRowMaterializer.ValidateSourceName(sourceName);
        this.sourceName = sourceName;
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public IImmutableInstance GetOrMaterialize(CanonicalProviderValueRow providerRow)
    {
        ArgumentNullException.ThrowIfNull(providerRow);

        var hasCanonicalKey = TryCreateCanonicalPrimaryKey(providerRow, out var canonicalProviderKey);
        if (hasCanonicalKey)
        {
            var cacheHit =
                runtime.TryGetCached(providerRow.Table, canonicalProviderKey, out var cached) &&
                cached is not null;
            runtime.RecordCacheLookup(providerRow.Table, cacheHit);

            if (cacheHit)
                return cached!;
        }

        var rowData = ProviderRowMaterializer.Materialize(providerRow, sourceName);
        var created = runtime.CreateImmutable(rowData)
            ?? throw new InvalidOperationException(
                $"Immutable factory returned null for model '{providerRow.Table.Model.CsType}'.");
        runtime.RecordMaterialization(providerRow.Table);

        if (!hasCanonicalKey)
            return created;

        var publication = runtime.PublishCached(
            providerRow.Table,
            canonicalProviderKey,
            rowData,
            created);

        switch (publication.Publication)
        {
            case ModelCachePublication.Inserted:
                runtime.RecordCacheInsertion(providerRow.Table);
                return created;

            case ModelCachePublication.Existing when publication.ExistingInstance is not null:
                return publication.ExistingInstance;

            case ModelCachePublication.NotCached:
                return created;

            default:
                throw new InvalidOperationException(
                    $"Materialization runtime returned an invalid cache publication result for table '{providerRow.Table.DbName}'.");
        }
    }

    private static bool TryCreateCanonicalPrimaryKey(
        CanonicalProviderValueRow providerRow,
        out DataLinqKey canonicalProviderKey)
    {
        var primaryKeyColumns = providerRow.Table.PrimaryKeyColumns;
        if (primaryKeyColumns.Count == 0)
        {
            canonicalProviderKey = DataLinqKey.Null;
            return false;
        }

        var values = new object?[primaryKeyColumns.Count];
        for (var index = 0; index < primaryKeyColumns.Count; index++)
        {
            var column = primaryKeyColumns[index];
            values[index] = providerRow[column];
            if (values[index] is null)
            {
                throw new InvalidOperationException(
                    $"Canonical provider row for table '{providerRow.Table.DbName}' contains null primary-key component '{column.DbName}'.");
            }
        }

        canonicalProviderKey = DataLinqKey.FromValues(values);
        return true;
    }
}
