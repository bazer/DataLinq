using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public abstract class Immutable<T, M>(RowData rowData, DataSourceAccess dataSource) : IImmutableInstance<M>, IEquatable<Immutable<T, M>>
    where T : IModel
    where M : IDatabaseModel
{
    protected Dictionary<RelationProperty, IKey> relationKeys = rowData.Table.Model.RelationProperties
        .ToDictionary(x => x.Value, x => KeyFactory.CreateKeyFromValues(rowData.GetValues(x.Value.RelationPart.ColumnIndex.Columns)));

    protected ConcurrentDictionary<string, object?>? lazyValues = null;

    public object? this[Column column] => rowData[column];
    public object? this[string propertyName] => rowData.GetValue(rowData.Table.Model.ValueProperties[propertyName].Column);

    public ModelMetadata Metadata() => rowData.Table.Model;
    public IKey PrimaryKeys() => KeyFactory.CreateKeyFromValues(rowData.GetValues(rowData.Table.PrimaryKeyColumns));
    public bool HasPrimaryKeysSet() => PrimaryKeys() is not NullKey;

    public RowData GetRowData() => rowData;
    IRowData IModelInstance.GetRowData() => GetRowData();

    public IEnumerable<KeyValuePair<Column, object?>> GetValues() => rowData.GetColumnAndValues();
    public IEnumerable<KeyValuePair<Column, object?>> GetValues(IEnumerable<Column> columns) => rowData.GetColumnAndValues(columns);


    protected void ClearLazy() => lazyValues?.Clear();

    protected void SetLazy<V>(string name, V value)
    {
        lazyValues ??= new ConcurrentDictionary<string, object?>();
        lazyValues[name] = value;
    }

    protected V? GetLazy<V>(string name, Func<V> fetchCode)
    {
        lazyValues ??= new ConcurrentDictionary<string, object?>();

        if (!lazyValues.TryGetValue(name, out var value) || value == null)
        {
            value = fetchCode();
            lazyValues[name] = value;
        }

        return (V?)value;
    }


    protected V? GetValue<V>(string propertyName) => rowData.GetValue<V>(rowData.Table.Model.ValueProperties[propertyName].Column);
    protected V? GetForeignKey<V>(string propertyName) where V : IImmutableInstance => GetRelation<V>(rowData.Table.Model.RelationProperties[propertyName]).SingleOrDefault();
    protected IEnumerable<V> GetRelation<V>(string propertyName) where V : IImmutableInstance => GetRelation<V>(rowData.Table.Model.RelationProperties[propertyName]);

    protected IEnumerable<V> GetRelation<V>(RelationProperty property) where V : IImmutableInstance
    {
        var source = GetDataSource();

        var otherSide = property.RelationPart.GetOtherSide();
        var result = source.Provider
            .GetTableCache(otherSide.ColumnIndex.Table)
            .GetRows(relationKeys[property], property, source)
            .Cast<V>();

        return result;
    }

    protected DataSourceAccess GetDataSource()
    {
        if (dataSource is Transaction transaction && (transaction.Status == DatabaseTransactionStatus.Committed || transaction.Status == DatabaseTransactionStatus.RolledBack))
            dataSource = dataSource.Provider.ReadOnlyAccess;

        return dataSource;
    }

    public bool Equals(Immutable<T, M>? other) => rowData.Equals(other?.GetRowData());
    public override bool Equals(object? obj) => obj is Immutable<T, M> other && Equals(other);
    public override int GetHashCode() => rowData.GetHashCode();
}
