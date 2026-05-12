using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Instances;

public interface IKey
{
    /// <summary>
    /// Compatibility value surface. Prefer <see cref="ValueCount"/>, <see cref="GetValue"/>,
    /// and <see cref="TryGetSingleValue"/> for key inspection on runtime paths.
    /// </summary>
    public KeyValues Values { get; }
    public int ValueCount => Values.Count;
    public object? GetValue(int index) => Values[index];

    public bool TryGetSingleValue(out object? value)
    {
        if (ValueCount != 1)
        {
            value = null;
            return false;
        }

        value = GetValue(0);
        return true;
    }
}

internal static class SingleKeyValue
{
    public const int Count = 1;

    public static object? Get(object? value, int index)
    {
        if (index == 0)
            return value;

        throw new IndexOutOfRangeException();
    }

    public static bool TryGet(object? source, out object? value)
    {
        value = source;
        return true;
    }
}

public readonly struct KeyValues : IReadOnlyList<object?>
{
    private readonly object? singleValue;
    private readonly object?[]? values;

    private KeyValues(object? singleValue)
    {
        this.singleValue = singleValue;
        values = null;
    }

    private KeyValues(object?[] values)
    {
        singleValue = null;
        this.values = values;
    }

    public static KeyValues Single(object? value) => new(value);
    internal static KeyValues Many(object?[] values) => new(values);

    public int Count => values?.Length ?? 1;

    public object? this[int index]
    {
        get
        {
            if (values is not null)
                return values[index];

            if (index == 0)
                return singleValue;

            throw new IndexOutOfRangeException();
        }
    }

    public object?[] ToArray() =>
        values is null ? [singleValue] : values.ToArray();

    public IEnumerator<object?> GetEnumerator()
    {
        if (values is not null)
            return ((IEnumerable<object?>)values).GetEnumerator();

        return EnumerateSingle(singleValue).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static IEnumerable<object?> EnumerateSingle(object? value)
    {
        yield return value;
    }
}

public readonly record struct NullKey() : IKey, IEquatable<NullKey>
{
    public KeyValues Values => KeyValues.Single(null);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(null, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(null, out value);

    public bool Equals(NullKey other) =>
        true;

    public override int GetHashCode() =>
        0571049712;
}

public readonly record struct ObjectKey(object Value) : IKey, IEquatable<ObjectKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(ObjectKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct UIntKey(uint Value) : IKey, IEquatable<UIntKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(UIntKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct IntKey(int Value) : IKey, IEquatable<IntKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(IntKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct ULongKey(ulong Value) : IKey, IEquatable<ULongKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(ULongKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct LongKey(long Value) : IKey, IEquatable<LongKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(LongKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct GuidKey(Guid Value) : IKey, IEquatable<GuidKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(GuidKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct BytesKey(byte[] Value) : IKey, IEquatable<BytesKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(BytesKey other) =>
        Value.SequenceEqual(other.Value);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (byte b in Value)
            hash.Add(b);
        return hash.ToHashCode();
    }
}

public readonly record struct StringKey(string Value) : IKey, IEquatable<StringKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);

    public bool Equals(StringKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct ShortKey(short Value) : IKey, IEquatable<ShortKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(ShortKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct UShortKey(ushort Value) : IKey, IEquatable<UShortKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(UShortKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct ByteKey(byte Value) : IKey, IEquatable<ByteKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(ByteKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct SByteKey(sbyte Value) : IKey, IEquatable<SByteKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(SByteKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct DateTimeKey(DateTime Value) : IKey, IEquatable<DateTimeKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(DateTimeKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct DateOnlyKey(DateOnly Value) : IKey, IEquatable<DateOnlyKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(DateOnlyKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct TimeOnlyKey(TimeOnly Value) : IKey, IEquatable<TimeOnlyKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(TimeOnlyKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct BoolKey(bool Value) : IKey, IEquatable<BoolKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(BoolKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct DecimalKey(decimal Value) : IKey, IEquatable<DecimalKey>
{
    public KeyValues Values => KeyValues.Single(Value);
    public int ValueCount => SingleKeyValue.Count;
    public object? GetValue(int index) => SingleKeyValue.Get(Value, index);
    public bool TryGetSingleValue(out object? value) => SingleKeyValue.TryGet(Value, out value);
    public bool Equals(DecimalKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct CompositeKey : IKey, IEquatable<CompositeKey>
{
    public readonly int cachedHashCode;
    private readonly object?[] values;

    public KeyValues Values => KeyValues.Many(values);
    public int ValueCount => values.Length;
    public object? GetValue(int index) => values[index];

    public bool TryGetSingleValue(out object? value)
    {
        if (values.Length != 1)
        {
            value = null;
            return false;
        }

        value = values[0];
        return true;
    }

    public CompositeKey(object?[] values)
    {
        this.values = values;
        cachedHashCode = ComputeHashCode(values);
    }

    public bool Equals(CompositeKey other)
    {
        if (values.Length != other.values.Length)
            return false;

        for (var i = 0; i < values.Length; i++)
        {
            if (!KeyValueEquals(values[i], other.values[i]))
                return false;
        }

        return true;
    }

    public override int GetHashCode() =>
        cachedHashCode;

    public static int ComputeHashCode(object?[] values)
    {
        var hash = new HashCode();

        foreach (var value in values)
            AddKeyValueHashCode(ref hash, value);

        return hash.ToHashCode();
    }

    private static bool KeyValueEquals(object? left, object? right)
    {
        if (left is byte[] leftBytes && right is byte[] rightBytes)
            return leftBytes.SequenceEqual(rightBytes);

        return Equals(left, right);
    }

    private static void AddKeyValueHashCode(ref HashCode hash, object? value)
    {
        if (value is byte[] bytes)
        {
            foreach (var b in bytes)
                hash.Add(b);

            return;
        }

        hash.Add(value);
    }
}
