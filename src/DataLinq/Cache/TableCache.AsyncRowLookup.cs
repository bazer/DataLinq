using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Cache;

public partial class TableCache
{
    /// <summary>
    /// Internal canonical-key slice, not the general public model lookup. Provider-sensitive
    /// key equality and model-key normalization require their separate query integration.
    /// </summary>
    internal Task<IImmutableInstance?> GetCanonicalRowAsyncCore(
        DataLinqKey key, IDataSourceAccess dataSource, CancellationToken token = default)
    {
        DataSourceAccess.EnsureReadAllowed(dataSource, "read an asynchronous cache row");
        if (GetCanonicalPrimaryKeySourceServices(dataSource) is null)
            throw new NotSupportedException("This key shape does not support neutral canonical row loading.");
        var read = new DataSourceAccessSourceRowLoader(dataSource).CaptureSingleAsyncRead(Table, key);
        RowReadGeneration? generation = null;
        return read.ExecuteAsync<IImmutableInstance?>((providerRow, step, cancellation) =>
        {
            if (providerRow is null) return null;
            cancellation.ThrowIfCancellationRequested();
            var services = GetCanonicalPrimaryKeySourceServices(dataSource, step)!;
            var row = services.MaterializationServices.MaterializeAfterKnownCacheMiss(
                new LoadedCanonicalRow(providerRow, key) { ReadGeneration = generation });
            MetricsHandle.RecordDatabaseRowsLoaded(1);
            return row;
        }, token, step =>
        {
            EnsureTransactionRowCache(dataSource, step);
            var hit = GetRowFromCache(key, dataSource, out var row);
            RecordSingleRowCacheLookup(hit);
            if (!hit) generation = CaptureReadGeneration();
            return (hit, row);
        });
    }
}
