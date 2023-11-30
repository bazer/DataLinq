using System.Collections.Generic;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class MutableRowData
{
    RowData ImmutableRowData { get; }
    Dictionary<Column, object> MutatedData { get; } = new Dictionary<Column, object>();

    public MutableRowData(RowData immutableRowData)
    {
        this.ImmutableRowData = immutableRowData;
    }

    public PrimaryKeys GetKey() =>
        new PrimaryKeys(this.ImmutableRowData);

    public object GetValue(Column column)
    {
        if (MutatedData.ContainsKey(column))
            return MutatedData[column];

        return ImmutableRowData.GetValue(column.DbName);
    }

    public void SetValue(Column column, object value)
    {
        MutatedData[column] = value;
    }

    public IEnumerable<KeyValuePair<Column, object>> GetChanges()
    {
        foreach (var change in MutatedData)
            yield return change;
    }
}
