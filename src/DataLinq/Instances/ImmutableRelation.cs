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
    private RelationSnapshot? snapshot;
    private object clearGeneration = new();

    // Each subscription belongs to exactly one load. Losing or superseded loads
    // are weakly held by the notification queue and cannot clear a newer snapshot.
    private sealed class RelationSnapshot(
        ImmutableRelation<T, TKey> owner,
        IDataSourceAccess source,
        ImmutableArray<T> values) : ICacheNotification
    {
        private FrozenDictionary<DataLinqKey, T>? instances;
        internal IDataSourceAccess Source { get; } = source;
        internal ImmutableArray<T> Values { get; } = values;
        internal bool Invalidated { get; set; } // Accessed under owner's loadLock.

        internal FrozenDictionary<DataLinqKey, T> GetInstances()
        {
            var current = Volatile.Read(ref instances);
            if (current is not null)
                return current;

            var created = Values.ToFrozenDictionary(row => row.PrimaryKeys());
            return Interlocked.CompareExchange(ref instances, created, null) ?? created;
        }

        public void Clear()
        {
            lock (owner.loadLock)
            {
                Invalidated = true;
                if (ReferenceEquals(owner.snapshot, this))
                    Volatile.Write(ref owner.snapshot, null);
            }
        }
    }

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
        if (dataSource is Transaction transaction)
        {
            if (transaction.Status == DatabaseTransactionStatus.Committed ||
                transaction.Status == DatabaseTransactionStatus.RolledBack)
            {
                transaction.EnsureTerminalReadSourceFallbackAllowed(
                    "switch a transaction-bound relation to committed reads");
                dataSource = dataSource.Provider.ReadOnlyAccess;
            }
            else
            {
                transaction.EnsureCanRead("access a transaction-bound relation");
            }
        }

        return dataSource;
    }

    protected ImmutableArray<T> GetValues() => GetSnapshot().Values;

    protected FrozenDictionary<DataLinqKey, T> GetInstances() => GetSnapshot().GetInstances();

    private RelationSnapshot GetSnapshot()
    {
        // Validate transaction state even on cache hits and never reuse a
        // transaction-local snapshot after switching to committed reads.
        var source = GetDataSource();
        var tableCache = GetTableCache(source);
        var current = Volatile.Read(ref snapshot);
        if (current is not null && ReferenceEquals(current.Source, source))
        {
            tableCache.MetricsHandle.RecordRelationCollectionCacheHit();
            return current;
        }

        object generation;
        lock (loadLock)
            generation = clearGeneration;

        // I/O and user model construction must not block Clear or notification
        // callbacks. Concurrent misses may load twice; only one snapshot wins.
        var readGeneration = tableCache.CaptureReadGeneration();
        var values = ToImmutableRelationValues(tableCache.GetRows(foreignKey, property, source));
        var created = new RelationSnapshot(this, source, values);
        tableCache.MetricsHandle.RecordRelationCollectionLoad();
        tableCache.SubscribeToChanges(
            created,
            source as Transaction,
            GetRelationCacheKey(),
            GetPrimaryKeys(values));

        lock (loadLock)
        {
            current = snapshot;
            if (current is not null && ReferenceEquals(current.Source, source))
                return current;

            // A notification before Subscribe is detected by the table generation;
            // one after Subscribe invalidates this candidate, even before publication.
            if (ReferenceEquals(generation, clearGeneration) && !created.Invalidated &&
                ReferenceEquals(readGeneration, tableCache.CaptureReadGeneration()))
            {
                Volatile.Write(ref snapshot, created);
            }
        }

        return created;
    }

    private static ImmutableArray<T> ToImmutableRelationValues(IEnumerable<IImmutableInstance> rows)
    {
        if (rows is IImmutableInstance[] rowArray)
        {
            if (rowArray.Length == 0)
                return ImmutableArray<T>.Empty;

            if (rowArray.Length == 1)
                return ImmutableArray.Create((T)rowArray[0]);

            var arrayBuilder = ImmutableArray.CreateBuilder<T>(rowArray.Length);
            for (var i = 0; i < rowArray.Length; i++)
                arrayBuilder.Add((T)rowArray[i]);

            return arrayBuilder.MoveToImmutable();
        }

        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var row in rows)
            builder.Add((T)row);

        return builder.ToImmutable();
    }

    private static DataLinqKey[] GetPrimaryKeys(ImmutableArray<T> values)
    {
        if (values.IsDefaultOrEmpty)
            return [];

        var primaryKeys = new DataLinqKey[values.Length];
        for (var i = 0; i < values.Length; i++)
            primaryKeys[i] = values[i].PrimaryKeys();

        return primaryKeys;
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
        lock (loadLock)
        {
            clearGeneration = new object();
            Volatile.Write(ref snapshot, null);
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
