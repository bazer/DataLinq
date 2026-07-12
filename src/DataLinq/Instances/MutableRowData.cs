using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class MutableRowData : IRowData
{
    private readonly object? mutationOwner;
    private long mutationVersion;
    internal long MutationVersion => Volatile.Read(ref mutationVersion);
    IRowData? ImmutableRowData { get; set; }
    Dictionary<ColumnDefinition, object?> MutatedData { get; } = new Dictionary<ColumnDefinition, object?>();
    public TableDefinition Table { get; }
    public bool HasChanges() => MutatedData.Count > 0;

    public object? this[ColumnDefinition column] => GetValue(column);
    public object? this[int columnIndex] => GetValue(columnIndex);

    public MutableRowData(TableDefinition table)
    {
        this.Table = table;
    }

    internal MutableRowData(TableDefinition table, object mutationOwner)
        : this(table)
    {
        this.mutationOwner = mutationOwner ?? throw new ArgumentNullException(nameof(mutationOwner));
    }

    public MutableRowData(IRowData immutableRowData)
    {
        this.ImmutableRowData = immutableRowData;
        this.Table = immutableRowData.Table;
    }

    internal MutableRowData(IRowData immutableRowData, object mutationOwner)
        : this(immutableRowData)
    {
        this.mutationOwner = mutationOwner ?? throw new ArgumentNullException(nameof(mutationOwner));
    }

    public void Reset()
    {
        ThrowIfOwnerControlled();
        ResetCore();
    }

    internal void Reset(object mutationOwner)
    {
        ValidateMutationOwner(mutationOwner);
        ResetCore();
    }

    private void ResetCore()
    {
        MutatedData.Clear();
        Interlocked.Increment(ref mutationVersion);
    }

    public void Reset(IRowData immutableRowData)
    {
        ThrowIfOwnerControlled();
        ResetCore(immutableRowData);
    }

    internal void Reset(IRowData immutableRowData, object mutationOwner)
    {
        ValidateMutationOwner(mutationOwner);
        ResetCore(immutableRowData);
    }

    private void ResetCore(IRowData immutableRowData)
    {
        ArgumentNullException.ThrowIfNull(immutableRowData);

        if (immutableRowData.Table != Table)
            throw new InvalidOperationException("Cannot reset row data with different table definition.");

        this.ImmutableRowData = immutableRowData;
        MutatedData.Clear();
        Interlocked.Increment(ref mutationVersion);
    }

    public object? GetValue(ColumnDefinition column)
    {
        ValidateMappedColumn(column);

        if (MutatedData.TryGetValue(column, out var value))
            return value;

        return ImmutableRowData?.GetValue(column);
    }

    public object? GetValue(int columnIndex) => GetValue(Table.Columns[columnIndex]);

    internal bool TryGetOriginalValue(ColumnDefinition column, out object? value)
    {
        if (ImmutableRowData is null)
        {
            value = null;
            return false;
        }

        value = ImmutableRowData.GetValue(column);
        return true;
    }

    public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
            yield return GetValue(column);
    }

    public void SetValue(ColumnDefinition column, object? value)
    {
        ThrowIfOwnerControlled();
        SetValueCore(column, value);
    }

    internal void SetValue(ColumnDefinition column, object? value, object mutationOwner)
    {
        ValidateMutationOwner(mutationOwner);
        SetValueCore(column, value);
    }

    private void SetValueCore(ColumnDefinition column, object? value)
    {
        ValidateMappedColumn(column);

        if (value == null)
            MutatedData[column] = value;
        else if (column.HasScalarConverter)
        {
            CanonicalProviderValueRow.ValidateModelValue(
                column,
                value,
                nameof(value));
            MutatedData[column] = value;
        }
        else if (column.ValueProperty.CsType.Type == null ||
                 value.GetType() == column.ValueProperty.CsType.Type)
            MutatedData[column] = value;
        else
            MutatedData[column] = Convert.ChangeType(value, column.ValueProperty.CsType.Type);

        Interlocked.Increment(ref mutationVersion);
    }

    private void ThrowIfOwnerControlled()
    {
        if (mutationOwner is not null)
        {
            throw new InvalidOperationException(
                "Direct mutation of row data owned by a mutable model is not supported. " +
                "Use the mutable model's property/indexer APIs or Reset() method instead.");
        }
    }

    private void ValidateMutationOwner(object candidate)
    {
        if (mutationOwner is null || !ReferenceEquals(mutationOwner, candidate))
            throw new InvalidOperationException("The mutable row-data mutation owner is invalid.");
    }

    private void ValidateMappedColumn(ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (column.Index < 0 ||
            column.Index >= Table.ColumnCount ||
            !ReferenceEquals(Table.Columns[column.Index], column))
        {
            throw new ArgumentException(
                "The column must be the exact mapped column definition for this row.",
                nameof(column));
        }
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues()
    {
        if (ImmutableRowData == null)
            return MutatedData.AsEnumerable();

        return GetColumnAndValues(ImmutableRowData.GetColumnAndValues().Select(x => x.Key));
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
            yield return new KeyValuePair<ColumnDefinition, object?>(column, GetValue(column));
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges()
    {
        foreach (var change in MutatedData)
            yield return change;
    }

}
