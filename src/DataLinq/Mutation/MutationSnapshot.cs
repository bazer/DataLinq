using System;
using System.Collections;
using System.Collections.Generic;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Mutation;

internal readonly record struct MutationSnapshotEntry(
    ColumnDefinition Column,
    object? Value,
    int Ordinal);

/// <summary>
/// Captures one immutable, model-domain view of a mutable's assignments for the
/// complete state-change execution. Generated DataLinq mutables use dense column
/// ordinals and occupancy bits; legacy implementations retain a bounded sparse
/// fallback so invalid foreign-column assignments can still be diagnosed during
/// preflight.
/// </summary>
internal sealed class MutationSnapshot
{
    private readonly object?[]? denseValues;
    private readonly KeyValuePair<ColumnDefinition, object?>[]? sparseChanges;
    private readonly object?[]? sparseInsertValues;
    private readonly ulong occupancy;
    private readonly ulong[]? overflowOccupancy;
    private readonly ulong arrayOccupancy;
    private readonly ulong[]? overflowArrayOccupancy;

    private MutationSnapshot(
        TableDefinition table,
        object?[] denseValues,
        ulong occupancy,
        ulong[]? overflowOccupancy,
        ulong arrayOccupancy,
        ulong[]? overflowArrayOccupancy,
        int count,
        long mutationVersion)
    {
        Table = table;
        this.denseValues = denseValues;
        this.occupancy = occupancy;
        this.overflowOccupancy = overflowOccupancy;
        this.arrayOccupancy = arrayOccupancy;
        this.overflowArrayOccupancy = overflowArrayOccupancy;
        Count = count;
        MutationVersion = mutationVersion;
    }

    private MutationSnapshot(
        TableDefinition table,
        KeyValuePair<ColumnDefinition, object?>[] sparseChanges,
        object?[]? sparseInsertValues)
    {
        Table = table;
        this.sparseChanges = sparseChanges;
        this.sparseInsertValues = sparseInsertValues;
        Count = sparseChanges.Length;
    }

    internal TableDefinition Table { get; }
    internal int Count { get; }
    internal long? MutationVersion { get; }
    internal bool IsEmpty => Count == 0;

    internal static MutationSnapshot Capture(
        IModelInstance model,
        TableDefinition table,
        bool captureInsertValues)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(table);

        if (model is IMutableChangeTracking trackedMutable &&
            model.GetRowData() is MutableRowData rowData &&
            ReferenceEquals(rowData.Table, table))
        {
            return CaptureDense(
                rowData,
                table,
                trackedMutable.MutationVersion,
                captureInsertValues);
        }

