using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class Mutable<T> : MutableInstanceBase
    where T: ImmutableInstanceBase
{
    private readonly ModelMetadata metadata;
    public ModelMetadata Metadata() => metadata;

    private readonly bool isNewModel;
    public bool IsNewModel() => isNewModel;

    private MutableRowData mutableRowData;
    public MutableRowData GetRowData() => mutableRowData;

    public Mutable()
    {
        metadata = ModelMetadata.Find<T>() ?? throw new InvalidOperationException($"Model {typeof(T).Name} not found");
        this.mutableRowData = new MutableRowData();
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

    protected V? GetValue<V>(string propertyName) => mutableRowData.GetValue<V>(metadata.ValueProperties[propertyName].Column);
    protected void SetValue<V>(string propertyName, V value) => mutableRowData.SetValue(metadata.ValueProperties[propertyName].Column, value);

    public IKey PrimaryKeys() => KeyFactory.CreateKeyFromValues(mutableRowData.GetValues(metadata.Table.PrimaryKeyColumns));
    public bool HasPrimaryKeysSet() => PrimaryKeys() is not NullKey;

    public IEnumerable<KeyValuePair<Column, object>> GetChanges()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<KeyValuePair<Column, object>> GetValues()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<KeyValuePair<Column, object>> GetValues(IEnumerable<Column> columns)
    {
        throw new NotImplementedException();
    }

    
}