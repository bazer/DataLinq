using System;
using System.Collections.Generic;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class Mutable<T> : IMutableInstance
    where T: IImmutableInstance
{
    private readonly ModelDefinition metadata;
    public ModelDefinition Metadata() => metadata;

    private readonly bool isNewModel;
    public bool IsNewModel() => isNewModel;

    private MutableRowData mutableRowData;
    public MutableRowData GetRowData() => mutableRowData;
    IRowData IModelInstance.GetRowData() => GetRowData();

    public object? this[ColumnDefinition column]
    {
        get => mutableRowData[column];
        set => mutableRowData.SetValue(column, value);
    }

    public object? this[string propertyName]
    {
        get => mutableRowData.GetValue(metadata.ValueProperties[propertyName].Column);
        set => mutableRowData.SetValue(metadata.ValueProperties[propertyName].Column, value);
    }

    public Mutable()
    {
        metadata = ModelDefinition.Find<T>() ?? throw new InvalidOperationException($"Model {typeof(T).Name} not found");
        this.mutableRowData = new MutableRowData(metadata.Table);
        isNewModel = true;
    }

    public Mutable(T model)
    {
        this.mutableRowData = new MutableRowData(model.GetRowData());
        this.metadata = model.Metadata();
        this.isNewModel = false;
    }

    public Mutable(RowData rowData)
    {
        this.mutableRowData = new MutableRowData(rowData);
        this.metadata = rowData.Table.Model;
        this.isNewModel = false;
    }

    public V? GetValue<V>(string propertyName) => mutableRowData.GetValue<V>(metadata.ValueProperties[propertyName].Column);
    public void SetValue<V>(string propertyName, V value) => mutableRowData.SetValue(metadata.ValueProperties[propertyName].Column, value);

    public IKey PrimaryKeys() => KeyFactory.CreateKeyFromValues(mutableRowData.GetValues(metadata.Table.PrimaryKeyColumns));
    public bool HasPrimaryKeysSet() => PrimaryKeys() is not NullKey;

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetChanges() => mutableRowData.GetChanges();

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => mutableRowData.GetColumnAndValues();

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => mutableRowData.GetColumnAndValues(columns);
}