using System;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Cache;

/// <summary>
/// Adapts the existing SQL read/transaction source scopes to the neutral materialization cache
/// boundary. SQL command and connection services deliberately remain outside this type.
/// </summary>
internal sealed class DataSourceAccessMaterializationCache : IReadSourceMaterializationCache
{
    private readonly IDataSourceAccess dataSource;

    internal DataSourceAccessMaterializationCache(IDataSourceAccess dataSource)
    {
        this.dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public bool TryGetCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        out IImmutableInstance? instance) =>
        GetTableCache(table).TryGetMaterializedRow(
            canonicalProviderKey,
            dataSource,
            out instance);

    public ModelCachePublicationResult PublishCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        RowData rowData,
        IImmutableInstance instance) =>
        GetTableCache(table).PublishMaterializedRow(
            canonicalProviderKey,
            rowData,
            instance,
            dataSource);

    public void RecordCacheLookup(TableDefinition table, bool hit) =>
        GetTableCache(table).RecordMaterializationCacheLookup(hit);

    public void RecordMaterialization(TableDefinition table) =>
        GetTableCache(table).RecordMaterializedRow();

    public void RecordCacheInsertion(TableDefinition table) =>
        GetTableCache(table).RecordMaterializationCacheInsertion();

    private TableCache GetTableCache(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (!ReferenceEquals(table.Database, dataSource.Metadata))
        {
            throw new InvalidOperationException(
                $"Read source metadata does not own table '{table.DbName}'.");
        }

        return dataSource.Provider.GetTableCache(table);
    }
}
