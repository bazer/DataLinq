using System;
using System.Collections.Generic;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Instances;

public static class KeyFactory
{
    public static DataLinqKey CreateKeyFromValue<T>(T? value) =>
        DataLinqKey.FromValue(value);

    public static DataLinqKey CreateKeyFromValues(IEnumerable<object?> values) =>
        DataLinqKey.FromValues(values);

    public static DataLinqKey GetKey(IDataLinqDataReader reader, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 1)
        {
            // The fast path
            var columnType = columns[0].ValueProperty.CsType.Type;
            if (columnType == typeof(int))
                return CreateKeyFromValue(reader.GetInt32(0));
            else if (columnType == typeof(Guid))
                return CreateKeyFromValue(reader.GetGuid(0));
            else if (columnType == typeof(string))
                return CreateKeyFromValue(reader.GetString(0));
            else
                return CreateKeyFromValue(reader.GetValue<object>(columns[0], 0));
        }

        var values = new object?[columns.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = reader.GetValue<object>(columns[i]);

        return CreateKeyFromValues(values);
    }

    public static DataLinqKey GetKey(IRowData row, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 1)
            return CreateKeyFromValue(row.GetValue(columns[0]));

        var values = new object?[columns.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = row.GetValue(columns[i]);

        return CreateKeyFromValues(values);
    }

    public static DataLinqKey GetKey(IModelInstance model, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 1)
            return CreateKeyFromValue(model[columns[0]]);

        var values = new object?[columns.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = model[columns[i]];

        return CreateKeyFromValues(values);
    }

    public static IEnumerable<DataLinqKey> GetKeys<T>(Select<T> select, IReadOnlyList<ColumnDefinition> columns)
    {
        foreach (var reader in select.ReadReader())
            yield return GetKey(reader, columns);
    }
}
