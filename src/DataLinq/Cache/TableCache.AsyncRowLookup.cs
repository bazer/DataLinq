using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Cache;

public partial class TableCache
{
    /// <summary>
    /// Strict neutral-key entry point for source-loader integration. General model lookups
    /// use GetProviderRowAsyncCore, which also preserves provider-sensitive matching.
    /// </summary>
    internal Task<IImmutableInstance?> GetCanonicalRowAsyncCore(
        DataLinqKey key, IDataSourceAccess dataSource, CancellationToken token = default)
    {
        DataSourceAccess.EnsureReadAllowed(dataSource, "read an asynchronous cache row");
        if (GetCanonicalPrimaryKeySourceServices(dataSource) is null)
            throw new NotSupportedException("This key shape does not support neutral canonical row loading.");
        return GetProviderRowAsyncCore(key, dataSource, token);
    }

    internal Task<IImmutableInstance?> GetProviderRowAsyncCore(
        DataLinqKey key, IDataSourceAccess dataSource, CancellationToken token = default,
        TransactionOperationGate.Step? owner = null, IAsyncSqlReaderFactory? factory = null,
        Action<IAsyncReadFailureEvidence>? observingRead = null,
        AsyncBufferedRead<CanonicalProviderValueRow?>? preparedRead = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DataSourceAccess.EnsureReadAllowed(dataSource, "read an asynchronous cache row", owner);
        if (dataSource is not IDataLinqSourceRowServices)
            throw new NotSupportedException("This read source does not support canonical model materialization.");
        var loader = new DataSourceAccessSourceRowLoader(dataSource, owner, factory);
        var read = preparedRead ?? (ProviderKeyComponents.SupportsNeutralSourceRowLoading(Table, dataSource.Provider.DatabaseType)
            ? loader.CaptureSingleAsyncRead(Table, key)
            : loader.CaptureProviderMatchedSingleAsyncRead(Table, key));
        observingRead?.Invoke(read);
        RowReadGeneration? generation = null;
        return read.ExecuteAsync<IImmutableInstance?>((providerRow, step, cancellation) =>
        {
            if (providerRow is null) return null;
            cancellation.ThrowIfCancellationRequested();
            if (!providerRow.TryCreateCanonicalPrimaryKey(out var returnedKey))
                throw new InvalidOperationException("A primary-key lookup returned a row without a canonical key.");
            var services = step is null ? (IDataLinqSourceRowServices)dataSource : ((DataSourceAccess)dataSource).GetOwnedRowServices(step);
            var row = services.MaterializationServices.MaterializeAfterKnownCacheMiss(
                new LoadedCanonicalRow(providerRow, returnedKey) { ReadGeneration = generation });
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
