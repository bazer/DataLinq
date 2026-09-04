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
    long RowPayloadBytes { get; }
    long TotalBytes { get; }
    long? OldestTick { get; }
    long? NewestTick { get; }

    CacheMemoryEstimate GetMemoryEstimate();
    void Clear();
    int RemoveRowsOverRowLimit(int maxRows);
    IReadOnlyList<DataLinqKey> RemoveRowsOverRowLimitAndReturnKeys(int maxRows);
    int RemoveRowsOverSizeLimit(long maxSize);
    IReadOnlyList<DataLinqKey> RemoveRowsOverSizeLimitAndReturnKeys(long maxSize);
    int RemoveRowsInsertedBeforeTick(long tick);
    IReadOnlyList<DataLinqKey> RemoveRowsInsertedBeforeTickAndReturnKeys(long tick);
    IReadOnlyList<DataLinqKey> RemoveOldestRows(int maxRows);
    bool TryGetKey(DataLinqKey key, out IImmutableInstance? row);
    bool TryAddKey(DataLinqKey key, int size, long rowContainerBytes, IImmutableInstance row);
    bool TryRemoveKey(DataLinqKey key, out int numRowsRemoved);
}

internal interface IRowStore<TKey> : IRowStore
    where TKey : notnull
{
    bool TryGet(TKey key, out IImmutableInstance? row);
    bool TryAdd(TKey key, int size, long rowContainerBytes, IImmutableInstance row);
    bool TryRemove(TKey key, out int numRowsRemoved);
}

