using System;
using System.Collections;
using System.Collections.Generic;

namespace DataLinq.Metadata;

public sealed class MetadataList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> items;

    public MetadataList()
    {
        items = [];
    }

    public MetadataList(IEnumerable<T> items)
    {
        this.items = [.. items];
    }

    public bool IsFrozen { get; private set; }
    public int Count => items.Count;
    public bool IsReadOnly => IsFrozen;

    public T this[int index]
    {
        get => items[index];
        [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
        set
        {
            SetCore(index, value);
        }
    }

    internal void SetCore(int index, T value)
    {
        ThrowIfFrozen();
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
        items.Add(item);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void AddRange(IEnumerable<T> range)
    {
        AddRangeCore(range);
    }

    internal void AddRangeCore(IEnumerable<T> range)
    {
        ThrowIfFrozen();
        items.AddRange(range);
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

    public bool Contains(T item) => items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    public int IndexOf(T item) => items.IndexOf(item);

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void Insert(int index, T item)
    {
        InsertCore(index, item);
    }

    internal void InsertCore(int index, T item)
    {
        ThrowIfFrozen();
        items.Insert(index, item);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public bool Remove(T item)
    {
        return RemoveCore(item);
    }

    internal bool RemoveCore(T item)
    {
        ThrowIfFrozen();
        return items.Remove(item);
    }

    [Obsolete(MetadataMutationGuard.PublicMutationObsoleteMessage)]
    public void RemoveAt(int index)
    {
        RemoveAtCore(index);
    }

    internal void RemoveAtCore(int index)
    {
        ThrowIfFrozen();
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
