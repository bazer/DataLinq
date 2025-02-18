using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class MutableRowData : IRowData
{
    RowData? ImmutableRowData { get; set; }
    Dictionary<ColumnDefinition, object?> MutatedData { get; } = new Dictionary<ColumnDefinition, object?>();
    public TableDefinition Table { get; }
    public bool HasChanges() => MutatedData.Count > 0;

    public object? this[ColumnDefinition column] => GetValue(column);

    public MutableRowData(TableDefinition table)
    {
        this.Table = table;
    }

    public MutableRowData(RowData immutableRowData)
    {
        this.ImmutableRowData = immutableRowData;
        this.Table = immutableRowData.Table;
    }

    public void Reset()
    {
        MutatedData.Clear();
    }

    public void Reset(RowData immutableRowData)
    {
        if (immutableRowData.Table != Table)
            throw new InvalidOperationException("Cannot reset row data with different table definition.");

        this.ImmutableRowData = immutableRowData;
        MutatedData.Clear();
    }

    public object? GetValue(ColumnDefinition column)
    {
        if (MutatedData.TryGetValue(column, out var value))
            return value;

        return ImmutableRowData?.GetValue(column);
    }

    public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
            yield return GetValue(column);
    }

    public void SetValue(ColumnDefinition column, object? value)
    {
        if (value == null || column.ValueProperty.CsType.Type == null || value.GetType() == column.ValueProperty.CsType.Type)
            MutatedData[column] = value;
        else
            MutatedData[column] = Convert.ChangeType(value, column.ValueProperty.CsType.Type);
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

    IEnumerable<object?> IRowData.GetValues(IEnumerable<ColumnDefinition> columns)
    {
        throw new System.NotImplementedException();
    }
}
