using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Instances;

namespace DataLinq.Cache;

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
    bool TryGetKey(DataLinqKey key, out IImmutableInstance? row);
    bool TryAddKey(DataLinqKey key, int size, IImmutableInstance row);
    bool TryRemoveKey(DataLinqKey key, out int numRowsRemoved);
}

internal interface IRowStore<TKey> : IRowStore
    where TKey : notnull
{
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
    private long totalBytes;

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

    public bool TryGetKey(DataLinqKey key, out IImmutableInstance? row)
    {
        if (TryConvertKey(key, out var providerKey))
            return TryGet(providerKey, out row);

        row = null;
        return false;
    }

    public bool TryAddKey(DataLinqKey key, int size, IImmutableInstance row)
    {
        return TryConvertKey(key, out var providerKey) &&
            TryAdd(providerKey, size, row);
    }

    public bool TryRemoveKey(DataLinqKey key, out int numRowsRemoved)
    {
        if (TryConvertKey(key, out var providerKey))
            return TryRemove(providerKey, out numRowsRemoved);

        numRowsRemoved = 0;
        return true;
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
