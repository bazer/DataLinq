using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Execution;
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

    /// <summary>
    /// Returns relation rows paired with their primary keys.
    /// Resolving the keyed collection may synchronously load the relation.
    /// </summary>
    IEnumerable<KeyValuePair<DataLinqKey, T>> AsKeyValuePairs();
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
    private Lazy<Snapshot> snapshot;

    public ImmutableRelationMock(IEnumerable<T> list)
    {
        this.list = list ?? throw new ArgumentNullException(nameof(list));
        snapshot = CreateSnapshot();
    }

    public T? this[DataLinqKey key] => Get(key);

    public int Count => Values.Length;

    public ImmutableArray<DataLinqKey> Keys => ToFrozenDictionary().Keys;

    public ImmutableArray<T> Values => Volatile.Read(ref snapshot).Value.Values;

    public IEnumerable<KeyValuePair<DataLinqKey, T>> AsKeyValuePairs()
    {
        return ToFrozenDictionary().AsEnumerable();
    }

    public void Clear()
    {
        Interlocked.Exchange(ref snapshot, CreateSnapshot());
    }

    public bool ContainsKey(DataLinqKey key)
    {
        return ToFrozenDictionary().ContainsKey(key);
    }

    public T? Get(DataLinqKey key)
    {
        return ToFrozenDictionary().TryGetValue(key, out var value) ? value : default;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return ((IEnumerable<T>)Values).GetEnumerator();
    }

    public FrozenDictionary<DataLinqKey, T> ToFrozenDictionary()
    {
        return Volatile.Read(ref snapshot).Value.Instances.Value;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private Lazy<Snapshot> CreateSnapshot() => new(() => new Snapshot(list.ToImmutableArray()));

    private sealed class Snapshot(ImmutableArray<T> values)
    {
        internal ImmutableArray<T> Values { get; } = values;
        // ToDictionary rejects ambiguous duplicate primary keys before freezing.
        internal Lazy<FrozenDictionary<DataLinqKey, T>> Instances { get; } =
            new(() => values.ToDictionary(value => value.PrimaryKeys()).ToFrozenDictionary());
    }
}

public class ImmutableRelation<T>(DataLinqKey foreignKey, IDataSourceAccess dataSource, RelationProperty property)
    : ImmutableRelation<T, DataLinqKey>(foreignKey, dataSource, property)
    where T : IImmutableInstance
{
}

public partial class ImmutableRelation<T, TKey>(TKey foreignKey, IDataSourceAccess dataSource, RelationProperty property) : IImmutableRelation<T>, ICacheNotification
    where T : IImmutableInstance
    where TKey : notnull
{
    private RelationSnapshot? snapshot;
    private object clearGeneration = new();
    private readonly SemaphoreSlim loadSlot = new(1, 1);

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
    /// <inheritdoc />
    public IEnumerable<KeyValuePair<DataLinqKey, T>> AsKeyValuePairs() => new KeyValueSequence(this);
    public FrozenDictionary<DataLinqKey, T> ToFrozenDictionary() => GetInstances();

    protected TableCache GetTableCache() => GetTableCache(GetDataSource());
    protected TableCache GetTableCache(IDataSourceAccess source) => source.Provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);

    protected IDataSourceAccess GetDataSource()
        => ResolveDataSource(validateRead: true);

    private IDataSourceAccess ResolveDataSource(bool validateRead)
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
            else if (validateRead)
            {
                transaction.EnsureCanRead("access a transaction-bound relation");
            }
        }

        return dataSource;
    }

    protected ImmutableArray<T> GetValues()
    {
        var source = GetDataSource();
        using var read = DataSourceAccess.BeginRead(source, "materialize relation values");
        try
        {
            return GetSnapshot(source, read?.Step).Values;
        }
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    protected FrozenDictionary<DataLinqKey, T> GetInstances()
    {
        var source = GetDataSource();
        using var read = DataSourceAccess.BeginRead(source, "materialize a relation dictionary");
        try
        {
            return GetSnapshot(source, read?.Step).GetInstances();
        }
        catch (Exception failure)
        {
            read?.ReportFailure(failure);
            throw;
        }
    }

    private RelationSnapshot GetSnapshot(IDataSourceAccess source, TransactionOperationGate.Step? owner)
    {
        // Validate transaction state even on cache hits and never reuse a
        // transaction-local snapshot after switching to committed reads.
        DataSourceAccess.EnsureReadAllowed(source, "read a relation snapshot", owner);
        var tableCache = GetTableCache(source);
        var current = Volatile.Read(ref snapshot);
        if (current is not null && ReferenceEquals(current.Source, source))
        {
            tableCache.MetricsHandle.RecordRelationCollectionCacheHit();
            return current;
        }

        loadSlot.Wait();
        try
        {
            current = Volatile.Read(ref snapshot);
            if (current is not null && ReferenceEquals(current.Source, source))
            {
                tableCache.MetricsHandle.RecordRelationCollectionCacheHit();
                return current;
            }
            return LoadSnapshot(source, owner, tableCache);
        }
        finally { loadSlot.Release(); }
    }

    private RelationSnapshot LoadSnapshot(IDataSourceAccess source, TransactionOperationGate.Step? owner, TableCache tableCache)
    {
        object generation;
        lock (loadLock)
            generation = clearGeneration;

        // The load slot coordinates callers, while Clear and notifications use
        // only the short state lock and remain independent of I/O.
        var readGeneration = tableCache.CaptureReadGeneration();
        var relationKey = GetRelationCacheKey();
        var values = ToImmutableRelationValues(tableCache.GetRows(foreignKey, property, source, owner));
        return PublishSnapshot(source, tableCache, values, generation, readGeneration, relationKey);
    }

    private RelationSnapshot PublishSnapshot(IDataSourceAccess source, TableCache tableCache,
        ImmutableArray<T> values, object generation, RowReadGeneration readGeneration,
        RelationCacheKey? relationKey, bool buildDictionary = false)
    {
        var created = new RelationSnapshot(this, source, values);
        if (buildDictionary) _ = created.GetInstances();
        tableCache.MetricsHandle.RecordRelationCollectionLoad();
        tableCache.SubscribeToChanges(
            created,
            source as Transaction,
            relationKey,
            GetPrimaryKeys(values));

        lock (loadLock)
        {
            var current = snapshot;
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
        var source = ResolveDataSource(validateRead: false);
        return DataSourceAccess.ReadSequence(source, "enumerate a relation",
            owner => (IEnumerable<T>)GetSnapshot(source, owner).Values).GetEnumerator();
    }

    private sealed class KeyValueSequence(ImmutableRelation<T, TKey> relation)
        : IEnumerable<KeyValuePair<DataLinqKey, T>>
    {
        public IEnumerator<KeyValuePair<DataLinqKey, T>> GetEnumerator()
        {
            var source = relation.ResolveDataSource(validateRead: false);
            return DataSourceAccess.ReadSequence(source, "enumerate relation keys and values",
                owner => relation.GetSnapshot(source, owner).GetInstances().AsEnumerable()).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
