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
    private readonly object ticksQueueLock = new();
    private (TKey keys, long ticks)? oldestTick;
    private readonly Queue<(TKey keys, long ticks)> ticks = new();

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

        lock (ticksQueueLock)
        {
            tickCount = ticks.Count;
            lock (cacheLock)
            {
                foreignKeyCount = foreignKeys.Count;
                reverseMapCount = primaryKeysToForeignKeys.Count;
            }
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
            CacheMemoryEstimator.QueueOverheadBytes(tickCount, CacheMemoryEstimator.TickQueueEntryBytes(typeof(TKey))));

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
        var ticksNow = DateTime.Now.Ticks;

        lock (ticksQueueLock)
        {
            lock (cacheLock)
            {
                if (!foreignKeys.TryAdd(foreignKey, storedPrimaryKeys))
                    return false;

                Interlocked.Add(ref indexPayloadBytes, EstimatePrimaryKeyArrayBytes(storedPrimaryKeys));

                foreach (var primaryKey in storedPrimaryKeys)
                    AddReverseMapping(primaryKey, foreignKey);
            }

            ticks.Enqueue((foreignKey, ticksNow));

            if (!oldestTick.HasValue)
                oldestTick = (foreignKey, ticksNow);
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

        foreach (var fk in GetForeignKeysByPrimaryKey(primaryKey).ToList())
        {
            TryRemoveProviderKeyCore(fk, out var num);
            numRowsRemoved += num;
        }

        return true;
    }

    public int RemoveInsertedBeforeTick(long tick)
    {
        if (!oldestTick.HasValue)
            return 0;

        var count = 0;
        lock (ticksQueueLock)
        {
            while (oldestTick?.ticks < tick)
            {
                if (TryRemoveProviderKeyCore(oldestTick.Value.keys, out var numRowsRemoved))
                {
                    count += numRowsRemoved;

                    ticks.TryDequeue(out var _);
                }
                else
                    break;

                if (ticks.TryPeek(out var nextTick))
                    oldestTick = nextTick;
                else
                {
                    oldestTick = null;
                    break;
                }
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
        lock (ticksQueueLock)
        {
            lock (cacheLock)
            {
                foreignKeys.Clear();
                primaryKeysToForeignKeys.Clear();
                Interlocked.Exchange(ref indexPayloadBytes, 0);
                Interlocked.Exchange(ref reverseMappingValueBytes, 0);
            }

            ticks.Clear();
            oldestTick = null;
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
