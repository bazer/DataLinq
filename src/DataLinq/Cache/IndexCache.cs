using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Instances;

namespace DataLinq.Cache;

public class IndexCache
{
    private readonly object ticksQueueLock = new();
    private (IKey keys, long ticks)? oldestTick;
    private readonly Queue<(IKey keys, long ticks)> ticks = new();

    private ConcurrentDictionary<IKey, List<IKey>> primaryKeysToForeignKeys = new();

    protected ConcurrentDictionary<IKey, IKey[]> foreignKeys = new();

    public int Count => foreignKeys.Count;

    public bool TryAdd(IKey foreignKey, IKey[] primaryKeys)
    {
        var ticksNow = DateTime.Now.Ticks;

        if (!foreignKeys.TryAdd(foreignKey, primaryKeys))
            return false;

        lock (ticksQueueLock)
        {
            ticks.Enqueue((foreignKey, ticksNow));

            if (!oldestTick.HasValue)
                oldestTick = (foreignKey, ticksNow);
        }

        foreach (var primaryKey in primaryKeys)
        {
            primaryKeysToForeignKeys.AddOrUpdate(primaryKey,
                new List<IKey> { foreignKey },
                (key, existingList) =>
                {
                    existingList.Add(foreignKey);
                    return existingList;
                });
        }

        return true;
    }

    public bool TryRemoveForeignKey(IKey foreignKey, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        if (foreignKeys.ContainsKey(foreignKey))
        {
            if (foreignKeys.TryRemove(foreignKey, out var pks))
            {
                numRowsRemoved = 1;
                foreach (var pk in pks)
                {
                    if (primaryKeysToForeignKeys.TryGetValue(pk, out var foreignKeysList))
                    {
                        foreignKeysList.Remove(foreignKey);
                        if (foreignKeysList.Count == 0)
                        {
                            primaryKeysToForeignKeys.TryRemove(pk, out _);
                        }
                    }
                }
                return true;
            }
            else
                return false;
        }

        return true;
    }

    public IEnumerable<IKey> GetForeignKeysByPrimaryKey(IKey primaryKey)
    {
        if (primaryKeysToForeignKeys.TryGetValue(primaryKey, out var foreignKeys))
            return foreignKeys;

        return Enumerable.Empty<IKey>();
    }

    public bool TryRemovePrimaryKey(IKey primaryKey, out int numRowsRemoved)
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

    public bool ContainsKey(IKey foreignKey) => foreignKeys.ContainsKey(foreignKey);

    public bool TryGetValue(IKey foreignKey, out IKey[]? keys) => foreignKeys.TryGetValue(foreignKey, out keys);

    public IEnumerable<IKey[]> Values => foreignKeys.Values;

    public void Clear()
    {
        foreignKeys.Clear();
        ticks.Clear();
        oldestTick = null;
    }
}
