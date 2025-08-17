using System;
using System.Linq;

namespace DataLinq.Instances;

public interface IKey
{
    public object?[] Values { get; }
}

public readonly record struct NullKey() : IKey, IEquatable<NullKey>
{
    public object?[] Values => [null];

    public bool Equals(NullKey other) =>
        true;

    public override int GetHashCode() =>
        0571049712;
}

public readonly record struct ObjectKey(object Value) : IKey, IEquatable<ObjectKey>
{
    public object?[] Values => [Value];

    public bool Equals(ObjectKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct UIntKey(uint Value) : IKey, IEquatable<UIntKey>
{
    public object?[] Values => [Value];

    public bool Equals(UIntKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct IntKey(int Value) : IKey, IEquatable<IntKey>
{
    public object?[] Values => [Value];

    public bool Equals(IntKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct ULongKey(ulong Value) : IKey, IEquatable<ULongKey>
{
    public object?[] Values => [Value];

    public bool Equals(ULongKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct LongKey(long Value) : IKey, IEquatable<LongKey>
{
    public object?[] Values => [Value];

    public bool Equals(LongKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct GuidKey(Guid Value) : IKey, IEquatable<GuidKey>
{
    public object?[] Values => [Value];

    public bool Equals(GuidKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct BytesKey(byte[] Value) : IKey, IEquatable<BytesKey>
{
    public object?[] Values => [Value];

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
    public object?[] Values => [Value];

    public bool Equals(StringKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

// For short / Int16
public readonly record struct ShortKey(short Value) : IKey, IEquatable<ShortKey>
{
    public object?[] Values => [Value];
    public bool Equals(ShortKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For ushort / UInt16
public readonly record struct UShortKey(ushort Value) : IKey, IEquatable<UShortKey>
{
    public object?[] Values => [Value];
    public bool Equals(UShortKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For byte / Byte
public readonly record struct ByteKey(byte Value) : IKey, IEquatable<ByteKey>
{
    public object?[] Values => [Value];
    public bool Equals(ByteKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For sbyte / SByte
public readonly record struct SByteKey(sbyte Value) : IKey, IEquatable<SByteKey>
{
    public object?[] Values => [Value];
    public bool Equals(SByteKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For DateTime
public readonly record struct DateTimeKey(DateTime Value) : IKey, IEquatable<DateTimeKey>
{
    public object?[] Values => [Value];
    public bool Equals(DateTimeKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For DateOnly
public readonly record struct DateOnlyKey(DateOnly Value) : IKey, IEquatable<DateOnlyKey>
{
    public object?[] Values => [Value];
    public bool Equals(DateOnlyKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For TimeOnly
public readonly record struct TimeOnlyKey(TimeOnly Value) : IKey, IEquatable<TimeOnlyKey>
{
    public object?[] Values => [Value];
    public bool Equals(TimeOnlyKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For bool / Boolean
public readonly record struct BoolKey(bool Value) : IKey, IEquatable<BoolKey>
{
    public object?[] Values => [Value];
    public bool Equals(BoolKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

// For decimal
public readonly record struct DecimalKey(decimal Value) : IKey, IEquatable<DecimalKey>
{
    public object?[] Values => [Value];
    public bool Equals(DecimalKey other) => Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly record struct CompositeKey : IKey, IEquatable<CompositeKey>
{
    public readonly int cachedHashCode;
    private readonly IKey[] keys;

    // The public 'Values' property still returns object?[] for API consistency.
    public object?[] Values => keys.Select(k => k.Values.FirstOrDefault()).ToArray();

    public CompositeKey(IKey[] keys)
    {
        this.keys = keys;
        this.cachedHashCode = ComputeHashCode(keys);
    }

    // The new Equals method is now incredibly simple and robust.
    public bool Equals(CompositeKey other)
    {
        // It relies on the correct Equals implementation of each inner IKey.
        return keys.SequenceEqual(other.keys);
    }

    public override int GetHashCode() =>
        cachedHashCode;

    // The new ComputeHashCode method is also much cleaner.
    public static int ComputeHashCode(IKey[] keys)
    {
        var hash = new HashCode();
        foreach (var key in keys)
            hash.Add(key); // Relies on the correct GetHashCode of each inner IKey.

        return hash.ToHashCode();
    }
}