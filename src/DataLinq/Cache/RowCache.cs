using System;
using System.Collections.Generic;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Utils;

namespace DataLinq.Cache;

public class RowCache
{
    private IRowStore? rowStore;

    public IEnumerable<IImmutableInstance> Rows => rowStore?.Rows ?? [];
    public int Count => rowStore?.Count ?? 0;

    public long? OldestTick => rowStore?.OldestTick;
    public long? NewestTick => rowStore?.NewestTick;
    /// <summary>Estimated bytes for row values only, excluding cache container overhead.</summary>
    public long RowPayloadBytes => rowStore?.RowPayloadBytes ?? 0;
    public string RowPayloadBytesFormatted => RowPayloadBytes.ToFileSize();
    /// <summary>Compatibility alias for <see cref="RowPayloadBytes"/>.</summary>
    public long TotalBytes => RowPayloadBytes;
    public string TotalBytesFormatted => RowPayloadBytesFormatted;

    public void ClearRows() => rowStore?.Clear();

    public int RemoveRowsOverRowLimit(int maxRows) =>
        rowStore?.RemoveRowsOverRowLimit(maxRows) ?? 0;

    public int RemoveRowsOverSizeLimit(long maxSize) =>
        rowStore?.RemoveRowsOverSizeLimit(maxSize) ?? 0;

    public int RemoveRowsInsertedBeforeTick(long tick) =>
        rowStore?.RemoveRowsInsertedBeforeTick(tick) ?? 0;

    internal bool TryGetValue(DataLinqKey primaryKey, out IImmutableInstance? row)
    {
        if (rowStore is not null)
            return rowStore.TryGetKey(primaryKey, out row);

        row = null;
        return false;
    }

    public bool TryGetValue<TKey>(TKey primaryKey, out IImmutableInstance? row)
        where TKey : notnull
    {
        if (primaryKey is null)
        {
            row = null;
            return false;
        }

        if (rowStore is IRowStore<TKey> typedStore)
            return typedStore.TryGet(primaryKey, out row);

        if (rowStore is IRowStore<DataLinqKey> dynamicStore)
            return dynamicStore.TryGet(ProviderKeyComponents.ToDataLinqKey(primaryKey), out row);

        row = null;
        return false;
    }

    internal bool TryRemoveRow(DataLinqKey primaryKey, out int numRowsRemoved)
    {
        if (rowStore is not null)
            return rowStore.TryRemoveKey(primaryKey, out numRowsRemoved);

        numRowsRemoved = 0;
        return true;
    }

    public bool TryRemoveProviderKey<TKey>(TKey primaryKey, out int numRowsRemoved)
        where TKey : notnull
    {
        if (primaryKey is null)
        {
            numRowsRemoved = 0;
            return true;
        }

        if (rowStore is IRowStore<TKey> typedStore)
            return typedStore.TryRemove(primaryKey, out numRowsRemoved);

        if (rowStore is IRowStore<DataLinqKey> dynamicStore)
            return dynamicStore.TryRemove(ProviderKeyComponents.ToDataLinqKey(primaryKey), out numRowsRemoved);

        numRowsRemoved = 0;
        return true;
    }

    internal bool TryAddRow(DataLinqKey key, RowData data, IImmutableInstance instance) =>
        TryAddRow(key, data.Size, instance);

    public bool TryAddRow<TKey>(
        TKey key,
        int size,
        IImmutableInstance instance)
        where TKey : notnull
    {
        if (key is null)
            return false;

        return GetOrCreateStore<TKey>().TryAdd(key, size, instance);
    }

    private IRowStore<TKey> GetOrCreateStore<TKey>()
        where TKey : notnull
    {
        var store = rowStore;
        if (store is null)
        {
            var created = new RowStore<TKey>();
            store = Interlocked.CompareExchange(ref rowStore, created, null) ?? created;
        }

        if (store is IRowStore<TKey> typedStore)
            return typedStore;

        throw new InvalidOperationException(
            $"Row cache already stores provider keys of type '{store.KeyType}', but was accessed with '{typeof(TKey)}'.");
    }

}
