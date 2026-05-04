using System;
using System.Collections.Generic;
using System.Linq;
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
    public static IImmutableInstance NewImmutableRow(IRowData rowData, IDataSourceAccess dataSource)
    {
        dataSource.Provider.GetTableCache(rowData.Table).MetricsHandle.RecordRowMaterialization();

        if (rowData.Table.Model.ImmutableFactory is not Func<IRowData, IDataSourceAccess, IImmutableInstance> factory)
        {
            throw new Exception(
                $"Generated immutable factory not defined for '{rowData.Table.Model.CsType}'. " +
                "Run the DataLinq source generator and ensure the immutable model factory hook is compiled into the application.");
        }

        return factory(rowData, dataSource) ?? throw new Exception($"Failed to create instance of immutable model type '{rowData.Table.Model.CsType}'");
    }

    public static T NewImmutableRow<T>(IRowData rowData, IDataSourceAccess dataSource)
    {
        return (T)NewImmutableRow(rowData, dataSource);
    }

    public static T NewDatabase<T>(IDataSourceAccess dataSource)
        where T : class, IDatabaseModel, IDataLinqGeneratedDatabaseModel<T>
    {
        return T.NewDataLinqDatabase(dataSource);
    }
}
