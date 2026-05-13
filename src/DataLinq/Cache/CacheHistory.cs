using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Cache;

public class CacheHistory(uint maxCapacity = 10000)
{
    public uint Count { get; private set; }
    public uint MaxCapacity { get; set; } = maxCapacity;

    public event Action<DatabaseCacheSnapshot>? OnAdd;

    private LinkedList<DatabaseCacheSnapshot> history = new();
    private readonly object lockObject = new();

    public void Add(DatabaseCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (lockObject)
        {
            history.AddLast(snapshot);
            Count++;

            while (Count > MaxCapacity)
            {
                Count--;
                history.RemoveFirst();
            }
        }

        OnAdd?.Invoke(snapshot);
    }

    public DatabaseCacheSnapshot[] GetHistory()
    {
        return history.ToArray();
    }

    public DatabaseCacheSnapshot? GetLatest()
    {
        return history.Last?.Value;
    }

    public void Clear()
    {
        history.Clear();
        Count = 0;
    }

    internal CacheMemoryEstimate GetMemoryEstimate()
    {
        lock (lockObject)
        {
            var snapshotBytes = CacheMemoryEstimator.CacheHistoryContainerBytes;
            snapshotBytes = CacheMemoryEstimator.SaturatingAdd(
                snapshotBytes,
                CacheMemoryEstimator.SaturatingMultiply(Count, CacheMemoryEstimator.LinkedListNodeBytes));

            foreach (var snapshot in history)
            {
                snapshotBytes = CacheMemoryEstimator.SaturatingAdd(
                    snapshotBytes,
                    CacheMemoryEstimator.DatabaseCacheSnapshotBytes(snapshot.TableCaches.Length));

                foreach (var tableSnapshot in snapshot.TableCaches)
                {
                    snapshotBytes = CacheMemoryEstimator.SaturatingAdd(
                        snapshotBytes,
                        CacheMemoryEstimator.TableCacheSnapshotBytes(tableSnapshot.Indices.Length));
                }
            }

            return new CacheMemoryEstimate(SnapshotBytes: snapshotBytes);
        }
    }
}
