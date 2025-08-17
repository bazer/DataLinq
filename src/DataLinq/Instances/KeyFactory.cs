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
        if (value is RowData)
            throw new Exception("Cannot create a primary key from a RowData object. Use CreatePrimaryKey(RowData, Column[]) instead.");

        if (value is Enum enumValue)
        {
            // Convert the enum to its underlying primitive type (int, byte, long, etc.)
            // and then call this method again. The recursion is safe because the type changes.
            return CreateKeyFromValue(Convert.ChangeType(enumValue, enumValue.GetTypeCode()));
        }

        return value switch
        {
            null => new NullKey(),

            int intValue => new IntKey(intValue),
            uint uintValue => new UIntKey(uintValue),
            Guid guidValue => new GuidKey(guidValue),
            byte[] bytesValue => new BytesKey(bytesValue),
            long longValue => new LongKey(longValue),
            ulong ulongValue => new ULongKey(ulongValue),
            short shortValue => new ShortKey(shortValue),
            ushort ushortValue => new UShortKey(ushortValue),
            byte byteValue => new ByteKey(byteValue),
            sbyte sbyteValue => new SByteKey(sbyteValue),
            string stringValue => new StringKey(stringValue),
            DateTime dateTimeValue => new DateTimeKey(dateTimeValue),
            DateOnly dateOnlyValue => new DateOnlyKey(dateOnlyValue),
            TimeOnly timeOnlyValue => new TimeOnlyKey(timeOnlyValue),
            bool boolValue => new BoolKey(boolValue),
            decimal decimalValue => new DecimalKey(decimalValue),

            // Floats and doubles are less ideal as keys due to precision,
            // but can be part of an index, so supporting them is safer.
            float floatValue => new ObjectKey(floatValue),
            double doubleValue => new ObjectKey(doubleValue),

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

        // Convert each object to its specific IKey type.
        var keys = array.Select(CreateKeyFromValue).ToArray();
        return new CompositeKey(keys);
    }

    public static IKey GetKey(IDataLinqDataReader reader, ColumnDefinition[] columns)
    {
        if (columns.Length == 1)
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

        return CreateKeyFromValues(columns.Select(x => reader.GetValue<object>(x)));
    }

    public static IKey GetKey(RowData row, ColumnDefinition[] columns) =>
        CreateKeyFromValues(columns.Select(row.GetValue));

    public static IEnumerable<IKey> GetKeys<T>(Select<T> select, ColumnDefinition[] columns) => select
        .ReadReader()
        .Select(x => GetKey(x, columns));
}
