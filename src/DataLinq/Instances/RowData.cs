using System;
using System.Collections.Generic;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public class RowData
{
    //public RowData(Dictionary<string, object> data, TableMetadata table)
    //{
    //    Data = data;
    //    Table = table;
    //}

    public RowData(IDataLinqDataReader reader, TableMetadata table)
    {
        Table = table;
        (Data, Size) = ReadReader(reader, table);
    }

    protected Dictionary<string, object> Data { get; }

    public TableMetadata Table { get; }

    public PrimaryKeys GetKeys() =>
        new PrimaryKeys(this);

    public int Size { get; }

    public object GetValue(string columnDbName)
    {
        return Data[columnDbName];
    }

    private (Dictionary<string, object> data, int size) ReadReader(IDataLinqDataReader reader, TableMetadata table)
    {
        var data = new Dictionary<string, object>();
        var size = 0;

        foreach (var column in table.Columns)
        {
            var value = reader.ReadColumn(column);
            size += GetSize(column, value);

            data.Add(column.DbName, value);
        }

        return (data, size);
    }

    private int GetSize(Column column, object value)
    {
        //if (column.ForeignKey)
        //    return 0;

        if (value == null)
            return 0;

        if (column.ValueProperty.CsSize.HasValue)
            return column.ValueProperty.CsSize.Value;

        if (column.ValueProperty.CsType == typeof(string))
            return (value as string).Length * sizeof(char) + sizeof(int);

        if (column.ValueProperty.CsType == typeof(byte[]))
            return (value as byte[]).Length;

        throw new NotImplementedException($"Size for type '{column.ValueProperty.CsType}' not implemented");
    }
}