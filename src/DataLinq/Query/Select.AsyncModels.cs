using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Query;

public partial class Select<T>
{
    internal IAsyncEnumerable<IImmutableInstance> ExecuteAsyncCore(CancellationToken token = default) =>
        new AsyncReaderEnumerable<IImmutableInstance>(CaptureModels, token);

    internal Task<List<IImmutableInstance>> ExecuteBufferedAsyncCore(CancellationToken token = default)
    {
        var captured = CaptureModels();
        return BufferModelsAsync(new AsyncReaderEnumerable<IImmutableInstance>(() => captured, token));
    }

    private static async Task<List<IImmutableInstance>> BufferModelsAsync(IAsyncEnumerable<IImmutableInstance> sequence)
    {
        var rows = new List<IImmutableInstance>();
        await foreach (var row in sequence.ConfigureAwait(false)) rows.Add(row);
        return rows;
    }

    internal AsyncReaderInvocation<IImmutableInstance> CaptureModels()
    {
        var source = query.DataSource;
        DataSourceAccess.EnsureReadAllowed(source, "capture an asynchronous model query", operationKind: ExecutionOperationKind.Query);
        var factory = IAsyncSqlReaderFactory.Require(source.DatabaseAccess).CaptureInvocation()
            ?? throw new InvalidOperationException("The async reader factory returned no invocation snapshot.");
        var table = query.Table;
        var hasKey = table.PrimaryKeyColumns.Count != 0;
        if (hasKey && source is not IDataLinqSourceRowServices)
            throw new NotSupportedException("This read source does not support canonical model materialization.");
        // Render a model/key projection without editing the caller's WhatList. SQL,
        // parameters, layouts and the factory belong to this enumeration/invocation.
        var sql = CapturedSql.Capture(hasKey
            ? RenderSql(null, table.PrimaryKeyColumns)
            : ToSql());
        var readerSource = factory.BindReader(sql);
        DataLinqKey? directKey = null;
        if (hasKey && query.TryGetSimplePrimaryKey() is { } simple &&
            ProviderKeyComponents.TryCreateExactCanonicalKey(simple, table.PrimaryKeyColumns, out var canonical))
            directKey = canonical;
        IAsyncReaderContinuation<IImmutableInstance> continuation = hasKey
            ? new ModelKeyContinuation(source, table, source.Provider.GetTableCache(table), factory, readerSource, directKey)
            : new ModelRowContinuation(source, table, readerSource, CaptureRowLayout());
        return new(readerSource, null, source as Transaction, Continuation: continuation,
            Identity: ReadExecutionIdentity.Capture(source, ExecutionOperationKind.Query));
    }

    private sealed class ModelKeyContinuation(
        IDataSourceAccess source, TableDefinition table, TableCache cache,
        IAsyncSqlReaderFactory factory, IAsyncReaderSource initialSource, DataLinqKey? directKey) : IAsyncReaderContinuation<IImmutableInstance>
    {
        private readonly List<DataLinqKey> keys = directKey.HasValue ? [directKey.Value] : [];
        private IAsyncReadFailureEvidence? evidence = initialSource as IAsyncReadFailureEvidence;

        public bool RequiresInitialReader => !directKey.HasValue;

        public void AddRow(IAsyncDataReader reader)
        {
            var columns = table.PrimaryKeyColumns;
            var values = new object?[columns.Count];
            for (var i = 0; i < values.Length; i++)
                values[i] = ProviderRowDecoder.DecodeCanonicalValue(reader, columns[i], i, "reader:model-query-key", columns[i].IsGuidColumn);
            keys.Add(DataLinqKey.FromOwnedValues(values));
        }

        public async Task<IReadOnlyList<IImmutableInstance>> CompleteAsync(TransactionOperationGate.Step? owner, CancellationToken token)
        {
            if (directKey.HasValue)
            {
                var row = await cache.GetProviderRowAsyncCore(directKey.Value, source, token, owner, factory, current => evidence = current,
                    operationKind: ExecutionOperationKind.Query).ConfigureAwait(false);
                return row is null ? [] : [row];
            }
            return await cache.LoadQueryRowsAsync(keys, source, factory, owner, current => evidence = current, token).ConfigureAwait(false);
        }

        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => evidence?.GetReadFailureEvidence(failure) ?? new();
    }

    private sealed class ModelRowContinuation(
        IDataSourceAccess source, TableDefinition table, IAsyncReaderSource initialSource,
        (ColumnDefinition Column, int ReaderOrdinal)[] layout) : IAsyncReaderContinuation<IImmutableInstance>
    {
        private readonly List<RowData> keylessRows = [];

        public bool RequiresInitialReader => true;

        public void AddRow(IAsyncDataReader reader)
        {
            keylessRows.Add(new RowData(reader, table, layout, "reader:model-query-row"));
        }

        public Task<IReadOnlyList<IImmutableInstance>> CompleteAsync(TransactionOperationGate.Step? owner, CancellationToken token)
        {
            var result = new List<IImmutableInstance>(keylessRows.Count);
            foreach (var row in keylessRows)
            {
                token.ThrowIfCancellationRequested();
                result.Add(InstanceFactory.NewImmutableRow(row, source));
            }
            return Task.FromResult<IReadOnlyList<IImmutableInstance>>(result);
        }

        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) =>
            initialSource is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();
    }
}
