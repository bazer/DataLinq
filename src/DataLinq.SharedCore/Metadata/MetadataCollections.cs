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
        set
        {
            ThrowIfFrozen();
            items[index] = value;
        }
    }

    public void Add(T item)
    {
        ThrowIfFrozen();
        items.Add(item);
    }

    public void AddRange(IEnumerable<T> range)
    {
        ThrowIfFrozen();
        items.AddRange(range);
    }

    public void Clear()
    {
        ThrowIfFrozen();
        items.Clear();
    }

    public bool Contains(T item) => items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => items.GetEnumerator();

    public int IndexOf(T item) => items.IndexOf(item);

    public void Insert(int index, T item)
    {
        ThrowIfFrozen();
        items.Insert(index, item);
    }

    public bool Remove(T item)
    {
        ThrowIfFrozen();
        return items.Remove(item);
    }

    public void RemoveAt(int index)
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
        set
        {
            ThrowIfFrozen();
            items[key] = value;
        }
    }

    public void Add(TKey key, TValue value)
    {
        ThrowIfFrozen();
        items.Add(key, value);
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        ThrowIfFrozen();
        ((ICollection<KeyValuePair<TKey, TValue>>)items).Add(item);
    }

    public void Clear()
    {
        ThrowIfFrozen();
        items.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item) => ((ICollection<KeyValuePair<TKey, TValue>>)items).Contains(item);

    public bool ContainsKey(TKey key) => items.ContainsKey(key);

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)items).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => items.GetEnumerator();

    public bool Remove(TKey key)
    {
        ThrowIfFrozen();
        return items.Remove(key);
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
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
