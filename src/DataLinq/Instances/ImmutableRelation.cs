using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public interface IImmutableRelation<T> : IEnumerable<T> where T : IModelInstance
{
    T? this[DataLinqKey key] { get; }

    int Count { get; }
    ImmutableArray<DataLinqKey> Keys { get; }
    ImmutableArray<T> Values { get; }

    IEnumerable<KeyValuePair<DataLinqKey, T>> AsEnumerable();
    void Clear();
    bool Any() => Count != 0;

    bool ContainsKey(DataLinqKey key);

    T First()
    {
        var values = Values;
        if (values.Length == 0)
            throw new InvalidOperationException("Sequence contains no elements");

        return values[0];
    }

    T? FirstOrDefault()
    {
        var values = Values;
        return values.Length == 0 ? default : values[0];
    }

    T Last()
    {
        var values = Values;
        if (values.Length == 0)
            throw new InvalidOperationException("Sequence contains no elements");

        return values[values.Length - 1];
    }

    T? LastOrDefault()
    {
        var values = Values;
        return values.Length == 0 ? default : values[values.Length - 1];
    }

    T Single()
    {
        var values = Values;
        return values.Length switch
        {
            0 => throw new InvalidOperationException("Sequence contains no elements"),
            1 => values[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element")
        };
    }

    T? SingleOrDefault()
    {
        var values = Values;
        return values.Length switch
        {
            0 => default,
            1 => values[0],
            _ => throw new InvalidOperationException("Sequence contains more than one element")
        };
    }

    T? Get(DataLinqKey key);
    FrozenDictionary<DataLinqKey, T> ToFrozenDictionary();
}

public class ImmutableRelationMock<T> : IImmutableRelation<T> where T : IModelInstance
{
    private readonly IEnumerable<T> list;

    public ImmutableRelationMock(IEnumerable<T> list)
    {
        this.list = list;
    }

    public T? this[DataLinqKey key] => throw new System.NotImplementedException();

    public int Count => throw new System.NotImplementedException();

    public ImmutableArray<DataLinqKey> Keys => throw new System.NotImplementedException();

    public ImmutableArray<T> Values => throw new System.NotImplementedException();

    public IEnumerable<KeyValuePair<DataLinqKey, T>> AsEnumerable()
    {
        throw new System.NotImplementedException();
    }

    public void Clear()
    {
        throw new System.NotImplementedException();
    }

    public bool ContainsKey(DataLinqKey key)
    {
        throw new System.NotImplementedException();
    }

    public T? Get(DataLinqKey key)
    {
        throw new System.NotImplementedException();
    }

    public IEnumerator<T> GetEnumerator()
    {
        throw new System.NotImplementedException();
    }

    public FrozenDictionary<DataLinqKey, T> ToFrozenDictionary()
    {
        throw new System.NotImplementedException();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public class ImmutableRelation<T>(DataLinqKey foreignKey, IDataSourceAccess dataSource, RelationProperty property)
    : ImmutableRelation<T, DataLinqKey>(foreignKey, dataSource, property)
    where T : IImmutableInstance
{
}

public class ImmutableRelation<T, TKey>(TKey foreignKey, IDataSourceAccess dataSource, RelationProperty property) : IImmutableRelation<T>, ICacheNotification
    where T : IImmutableInstance
    where TKey : notnull
{
    private volatile FrozenDictionary<DataLinqKey, T>? relationInstances;
    private ImmutableArray<T> relationValues;
    private volatile bool relationValuesLoaded;

#if NET9_0_OR_GREATER
    protected readonly Lock loadLock = new();
#else
    protected readonly object loadLock = new();
#endif

    /// <summary>
    /// Indexer to get an instance by its primary key.
    /// Returns null if the key is not found.
    /// </summary>
    public T? this[DataLinqKey key] => Get(key);

    /// <summary>
    /// A method that does the same as the indexer:
    /// returns the instance corresponding to the primary key, or null if not found.
    /// </summary>
    public T? Get(DataLinqKey key) => GetInstances().TryGetValue(key, out var instance) ? instance : default;

    public ImmutableArray<T> Values => GetValues();
    public ImmutableArray<DataLinqKey> Keys => GetInstances().Keys;
    public int Count => GetValues().Length;
    public bool ContainsKey(DataLinqKey key) => GetInstances().ContainsKey(key);
    public IEnumerable<KeyValuePair<DataLinqKey, T>> AsEnumerable() => GetInstances().AsEnumerable();
    public FrozenDictionary<DataLinqKey, T> ToFrozenDictionary() => GetInstances();

    protected TableCache GetTableCache() => GetTableCache(GetDataSource());
    protected TableCache GetTableCache(IDataSourceAccess source) => source.Provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);

    protected IDataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            dataSource = dataSource.Provider.ReadOnlyAccess;

        return dataSource;
    }

    protected ImmutableArray<T> GetValues()
    {
        if (relationValuesLoaded)
        {
            GetTableCache().MetricsHandle.RecordRelationCollectionCacheHit();
            return relationValues;
        }

        lock (loadLock)
        {
            // Check if another thread loaded relationInstances while we were waiting for the lock.
            if (!relationValuesLoaded)
                return LoadValues();

            return relationValues;
        }
    }

    protected FrozenDictionary<DataLinqKey, T> GetInstances()
    {
        var localInstance = relationInstances;
        if (localInstance != null)
        {
            GetTableCache().MetricsHandle.RecordRelationCollectionCacheHit();
            return localInstance;
        }

        lock (loadLock)
        {
            if (relationInstances == null)
            {
                var valuesWereLoaded = relationValuesLoaded;
                var values = valuesWereLoaded ? relationValues : LoadValues();
                if (valuesWereLoaded)
                    GetTableCache().MetricsHandle.RecordRelationCollectionCacheHit();

                relationInstances = values.ToFrozenDictionary(x => x.PrimaryKeys());
            }

            return relationInstances;
        }
    }

    private ImmutableArray<T> LoadValues()
    {
        // Load the relation instances from the data source.
        // This will only happen once, and subsequent calls will return the cached value.
        var source = GetDataSource();
        var tableCache = GetTableCache(source);

        relationValues = tableCache
            .GetRows(foreignKey, property, source)
            .Select(x => (T)x)
            .ToImmutableArray();

        relationValuesLoaded = true;
        tableCache.MetricsHandle.RecordRelationCollectionLoad();
        tableCache.SubscribeToChanges(
            this,
            source as Transaction,
            GetRelationCacheKey(),
            relationValues.Select(x => x.PrimaryKeys()).ToArray());

        return relationValues;
    }

    private RelationCacheKey? GetRelationCacheKey()
    {
        if (ProviderKeyComponents.IsNull(foreignKey))
            return null;

        var index = property.RelationPart.GetOtherSide().ColumnIndex;
        return new RelationCacheKey(index, ProviderKeyComponents.ToDataLinqKey(foreignKey));
    }

    public void Clear()
    {
        if (relationValuesLoaded || relationInstances != null)
        {
            lock (loadLock)
            {
                relationInstances = null;
                relationValues = default;
                relationValuesLoaded = false;
            }
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        // Cast to IEnumerable<T> so that we get an IEnumerator<T>.
        return ((IEnumerable<T>)GetValues()).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
