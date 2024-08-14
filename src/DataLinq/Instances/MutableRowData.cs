using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class MutableRowData : IRowData
{
    RowData? ImmutableRowData { get; }
    Dictionary<Column, object?> MutatedData { get; } = new Dictionary<Column, object?>();

    public TableMetadata Table { get; }

    public object? this[Column column] => GetValue(column);

    public MutableRowData(TableMetadata table)
    {
        this.Table = table;
    }

    public MutableRowData(RowData immutableRowData)
    {
        this.ImmutableRowData = immutableRowData;
        this.Table = immutableRowData.Table;
    }

    public T? GetValue<T>(Column column)
    {
        return (T?)GetValue(column);
    }

    public object? GetValue(Column column)
    {
        if (MutatedData.ContainsKey(column))
            return MutatedData[column];

        return ImmutableRowData?.GetValue(column);
    }

    public IEnumerable<object?> GetValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return GetValue(column);
    }

    public void SetValue(Column column, object? value)
    {
        if (value == null || value.GetType() == column.ValueProperty.CsType)
            MutatedData[column] = value;
        else
            MutatedData[column] = Convert.ChangeType(value, column.ValueProperty.CsType);
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues()
    {
        if (ImmutableRowData == null)
            return MutatedData.AsEnumerable();

        return GetColumnAndValues(ImmutableRowData.GetColumnAndValues().Select(x => x.Key));
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return new KeyValuePair<Column, object?>(column, GetValue(column));
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetChanges()
    {
        foreach (var change in MutatedData)
            yield return change;
    }

    IEnumerable<object?> IRowData.GetValues(IEnumerable<Column> columns)
    {
        throw new System.NotImplementedException();
    }
}
