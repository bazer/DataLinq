using System;
using System.Collections.Generic;
using System.Linq;
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

internal interface IRowStore
{
    Type KeyType { get; }
    IEnumerable<IImmutableInstance> Rows { get; }
    int Count { get; }
    long TotalBytes { get; }
    long? OldestTick { get; }
    long? NewestTick { get; }

    void Clear();
    int RemoveRowsOverRowLimit(int maxRows);
    int RemoveRowsOverSizeLimit(long maxSize);
    int RemoveRowsInsertedBeforeTick(long tick);
    bool TryGetLegacyKey(IKey key, out IImmutableInstance? row);
    bool TryAddLegacyKey(IKey key, int size, IImmutableInstance row);
    bool TryRemoveLegacyKey(IKey key, out int numRowsRemoved);
}

internal interface IRowStore<TKey> : IRowStore
    where TKey : notnull
{
    void SetLegacyKeyFactory(ProviderKeyFromLegacyKey<TKey>? legacyKeyFactory);
    bool TryGet(TKey key, out IImmutableInstance? row);
    bool TryAdd(TKey key, int size, IImmutableInstance row);
    bool TryRemove(TKey key, out int numRowsRemoved);
}

internal sealed class RowStore<TKey> : IRowStore<TKey>
    where TKey : notnull
{
    private sealed class RowEntry(IImmutableInstance row, int size, long ticks)
    {
        public IImmutableInstance Row { get; } = row;
        public int Size { get; } = size;
        public long Ticks { get; } = ticks;
    }

    private readonly object rowsLock = new();
    private readonly Dictionary<TKey, RowEntry> rows = new();
    private ProviderKeyFromLegacyKey<TKey>? legacyKeyFactory;
    private long totalBytes;

    public RowStore(ProviderKeyFromLegacyKey<TKey>? legacyKeyFactory = null)
    {
        this.legacyKeyFactory = legacyKeyFactory;
    }

    public Type KeyType => typeof(TKey);

    public IEnumerable<IImmutableInstance> Rows
    {
        get
        {
            lock (rowsLock)
                return rows.Values.Select(static x => x.Row).ToArray();
        }
    }

    public int Count
    {
        get
        {
            lock (rowsLock)
                return rows.Count;
        }
    }

    public long TotalBytes => Interlocked.Read(ref totalBytes);

    public long? OldestTick
    {
        get
        {
            lock (rowsLock)
                return rows.Count == 0 ? null : rows.Values.Min(static x => x.Ticks);
        }
    }

    public long? NewestTick
    {
        get
        {
            lock (rowsLock)
                return rows.Count == 0 ? null : rows.Values.Max(static x => x.Ticks);
        }
    }

    public void SetLegacyKeyFactory(ProviderKeyFromLegacyKey<TKey>? legacyKeyFactory)
    {
        if (legacyKeyFactory is not null)
            this.legacyKeyFactory ??= legacyKeyFactory;
    }

    public void Clear()
    {
        lock (rowsLock)
        {
            rows.Clear();
            Interlocked.Exchange(ref totalBytes, 0);
        }
    }

    public int RemoveRowsOverRowLimit(int maxRows)
    {
        var removed = 0;

        lock (rowsLock)
        {
            while (rows.Count > maxRows)
            {
                if (!TryFindOldestKey(out var oldestKey, out _))
                    break;

                removed += RemoveExisting(oldestKey);
            }
        }

        return removed;
    }

    public int RemoveRowsOverSizeLimit(long maxSize)
    {
        var removed = 0;

        lock (rowsLock)
        {
            while (TotalBytes > maxSize)
            {
                if (!TryFindOldestKey(out var oldestKey, out _))
                    break;

                removed += RemoveExisting(oldestKey);
            }
        }

        return removed;
    }

    public int RemoveRowsInsertedBeforeTick(long tick)
    {
        var removed = 0;

        lock (rowsLock)
        {
            foreach (var key in rows.Where(x => x.Value.Ticks < tick).Select(x => x.Key).ToArray())
                removed += RemoveExisting(key);
        }

        return removed;
    }

    public bool TryGet(TKey key, out IImmutableInstance? row)
    {
        lock (rowsLock)
        {
            if (rows.TryGetValue(key, out var entry))
            {
                row = entry.Row;
                return true;
            }
        }

        row = null;
        return false;
    }

    public bool TryAdd(TKey key, int size, IImmutableInstance row)
    {
        var ticks = DateTime.Now.Ticks;

        lock (rowsLock)
        {
            if (rows.ContainsKey(key))
                return false;

            rows.Add(key, new RowEntry(row, size, ticks));
            Interlocked.Add(ref totalBytes, size);
            return true;
        }
    }

    public bool TryRemove(TKey key, out int numRowsRemoved)
    {
        lock (rowsLock)
        {
            numRowsRemoved = RemoveExisting(key);
            return true;
        }
    }

    public bool TryGetLegacyKey(IKey key, out IImmutableInstance? row)
    {
        if (TryConvertLegacyKey(key, out var providerKey))
            return TryGet(providerKey, out row);

        row = null;
        return false;
    }

    public bool TryAddLegacyKey(IKey key, int size, IImmutableInstance row)
    {
        return TryConvertLegacyKey(key, out var providerKey) &&
            TryAdd(providerKey, size, row);
    }

    public bool TryRemoveLegacyKey(IKey key, out int numRowsRemoved)
    {
        if (TryConvertLegacyKey(key, out var providerKey))
            return TryRemove(providerKey, out numRowsRemoved);

        numRowsRemoved = 0;
        return true;
    }

    private bool TryConvertLegacyKey(IKey key, out TKey providerKey)
    {
        if (legacyKeyFactory is not null)
            return legacyKeyFactory(key, out providerKey);

        if (key.TryGetSingleValue(out var value) && value is TKey typedKey)
        {
            providerKey = typedKey;
            return true;
        }

        providerKey = default!;
        return false;
    }

    private bool TryFindOldestKey(out TKey key, out RowEntry? entry)
    {
        key = default!;
        entry = null;

        foreach (var row in rows)
        {
            if (entry is null || row.Value.Ticks < entry.Ticks)
            {
                key = row.Key;
                entry = row.Value;
            }
        }

        return entry is not null;
    }

    private int RemoveExisting(TKey key)
    {
        if (!rows.Remove(key, out var entry))
            return 0;

        Interlocked.Add(ref totalBytes, -entry.Size);
        return 1;
    }
}
