using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class MutableRowData : IRowData
{
    RowData? ImmutableRowData { get; }
    Dictionary<ColumnDefinition, object?> MutatedData { get; } = new Dictionary<ColumnDefinition, object?>();

    public TableDefinition Table { get; }

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

    public T? GetValue<T>(ColumnDefinition column)
    {
        var val = GetValue(column);

        if (val is null)
            return default;

        return (T?)val;
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
        if (value == null || value.GetType() == column.ValueProperty.CsType)
            MutatedData[column] = value;
        else
            MutatedData[column] = Convert.ChangeType(value, column.ValueProperty.CsType);
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
