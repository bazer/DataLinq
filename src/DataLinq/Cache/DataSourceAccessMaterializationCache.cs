using System;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Cache;

/// <summary>
/// Adapts the existing SQL read/transaction source scopes to the neutral materialization cache
/// boundary. SQL command and connection services deliberately remain outside this type.
/// </summary>
internal sealed class DataSourceAccessMaterializationCache : IReadSourceMaterializationCache
{
    private readonly IDataSourceAccess dataSource;
    private readonly TransactionOperationGate.Step? owner;

    internal DataSourceAccessMaterializationCache(
        IDataSourceAccess dataSource, TransactionOperationGate.Step? owner = null)
    {
        this.dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        this.owner = owner;
    }

    public bool TryGetCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        out IImmutableInstance? instance) =>
        GetTableCache(table).TryGetMaterializedRow(
            canonicalProviderKey,
            dataSource,
            out instance, owner);

    public ModelCachePublicationResult PublishCached(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        RowData rowData,
        IImmutableInstance instance,
        RowReadGeneration? readGeneration = null) =>
        GetTableCache(table).PublishMaterializedRow(
            canonicalProviderKey,
            rowData,
            instance,
            dataSource,
            readGeneration, owner);

    public void RecordCacheLookup(TableDefinition table, bool hit) =>
        GetTableCache(table).RecordMaterializationCacheLookup(hit);

    public void RecordMaterialization(TableDefinition table) =>
        GetTableCache(table).RecordMaterializedRow();

    public void RecordCacheInsertion(TableDefinition table) =>
        GetTableCache(table).RecordMaterializationCacheInsertion();

    private TableCache GetTableCache(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);
        DataSourceAccess.EnsureReadAllowed(dataSource, "access materialized rows", owner);

        if (!ReferenceEquals(table.Database, dataSource.Metadata))
        {
            throw new InvalidOperationException(
                $"Read source metadata does not own table '{table.DbName}'.");
        }

        return dataSource.Provider.GetTableCache(table);
    }
}
