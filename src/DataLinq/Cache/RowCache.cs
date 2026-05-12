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
    public long TotalBytes => rowStore?.TotalBytes ?? 0;
    public string TotalBytesFormatted => TotalBytes.ToFileSize();

    public void ClearRows() => rowStore?.Clear();

    public int RemoveRowsOverRowLimit(int maxRows) =>
        rowStore?.RemoveRowsOverRowLimit(maxRows) ?? 0;

    public int RemoveRowsOverSizeLimit(long maxSize) =>
        rowStore?.RemoveRowsOverSizeLimit(maxSize) ?? 0;

    public int RemoveRowsInsertedBeforeTick(long tick) =>
        rowStore?.RemoveRowsInsertedBeforeTick(tick) ?? 0;

    public bool TryGetValue(IKey primaryKeys, out IImmutableInstance? row)
    {
        if (rowStore is not null)
            return rowStore.TryGetLegacyKey(primaryKeys, out row);

        row = null;
        return false;
    }

    public bool TryGetValue<TKey>(TKey primaryKey, out IImmutableInstance? row)
        where TKey : notnull
    {
        if (primaryKey is null || primaryKey is IKey)
        {
            row = null;
            return false;
        }

        if (rowStore is IRowStore<TKey> typedStore)
            return typedStore.TryGet(primaryKey, out row);

        row = null;
        return false;
    }

    public bool TryRemoveRow(IKey primaryKeys, out int numRowsRemoved)
    {
        if (rowStore is not null)
            return rowStore.TryRemoveLegacyKey(primaryKeys, out numRowsRemoved);

        numRowsRemoved = 0;
        return true;
    }

    public bool TryRemoveProviderKey<TKey>(TKey primaryKey, out int numRowsRemoved)
        where TKey : notnull
    {
        if (primaryKey is null || primaryKey is IKey)
        {
            numRowsRemoved = 0;
            return true;
        }

        if (rowStore is IRowStore<TKey> typedStore)
            return typedStore.TryRemove(primaryKey, out numRowsRemoved);

        numRowsRemoved = 0;
        return true;
    }

    public bool TryAddRow(IKey keys, RowData data, IImmutableInstance instance)
    {
        if (rowStore is not null)
            return rowStore.TryAddLegacyKey(keys, data.Size, instance);

        return TryCreateScalarStoreFromLegacyKey(keys, data.Size, instance);
    }

    public bool TryAddRow<TKey>(
        TKey key,
        int size,
        IImmutableInstance instance,
        ProviderKeyFromLegacyKey<TKey>? legacyKeyFactory = null)
        where TKey : notnull
    {
        if (key is null || key is IKey)
            return false;

        return GetOrCreateStore(legacyKeyFactory).TryAdd(key, size, instance);
    }

    private IRowStore<TKey> GetOrCreateStore<TKey>(ProviderKeyFromLegacyKey<TKey>? legacyKeyFactory)
        where TKey : notnull
    {
        var store = rowStore;
        if (store is null)
        {
            var created = new RowStore<TKey>(legacyKeyFactory);
            store = Interlocked.CompareExchange(ref rowStore, created, null) ?? created;
        }

        if (store is IRowStore<TKey> typedStore)
        {
            typedStore.SetLegacyKeyFactory(legacyKeyFactory);
            return typedStore;
        }

        throw new InvalidOperationException(
            $"Row cache already stores provider keys of type '{store.KeyType}', but was accessed with '{typeof(TKey)}'.");
    }

    private bool TryCreateScalarStoreFromLegacyKey(IKey keys, int size, IImmutableInstance instance)
    {
        if (!keys.TryGetSingleValue(out var value) || value is null)
            return false;

        return value switch
        {
            int intKey => TryAddRow(intKey, size, instance),
            long longKey => TryAddRow(longKey, size, instance),
            Guid guidKey => TryAddRow(guidKey, size, instance),
            string stringKey => TryAddRow(stringKey, size, instance),
            _ => false
        };
    }
}
