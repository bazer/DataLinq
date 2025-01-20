using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;
public class ImmutableRelation<T>(IKey foreignKey, DataSourceAccess dataSource, RelationProperty property)
    : IEnumerable<T> where T: IImmutableInstance
{
    protected ConcurrentDictionary<IKey, T>? relationInstances;

    protected IEnumerable<IImmutableInstance> GetRelation()
    {
        var source = GetDataSource();

        var otherSide = property.RelationPart.GetOtherSide();
        var result = source.Provider
            .GetTableCache(otherSide.ColumnIndex.Table)
            .GetRows(foreignKey, property, source);

        return result;
    }

    protected DataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            dataSource = dataSource.Provider.ReadOnlyAccess;

        return dataSource;
    }

    protected IEnumerable<T> LoadInstances()
    {
        if (relationInstances == null || relationInstances.IsEmpty)
        {
            relationInstances ??= [];
       
            foreach (var instance in GetRelation())
                relationInstances.TryAdd(instance.PrimaryKeys(), (T)instance);

            //relationInstances.ToFrozenDictionary();
        }

        return relationInstances.Values;
    }

    public void Clear()
    {
        relationInstances?.Clear();
    }

    public IEnumerator<T> GetEnumerator()
    {
        return LoadInstances().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

