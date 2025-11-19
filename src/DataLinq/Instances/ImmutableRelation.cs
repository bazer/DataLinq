using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public interface IImmutableRelation<T> : IEnumerable<T> where T : IModelInstance
{
    T? this[IKey key] { get; }

    int Count { get; }
    ImmutableArray<IKey> Keys { get; }
    ImmutableArray<T> Values { get; }

    IEnumerable<KeyValuePair<IKey, T>> AsEnumerable();
    void Clear();
    bool ContainsKey(IKey key);
    T? Get(IKey key);
    FrozenDictionary<IKey, T> ToFrozenDictionary();
}

public class ImmutableRelationMock<T> : IImmutableRelation<T> where T : IModelInstance
{
    private readonly IEnumerable<T> list;

    public ImmutableRelationMock(IEnumerable<T> list)
    {
        this.list = list;
    }

    public T? this[IKey key] => throw new System.NotImplementedException();

    public int Count => throw new System.NotImplementedException();

    public ImmutableArray<IKey> Keys => throw new System.NotImplementedException();

    public ImmutableArray<T> Values => throw new System.NotImplementedException();

    public IEnumerable<KeyValuePair<IKey, T>> AsEnumerable()
    {
        throw new System.NotImplementedException();
    }

    public void Clear()
    {
        throw new System.NotImplementedException();
    }

    public bool ContainsKey(IKey key)
    {
        throw new System.NotImplementedException();
    }

    public T? Get(IKey key)
    {
        throw new System.NotImplementedException();
    }

    public IEnumerator<T> GetEnumerator()
    {
        throw new System.NotImplementedException();
    }

    public FrozenDictionary<IKey, T> ToFrozenDictionary()
    {
        throw new System.NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class ImmutableRelation<T>(IKey foreignKey, IDataSourceAccess dataSource, RelationProperty property) : IImmutableRelation<T>, ICacheNotification
    where T : IImmutableInstance
{
    private volatile FrozenDictionary<IKey, T>? relationInstances;

#if NET9_0_OR_GREATER
    protected readonly Lock loadLock = new();
#else
    protected readonly object loadLock = new();
#endif

    /// <summary>
    /// Indexer to get an instance by its primary key.
    /// Returns null if the key is not found.
    /// </summary>
    public T? this[IKey key] => Get(key);

    /// <summary>
    /// A method that does the same as the indexer:
    /// returns the instance corresponding to the primary key, or null if not found.
    /// </summary>
    public T? Get(IKey key) => GetInstances().TryGetValue(key, out var instance) ? instance : default;

    public ImmutableArray<T> Values => GetInstances().Values;
    public ImmutableArray<IKey> Keys => GetInstances().Keys;
    public int Count => GetInstances().Count;
    public bool ContainsKey(IKey key) => GetInstances().ContainsKey(key);
    public IEnumerable<KeyValuePair<IKey, T>> AsEnumerable() => GetInstances().AsEnumerable();
    public FrozenDictionary<IKey, T> ToFrozenDictionary() => GetInstances();

    protected TableCache GetTableCache() => GetTableCache(GetDataSource());
    protected TableCache GetTableCache(IDataSourceAccess source) => source.Provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);

    protected IDataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            dataSource = dataSource.Provider.ReadOnlyAccess;

        return dataSource;
    }

    protected FrozenDictionary<IKey, T> GetInstances()
    {
        // Copy to local instance in case relationInstance gets changed
        // by another thread inbetween the null check and return.
        var localInstance = relationInstances;
        if (localInstance != null)
            return localInstance;

        lock (loadLock)
        {
            // Check if another thread loaded relationInstances while we were waiting for the lock.
            if (relationInstances == null)
            {
                // Load the relation instances from the data source.
                // This will only happen once, and subsequent calls will return the cached value.
                var source = GetDataSource();
                var tableCache = GetTableCache(source);

                tableCache.SubscribeToChanges(this);

                relationInstances = tableCache
                    .GetRows(foreignKey, property, source)
                    .Select(x => (T)x)
                    .ToFrozenDictionary(x => x.PrimaryKeys());
            }

            return relationInstances;
        }
    }

    public void Clear()
    {
        if (relationInstances != null)
        {
            lock (loadLock)
            {
                relationInstances = null;
            }
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        // Cast to IEnumerable<T> so that we get an IEnumerator<T>.
        return ((IEnumerable<T>)GetInstances().Values).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}