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

    internal CacheMemoryEstimate GetMemoryEstimate() =>
        rowStore?.GetMemoryEstimate() ?? CacheMemoryEstimate.Empty;

    public int RemoveRowsOverRowLimit(int maxRows) =>
        rowStore?.RemoveRowsOverRowLimit(maxRows) ?? 0;

    internal IReadOnlyList<DataLinqKey> RemoveRowsOverRowLimitAndReturnKeys(int maxRows) =>
        rowStore?.RemoveRowsOverRowLimitAndReturnKeys(maxRows) ?? [];

    public int RemoveRowsOverSizeLimit(long maxSize) =>
        rowStore?.RemoveRowsOverSizeLimit(maxSize) ?? 0;

    internal IReadOnlyList<DataLinqKey> RemoveRowsOverSizeLimitAndReturnKeys(long maxSize) =>
        rowStore?.RemoveRowsOverSizeLimitAndReturnKeys(maxSize) ?? [];

    public int RemoveRowsInsertedBeforeTick(long tick) =>
        rowStore?.RemoveRowsInsertedBeforeTick(tick) ?? 0;

    internal IReadOnlyList<DataLinqKey> RemoveRowsInsertedBeforeTickAndReturnKeys(long tick) =>
        rowStore?.RemoveRowsInsertedBeforeTickAndReturnKeys(tick) ?? [];

    internal IReadOnlyList<DataLinqKey> RemoveOldestRows(int maxRows) =>
        rowStore?.RemoveOldestRows(maxRows) ?? [];

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
        TryAddRow(key, data, instance, static (cache, rowKey, rowSize, rowContainerBytes, row) =>
            cache.TryAddRow(rowKey, rowSize, rowContainerBytes, row));

    public bool TryAddRow<TKey>(
        TKey key,
        RowData data,
        IImmutableInstance instance)
        where TKey : notnull =>
        TryAddRow(key, data, instance, static (cache, rowKey, rowSize, rowContainerBytes, row) =>
            cache.TryAddRow(rowKey, rowSize, rowContainerBytes, row));

    public bool TryAddRow<TKey>(
        TKey key,
        int size,
        IImmutableInstance instance)
        where TKey : notnull
        => TryAddRow(key, size, rowContainerBytes: 0, instance);

    private bool TryAddRow<TKey>(
        TKey key,
        RowData data,
        IImmutableInstance instance,
        Func<RowCache, TKey, int, long, IImmutableInstance, bool> add)
        where TKey : notnull
    {
        var rowContainerBytes = CacheMemoryEstimator.RowDataContainerBytes(data.Table.ColumnCount);
        return add(this, key, data.Size, rowContainerBytes, instance);
    }

    private bool TryAddRow<TKey>(
        TKey key,
        int size,
        long rowContainerBytes,
        IImmutableInstance instance)
        where TKey : notnull
    {
        if (key is null)
            return false;

        if (RequiresStructuralKeyStore(key))
        {
            var structuralKey = ProviderKeyComponents.ToDataLinqKey(key);
            return GetOrCreateStore<DataLinqKey>().TryAdd(structuralKey, size, rowContainerBytes, instance);
        }

        return GetOrCreateStore<TKey>().TryAdd(key, size, rowContainerBytes, instance);
    }

    private static bool RequiresStructuralKeyStore<TKey>(TKey key)
        where TKey : notnull
    {
        // Arrays use mutable reference identity in generic dictionaries and generated record keys.
        // Keep ordinary provider keys typed, but snapshot binary shapes into DataLinqKey.
        if (key is byte[])
            return true;

        if (key is not IProviderKey providerKey || key is DataLinqKey)
            return false;

        for (var i = 0; i < providerKey.ValueCount; i++)
        {
            if (providerKey.GetValue(i) is byte[])
                return true;
        }

        return false;
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
