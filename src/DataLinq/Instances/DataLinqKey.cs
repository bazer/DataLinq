using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Instances;

public readonly struct DataLinqKey : IEquatable<DataLinqKey>, IProviderKey
{
    private readonly object? singleValue;
    private readonly object?[]? values;
    private readonly int valueCount;
    private readonly int cachedHashCode;

    private DataLinqKey(object? value)
    {
        singleValue = NormalizeKeyValue(value);
        values = null;
        valueCount = 1;
        cachedHashCode = ComputeHashCode(singleValue);
    }

    private DataLinqKey(object?[] values)
    {
        singleValue = null;
        this.values = values;
        valueCount = values.Length;
        cachedHashCode = ComputeHashCode(values);
    }

    public static DataLinqKey Null { get; } = new((object?)null);

    public int ValueCount => valueCount == 0 ? 1 : valueCount;

    public bool IsNull => ValueCount == 1 && GetValue(0) is null;

    public object? GetValue(int index)
    {
        if (values is not null)
            return values[index];

        if (index == 0)
            return singleValue;

        throw new IndexOutOfRangeException();
    }

    public static DataLinqKey FromValue<T>(T? value)
    {
        if (value is RowData)
            throw new Exception("Cannot create a primary key from a RowData object. Use FromValues(...) with row values instead.");

        return new DataLinqKey(value);
    }

    public static DataLinqKey FromValues(IEnumerable<object?> values)
    {
        if (values is IReadOnlyList<object?> list)
            return FromList(list);

        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
            return Null;

        var firstValue = enumerator.Current;
        if (!enumerator.MoveNext())
            return FromValue(firstValue);

        var array = new object?[] { NormalizeKeyValue(firstValue), NormalizeKeyValue(enumerator.Current) };
        var count = 2;

        while (enumerator.MoveNext())
        {
            if (count == array.Length)
                Array.Resize(ref array, array.Length * 2);

            array[count++] = NormalizeKeyValue(enumerator.Current);
        }

        if (count != array.Length)
            Array.Resize(ref array, count);

        return FromNormalizedValues(array);
    }

    public static DataLinqKey FromProviderKey(IProviderKey key)
    {
        if (key.ValueCount == 1)
            return FromValue(key.GetValue(0));

        var values = new object?[key.ValueCount];
        for (var i = 0; i < values.Length; i++)
            values[i] = NormalizeKeyValue(key.GetValue(i));

        return FromNormalizedValues(values);
    }

    public bool Equals(DataLinqKey other)
    {
        if (IsNull && other.IsNull)
            return true;

        if (ValueCount != other.ValueCount)
            return false;

        for (var i = 0; i < ValueCount; i++)
        {
            if (!KeyValueEquals(GetValue(i), other.GetValue(i)))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is DataLinqKey other && Equals(other);

    public override int GetHashCode() =>
        IsNull ? 0571049712 : cachedHashCode;

    public override string ToString()
    {
        if (ValueCount == 1)
            return GetValue(0)?.ToString() ?? "<null>";

        var valueStrings = new string[ValueCount];
        for (var i = 0; i < valueStrings.Length; i++)
            valueStrings[i] = GetValue(i)?.ToString() ?? "<null>";

        return $"({string.Join(", ", valueStrings)})";
    }

    public static bool operator ==(DataLinqKey left, DataLinqKey right) => left.Equals(right);
    public static bool operator !=(DataLinqKey left, DataLinqKey right) => !left.Equals(right);

    private static DataLinqKey FromList(IReadOnlyList<object?> values)
    {
        if (values.Count == 0)
            return Null;

        if (values.Count == 1)
            return FromValue(values[0]);

        var array = new object?[values.Count];
        for (var i = 0; i < values.Count; i++)
            array[i] = NormalizeKeyValue(values[i]);

        return FromNormalizedValues(array);
    }

    private static DataLinqKey FromNormalizedValues(object?[] values)
    {
        if (AllValuesAreNull(values))
            return Null;

        return new DataLinqKey(values);
    }

    private static bool AllValuesAreNull(object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is not null)
                return false;
        }

        return true;
    }

    private static object? NormalizeKeyValue(object? value)
    {
        if (value is Enum enumValue)
            return Convert.ChangeType(enumValue, enumValue.GetTypeCode());

        return value;
    }

    private static bool KeyValueEquals(object? left, object? right)
    {
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.SequenceEqual(rightBytes);

        return Equals(left, right);
    }

    private static int ComputeHashCode(object? value)
    {
        if (value is byte[] bytes)
            return ComputeBytesHashCode(bytes);

        return value?.GetHashCode() ?? 0571049712;
    }

    private static int ComputeHashCode(IEnumerable<object?> values)
    {
        var hash = new HashCode();

        foreach (var value in values)
            AddKeyValueHashCode(ref hash, value);

        return hash.ToHashCode();
    }

    private static void AddKeyValueHashCode(ref HashCode hash, object? value)
    {
        if (value is byte[] bytes)
        {
            AddBytesHashCode(ref hash, bytes);

            return;
        }

        hash.Add(value);
    }

    private static int ComputeBytesHashCode(byte[] bytes)
    {
        var hash = new HashCode();
        AddBytesHashCode(ref hash, bytes);
        return hash.ToHashCode();
    }

    private static void AddBytesHashCode(ref HashCode hash, byte[] bytes)
    {
        foreach (var b in bytes)
            hash.Add(b);
    }
}
