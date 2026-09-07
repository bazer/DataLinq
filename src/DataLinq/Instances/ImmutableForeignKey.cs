using System.Linq;
using System.Threading;
using System;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public interface IImmutableForeignKey<out T> : ICacheNotification
    where T : IImmutableInstance
{
    T? Value { get; }
}

public class ImmutableForeignKey<T>(DataLinqKey foreignKey, IDataSourceAccess dataSource, RelationProperty property)
    : ImmutableForeignKey<T, DataLinqKey>(foreignKey, dataSource, property)
    where T : IImmutableInstance
{
    public static implicit operator T?(ImmutableForeignKey<T> foreignKey) => foreignKey.Value;
}

public class ImmutableForeignKey<T, TKey>(TKey foreignKey, IDataSourceAccess dataSource, RelationProperty property) : IImmutableForeignKey<T>
    where T : IImmutableInstance
    where TKey : notnull
{
    private sealed class ValueHolder(ImmutableForeignKey<T, TKey> owner, IDataSourceAccess source, T? value) : ICacheNotification
    {
        internal readonly T? Value = value;
        internal readonly IDataSourceAccess Source = source;
        internal bool Invalidated { get; set; } // Accessed under owner's loadLock.

        public void Clear()
        {
            lock (owner.loadLock)
            {
                Invalidated = true;
                if (ReferenceEquals(owner.valueHolder, this))
                    Volatile.Write(ref owner.valueHolder, null);
            }
        }
    }

    private ValueHolder? valueHolder;
    private object clearGeneration = new();

#if NET9_0_OR_GREATER
    protected readonly Lock loadLock = new();
#else
    protected readonly object loadLock = new();
#endif

    protected TableCache GetTableCache() => GetTableCache(GetDataSource());
    protected TableCache GetTableCache(IDataSourceAccess source) => source.Provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);
    public T? Value => GetInstance();

    protected IDataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction)
        {
            if (transaction.Status == DatabaseTransactionStatus.Committed ||
                transaction.Status == DatabaseTransactionStatus.RolledBack)
            {
                transaction.EnsureTerminalReadSourceFallbackAllowed(
                    "switch a transaction-bound foreign key to committed reads");
                dataSource = dataSource.Provider.ReadOnlyAccess;
            }
            else
            {
                transaction.EnsureCanRead("access a transaction-bound foreign key");
            }
        }

        return dataSource;
    }

    protected T? GetInstance()
    {
        var source = GetDataSource();
        var localHolder = Volatile.Read(ref valueHolder);
        if (localHolder is not null && ReferenceEquals(localHolder.Source, source))
        {
            GetTableCache(source).MetricsHandle.RecordRelationReferenceCacheHit();
            return localHolder.Value;
        }

        if (ProviderKeyComponents.IsNull(foreignKey))
            return default;

        object generation;
        lock (loadLock)
            generation = clearGeneration;

        // Use the same load/subscribe/publication protocol as collection relations.
        // Neither the query nor model constructors run under the relation gate.
        var tableCache = GetTableCache(source);
        var readGeneration = tableCache.CaptureReadGeneration();
        var instance = LoadInstance(tableCache, source);
        var created = new ValueHolder(this, source, instance);
        tableCache.SubscribeToChanges(
            created,
            source as Transaction,
            GetRelationCacheKey(tableCache),
            instance is null ? [] : [instance.PrimaryKeys()]);
        tableCache.MetricsHandle.RecordRelationReferenceLoad();

        lock (loadLock)
        {
            localHolder = valueHolder;
            if (localHolder is not null && ReferenceEquals(localHolder.Source, source))
                return localHolder.Value;

            if (ReferenceEquals(generation, clearGeneration) && !created.Invalidated &&
                ReferenceEquals(readGeneration, tableCache.CaptureReadGeneration()))
            {
                Volatile.Write(ref valueHolder, created);
            }
        }

        return instance;
    }

    private T? LoadInstance(TableCache tableCache, IDataSourceAccess source)
    {
        var otherSide = property.RelationPart.GetOtherSide();
        if (tableCache.Table.PrimaryKeyColumns.SequenceEqual(otherSide.ColumnIndex.Columns))
            return (T?)tableCache.GetRow(foreignKey, source);

        return (T?)tableCache
            .GetRows(foreignKey, property, source)
            .SingleOrDefault();
    }

    private RelationCacheKey? GetRelationCacheKey(TableCache tableCache)
    {
        if (ProviderKeyComponents.IsNull(foreignKey))
            return null;

        var index = property.RelationPart.GetOtherSide().ColumnIndex;
        if (!ReferenceEquals(index.Table, tableCache.Table))
            return null;

        return new RelationCacheKey(index, ProviderKeyComponents.ToDataLinqKey(foreignKey));
    }

    public void Clear()
    {
        lock (loadLock)
        {
            clearGeneration = new object();
            Volatile.Write(ref valueHolder, null);
        }
    }

    public static implicit operator T?(ImmutableForeignKey<T, TKey> foreignKey) => foreignKey.Value;

    public override string ToString() => Value?.ToString() ?? "null";
}