        return CaptureSparse(model, table, captureInsertValues);
    }

    private static MutationSnapshot CaptureDense(
        MutableRowData rowData,
        TableDefinition table,
        long mutationVersion,
        bool captureInsertValues)
    {
        var values = new object?[table.ColumnCount];
        var occupancy = 0UL;
        ulong[]? overflowOccupancy = null;
        var arrayOccupancy = 0UL;
        ulong[]? overflowArrayOccupancy = null;
        var count = 0;

        foreach (var change in rowData.MutationValues)
        {
            var ordinal = change.Key.Index;
            var value = SnapshotValue(change.Value);
            values[ordinal] = value;
            SetBit(ref occupancy, ref overflowOccupancy, ordinal, table.ColumnCount);
            if (value is Array)
            {
                SetBit(
                    ref arrayOccupancy,
                    ref overflowArrayOccupancy,
                    ordinal,
                    table.ColumnCount);
            }

            count++;
        }

        if (captureInsertValues)
        {
            for (var ordinal = 0; ordinal < table.ColumnCount; ordinal++)
            {
                if (!IsBitSet(occupancy, overflowOccupancy, ordinal))
                    values[ordinal] = SnapshotValue(rowData.GetBorrowedValue(ordinal));
            }
        }

        return new MutationSnapshot(
            table,
            values,
            occupancy,
            overflowOccupancy,
            arrayOccupancy,
            overflowArrayOccupancy,
            count,
            mutationVersion);
    }

    private static MutationSnapshot CaptureSparse(
        IModelInstance model,
        TableDefinition table,
        bool captureInsertValues)
    {
        if (model is not IMutableInstance mutable)
            return new MutationSnapshot(table, [], captureInsertValues ? CaptureModelValues(model, table) : null);

        var captured = new List<KeyValuePair<ColumnDefinition, object?>>();
        foreach (var change in mutable.GetChanges())
        {
            captured.Add(new KeyValuePair<ColumnDefinition, object?>(
                change.Key,
                SnapshotValue(change.Value)));
        }

        return new MutationSnapshot(
            table,
            captured.ToArray(),
            captureInsertValues ? CaptureModelValues(model, table) : null);
    }

    private static object?[] CaptureModelValues(
        IModelInstance model,
        TableDefinition table)
    {
        var values = new object?[table.ColumnCount];
        for (var ordinal = 0; ordinal < table.ColumnCount; ordinal++)
            values[ordinal] = SnapshotValue(model[table.Columns[ordinal]]);

        return values;
    }

    internal bool Contains(ColumnDefinition column)
    {
        if (denseValues is not null)
        {
            var ordinal = column.Index;
            return ordinal >= 0 &&
                ordinal < Table.ColumnCount &&
                ReferenceEquals(Table.Columns[ordinal], column) &&
                IsBitSet(occupancy, overflowOccupancy, ordinal);
        }

        var sparse = sparseChanges!;
        for (var index = 0; index < sparse.Length; index++)
        {
            if (ReferenceEquals(sparse[index].Key, column))
                return true;
        }

        return false;
    }

    internal object? GetInsertModelValue(ColumnDefinition column)
    {
        if (denseValues is not null)
            return denseValues[column.Index];

        return sparseInsertValues![column.Index];
    }

    internal bool MatchesCurrent(IModelInstance model, long? expectedMutationVersion = null)
    {
        var version = expectedMutationVersion ?? MutationVersion;
        if (version is long capturedVersion)
        {
            if (model is not IMutableChangeTracking trackedMutable ||
                trackedMutable.MutationVersion != capturedVersion)
            {
                return false;
            }

            return ArraysMatchCurrent(model);
        }

        if (model is not IMutableInstance mutable)
            return true;

        var currentChanges = new List<KeyValuePair<ColumnDefinition, object?>>();
        foreach (var change in mutable.GetChanges())
            currentChanges.Add(change);

        if (currentChanges.Count != Count)
            return false;

        foreach (var captured in this)
        {
            var found = false;
            for (var currentIndex = 0; currentIndex < currentChanges.Count; currentIndex++)
            {
                var current = currentChanges[currentIndex];
                if (!ReferenceEquals(captured.Column, current.Key))
                    continue;

                if (!ValuesEqual(captured.Value, current.Value))
                    return false;

                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }

    private bool ArraysMatchCurrent(IModelInstance model)
    {
        if (arrayOccupancy == 0 && overflowArrayOccupancy is null)
            return true;

        for (var ordinal = 0; ordinal < Table.ColumnCount; ordinal++)
        {
            if (!IsBitSet(arrayOccupancy, overflowArrayOccupancy, ordinal) ||
                ValuesEqual(denseValues![ordinal], model[Table.Columns[ordinal]]))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    internal IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetDetachedChanges()
    {
        foreach (var change in this)
        {
            yield return new KeyValuePair<ColumnDefinition, object?>(
                change.Column,
                SnapshotValue(change.Value));
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly MutationSnapshot snapshot;
        private int index;

        internal Enumerator(MutationSnapshot snapshot)
        {
            this.snapshot = snapshot;
            index = -1;
            Current = default;
        }

        public MutationSnapshotEntry Current { get; private set; }

        public bool MoveNext()
        {
            if (snapshot.denseValues is not null)
            {
                while (++index < snapshot.Table.ColumnCount)
                {
                    if (!IsBitSet(snapshot.occupancy, snapshot.overflowOccupancy, index))
                        continue;

                    Current = new MutationSnapshotEntry(
                        snapshot.Table.Columns[index],
                        snapshot.denseValues[index],
                        index);
                    return true;
                }

                return false;
            }

            var sparse = snapshot.sparseChanges!;
            if (++index >= sparse.Length)
                return false;

            var change = sparse[index];
            var ordinal = change.Key is { } column &&
                column.Index >= 0 &&
                column.Index < snapshot.Table.ColumnCount &&
                ReferenceEquals(snapshot.Table.Columns[column.Index], column)
                    ? column.Index
                    : -1;
            Current = new MutationSnapshotEntry(change.Key, change.Value, ordinal);
            return true;
        }
    }

    internal static object? SnapshotValue(object? value) =>
        value is Array array
            ? array.Clone()
            : value;

    internal static bool ValuesEqual(object? captured, object? current)
    {
        if (captured is Array || current is Array)
            return StructuralComparisons.StructuralEqualityComparer.Equals(captured, current);

        return Equals(captured, current);
    }

    internal static void SetBit(
        ref ulong primary,
        ref ulong[]? overflow,
        int ordinal,
        int columnCount)
    {
        if (ordinal < 64)
        {
            primary |= 1UL << ordinal;
            return;
        }

        overflow ??= new ulong[(columnCount - 1) / 64];
        var overflowOrdinal = ordinal - 64;
        overflow[overflowOrdinal / 64] |= 1UL << (overflowOrdinal % 64);
    }

    internal static bool IsBitSet(
        ulong primary,
        ulong[]? overflow,
        int ordinal)
    {
        if (ordinal < 64)
            return (primary & (1UL << ordinal)) != 0;

        if (overflow is null)
            return false;

        var overflowOrdinal = ordinal - 64;
        return (overflow[overflowOrdinal / 64] & (1UL << (overflowOrdinal % 64))) != 0;
    }
}
