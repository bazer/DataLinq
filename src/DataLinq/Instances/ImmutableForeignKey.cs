using System.Linq;
using System.Threading;
using DataLinq.Cache;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public class ImmutableForeignKey<T>(IKey foreignKey, DataSourceAccess dataSource, RelationProperty property) : ICacheNotification
    where T : IImmutableInstance
{
    protected T? cachedValue;
    protected bool isValueCached;

    // Flag to ensure we only attach our listener once.
    protected bool isSubscribed = false;
    protected readonly Lock loadLock = new();

    protected TableCache GetTableCache() => GetTableCache(GetDataSource());
    protected TableCache GetTableCache(DataSourceAccess source) => source.Provider.GetTableCache(property.RelationPart.GetOtherSide().ColumnIndex.Table);
    public T? Value => GetInstance();

    protected DataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            dataSource = dataSource.Provider.ReadOnlyAccess;

        return dataSource;
    }

    protected T? GetInstance()
    {
        if (!isValueCached)
        {
            lock (loadLock)
            {
                if (!isValueCached)
                {
                    if (foreignKey is NullKey)
                    {
                        cachedValue = default;
                    }
                    else
                    {
                        var source = GetDataSource();
                        var tableCache = GetTableCache(source);

                        if (!isSubscribed)
                        {
                            tableCache.SubscribeToChanges(this);
                            isSubscribed = true;
                        }

                        cachedValue = (T?)tableCache
                            .GetRows(foreignKey, property, dataSource)
                            .SingleOrDefault();
                    }

                    isValueCached = true;
                }
            }
        }

        return cachedValue;
    }

    public void Clear()
    {
        if (isValueCached)
        {
            lock (loadLock)
            {
                if (isValueCached)
                {
                    cachedValue = default;
                    isValueCached = false;
                }
            }
        }
    }

    public static implicit operator T?(ImmutableForeignKey<T> foreignKey) => foreignKey.Value;

    public override string ToString() => Value?.ToString() ?? "null";
}