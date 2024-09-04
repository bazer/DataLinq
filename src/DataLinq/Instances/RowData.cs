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

    T? GetValue<T>(ColumnDefinition column);

    IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns);

    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues();

    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns);
}

public class RowData : IRowData, IEquatable<RowData>
{
    public RowData(IDataLinqDataReader reader, TableDefinition table, ReadOnlySpan<ColumnDefinition> columns)
    {
        Table = table;
        (Data, Size) = ReadReader(reader, columns);
    }

    protected Dictionary<ColumnDefinition, object?> Data { get; }

    public TableDefinition Table { get; }

    public int Size { get; }

    public object? this[ColumnDefinition column] => GetValue(column);

    public object? GetValue(ColumnDefinition column)
    {
        if (Data == null || !Data.TryGetValue(column, out var value))
            throw new InvalidOperationException($"Data dictionary is not initialized or column '{column.DbName}' key does not exist.");

        return value;
    }

    public T? GetValue<T>(ColumnDefinition column)
    {
        if (Data == null || !Data.TryGetValue(column, out var value))
            throw new InvalidOperationException($"Data dictionary is not initialized or column '{column.DbName}' key does not exist.");

        return value == null
            ? default
            : (T?)value;
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues()
    {
        return Data.AsEnumerable();
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

    private static (Dictionary<ColumnDefinition, object?> data, int size) ReadReader(IDataLinqDataReader reader, ReadOnlySpan<ColumnDefinition> columns)
    {
        var data = new Dictionary<ColumnDefinition, object?>();
        var size = 0;

        foreach (var column in columns)
        {
            var value = reader.GetValue<object>(column);
            size += GetSize(column, value);

            data.Add(column, value);
        }

        return (data, size);
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