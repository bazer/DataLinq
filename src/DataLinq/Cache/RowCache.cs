using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Instances;
using DataLinq.Utils;

namespace DataLinq.Cache;

public class RowCache
{
    private readonly object keyTicksQueueLock = new();
    private (PrimaryKeys keys, long ticks, int size)? oldestKeyTick;
    private readonly Queue<(PrimaryKeys keys, long ticks, int size)> keysTicks = new();

    protected ConcurrentDictionary<PrimaryKeys, ImmutableInstanceBase> rows = new();

    public IEnumerable<ImmutableInstanceBase> Rows => rows.Values.AsEnumerable();
    public int Count => rows.Count;

    public long? OldestTick => oldestKeyTick?.ticks;
    public long? NewestTick
    {
        get
        {
            if (!oldestKeyTick.HasValue)
                return null;

            lock (keyTicksQueueLock)
            {
                // Get the last element or use default (an empty tuple)
                var lastItem = keysTicks.LastOrDefault();

                // If the queue is empty, the default is returned and all elements of the tuple will be default values
                return lastItem.Equals(default) ? null : lastItem.ticks;
            }
        }
    }

    public long TotalBytes
    {
        get
        {
            lock (keyTicksQueueLock)
            {
                return keysTicks.Sum(x => x.size);
            }
        }
    }

    public string TotalBytesFormatted => TotalBytes.ToFileSize();

    public void ClearRows()
    {
        rows.Clear();
        lock (keyTicksQueueLock)
        {
            keysTicks.Clear();
        }
        oldestKeyTick = null;
    }

    public int RemoveRowsOverRowLimit(int maxRows)
    {
        var count = 0;
        var rowCount = rows.Count;

        if (rowCount <= maxRows)
            return 0;

        lock (keyTicksQueueLock)
        {
            if (!oldestKeyTick.HasValue)
                return 0;

            while (rowCount > maxRows)
            {
                if (TryRemoveRow(oldestKeyTick.Value.keys, out var numRowsRemoved))
                {
                    rowCount -= numRowsRemoved;
                    count += numRowsRemoved;

                    keysTicks.TryDequeue(out _);

                    if (keysTicks.TryPeek(out var nextTick))
                        oldestKeyTick = nextTick;
                    else
                    {
                        oldestKeyTick = null;
                        break;
                    }
                }
                else
                    break;
            }
        }

        return count;
    }

    public int RemoveRowsOverSizeLimit(long maxSize)
    {
        var count = 0;
        var totalSize = TotalBytes;

        if (totalSize <= maxSize)
            return 0;

        lock (keyTicksQueueLock)
        {
            if (!oldestKeyTick.HasValue)
                return 0;

            while (totalSize > maxSize)
            {
                if (TryRemoveRow(oldestKeyTick.Value.keys, out var numRowsRemoved))
                {
                    totalSize -= oldestKeyTick.Value.size;
                    count += numRowsRemoved;

                    keysTicks.TryDequeue(out _);

                    if (keysTicks.TryPeek(out var nextTick))
                        oldestKeyTick = nextTick;
                    else
                    {
                        oldestKeyTick = null;
                        break;
                    }
                }
                else
                    break;
            }
        }

        return count;
    }

    public int RemoveRowsInsertedBeforeTick(long tick)
    {
        if (!oldestKeyTick.HasValue)
            return 0;

        var count = 0;
        lock (keyTicksQueueLock)
        {
            while (oldestKeyTick?.ticks < tick)
            {
                if (TryRemoveRow(oldestKeyTick.Value.keys, out var numRowsRemoved))
                {
                    count += numRowsRemoved;

                    keysTicks.TryDequeue(out var _);
                }
                else
                    break;

                if (keysTicks.TryPeek(out var nextTick))
                    oldestKeyTick = nextTick;
                else
                {
                    oldestKeyTick = null;
                    break;
                }
            }
        }

        return count;
    }

    public bool TryGetValue(PrimaryKeys primaryKeys, out ImmutableInstanceBase? row) => rows.TryGetValue(primaryKeys, out row);

    public bool TryRemoveRow(PrimaryKeys primaryKeys, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        if (rows.ContainsKey(primaryKeys))
        {
            if (rows.TryRemove(primaryKeys, out var _))
            {
                numRowsRemoved = 1;
                return true;
            }
            else
                return false;
        }

        return true;
    }

    public bool TryAddRow(PrimaryKeys keys, RowData data, ImmutableInstanceBase instance)
    {
        var ticks = DateTime.Now.Ticks;

        if (!rows.TryAdd(keys, instance))
            return false;

        lock (keyTicksQueueLock)
        {
            keysTicks.Enqueue((keys, ticks, data.Size));

            if (!oldestKeyTick.HasValue)
                oldestKeyTick = (keys, ticks, data.Size);
        }


        return true;
    }
}
