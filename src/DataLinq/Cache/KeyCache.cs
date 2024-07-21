using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DataLinq.Instances;

namespace DataLinq.Cache;
public class KeyCache<T> where T : IKey
{
    private ConcurrentDictionary<int, T> cachedKeys = new();

    public int Count => cachedKeys.Count;
    public IEnumerable<T> Values => cachedKeys.Values;

    public bool TryAdd(T keys) => cachedKeys.TryAdd(keys.GetHashCode(), keys);

    public bool ContainsKey(int hashCode) => cachedKeys.ContainsKey(hashCode);
    public bool ContainsKey(T keys) => cachedKeys.ContainsKey(keys.GetHashCode());

    public bool TryGetValue(int hashCode, out T? keys) => cachedKeys.TryGetValue(hashCode, out keys);

    public void Clear()
    {
        cachedKeys.Clear();
    }
}
