using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public interface IRowData
{
    TableDefinition Table { get; }

    object? this[ColumnDefinition column] { get; }

    object? GetValue(ColumnDefinition column);

    IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns);

    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues();

    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns);
}

public sealed class RowData : IRowData, IEquatable<RowData>
{
    private readonly object?[] data;

    public RowData(IDataLinqDataReader reader, TableDefinition table, ReadOnlySpan<ColumnDefinition> columns, bool hasIndexedColumns)
    {
        Table = table;

        // Initialize array sized to the total number of columns in the table definition
        // This allows O(1) access by Column.Index
        data = new object?[table.Columns.Length];

        // Read values based on the *requested* columns (which match the reader's ordinal order)
        // and place them into their correct slots in the dense array.
        Size = hasIndexedColumns ? ReadOrderedIndexReader(reader, columns, data) : ReadUnorderedReader(reader, columns, data);
    }

    //protected Dictionary<ColumnDefinition, object?> Data { get; }

    public TableDefinition Table { get; }

    public int Size { get; }

    public object? this[ColumnDefinition column] => GetValue(column);

    public object? GetValue(ColumnDefinition column)
    {
        // Fast array access using the pre-calculated index
        return data[column.Index];
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues()
    {
        return Table.Columns.Select(col => new KeyValuePair<ColumnDefinition, object?>(col, data[col.Index]));
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
            yield return new KeyValuePair<ColumnDefinition, object?>(column, GetValue(column));
    }

    public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
            yield return GetValue(column);
    }

    private static int ReadOrderedIndexReader(IDataLinqDataReader reader, ReadOnlySpan<ColumnDefinition> columns, object?[] data)
    {
        var size = 0;

        // Iterate using the span length. The reader ordinals 0..N match this span's order.
        for (int i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            var value = reader.GetValue<object>(column, i); // Pass 'i' directly as ordinal!
            size += GetSize(column, value); // Keep existing size calc logic
            data[column.Index] = value;
        }
        return size;
    }

    private static int ReadUnorderedReader(IDataLinqDataReader reader, ReadOnlySpan<ColumnDefinition> columns, object?[] data)
    {
        var size = 0;

        foreach (var column in columns)
        {
            var value = reader.GetValue<object>(column);
            size += GetSize(column, value);

            data[column.Index] = value;
        }
        return size;
    }

    

    private static int GetSize(ColumnDefinition column, object? value)
    {
        if (value == null)
            return 0;

        if (column.ValueProperty.CsSize.HasValue)
            return column.ValueProperty.CsSize.Value;

        if (column.ValueProperty.CsType.Type == typeof(string) && value is string s)
            return s.Length * sizeof(char) + sizeof(int);

        if (column.ValueProperty.CsType.Type == typeof(byte[]) && value is byte[] b)
            return b.Length;

        throw new NotImplementedException($"Size for type '{column.ValueProperty.CsType}' not implemented");
    }

    public bool Equals(RowData? other)
    {
        if (other == null) return false;
        if (data.Length != other.data.Length) return false;

        for (int i = 0; i < data.Length; i++)
        {
            if (!object.Equals(data[i], other.data[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is RowData other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        // Hash based on content
        foreach (var item in data)
            hash.Add(item);

        return hash.ToHashCode();
    }
}