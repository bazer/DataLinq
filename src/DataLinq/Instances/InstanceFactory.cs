﻿using System;
using System.Collections.Generic;
using System.Linq;
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
    void Reset(RowData rowData);
    void SetLazy<V>(string name, V value);
}

public interface IMutableInstance<T> : IMutableInstance, IModelInstance<T>
    where T : IDatabaseModel
{
}


public static class InstanceFactory
{
    public static IImmutableInstance NewImmutableRow(RowData rowData, IDatabaseProvider databaseProvider, DataSourceAccess dataSource)
    {
        if (rowData.Table.Model.ImmutableType?.Type == null)
            throw new Exception($"Immutable model type not defined for '{rowData.Table.Model.CsType}'");

        var model = Activator.CreateInstance(rowData.Table.Model.ImmutableType.Value.Type!, rowData, dataSource);

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