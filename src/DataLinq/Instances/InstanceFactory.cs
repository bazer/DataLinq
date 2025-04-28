using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public interface IModelInstance : IModel
{
    object? this[string propertyName] { get; }
    object? this[ColumnDefinition column] { get; }
    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues();
    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns);
    bool HasPrimaryKeysSet();
    ModelDefinition Metadata();
    IKey PrimaryKeys();
    IRowData GetRowData();
    void ClearLazy();
    V? GetLazy<V>(string name, Func<V> fetchCode);
}

public interface IModelInstance<T> : IModelInstance
    where T : IDatabaseModel
{
}

public interface IImmutableInstance : IModelInstance
{
    new RowData GetRowData();
    DataSourceAccess GetDataSource();
    //static abstract IImmutableInstance NewInstance(RowData rowData, DataSourceAccess dataSource);
}

public interface IImmutableInstance<T> : IImmutableInstance, IModelInstance<T>
    where T : IDatabaseModel
{

}

public interface IImmutable<T>
    where T : IModel
{
    static T? Get(IKey key, DataSourceAccess dataSource)
    {
        var tableModel = dataSource.Provider.Metadata.TableModels.SingleOrDefault(x => x.Model.CsType.Type == typeof(T));
        if (tableModel == null)
            throw new Exception($"Found no TableDefinition for model '{typeof(T)}'");

        return (T?)dataSource.Provider.GetTableCache(tableModel.Table).GetRow(key, dataSource.Provider.ReadOnlyAccess);
    }
}


public interface IMutableInstance : IModelInstance
{
    new object? this[string propertyName] { get; set; }
    new object? this[ColumnDefinition column] { get; set; }

    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges();
    bool IsNew();
    new MutableRowData GetRowData();
    bool IsDeleted();
    void SetDeleted();
    void Reset();
    void SetLazy<V>(string name, V value);
}

public interface IMutableInstance<T> : IMutableInstance, IModelInstance<T>
    where T : IDatabaseModel
{
}


public static class InstanceFactory
{
    // Cache the compiled factory delegates keyed by the model type.
    private static readonly ConcurrentDictionary<Type, Delegate> factoryCache = new();

    public static IImmutableInstance NewImmutableRow(RowData rowData, DataSourceAccess dataSource)
    {
        var type = rowData.Table.Model.ImmutableType?.Type;

        if (type == null)
            throw new Exception($"Immutable model type not defined for '{rowData.Table.Model.CsType}'");

        if (!factoryCache.TryGetValue(type, out var del))
        {
            // Look for the constructor with parameters (RowData, DataSourceAccess)
            var constructor = type.GetConstructor([typeof(RowData), typeof(DataSourceAccess)]);
            if (constructor == null)
                throw new Exception($"No matching constructor found for {type.FullName}");

            // Build the expression tree: (rowData, dataSource) => new T(rowData, dataSource)
            var rowDataParam = Expression.Parameter(typeof(RowData), "rowData");
            var dataSourceParam = Expression.Parameter(typeof(DataSourceAccess), "dataSource");
            var newExp = Expression.New(constructor, rowDataParam, dataSourceParam);
            var lambda = Expression.Lambda<Func<RowData, DataSourceAccess, IImmutableInstance>>(newExp, rowDataParam, dataSourceParam);

            // Compile the delegate and add it to the cache
            var compiled = lambda.Compile();
            factoryCache.TryAdd(type, compiled);
            del = compiled;
        }

        return ((Func<RowData, DataSourceAccess, IImmutableInstance>)del)(rowData, dataSource) ?? throw new Exception($"Failed to create instance of immutable model type '{rowData.Table.Model.CsType}'");
    }

    public static T NewImmutableRow<T>(RowData rowData, DataSourceAccess dataSource)
    {
        return (T)NewImmutableRow(rowData, dataSource);
    }

    public static T NewDatabase<T>(DataSourceAccess dataSource)
    {
        var db = Activator.CreateInstance(typeof(T), dataSource);

        if (db == null)
            throw new Exception($"Failed to create instance of database model type '{typeof(T)}'");

        return (T)db;
    }
}