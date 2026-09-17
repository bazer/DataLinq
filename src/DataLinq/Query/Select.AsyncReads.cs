using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Query;

public partial class Select<T>
{
    // Internal until native W2 evidence and the W3 public surface. Ordinary sequences
    // capture per enumerator; terminal calls capture before their first suspension.
    internal IAsyncEnumerable<RowData> ReadRowsAsyncCore(CancellationToken cancellationToken = default) =>
        new AsyncReaderEnumerable<RowData>(CaptureRows, cancellationToken);

    internal IAsyncEnumerable<IAsyncDataReader> ReadReaderAsyncCore(CancellationToken cancellationToken = default) =>
        new AsyncReaderEnumerable<IAsyncDataReader>(() =>
        {
            var factory = IAsyncSqlReaderFactory.Require(query.DataSource.DatabaseAccess);
            var sql = CapturedSql.Capture(ToSql());
            return new(factory.BindReader(sql), static reader => reader, query.DataSource as Transaction);
        }, cancellationToken);

    internal Task<RowData?> ReadFirstRowAsyncCore(CancellationToken cancellationToken = default)
    {
        var invocation = CaptureRows();
        return ReadFirstAsync(invocation, cancellationToken);
    }

    private static async Task<RowData?> ReadFirstAsync(AsyncReaderInvocation<RowData> invocation, CancellationToken token)
    {
        await using var rows = new AsyncReaderEnumerable<RowData>(() => invocation, token).GetAsyncEnumerator();
        return await rows.MoveNextAsync().ConfigureAwait(false) ? rows.Current : null;
    }

    internal Task<List<RowData>> ReadRowsBufferedAsyncCore(CancellationToken cancellationToken = default)
    {
        var invocation = CaptureRows();
        return ReadBufferedAsync(invocation, cancellationToken);
    }

    private static async Task<List<RowData>> ReadBufferedAsync(AsyncReaderInvocation<RowData> invocation, CancellationToken token)
    {
        var result = new List<RowData>();
        await using var rows = new AsyncReaderEnumerable<RowData>(() => invocation, token).GetAsyncEnumerator();
        while (await rows.MoveNextAsync().ConfigureAwait(false)) result.Add(rows.Current);
        return result;
    }

    private AsyncReaderInvocation<RowData> CaptureRows()
    {
        var source = query.DataSource;
        var factory = IAsyncSqlReaderFactory.Require(source.DatabaseAccess);
        var table = query.Table;
        var sql = CapturedSql.Capture(ToSql());
        var layout = CaptureRowLayout();
        var sourceName = $"sql:{source.Provider.DatabaseType}:select-rows";
        return new(factory.BindReader(sql), reader => new RowData(reader, table, layout, sourceName), source as Transaction);
    }

    private (ColumnDefinition Column, int ReaderOrdinal)[] CaptureRowLayout()
    {
        if (query.WhatList is not { } selectors)
        {
            var columns = query.Table.Columns;
            var all = new (ColumnDefinition, int)[columns.Length];
            for (var i = 0; i < columns.Length; i++) all[i] = (columns[i], i);
            return all;
        }
        var layout = new List<(ColumnDefinition, int)>();
        for (var ordinal = 0; ordinal < selectors.Count; ordinal++)
        {
            var name = SqlIdentifier.Unquote(selectors[ordinal], query.EscapeCharacter);
            var column = query.Table.TryGetColumnByDbName(name, out var exact) ? exact
                : query.Table.TryGetColumnByDbName(name, StringComparison.OrdinalIgnoreCase, out var insensitive) ? insensitive : null;
            // An unmapped expression has no RowData slot, but still consumes a reader
            // ordinal. Never shift later mapped columns onto that expression's value.
            if (column is not null) layout.Add((column, ordinal));
        }
        return layout.ToArray();
    }
}