internal sealed class RowStore<TKey> : IRowStore<TKey>
    where TKey : notnull
{
    private sealed class RowEntry(IImmutableInstance row, int size, long overheadBytes, long ticks, LinkedListNode<TKey> evictionNode)
    {
        public IImmutableInstance Row { get; } = row;
        public int Size { get; } = size;
        public long OverheadBytes { get; } = overheadBytes;
        public long Ticks { get; } = ticks;
        public LinkedListNode<TKey> EvictionNode { get; } = evictionNode;
    }

    private readonly object rowsLock = new();
    private readonly Dictionary<TKey, RowEntry> rows = new();
    private readonly LinkedList<TKey> evictionOrder = new();
    private readonly Func<long> getCurrentTick;
    private long rowPayloadBytes;
    private long rowOwnedOverheadBytes;

    internal RowStore(Func<long>? getCurrentTick = null)
    {
        this.getCurrentTick = getCurrentTick ?? (static () => DateTime.Now.Ticks);
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

    public long RowPayloadBytes => Interlocked.Read(ref rowPayloadBytes);

    public long TotalBytes => RowPayloadBytes;

    public CacheMemoryEstimate GetMemoryEstimate()
    {
        int count;
        lock (rowsLock)
            count = rows.Count;

        var rowStoreOverheadBytes = CacheMemoryEstimator.SaturatingAdd(
            CacheMemoryEstimator.RowStoreContainerBytes,
            CacheMemoryEstimator.DictionaryOverheadBytes(count));
        rowStoreOverheadBytes = CacheMemoryEstimator.SaturatingAdd(
            rowStoreOverheadBytes,
            CacheMemoryEstimator.ObjectHeaderBytes + CacheMemoryEstimator.ReferenceSize + sizeof(int));
        rowStoreOverheadBytes = CacheMemoryEstimator.SaturatingAdd(
            rowStoreOverheadBytes,
            Interlocked.Read(ref rowOwnedOverheadBytes));

        return new CacheMemoryEstimate(
            RowPayloadBytes: RowPayloadBytes,
            RowStoreOverheadBytes: rowStoreOverheadBytes);
    }

    public long? OldestTick
    {
        get
        {
            lock (rowsLock)
                return evictionOrder.First is { } first ? rows[first.Value].Ticks : null;
        }
    }

    public long? NewestTick
    {
        get
        {
            lock (rowsLock)
                return evictionOrder.Last is { } last ? rows[last.Value].Ticks : null;
        }
    }

    public void Clear()
    {
        lock (rowsLock)
        {
            rows.Clear();
            evictionOrder.Clear();
            Interlocked.Exchange(ref rowPayloadBytes, 0);
            Interlocked.Exchange(ref rowOwnedOverheadBytes, 0);
        }
    }

    public int RemoveRowsOverRowLimit(int maxRows) =>
        RemoveRowsOverRowLimitAndReturnKeys(maxRows).Count;

    public IReadOnlyList<DataLinqKey> RemoveRowsOverRowLimitAndReturnKeys(int maxRows)
    {
        var removedKeys = new List<DataLinqKey>();

        lock (rowsLock)
        {
            while (rows.Count > maxRows)
            {
                if (!TryFindOldestKey(out var oldestKey, out _))
                    break;

                RemoveExisting(oldestKey, removedKeys);
            }
        }

        return removedKeys;
    }

    public int RemoveRowsOverSizeLimit(long maxSize) =>
        RemoveRowsOverSizeLimitAndReturnKeys(maxSize).Count;

    public IReadOnlyList<DataLinqKey> RemoveRowsOverSizeLimitAndReturnKeys(long maxSize)
    {
        var removedKeys = new List<DataLinqKey>();

        lock (rowsLock)
        {
            while (RowPayloadBytes > maxSize)
            {
                if (!TryFindOldestKey(out var oldestKey, out _))
                    break;

                RemoveExisting(oldestKey, removedKeys);
            }
        }

        return removedKeys;
    }

    public int RemoveRowsInsertedBeforeTick(long tick) =>
        RemoveRowsInsertedBeforeTickAndReturnKeys(tick).Count;

    public IReadOnlyList<DataLinqKey> RemoveRowsInsertedBeforeTickAndReturnKeys(long tick)
    {
        var removedKeys = new List<DataLinqKey>();

        lock (rowsLock)
        {
            while (TryFindOldestKey(out var key, out var entry) && entry!.Ticks < tick)
                RemoveExisting(key, removedKeys);
        }

        return removedKeys;
    }

    public IReadOnlyList<DataLinqKey> RemoveOldestRows(int maxRows)
    {
        if (maxRows <= 0)
            return [];

        var removedKeys = new List<DataLinqKey>(maxRows);

        lock (rowsLock)
        {
            while (removedKeys.Count < maxRows)
            {
                if (!TryFindOldestKey(out var oldestKey, out _))
                    break;

                RemoveExisting(oldestKey, removedKeys);
            }
        }

        return removedKeys;
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

    public bool TryAdd(TKey key, int size, long rowContainerBytes, IImmutableInstance row)
    {
        var overheadBytes = EstimateRowEntryOverhead(key, rowContainerBytes);

        lock (rowsLock)
        {
            if (rows.ContainsKey(key))
                return false;

            var ticks = getCurrentTick();
            var node = new LinkedListNode<TKey>(key);
            // Normally append in O(1), but keep absolute age order if the wall clock moves backwards.
            var previous = evictionOrder.Last;
            while (previous is not null && rows[previous.Value].Ticks > ticks)
                previous = previous.Previous;
            rows.Add(key, new RowEntry(row, size, overheadBytes, ticks, node));
            if (previous is null)
                evictionOrder.AddFirst(node);
            else
                evictionOrder.AddAfter(previous, node);
            Interlocked.Add(ref rowPayloadBytes, size);
            Interlocked.Add(ref rowOwnedOverheadBytes, overheadBytes);
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

    public bool TryAddKey(DataLinqKey key, int size, long rowContainerBytes, IImmutableInstance row)
    {
        return TryConvertKey(key, out var providerKey) &&
            TryAdd(providerKey, size, rowContainerBytes, row);
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
        if (evictionOrder.First is { } oldest)
        {
            key = oldest.Value;
            entry = rows[key];
            return true;
        }
        key = default!;
        entry = null;
        return false;
    }

    private int RemoveExisting(TKey key, List<DataLinqKey>? removedKeys = null)
    {
        if (!rows.Remove(key, out var entry))
            return 0;

        evictionOrder.Remove(entry.EvictionNode);
        Interlocked.Add(ref rowPayloadBytes, -entry.Size);
        Interlocked.Add(ref rowOwnedOverheadBytes, -entry.OverheadBytes);
        removedKeys?.Add(ProviderKeyComponents.ToDataLinqKey(key));
        return 1;
    }

    private static long EstimateRowEntryOverhead(TKey key, long rowContainerBytes)
    {
        var overhead = CacheMemoryEstimator.RowEntryBytes;
        overhead = CacheMemoryEstimator.SaturatingAdd(overhead,
            CacheMemoryEstimator.ReferenceSize + CacheMemoryEstimator.LinkedListNodeBytes
            + CacheMemoryEstimator.EstimateArrayElementBytes(typeof(TKey)));
        overhead = CacheMemoryEstimator.SaturatingAdd(overhead, CacheMemoryEstimator.EstimateKeyPayloadBytes(key));
        overhead = CacheMemoryEstimator.SaturatingAdd(overhead, CacheMemoryEstimator.ImmutableRowInstanceBytes);
        return CacheMemoryEstimator.SaturatingAdd(overhead, rowContainerBytes);
    }
}
