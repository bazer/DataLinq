using System;
using DataLinq.Cache;

namespace DataLinq.Instances;

public delegate bool ProviderKeyFromLegacyKey<TKey>(IKey legacyKey, out TKey providerKey)
    where TKey : notnull;

public interface IProviderKey
{
    int ValueCount { get; }
    object? GetValue(int index);
}

public interface IProviderKeyRowStoreAccessor
{
    bool TryAddRow(RowCache cache, RowData rowData, IImmutableInstance row);
}
