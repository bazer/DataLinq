using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public interface IRowData
{
    TableMetadata Table { get; }

    object? this[Column column] { get; }

    object? GetValue(Column column);

    T? GetValue<T>(Column column);

    IEnumerable<object?> GetValues(IEnumerable<Column> columns);

    IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues();

    IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues(IEnumerable<Column> columns);
}

public class RowData : IRowData, IEquatable<RowData>
{
    public RowData(IDataLinqDataReader reader, TableMetadata table, ReadOnlySpan<Column> columns)
    {
        Table = table;
        (Data, Size) = ReadReader(reader, columns);
    }

    protected Dictionary<Column, object?> Data { get; }

    public TableMetadata Table { get; }

    public int Size { get; }

    public object? this[Column column] => GetValue(column);

    public object? GetValue(Column column)
    {
        if (Data == null || !Data.TryGetValue(column, out var value))
            throw new InvalidOperationException($"Data dictionary is not initialized or column '{column.DbName}' key does not exist.");

        return value;
    }

    public T? GetValue<T>(Column column)
    {
        if (Data == null || !Data.TryGetValue(column, out var value))
            throw new InvalidOperationException($"Data dictionary is not initialized or column '{column.DbName}' key does not exist.");

        return value == null
            ? default
            : (T?)value;
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues()
    {
        return Data.AsEnumerable();
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return new KeyValuePair<Column, object?>(column, GetValue(column));
    }

    public IEnumerable<object?> GetValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return GetValue(column);
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

    public bool Equals(RowData? other)
    {
        if (Data.Count != other?.Data.Count)
            return false;

        foreach (var kvp in Data)
        {
            if (!other.Data.TryGetValue(kvp.Key, out var value) && value != kvp.Value)
                return false;
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

        foreach (var kvp in Data.OrderBy(kvp => kvp.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }

        return hash.ToHashCode();
    }
}