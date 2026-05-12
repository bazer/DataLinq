using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Instances;

namespace DataLinq.Cache;

public class IndexCache
{
    private readonly object cacheLock = new();
    private readonly object ticksQueueLock = new();
    private (DataLinqKey keys, long ticks)? oldestTick;
    private readonly Queue<(DataLinqKey keys, long ticks)> ticks = new();

    private readonly ConcurrentDictionary<DataLinqKey, ImmutableArray<DataLinqKey>> primaryKeysToForeignKeys = new();

    protected readonly ConcurrentDictionary<DataLinqKey, DataLinqKey[]> foreignKeys = new();

    public int Count => foreignKeys.Count;

    public bool TryAdd(DataLinqKey foreignKey, DataLinqKey[] primaryKeys)
    {
        var ticksNow = DateTime.Now.Ticks;

        lock (ticksQueueLock)
        {
            lock (cacheLock)
            {
                if (!foreignKeys.TryAdd(foreignKey, primaryKeys))
                    return false;

                foreach (var primaryKey in primaryKeys)
                    AddReverseMapping(primaryKey, foreignKey);
            }

            ticks.Enqueue((foreignKey, ticksNow));

            if (!oldestTick.HasValue)
                oldestTick = (foreignKey, ticksNow);
        }

        return true;
    }

    public bool TryRemoveForeignKey(DataLinqKey foreignKey, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        lock (cacheLock)
        {
            if (foreignKeys.ContainsKey(foreignKey))
            {
                if (foreignKeys.TryRemove(foreignKey, out var pks))
                {
                    numRowsRemoved = 1;
                    foreach (var pk in pks)
                        RemoveReverseMapping(pk, foreignKey);

                    return true;
                }
                else
                    return false;
            }
        }

        return true;
    }

    public IEnumerable<DataLinqKey> GetForeignKeysByPrimaryKey(DataLinqKey primaryKey)
    {
        lock (cacheLock)
        {
            if (primaryKeysToForeignKeys.TryGetValue(primaryKey, out var foreignKeys))
                return foreignKeys.IsDefaultOrEmpty ? [] : foreignKeys;
        }

        return Enumerable.Empty<DataLinqKey>();
    }

    public bool TryRemovePrimaryKey(DataLinqKey primaryKey, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        foreach (var fk in GetForeignKeysByPrimaryKey(primaryKey).ToList())
        {
            TryRemoveForeignKey(fk, out var num);
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
                if (TryRemoveForeignKey(oldestTick.Value.keys, out var numRowsRemoved))
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

    public bool ContainsKey(DataLinqKey foreignKey) => foreignKeys.ContainsKey(foreignKey);

    public bool TryGetValue(DataLinqKey foreignKey, out DataLinqKey[]? keys) => foreignKeys.TryGetValue(foreignKey, out keys);

    public IEnumerable<DataLinqKey[]> Values => foreignKeys.Values;

    public void Clear()
    {
        lock (ticksQueueLock)
        {
            lock (cacheLock)
            {
                foreignKeys.Clear();
                primaryKeysToForeignKeys.Clear();
            }

            ticks.Clear();
            oldestTick = null;
        }
    }

    private void AddReverseMapping(DataLinqKey primaryKey, DataLinqKey foreignKey)
    {
        primaryKeysToForeignKeys.AddOrUpdate(
            primaryKey,
            ImmutableArray.Create(foreignKey),
            (_, existingForeignKeys) => existingForeignKeys.Contains(foreignKey)
                ? existingForeignKeys
                : existingForeignKeys.Add(foreignKey));
    }

    private void RemoveReverseMapping(DataLinqKey primaryKey, DataLinqKey foreignKey)
    {
        if (!primaryKeysToForeignKeys.TryGetValue(primaryKey, out var existingForeignKeys))
            return;

        var updatedForeignKeys = existingForeignKeys.Remove(foreignKey);
        if (updatedForeignKeys.IsDefaultOrEmpty)
            primaryKeysToForeignKeys.TryRemove(primaryKey, out _);
        else
            primaryKeysToForeignKeys[primaryKey] = updatedForeignKeys;
    }
}
