using System;
using System.Collections.Generic;

namespace DataLinq.Instances;

/// <summary>
/// Represents provider primary-key components for dynamic cache invalidation calls.
/// </summary>
public readonly struct DataLinqKeyComponents : IEquatable<DataLinqKeyComponents>
{
    private readonly DataLinqKey key;

    private DataLinqKeyComponents(DataLinqKey key)
    {
        this.key = key;
    }

    public int Count => key.ValueCount;

    public bool IsNull => key.IsNull;

    public object? this[int index] => key.GetValue(index);

    public static DataLinqKeyComponents FromValue<T>(T? value) =>
        new(DataLinqKey.FromValue(value));

    public static DataLinqKeyComponents FromValues(params object?[] values) =>
        new(DataLinqKey.FromValues(values));

    public static DataLinqKeyComponents FromValues(IEnumerable<object?> values) =>
        new(DataLinqKey.FromValues(values));

    public static DataLinqKeyComponents FromProviderKey(IProviderKey providerKey)
    {
        if (providerKey is null)
            throw new ArgumentNullException(nameof(providerKey));

        return new DataLinqKeyComponents(DataLinqKey.FromProviderKey(providerKey));
    }

    internal DataLinqKey ToDataLinqKey() => key;

    public bool Equals(DataLinqKeyComponents other) => key.Equals(other.key);

    public override bool Equals(object? obj) =>
        obj is DataLinqKeyComponents other && Equals(other);

    public override int GetHashCode() => key.GetHashCode();

    public override string ToString() => key.ToString();

    public static bool operator ==(DataLinqKeyComponents left, DataLinqKeyComponents right) => left.Equals(right);
    public static bool operator !=(DataLinqKeyComponents left, DataLinqKeyComponents right) => !left.Equals(right);
}
