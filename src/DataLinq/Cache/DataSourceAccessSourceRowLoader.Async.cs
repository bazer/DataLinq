using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Cache;

internal sealed partial class DataSourceAccessSourceRowLoader
{
    // Internal W1 integration. These paths require an explicit async SQL capability;
    // existing synchronous loaders remain direct and unchanged.
    internal Task<CanonicalProviderValueRow?> LoadSingleAsync(
        TableDefinition table, DataLinqKey key, CancellationToken token = default) =>
        CaptureSingleAsyncRead(table, key).ExecuteAsync(token);

    internal AsyncBufferedRead<CanonicalProviderValueRow?> CaptureSingleAsyncRead(TableDefinition table, DataLinqKey key)
    {
        SourceRowLoadingValidation.ValidatePrimaryKeyTable(table);
        SourceRowLoadingValidation.ValidateCanonicalKey(table, key, 0, nameof(key));
        EnsureCanLoad(table, "load one asynchronous source row");
        var factory = IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
        var sql = CapturedSql.Capture(CreateSingleQuery(table, in key, default).ToSql());
        CanonicalProviderValueRow? row = null;
        return new(dataSource, factory.BindReader(sql), reader =>
        {
            if (row is not null)
                throw new InvalidOperationException($"Singular source-row query for table '{table.DbName}' returned more than one row.");
            row = ProviderRowDecoder.DecodeFullRow(reader, table, sourceName);
            SourceRowLoadingValidation.ValidateSingleResult(table, in key, row, "Source row loader");
        }, () => row, owner);
    }

    internal Task<SourceRowLoadResult> LoadAsync(SourcePrimaryKeyRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load asynchronous source rows");
        var factory = IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
        // Borrowed slices are synchronous-only storage. Detach even that storage before
        // suspension; both SQL binding and returned-key validation use the same snapshot.
        var captured = new SourcePrimaryKeyRowRequest(request.Table, request.CanonicalProviderKeys);
        var sql = CapturedSql.Capture(CreateSelect(captured).ToSql());
        var builder = new SourceRowLoadResult.Builder(captured, captured.CanonicalProviderKeys.Length);
        var read = new AsyncBufferedRead<SourceRowLoadResult>(dataSource, factory.BindReader(sql),
            reader => builder.Add(ProviderRowDecoder.DecodeFullRow(reader, captured.Table, sourceName)),
            () => builder.Build(request), owner);
        return read.ExecuteAsync(request.CancellationToken);
    }

    internal Task<SourceIndexRowLoadResult> LoadAsync(SourceIndexRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load indexed asynchronous source rows");
        var factory = IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
        // SourceIndexRowRequest already owns its canonical key. Suppress cancellation
        // only during I/O-free binding so capability validation precedes cancellation.
        var captured = new SourceIndexRowRequest(request.Table, request.Index, request.CanonicalProviderIndexKey);
        var sql = CapturedSql.Capture(CreateSelect(captured).ToSql());
        var builder = new SourceIndexRowLoadResult.Builder(request);
        var read = new AsyncBufferedRead<SourceIndexRowLoadResult>(dataSource, factory.BindReader(sql),
            reader => builder.Add(ProviderRowDecoder.DecodeFullRow(reader, request.Table, sourceName)),
            () => builder.Build(), owner);
        return read.ExecuteAsync(request.CancellationToken);
    }
}
