using System;
using DataLinq.Cache;

namespace DataLinq.Instances;

public interface IProviderKey
{
    int ValueCount { get; }
    object? GetValue(int index);
}

public interface IProviderKeyRowStoreAccessor
{
    bool TryAddRow(RowCache cache, RowData rowData, IImmutableInstance row);
    bool TryGetRow(RowCache cache, DataLinqKey key, out IImmutableInstance? row);
    bool TryRemoveRow(RowCache cache, DataLinqKey key, out int numRowsRemoved);
    bool TryCreateKey(IRowData rowData, out DataLinqKey key);
    bool TryCreateKey(IModelInstance model, out DataLinqKey key);
}
