using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Query;

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
        var factory = asyncFactory ?? IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
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

    internal Task<SourceRowLoadResult> LoadAsync(SourcePrimaryKeyRowRequest request) =>
        CaptureAsyncRead(request).ExecuteAsync(request.CancellationToken);

    internal AsyncBufferedRead<SourceRowLoadResult> CaptureAsyncRead(SourcePrimaryKeyRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load asynchronous source rows");
        var factory = asyncFactory ?? IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
        // Borrowed slices are synchronous-only storage. Detach even that storage before
        // suspension; both SQL binding and returned-key validation use the same snapshot.
        var captured = new SourcePrimaryKeyRowRequest(request.Table, request.CanonicalProviderKeys);
        var sql = CapturedSql.Capture(CreateSelect(captured).ToSql());
        var builder = new SourceRowLoadResult.Builder(captured, captured.CanonicalProviderKeys.Length);
        return new AsyncBufferedRead<SourceRowLoadResult>(dataSource, factory.BindReader(sql),
            reader => builder.Add(ProviderRowDecoder.DecodeFullRow(reader, captured.Table, sourceName)),
            () => builder.Build(request), owner);
    }

    internal Task<SourceIndexRowLoadResult> LoadAsync(SourceIndexRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load indexed asynchronous source rows");
        var factory = asyncFactory ?? IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
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

    internal AsyncBufferedRead<IReadOnlyList<LoadedCanonicalRow>> CaptureProviderMatchedAsyncRead(SourcePrimaryKeyRowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCanLoad(request.Table, "load asynchronous provider-matched rows");
        var factory = asyncFactory ?? IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
        var captured = new SourcePrimaryKeyRowRequest(request.Table, request.CanonicalProviderKeys);
        var sql = CapturedSql.Capture(CreateSelect(captured).ToSql());
        var rows = new List<LoadedCanonicalRow>();
        return new(dataSource, factory.BindReader(sql), reader =>
        {
            var row = ProviderRowDecoder.DecodeFullRow(reader, captured.Table, sourceName);
            if (!row.TryCreateCanonicalPrimaryKey(out var key))
                throw new InvalidOperationException("A primary-key query returned a row without a canonical key.");
            // Preserve the existing SQL batch path: provider collation/storage decides
            // matches. Do not apply neutral requested-key or duplicate validation here.
            rows.Add(new LoadedCanonicalRow(row, key));
        }, () => rows, owner);
    }

    internal AsyncBufferedRead<CanonicalProviderValueRow?> CaptureProviderMatchedSingleAsyncRead(TableDefinition table, DataLinqKey key)
    {
        SourceRowLoadingValidation.ValidatePrimaryKeyTable(table);
        ProviderKeyComponents.ThrowIfComponentCountMismatch(key, table.PrimaryKeyColumns.Count, $"Provider key for table '{table.DbName}'");
        EnsureCanLoad(table, "load an asynchronous provider-matched row");
        var factory = asyncFactory ?? IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess);
        // In particular, NULL retains the existing SQL predicate semantics. Do not
        // turn provider-sensitive keys into neutral CLR-equality validation.
        var query = new SqlQuery(table, dataSource).Where(table.PrimaryKeyColumns, key).SelectQuery();
        var sql = CapturedSql.Capture(query.ToSql());
        CanonicalProviderValueRow? row = null;
        return new(dataSource, factory.BindReader(sql), reader => row = ProviderRowDecoder.DecodeFullRow(reader, table, sourceName),
            () => row, owner, firstRowOnly: true);
    }
}
