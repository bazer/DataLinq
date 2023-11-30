using System;
using DataLinq.Metadata;

namespace DataLinq;

public interface IDataLinqDataReader : IDisposable
{
    object? GetValue(Column column);
    object GetValue(int ordinal);
    int GetOrdinal(string name);
    string GetString(int ordinal);
    bool GetBoolean(int ordinal);
    int GetInt32(int ordinal);
    DateOnly GetDateOnly(int ordinal);
    bool Read();
}

public static class DataReader
{
    public static object? ReadColumn(this IDataLinqDataReader reader, Column column)
    {
        return reader.GetValue(column);

        //var ordinal = reader.GetOrdinal(column.DbName);
        //object value;

        //if (column.ValueProperty.CsType == typeof(DateOnly))
        //{
        //    value = reader.GetDateOnly(ordinal);
        //}
        //else
        //{
        //    value = reader.GetValue(ordinal);
        //}

        //if (value is DBNull)
        //    return null;
        //else if (column.ValueProperty.CsType.IsEnum && value is string stringValue)
        //    return Enum.ToObject(column.ValueProperty.CsType, column.ValueProperty.EnumProperty.Value.EnumValues.Single(x => x.name.Equals(stringValue, StringComparison.OrdinalIgnoreCase)).value);
        //else if (column.ValueProperty.CsType.IsEnum)
        //    return Enum.ToObject(column.ValueProperty.CsType, value);
        //else if (column.ValueProperty.CsNullable)
        //    return Convert.ChangeType(value, TypeUtils.GetNullableConversionType(column.ValueProperty.CsType));
        //else if (value.GetType() != column.ValueProperty.CsType)
        //    return Convert.ChangeType(value, column.ValueProperty.CsType);

        //return value;
    }
}