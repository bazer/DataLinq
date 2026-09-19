using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Mutation;

public abstract partial class DataSourceAccess
{
    internal IAsyncEnumerable<T> GetFromQueryAsyncCore<T>(string sql, CancellationToken token = default) where T : IModel =>
        new AsyncReaderEnumerable<T>(() =>
        {
            ArgumentNullException.ThrowIfNull(sql);
            EnsureReadAllowed(this, "read models from asynchronous SQL", operationKind: ExecutionOperationKind.RawCommand);
            var table = Provider.Metadata.GetTableModel(typeof(T)).Table;
            var factory = IAsyncSqlReaderFactory.Require(DatabaseAccess);
            return CaptureRawModels<T>(table, factory.BindReader(CapturedSql.Capture(new Sql(sql))), "raw-query");
        }, token);

    internal IAsyncEnumerable<T> GetFromCommandAsyncCore<T>(IDbCommand command, CancellationToken token = default) where T : IModel =>
        new AsyncReaderEnumerable<T>(() =>
        {
            ArgumentNullException.ThrowIfNull(command);
            EnsureReadAllowed(this, "read models from an asynchronous borrowed command", operationKind: ExecutionOperationKind.RawCommand);
            var table = Provider.Metadata.GetTableModel(typeof(T)).Table;
            var factory = IAsyncBorrowedReaderFactory.Require(DatabaseAccess);
            return CaptureRawModels<T>(table, factory.BindBorrowedReader(command), "raw-command");
        }, token);

    private AsyncReaderInvocation<T> CaptureRawModels<T>(TableDefinition table, IAsyncReaderSource source, string kind) where T : IModel
    {
        int[]? layout = null;
        var sourceName = $"sql:{Provider.DatabaseType}:{kind}";
        return new(RawAsyncReaderSource.Wrap(source), reader =>
        {
            // Raw SQL may reorder columns. Resolve its layout on the actual reader;
            // canonical decoding owns values before provider-to-model conversion.
            if (layout is null)
            {
                layout = new int[table.ColumnCount];
                for (var i = 0; i < layout.Length; i++)
                    layout[i] = reader.GetOrdinal(table.Columns[i].DbName);
            }
            var providerRow = ProviderRowDecoder.DecodeFullRow(reader, table, layout, sourceName);
            var row = ProviderRowMaterializer.Materialize(providerRow, sourceName);
            // The canonical identity is already known. Reconstructing it from the
            // converted model would call user ToProvider converters a second time.
            IRowData constructionRow = providerRow.TryCreateCanonicalPrimaryKey(out var key)
                ? new KnownCanonicalPrimaryKeyRowData(row, key) : row;
            return InstanceFactory.NewImmutableRow<T>(constructionRow, this);
        }, this as Transaction, Identity: ReadExecutionIdentity.Capture(this, ExecutionOperationKind.RawCommand));
    }
}
