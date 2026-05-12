using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DataLinq.Metadata;

public sealed class MetadataCollection<T> : IReadOnlyList<T>
{
    public static MetadataCollection<T> Empty { get; } = new([]);

    private readonly T[] items;

    public MetadataCollection(IEnumerable<T> items)
    {
        this.items = items?.ToArray() ?? [];
    }

    public int Count => items.Length;
    public int Length => items.Length;

    public T this[int index] => items[index];

    public T[] ToArray() => items.ToArray();

    public Enumerator GetEnumerator() => new(items);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<T>
    {
        private readonly T[] items;
        private int index;

        internal Enumerator(T[] items)
        {
            this.items = items;
            index = -1;
        }

        public T Current => items[index];

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var nextIndex = index + 1;
            if ((uint)nextIndex >= (uint)items.Length)
            {
                index = items.Length;
                return false;
            }

            index = nextIndex;
            return true;
        }

        public void Reset()
        {
            index = -1;
        }

        public void Dispose()
        {
        }
    }
}

public sealed class MetadataList<T> : IList<T>, IReadOnlyList<T>
{
    private List<T>? items;

    public MetadataList()
    {
    }

    public MetadataList(IEnumerable<T> items)
    {
        this.items = [.. items];
    }

    public bool IsFrozen { get; private set; }
    public int Count => items?.Count ?? 0;
    public bool IsReadOnly => IsFrozen;

    public T this[int index]
    {
        get
        {
            if (items is null)
                throw new ArgumentOutOfRangeException(nameof(index));

            return items[index];
        }
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetCore(index, value);
        }
    }

    internal void SetCore(int index, T value)
    {
        ThrowIfFrozen();
        if (items is null)
            throw new ArgumentOutOfRangeException(nameof(index));

        items[index] = value;
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void Add(T item)
    {
        AddCore(item);
    }

    internal void AddCore(T item)
    {
        ThrowIfFrozen();
        (items ??= []).Add(item);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddRange(IEnumerable<T> range)
    {
        AddRangeCore(range);
    }

    internal void AddRangeCore(IEnumerable<T> range)
    {
        ThrowIfFrozen();
        if (range is null)
            throw new ArgumentNullException(nameof(range));

        if (range is ICollection<T> { Count: 0 })
            return;

        (items ??= []).AddRange(range);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void Clear()
    {
        ClearCore();
    }

    internal void ClearCore()
    {
        ThrowIfFrozen();
        items?.Clear();
    }

    public bool Contains(T item) => items?.Contains(item) ?? false;

    public void CopyTo(T[] array, int arrayIndex)
    {
        if (items is null)
        {
            Array.Empty<T>().CopyTo(array, arrayIndex);
            return;
        }

        items.CopyTo(array, arrayIndex);
    }

    public IEnumerator<T> GetEnumerator() =>
        items is null
            ? ((IEnumerable<T>)Array.Empty<T>()).GetEnumerator()
            : items.GetEnumerator();

    public int IndexOf(T item) => items?.IndexOf(item) ?? -1;

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void Insert(int index, T item)
    {
        InsertCore(index, item);
    }

    internal void InsertCore(int index, T item)
    {
        ThrowIfFrozen();
        (items ??= []).Insert(index, item);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public bool Remove(T item)
    {
        return RemoveCore(item);
    }

    internal bool RemoveCore(T item)
    {
        ThrowIfFrozen();
        return items?.Remove(item) ?? false;
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void RemoveAt(int index)
    {
        RemoveAtCore(index);
    }

    internal void RemoveAtCore(int index)
    {
        ThrowIfFrozen();
        if (items is null)
            throw new ArgumentOutOfRangeException(nameof(index));

        items.RemoveAt(index);
    }

    internal void Freeze()
    {
        IsFrozen = true;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}

public sealed class MetadataDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> items = new();

    public bool IsFrozen { get; private set; }
    public int Count => items.Count;
    public bool IsReadOnly => IsFrozen;
    public ICollection<TKey> Keys => items.Keys;
    public ICollection<TValue> Values => items.Values;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => items.Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => items.Values;

    public TValue this[TKey key]
    {
        get => items[key];
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetCore(key, value);
        }
    }

    internal void SetCore(TKey key, TValue value)
    {
        ThrowIfFrozen();
        items[key] = value;
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void Add(TKey key, TValue value)
    {
        AddCore(key, value);
    }

    internal void AddCore(TKey key, TValue value)
    {
        ThrowIfFrozen();
        items.Add(key, value);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void Add(KeyValuePair<TKey, TValue> item)
    {
        AddCore(item);
    }

    internal void AddCore(KeyValuePair<TKey, TValue> item)
    {
        ThrowIfFrozen();
        ((ICollection<KeyValuePair<TKey, TValue>>)items).Add(item);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void Clear()
    {
        ClearCore();
    }

    internal void ClearCore()
    {
        ThrowIfFrozen();
        items.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item) => ((ICollection<KeyValuePair<TKey, TValue>>)items).Contains(item);

    public bool ContainsKey(TKey key) => items.ContainsKey(key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)items).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => items.GetEnumerator();

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public bool Remove(TKey key)
    {
        return RemoveCore(key);
    }

    internal bool RemoveCore(TKey key)
    {
        ThrowIfFrozen();
        return items.Remove(key);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return RemoveCore(item);
    }

    internal bool RemoveCore(KeyValuePair<TKey, TValue> item)
    {
        ThrowIfFrozen();
        return ((ICollection<KeyValuePair<TKey, TValue>>)items).Remove(item);
    }

    public bool TryGetValue(TKey key, out TValue value) => items.TryGetValue(key, out value!);

    internal void Freeze()
    {
        IsFrozen = true;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void ThrowIfFrozen() => MetadataMutationGuard.ThrowIfFrozen(IsFrozen, this);
}
