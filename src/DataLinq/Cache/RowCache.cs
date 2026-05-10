using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Utils;

namespace DataLinq.Cache;

public class RowCache
{
    private readonly object rowsLock = new();
    private readonly object keyTicksQueueLock = new();
    private (IKey keys, long ticks, int size)? oldestKeyTick;
    private readonly Queue<(IKey keys, long ticks, int size)> keysTicks = new();

    protected ConcurrentDictionary<IKey, IImmutableInstance> rows = new();
    private readonly ConcurrentDictionary<IKey, (int size, long ticks)> rowMetadata = new();
    private long totalBytes;

    public IEnumerable<IImmutableInstance> Rows => rows.Values.AsEnumerable();
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

    public long TotalBytes => Interlocked.Read(ref totalBytes);

    public string TotalBytesFormatted => TotalBytes.ToFileSize();

    public void ClearRows()
    {
        lock (keyTicksQueueLock)
        {
            lock (rowsLock)
            {
                rows.Clear();
                rowMetadata.Clear();
                Interlocked.Exchange(ref totalBytes, 0);
            }

            keysTicks.Clear();
            oldestKeyTick = null;
        }
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
                if (TryRemoveRow(oldestKeyTick.Value.keys, oldestKeyTick.Value.ticks, out var numRowsRemoved))
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
                if (TryRemoveRow(oldestKeyTick.Value.keys, oldestKeyTick.Value.ticks, out var numRowsRemoved))
                {
                    if (numRowsRemoved > 0)
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
                if (TryRemoveRow(oldestKeyTick.Value.keys, oldestKeyTick.Value.ticks, out var numRowsRemoved))
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

    public bool TryGetValue(IKey primaryKeys, out IImmutableInstance? row) => rows.TryGetValue(primaryKeys, out row);

    public bool TryRemoveRow(IKey primaryKeys, out int numRowsRemoved)
        => TryRemoveRow(primaryKeys, null, out numRowsRemoved);

    private bool TryRemoveRow(IKey primaryKeys, long? expectedTicks, out int numRowsRemoved)
    {
        numRowsRemoved = 0;

        lock (rowsLock)
        {
            if (rows.ContainsKey(primaryKeys))
            {
                if (expectedTicks.HasValue &&
                    (!rowMetadata.TryGetValue(primaryKeys, out var currentMetadata) ||
                     currentMetadata.ticks != expectedTicks.Value))
                    return true;

                if (rows.TryRemove(primaryKeys, out var _))
                {
                    if (rowMetadata.TryRemove(primaryKeys, out var metadata))
                        Interlocked.Add(ref totalBytes, -metadata.size);

                    numRowsRemoved = 1;
                    return true;
                }
                else
                    return false;
            }
        }

        return true;
    }

    public bool TryAddRow(IKey keys, RowData data, IImmutableInstance instance)
    {
        var ticks = DateTime.Now.Ticks;

        lock (keyTicksQueueLock)
        {
            lock (rowsLock)
            {
                if (!rows.TryAdd(keys, instance))
                    return false;

                if (rowMetadata.TryGetValue(keys, out var existingMetadata))
                {
                    rowMetadata[keys] = (data.Size, ticks);
                    Interlocked.Add(ref totalBytes, data.Size - existingMetadata.size);
                }
                else
                {
                    rowMetadata[keys] = (data.Size, ticks);
                    Interlocked.Add(ref totalBytes, data.Size);
                }
            }

            keysTicks.Enqueue((keys, ticks, data.Size));

            if (!oldestKeyTick.HasValue)
                oldestKeyTick = (keys, ticks, data.Size);
        }

        return true;
    }
}
