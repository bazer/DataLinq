using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class MutableRowData : IRowData
{
    RowData? ImmutableRowData { get; }
    Dictionary<Column, object?> MutatedData { get; } = new Dictionary<Column, object?>();

    public TableMetadata Table => throw new System.NotImplementedException();

    public object? this[Column column] => throw new System.NotImplementedException();

    public MutableRowData()
    {
    }

    public MutableRowData(RowData immutableRowData)
    {
        this.ImmutableRowData = immutableRowData;
    }

    //public IKey GetKey() =>
    //    KeyFactory.GetKey(this.ImmutableRowData, this.ImmutableRowData.Table.PrimaryKeyColumns);

    //public T? GetValue<T>(string columnDbName)
    //{
    //    return (T?)GetValue(ImmutableRowData.Table.Columns.Single(x => x.DbName == columnDbName));
    //}

    public T? GetValue<T>(Column column)
    {
        return (T?)GetValue(column);
    }

    public object? GetValue(Column column)
    {
        if (MutatedData.ContainsKey(column))
            return MutatedData[column];

        return ImmutableRowData?.GetValue(column);
    }

    public IEnumerable<object?> GetValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return GetValue(column);
    }

    //public void SetValue<T>(string columnDbName, T? value)
    //{
    //    SetValue(ImmutableRowData.Table.Columns.Single(x => x.DbName == columnDbName), value);
    //}

    public void SetValue(Column column, object? value)
    {
        MutatedData[column] = value;
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues()
    {
        if (ImmutableRowData == null)
            return MutatedData.AsEnumerable();

        return GetColumnAndValues(ImmutableRowData.GetColumnAndValues().Select(x => x.Key));
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetColumnAndValues(IEnumerable<Column> columns)
    {
        foreach (var column in columns)
            yield return new KeyValuePair<Column, object?>(column, GetValue(column));
    }

    public IEnumerable<KeyValuePair<Column, object?>> GetChanges()
    {
        foreach (var change in MutatedData)
            yield return change;
    }

   

    IEnumerable<object?> IRowData.GetValues(IEnumerable<Column> columns)
    {
        throw new System.NotImplementedException();
    }
}
