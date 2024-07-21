using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class RowData
{
    public RowData(IDataLinqDataReader reader, TableMetadata table, ReadOnlySpan<Column> columns)
    {
        Table = table;
        (Data, Size) = ReadReader(reader, columns);
    }

    protected Dictionary<Column, object?> Data { get; }

    public TableMetadata Table { get; }

    public int Size { get; }

    public object? this[Column column] => Data[column];

    public object? GetValue(Column column)
    {
        return Data[column];
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues()
    {
        return Data.AsEnumerable();
    }

    public IEnumerable<object?> GetValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return Data[column];
    }

    private static (Dictionary<Column, object?> data, int size) ReadReader(IDataLinqDataReader reader, ReadOnlySpan<Column> columns)
    {
        var data = new Dictionary<Column, object?>();
        var size = 0;

        foreach (var column in columns)
        {
            var value = reader.GetValue<object>(column);
            size += GetSize(column, value);

            data.Add(column, value);
        }

        return (data, size);
    }

    private static int GetSize(Column column, object? value)
    {
        if (value == null)
            return 0;

        if (column.ValueProperty.CsSize.HasValue)
            return column.ValueProperty.CsSize.Value;

        if (column.ValueProperty.CsType == typeof(string) && value is string s)
            return s.Length * sizeof(char) + sizeof(int);

        if (column.ValueProperty.CsType == typeof(byte[]) && value is byte[] b)
            return b.Length;

        throw new NotImplementedException($"Size for type '{column.ValueProperty.CsType}' not implemented");
    }
}