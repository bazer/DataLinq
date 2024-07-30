using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Instances;

public static class KeyFactory
{
    public static IKey CreateKeyFromValue<T>(T? value)
    {
        if (value is RowData row)
            throw new Exception("Cannot create a primary key from a RowData object. Use CreatePrimaryKey(RowData, Column[]) instead.");

        return value switch
        {
            null => new NullKey(),
            int intValue => new IntKey(intValue),
            Guid guidValue => new GuidKey(guidValue),
            string stringValue => new StringKey(stringValue),
            _ => throw new Exception($"Type {value.GetType()} not supported as a key value")
        };
    }

    public static IKey CreateKeyFromValues(IEnumerable<object?> values)
    {
        var array = values.ToArray();

        if (array.Length == 1)
            return CreateKeyFromValue(array[0]);

        if (array.All(x => x == null))
            return new NullKey();

        return new CompositeKey(array);
    }

    public static IKey GetKey(IDataLinqDataReader reader, Column[] columns)
    {
        if (columns.Length == 1)
        {
            var columnType = columns[0].ValueProperty.CsType;
            if (columnType == typeof(int))
                return CreateKeyFromValue(reader.GetInt32(0));
            else if (columnType == typeof(Guid))
                return CreateKeyFromValue(reader.GetGuid(0));
            else if (columnType == typeof(string))
                return CreateKeyFromValue(reader.GetString(0));
            else
                return CreateKeyFromValue(reader.GetValue<object>(columns[0], 0));
        }

        return new CompositeKey(columns.Select(x => reader.GetValue<object>(x)).ToArray());
    }

    public static IKey GetKey(RowData row, Column[] columns) =>
        CreateKeyFromValues(columns.Select(row.GetValue));

    public static IEnumerable<IKey> GetKeys<T>(Select<T> select, Column[] columns) => select
        .ReadReader()
        .Select(x => GetKey(x, columns));
}
