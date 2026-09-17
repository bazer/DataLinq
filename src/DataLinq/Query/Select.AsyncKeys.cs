using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Query;

public partial class Select<T>
{
    internal IAsyncEnumerable<DataLinqKey> ReadKeysAsyncCore(CancellationToken cancellationToken = default) =>
        new AsyncReaderEnumerable<DataLinqKey>(() =>
        {
            var factory = IAsyncSqlReaderFactory.Require(query.DataSource.DatabaseAccess);
            var sql = CapturedSql.Capture(ToSql());
            var layout = CaptureKeyLayout(query.Table.PrimaryKeyColumns);
            return new(factory.BindReader(sql), reader => ReadCapturedKey(reader, layout), query.DataSource as Transaction);
        }, cancellationToken);

    internal IAsyncEnumerable<(DataLinqKey fk, DataLinqKey[] pks)> ReadPrimaryAndForeignKeysAsyncCore(
        ColumnIndex foreignKeyIndex, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(foreignKeyIndex);
        return new AsyncReaderEnumerable<(DataLinqKey, DataLinqKey[])>(() =>
        {
            var factory = IAsyncSqlReaderFactory.Require(query.DataSource.DatabaseAccess);
            var sql = CapturedSql.Capture(ToSql());
            var primary = CaptureKeyLayout(query.Table.PrimaryKeyColumns);
            var foreign = CaptureKeyLayout(foreignKeyIndex.Columns);
            return new(factory.BindReader(sql), null, query.DataSource as Transaction, new KeyGroupBuffer(primary, foreign));
        }, cancellationToken);
    }

    private (ColumnDefinition Column, int ReaderOrdinal)[] CaptureKeyLayout(IReadOnlyList<ColumnDefinition> columns)
    {
        var selected = CaptureRowLayout();
        var layout = new (ColumnDefinition, int)[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (!ReferenceEquals(column.Table, query.Table))
                throw new ArgumentException("Key columns must belong to the query table.", nameof(columns));
            var ordinal = -1;
            foreach (var cell in selected)
                if (ReferenceEquals(cell.Column, column)) ordinal = cell.ReaderOrdinal;
            if (ordinal < 0)
                throw new InvalidOperationException($"The captured selection does not include key column '{column.DbName}'.");
            layout[i] = (column, ordinal);
        }
        return layout;
    }

    private static DataLinqKey ReadCapturedKey(IAsyncDataReader reader, (ColumnDefinition Column, int ReaderOrdinal)[] layout)
    {
        // Decode provider-domain components directly. A model converter round trip can
        // change identity, allocate wrappers or throw despite a valid stored key.
        var values = new object?[layout.Length];
        for (var i = 0; i < values.Length; i++)
            values[i] = ReadCapturedKeyValue(reader, layout[i]);
        return DataLinqKey.FromOwnedValues(values);
    }

    private static object? ReadCapturedKeyValue(IAsyncDataReader reader, (ColumnDefinition Column, int ReaderOrdinal) cell) =>
        ProviderRowDecoder.DecodeCanonicalValue(reader, cell.Column, cell.ReaderOrdinal,
            "reader:captured-key", useColumnAwareGuid: cell.Column.IsGuidColumn);

    private sealed class KeyGroupBuffer : IAsyncReaderBuffer<(DataLinqKey fk, DataLinqKey[] pks)>
    {
        private readonly Dictionary<DataLinqKey, List<DataLinqKey>> groups = [];
        private readonly (ColumnDefinition Column, int ReaderOrdinal)[] layout;
        private readonly int[] primaryPositions;
        private readonly int[] foreignPositions;

        internal KeyGroupBuffer((ColumnDefinition Column, int ReaderOrdinal)[] primary,
            (ColumnDefinition Column, int ReaderOrdinal)[] foreign)
        {
            layout = primary.Concat(foreign).DistinctBy(x => x.Column).OrderBy(x => x.ReaderOrdinal).ToArray();
            primaryPositions = primary.Select(x => Array.FindIndex(layout, y => ReferenceEquals(x.Column, y.Column))).ToArray();
            foreignPositions = foreign.Select(x => Array.FindIndex(layout, y => ReferenceEquals(x.Column, y.Column))).ToArray();
        }

        public void AddRow(IAsyncDataReader reader)
        {
            // Shared PK/FK cells are decoded once, in reader order. This also avoids
            // transferring an owned binary field twice just to construct two keys.
            var values = new object?[layout.Length];
            for (var i = 0; i < values.Length; i++) values[i] = ReadCapturedKeyValue(reader, layout[i]);
            var fk = CreateKey(values, foreignPositions);
            var pk = CreateKey(values, primaryPositions);
            if (!groups.TryGetValue(fk, out var keys)) groups.Add(fk, keys = []);
            keys.Add(pk);
        }

        private static DataLinqKey CreateKey(object?[] values, int[] positions)
        {
            var components = new object?[positions.Length];
            for (var i = 0; i < components.Length; i++) components[i] = values[positions[i]];
            return DataLinqKey.FromOwnedValues(components);
        }

        public IReadOnlyList<(DataLinqKey fk, DataLinqKey[] pks)> Complete(CancellationToken cancellationToken)
        {
            var result = new List<(DataLinqKey, DataLinqKey[])>(groups.Count);
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add((group.Key, group.Value.ToArray()));
            }
            return result;
        }
    }
}
