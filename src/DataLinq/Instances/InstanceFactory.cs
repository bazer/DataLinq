using System;
using System.Collections.Generic;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public interface IModelInstance : IModel
{
    object? this[string propertyName] { get; }
    object? this[Column column] { get; }
    IEnumerable<KeyValuePair<Column, object?>> GetValues();
    IEnumerable<KeyValuePair<Column, object?>> GetValues(IEnumerable<Column> columns);
    bool HasPrimaryKeysSet();
    ModelMetadata Metadata();
    IKey PrimaryKeys();
    IRowData GetRowData();
}

public interface IModelInstance<T> : IModelInstance
    where T : IDatabaseModel
{
}

public interface IImmutableInstance : IModelInstance
{
    new RowData GetRowData();
    DataSourceAccess GetDataSource();
}

public interface IImmutableInstance<T> : IImmutableInstance, IModelInstance<T>
    where T : IDatabaseModel
{
}

public interface IMutableInstance : IModelInstance
{
    new object? this[string propertyName] { get; set; }
    new object? this[Column column] { get; set; }

    IEnumerable<KeyValuePair<Column, object?>> GetChanges();
    bool IsNewModel();
    new MutableRowData GetRowData();
}

public interface IMutableInstance<T> : IMutableInstance, IModelInstance<T>
    where T : IDatabaseModel
{
}


public static class InstanceFactory
{
    public static IImmutableInstance NewImmutableRow(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess dataSource)
    {
        var model = Activator.CreateInstance(rowData.Table.Model.ImmutableType.CsType, rowData, dataSource);

        if (model == null)
            throw new Exception($"Failed to create instance of immutable model type '{rowData.Table.Model.CsType}'");

        return (IImmutableInstance)model;
    }

    public static T NewImmutableRow<T>(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess dataSource)
    {
        var model = NewImmutableRow(rowData, databaseProvider, dataSource);

        if (model == null)
            throw new Exception($"Failed to create instance of immutable model type '{typeof(T)}'");

        return (T)model;
    }

    public static T NewDatabase<T>(DataSourceAccess dataSource)
    {
        var db = Activator.CreateInstance(typeof(T), dataSource);

        if (db == null)
            throw new Exception($"Failed to create instance of database model type '{typeof(T)}'");

        return (T)db;
    }
}