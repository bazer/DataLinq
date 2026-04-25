using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Diagnostics;
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
    new IRowData GetRowData();
    IDataSourceAccess GetDataSource();
    //static abstract IImmutableInstance NewInstance(RowData rowData, DataSourceAccess dataSource);
}

public interface IImmutableInstance<T> : IImmutableInstance, IModelInstance<T>
    where T : IDatabaseModel
{

}

public interface IImmutable<T>
    where T : IModel
{
    static T? Get(IKey key, IDataSourceAccess dataSource)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key), "Key cannot be null");

        if (dataSource == null)
            throw new ArgumentNullException(nameof(dataSource), "Data source cannot be null");

        var tableModel = dataSource.Provider.Metadata.TableModels.SingleOrDefault(x => x.Model.CsType.Type == typeof(T));
        if (tableModel == null)
            throw new Exception($"Found no TableDefinition for model '{typeof(T)}'");

        return (T?)dataSource.Provider.GetTableCache(tableModel.Table).GetRow(key, dataSource);
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
    private const string GeneratedImmutableFactoryMethodName = "NewDataLinqImmutableInstance";

    // Cache the generated or fallback factory delegates keyed by the concrete immutable model type.
    private static readonly ConcurrentDictionary<Type, Func<IRowData, IDataSourceAccess, IImmutableInstance>> factoryCache = new();
    private static readonly ConcurrentDictionary<Type, Func<IDataSourceAccess, object>> databaseFactoryCache = new();

    public static IImmutableInstance NewImmutableRow(IRowData rowData, IDataSourceAccess dataSource)
    {
        dataSource.Provider.GetTableCache(rowData.Table).MetricsHandle.RecordRowMaterialization();

        var type = rowData.Table.Model.ImmutableType?.Type;

        if (type == null)
            throw new Exception($"Immutable model type not defined for '{rowData.Table.Model.CsType}'");

        var factory = factoryCache.GetOrAdd(type, CreateImmutableFactory);

        return factory(rowData, dataSource) ?? throw new Exception($"Failed to create instance of immutable model type '{rowData.Table.Model.CsType}'");
    }

    public static T NewImmutableRow<T>(IRowData rowData, IDataSourceAccess dataSource)
    {
        return (T)NewImmutableRow(rowData, dataSource);
    }

    public static T NewDatabase<T>(IDataSourceAccess dataSource)
    {
        var factory = databaseFactoryCache.GetOrAdd(typeof(T), CreateDatabaseFactory);
        var db = factory(dataSource);

        if (db == null)
            throw new Exception($"Failed to create instance of database model type '{typeof(T)}'");

        return (T)db;
    }

    private static Func<IRowData, IDataSourceAccess, IImmutableInstance> CreateImmutableFactory(Type type)
    {
        var generatedFactory = type.GetMethod(
            GeneratedImmutableFactoryMethodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(IRowData), typeof(IDataSourceAccess)],
            modifiers: null);

        if (generatedFactory is not null)
        {
            if (!typeof(IImmutableInstance).IsAssignableFrom(generatedFactory.ReturnType))
            {
                throw new Exception(
                    $"Generated immutable factory '{type.FullName}.{GeneratedImmutableFactoryMethodName}' must return '{typeof(IImmutableInstance).FullName}'.");
            }

            return (Func<IRowData, IDataSourceAccess, IImmutableInstance>)generatedFactory.CreateDelegate(typeof(Func<IRowData, IDataSourceAccess, IImmutableInstance>));
        }

        var constructor = type.GetConstructor([typeof(IRowData), typeof(IDataSourceAccess)]);
        if (constructor == null)
            throw new Exception($"No matching constructor found for {type.FullName}");

        var rowDataParam = Expression.Parameter(typeof(IRowData), "rowData");
        var dataSourceParam = Expression.Parameter(typeof(IDataSourceAccess), "dataSource");
        var newExp = Expression.New(constructor, rowDataParam, dataSourceParam);
        var lambda = Expression.Lambda<Func<IRowData, IDataSourceAccess, IImmutableInstance>>(newExp, rowDataParam, dataSourceParam);

        return lambda.Compile();
    }

    private static Func<IDataSourceAccess, object> CreateDatabaseFactory(Type type)
    {
        var constructor = type.GetConstructor([typeof(DataSourceAccess)]);
        var dataSourceParameterType = typeof(DataSourceAccess);

        if (constructor is null)
        {
            constructor = type.GetConstructor([typeof(IDataSourceAccess)]);
            dataSourceParameterType = typeof(IDataSourceAccess);
        }

        if (constructor == null)
            throw new Exception($"No matching constructor found for database model type {type.FullName}");

        var dataSourceParam = Expression.Parameter(typeof(IDataSourceAccess), "dataSource");
        Expression constructorArgument = dataSourceParameterType == typeof(IDataSourceAccess)
            ? dataSourceParam
            : Expression.Convert(dataSourceParam, dataSourceParameterType);
        var newExp = Expression.New(constructor, constructorArgument);
        var lambda = Expression.Lambda<Func<IDataSourceAccess, object>>(newExp, dataSourceParam);

        return lambda.Compile();
    }
}
