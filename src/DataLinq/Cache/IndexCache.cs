using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Instances;

namespace DataLinq.Cache;

internal interface IIndexCache
{
    Type KeyType { get; }
    int Count { get; }
    IEnumerable<DataLinqKey[]> Values { get; }

    CacheMemoryEstimate GetMemoryEstimate();
    bool TryAdd<TKey>(TKey foreignKey, DataLinqKey[] primaryKeys)
        where TKey : notnull;
    bool TryRemove<TKey>(TKey foreignKey, out int numRowsRemoved)
        where TKey : notnull;
    bool TryRemovePrimaryKey(DataLinqKey primaryKey, out int numRowsRemoved);
    int RemoveInsertedBeforeTick(long tick);
    bool TryGet<TKey>(TKey foreignKey, out DataLinqKey[]? keys)
        where TKey : notnull;
    void Clear();
}

internal class IndexCache : TypedIndexCache<DataLinqKey>
{
}

internal class TypedIndexCache<TKey> : IIndexCache
    where TKey : notnull
{
    private readonly object cacheLock = new();
    private readonly LinkedList<(TKey key, long tick)> expirationOrder = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey key, long tick)>> expirationNodes = new();
    private readonly Func<long> getCurrentTick;

    internal TypedIndexCache(Func<long>? getCurrentTick = null)
    {
        this.getCurrentTick = getCurrentTick ?? (static () => DateTime.Now.Ticks);
    }

    private readonly Dictionary<DataLinqKey, ImmutableArray<TKey>> primaryKeysToForeignKeys = new();

    protected readonly ConcurrentDictionary<TKey, DataLinqKey[]> foreignKeys = new();
    private long indexPayloadBytes;
    private long reverseMappingValueBytes;

    public int Count => foreignKeys.Count;

    public Type KeyType => typeof(TKey);

    public CacheMemoryEstimate GetMemoryEstimate()
    {
        int foreignKeyCount;
        int reverseMapCount;
        int tickCount;

        lock (cacheLock)
        {
            tickCount = expirationOrder.Count;
            foreignKeyCount = foreignKeys.Count;
            reverseMapCount = primaryKeysToForeignKeys.Count;
        }

        var overheadBytes = CacheMemoryEstimator.IndexCacheContainerBytes;
        overheadBytes = CacheMemoryEstimator.SaturatingAdd(
            overheadBytes,
            CacheMemoryEstimator.ConcurrentDictionaryOverheadBytes(foreignKeyCount));
        overheadBytes = CacheMemoryEstimator.SaturatingAdd(
            overheadBytes,
            CacheMemoryEstimator.DictionaryOverheadBytes(reverseMapCount));
        overheadBytes = CacheMemoryEstimator.SaturatingAdd(
            overheadBytes,
            Interlocked.Read(ref reverseMappingValueBytes));
        overheadBytes = CacheMemoryEstimator.SaturatingAdd(
            overheadBytes,
            CacheMemoryEstimator.DictionaryOverheadBytes(tickCount));
        overheadBytes = CacheMemoryEstimator.SaturatingAdd(
            overheadBytes,
            CacheMemoryEstimator.ObjectHeaderBytes + CacheMemoryEstimator.ReferenceSize + sizeof(int));
        overheadBytes = CacheMemoryEstimator.SaturatingAdd(
            overheadBytes,
            CacheMemoryEstimator.SaturatingMultiply(tickCount,
                CacheMemoryEstimator.LinkedListNodeBytes + CacheMemoryEstimator.TickQueueEntryBytes(typeof(TKey))));

        return new CacheMemoryEstimate(
            IndexPayloadBytes: Interlocked.Read(ref indexPayloadBytes),
            IndexOverheadBytes: overheadBytes);
    }

    public bool TryAdd<TProviderKey>(TProviderKey foreignKey, DataLinqKey[] primaryKeys)
        where TProviderKey : notnull
    {
        return TryConvertProviderKey(foreignKey, out var providerKey) &&
            TryAddCore(providerKey, primaryKeys);
    }

    private bool TryAddCore(TKey foreignKey, DataLinqKey[] primaryKeys)
    {
        // Forward and reverse mappings must observe the same cache-owned snapshot.
        var storedPrimaryKeys = (DataLinqKey[])primaryKeys.Clone();
        lock (cacheLock)
        {
            if (foreignKeys.ContainsKey(foreignKey))
                return false;

            var ticksNow = getCurrentTick();
            foreignKeys.TryAdd(foreignKey, storedPrimaryKeys);
            Interlocked.Add(ref indexPayloadBytes, EstimatePrimaryKeyArrayBytes(storedPrimaryKeys));
            foreach (var primaryKey in storedPrimaryKeys)
                AddReverseMapping(primaryKey, foreignKey);

            // Usually append in O(1); preserve timestamp order if the wall clock moves backwards.
            var previous = expirationOrder.Last;
            while (previous is not null && previous.Value.tick > ticksNow)
                previous = previous.Previous;
            var entry = (foreignKey, ticksNow);
            expirationNodes.Add(foreignKey, previous is null
                ? expirationOrder.AddFirst(entry)
                : expirationOrder.AddAfter(previous, entry));
        }

        return true;
    }

    public bool TryRemove<TProviderKey>(TProviderKey foreignKey, out int numRowsRemoved)
        where TProviderKey : notnull
    {
        if (TryConvertProviderKey(foreignKey, out var providerKey))
            return TryRemoveProviderKeyCore(providerKey, out numRowsRemoved);

        numRowsRemoved = 0;
        return true;
    }

    private bool TryRemoveProviderKeyCore(TKey foreignKey, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        lock (cacheLock)
        {
            if (foreignKeys.TryRemove(foreignKey, out var pks))
            {
                if (expirationNodes.Remove(foreignKey, out var node))
                    expirationOrder.Remove(node);
                Interlocked.Add(ref indexPayloadBytes, -EstimatePrimaryKeyArrayBytes(pks));

                numRowsRemoved = 1;
                foreach (var pk in pks)
                    RemoveReverseMapping(pk, foreignKey);

                return true;
            }
        }

        return true;
    }

    private IEnumerable<TKey> GetForeignKeysByPrimaryKey(DataLinqKey primaryKey)
    {
        lock (cacheLock)
        {
            if (primaryKeysToForeignKeys.TryGetValue(primaryKey, out var foreignKeys))
                return foreignKeys.IsDefaultOrEmpty ? [] : foreignKeys;
        }

        return Enumerable.Empty<TKey>();
    }

    public bool TryRemovePrimaryKey(DataLinqKey primaryKey, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        lock (cacheLock)
        {
            foreach (var fk in GetForeignKeysByPrimaryKey(primaryKey))
            {
                TryRemoveProviderKeyCore(fk, out var num);
                numRowsRemoved += num;
            }
        }

        return true;
    }

    public int RemoveInsertedBeforeTick(long tick)
    {
        var count = 0;
        lock (cacheLock)
        {
            while (expirationOrder.First is { } oldest && oldest.Value.tick < tick)
            {
                TryRemoveProviderKeyCore(oldest.Value.key, out var numRowsRemoved);
                count += numRowsRemoved;
            }
        }

        return count;
    }

    public bool TryGet<TProviderKey>(TProviderKey foreignKey, out DataLinqKey[]? keys)
        where TProviderKey : notnull
    {
        if (TryConvertProviderKey(foreignKey, out var providerKey))
            return foreignKeys.TryGetValue(providerKey, out keys);

        keys = null;
        return false;
    }

    public IEnumerable<DataLinqKey[]> Values => foreignKeys.Values;

    public void Clear()
    {
        lock (cacheLock)
        {
            foreignKeys.Clear();
            primaryKeysToForeignKeys.Clear();
            expirationOrder.Clear();
            expirationNodes.Clear();
            Interlocked.Exchange(ref indexPayloadBytes, 0);
            Interlocked.Exchange(ref reverseMappingValueBytes, 0);
        }
    }

    private void AddReverseMapping(DataLinqKey primaryKey, TKey foreignKey)
    {
        if (!primaryKeysToForeignKeys.TryGetValue(primaryKey, out var existingForeignKeys))
        {
            var created = ImmutableArray.Create(foreignKey);
            primaryKeysToForeignKeys.Add(primaryKey, created);
            Interlocked.Add(ref reverseMappingValueBytes, EstimateImmutableArrayBytes(created));
            return;
        }

        if (existingForeignKeys.Contains(foreignKey))
            return;

        var updatedForeignKeys = existingForeignKeys.Add(foreignKey);
        primaryKeysToForeignKeys[primaryKey] = updatedForeignKeys;
        Interlocked.Add(
            ref reverseMappingValueBytes,
            EstimateImmutableArrayBytes(updatedForeignKeys) - EstimateImmutableArrayBytes(existingForeignKeys));
    }

    private void RemoveReverseMapping(DataLinqKey primaryKey, TKey foreignKey)
    {
        if (!primaryKeysToForeignKeys.TryGetValue(primaryKey, out var existingForeignKeys))
            return;

        var updatedForeignKeys = existingForeignKeys.Remove(foreignKey);
        if (updatedForeignKeys.IsDefaultOrEmpty)
        {
            primaryKeysToForeignKeys.Remove(primaryKey);
            Interlocked.Add(ref reverseMappingValueBytes, -EstimateImmutableArrayBytes(existingForeignKeys));
        }
        else
        {
            primaryKeysToForeignKeys[primaryKey] = updatedForeignKeys;
            Interlocked.Add(
                ref reverseMappingValueBytes,
                EstimateImmutableArrayBytes(updatedForeignKeys) - EstimateImmutableArrayBytes(existingForeignKeys));
        }
    }

    private static long EstimatePrimaryKeyArrayBytes(DataLinqKey[] primaryKeys)
    {
        var bytes = CacheMemoryEstimator.DataLinqKeyArrayBytes(primaryKeys.Length);
        for (var i = 0; i < primaryKeys.Length; i++)
            bytes = CacheMemoryEstimator.SaturatingAdd(bytes, CacheMemoryEstimator.EstimateDataLinqKeyPayloadBytes(primaryKeys[i]));

        return bytes;
    }

    private static long EstimateImmutableArrayBytes(ImmutableArray<TKey> values)
    {
        if (values.IsDefaultOrEmpty)
            return 0;

        return CacheMemoryEstimator.ImmutableArrayBackingBytes(typeof(TKey), values.Length);
    }

    private bool TryConvertProviderKey<TProviderKey>(TProviderKey key, out TKey providerKey)
        where TProviderKey : notnull
    {
        if (key is TKey typedKey)
        {
            providerKey = typedKey;
            return true;
        }

        if (key is DataLinqKey dataLinqKey)
            return TryConvertKey(dataLinqKey, out providerKey);

        if (key is IProviderKey componentKey)
        {
            if (typeof(TKey) == typeof(DataLinqKey))
            {
                providerKey = (TKey)(object)DataLinqKey.FromProviderKey(componentKey);
                return true;
            }

            if (componentKey.ValueCount == 1 &&
                componentKey.GetValue(0) is TKey componentValue)
            {
                providerKey = componentValue;
                return true;
            }
        }

        providerKey = default!;
        return false;
    }

    private bool TryConvertKey(DataLinqKey key, out TKey providerKey)
    {
        if (key is TKey directKey)
        {
            providerKey = directKey;
            return true;
        }

        if (key.ValueCount == 1 && key.GetValue(0) is TKey typedKey)
        {
            providerKey = typedKey;
            return true;
        }

        providerKey = default!;
        return false;
    }
}
