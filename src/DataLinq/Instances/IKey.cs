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

public readonly record struct IntKey(int Value) : IKey, IEquatable<IntKey>
{
    public object?[] Values => [Value];

    public bool Equals(IntKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct UInt64Key(ulong Value) : IKey, IEquatable<UInt64Key>
{
    public object?[] Values => [Value];

    public bool Equals(UInt64Key other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct Int64Key(long Value) : IKey, IEquatable<Int64Key>
{
    public object?[] Values => [Value];

    public bool Equals(Int64Key other) =>
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

public readonly record struct StringKey(string Value) : IKey, IEquatable<StringKey>
{
    public object?[] Values => [Value];

    public bool Equals(StringKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct CompositeKey : IKey, IEquatable<CompositeKey>
{
    public readonly int cachedHashCode;
    public object?[] Values => values;
    private readonly object?[] values;

    public CompositeKey(object?[] values)
    {
        this.values = values;
        this.cachedHashCode = ComputeHashCode(values);
    }

    public bool Equals(CompositeKey other) =>
        values.SequenceEqual(other.values);

    public override int GetHashCode() =>
        cachedHashCode;

    public static int ComputeHashCode(object?[] values)
    {
        var hash = new HashCode();

        foreach (var val in values.Where(x => x != null))
            hash.Add(val);
        
        return hash.ToHashCode();
    }
}