﻿using System.Collections.Generic;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class MutableRowData
{
    RowData ImmutableRowData { get; }
    Dictionary<Column, object?> MutatedData { get; } = new Dictionary<Column, object?>();

    public MutableRowData(RowData immutableRowData)
    {
        this.ImmutableRowData = immutableRowData;
    }

    public IKey GetKey() =>
        KeyFactory.GetKey(this.ImmutableRowData, this.ImmutableRowData.Table.PrimaryKeyColumns);

    public object? GetValue(Column column)
    {
        if (MutatedData.ContainsKey(column))
            return MutatedData[column];

        return ImmutableRowData.GetValue(column);
    }

    public void SetValue(Column column, object? value)
    {
        MutatedData[column] = value;
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetValues()
    {
        return ImmutableRowData.GetColumnAndValues();
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return new KeyValuePair<Column, object?>(column, GetValue(column));
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetChanges()
    {
        foreach (var change in MutatedData)
            yield return change;
    }
}
