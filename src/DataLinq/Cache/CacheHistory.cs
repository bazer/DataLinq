using System;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Cache;

public class CacheHistory(uint maxCapacity = 10000)
{
    public uint Count
    {
        get { lock (lockObject) return (uint)history.Count; }
    }

    /// <summary>Maximum retained snapshots. Reducing it immediately discards the oldest snapshots.</summary>
    public uint MaxCapacity
    {
        get { lock (lockObject) return capacity; }
        set
        {
            lock (lockObject)
            {
                capacity = value;
                TrimToCapacity();
            }
        }
    }

    public event Action<DatabaseCacheSnapshot>? OnAdd;

    private readonly LinkedList<DatabaseCacheSnapshot> history = new();
    private readonly object lockObject = new();
    private uint capacity = maxCapacity;

    public void Add(DatabaseCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (lockObject)
        {
            history.AddLast(snapshot);
            TrimToCapacity();
        }

        OnAdd?.Invoke(snapshot);
    }

    public DatabaseCacheSnapshot[] GetHistory()
    {
        lock (lockObject)
            return history.ToArray();
    }

    public DatabaseCacheSnapshot? GetLatest()
    {
        lock (lockObject)
            return history.Last?.Value;
    }

    public void Clear()
    {
        lock (lockObject)
            history.Clear();
    }

    private void TrimToCapacity()
    {
        while ((uint)history.Count > capacity)
            history.RemoveFirst();
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
