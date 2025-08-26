using System.Linq;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public class ImmutableForeignKey<T>(IKey foreignKey, IDataSourceAccess dataSource, RelationProperty property) : ICacheNotification
    where T : IImmutableInstance
{
    // A simple private class to hold our value. This is our "tuple on the heap".
    // A reference to this class can be read and written atomically.
    private sealed class ValueHolder(T? value)
    {
        internal readonly T? Value = value;
    }

    private volatile ValueHolder? valueHolder;
    protected readonly Lock loadLock = new();

    protected TableCache GetTableCache() => GetTableCache(GetDataSource());
    protected TableCache GetTableCache(IDataSourceAccess source) => source.Provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);
    public T? Value => GetInstance();

    protected IDataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            dataSource = dataSource.Provider.ReadOnlyAccess;

        return dataSource;
    }

    protected T? GetInstance()
    {
        var localHolder = valueHolder;
        if (localHolder != null)
        {
            return localHolder.Value;
        }

        lock (loadLock)
        {
            if (valueHolder == null)
            {
                if (foreignKey is NullKey)
                {
                    valueHolder = new (default);
                }
                else
                {
                    var source = GetDataSource();
                    var tableCache = GetTableCache(source);

                    tableCache.SubscribeToChanges(this);

                    valueHolder = new((T?)tableCache
                        .GetRows(foreignKey, property, dataSource)
                        .SingleOrDefault());
                }
            }

            return valueHolder.Value;
        }
    }

    public void Clear()
    {
        if (valueHolder != null)
        {
            lock (loadLock)
            {
                if (valueHolder != null)
                {
                    valueHolder = null;
                }
            }
        }
    }

    public static implicit operator T?(ImmutableForeignKey<T> foreignKey) => foreignKey.Value;

    public override string ToString() => Value?.ToString() ?? "null";
}