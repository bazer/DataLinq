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

        var normalized = NormalizeKeyValue(value);

        return normalized switch
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

            _ => throw new Exception($"Type {normalized.GetType()} not supported as a key value")
        };
    }

    public static IKey CreateKeyFromValues(IEnumerable<object?> values)
    {
        if (values is IReadOnlyList<object?> list)
            return CreateKeyFromList(list);

        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
            return new NullKey();

        var firstValue = enumerator.Current;
        if (!enumerator.MoveNext())
            return CreateKeyFromValue(firstValue);

        var array = new object?[] { NormalizeKeyValue(firstValue), NormalizeKeyValue(enumerator.Current) };
        var count = 2;

        while (enumerator.MoveNext())
        {
            if (count == array.Length)
                Array.Resize(ref array, array.Length * 2);

            array[count++] = NormalizeKeyValue(enumerator.Current);
        }

        if (count != array.Length)
            Array.Resize(ref array, count);

        return CreateCompositeKeyFromNormalizedValues(array);
    }

    private static IKey CreateKeyFromList(IReadOnlyList<object?> values)
    {
        if (values.Count == 0)
            return new NullKey();

        if (values.Count == 1)
            return CreateKeyFromValue(values[0]);

        var array = new object?[values.Count];
        for (var i = 0; i < values.Count; i++)
            array[i] = NormalizeKeyValue(values[i]);

        return CreateCompositeKeyFromNormalizedValues(array);
    }

    private static IKey CreateCompositeKeyFromNormalizedValues(object?[] array)
    {
        if (AllValuesAreNull(array))
            return new NullKey();

        return new CompositeKey(array);
    }

    private static bool AllValuesAreNull(object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is not null)
                return false;
        }

        return true;
    }

    private static object? NormalizeKeyValue(object? value)
    {
        if (value is Enum enumValue)
            return Convert.ChangeType(enumValue, enumValue.GetTypeCode());

        return value;
    }

    public static IKey GetKey(IDataLinqDataReader reader, IReadOnlyList<ColumnDefinition> columns)
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

    public static IKey GetKey(IRowData row, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 1)
            return CreateKeyFromValue(row.GetValue(columns[0]));

        var values = new object?[columns.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = row.GetValue(columns[i]);

        return CreateKeyFromValues(values);
    }

    public static IEnumerable<IKey> GetKeys<T>(Select<T> select, IReadOnlyList<ColumnDefinition> columns) => select
        .ReadReader()
        .Select(x => GetKey(x, columns));
}
