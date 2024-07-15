﻿using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Utils;
using MySqlConnector;

namespace DataLinq.MySql;

public class MySqlDataLinqDataReader : IDataLinqDataReader
{
    public MySqlDataLinqDataReader(MySqlDataReader dataReader)
    {
        this.dataReader = dataReader;
    }

    protected MySqlDataReader dataReader;

    public void Dispose()
    {
        dataReader.Dispose();
    }

    public string GetString(int ordinal)
    {
        return dataReader.GetString(ordinal);
    }

    public bool GetBoolean(int ordinal)
    {
        return dataReader.GetBoolean(ordinal);
    }

    public int GetInt32(int ordinal)
    {
        return dataReader.GetInt32(ordinal);
    }

    public DateOnly GetDateOnly(int ordinal)
    {
        return dataReader.GetDateOnly(ordinal);
    }

    public int GetOrdinal(string name)
    {
        return dataReader.GetOrdinal(name);
    }

    public object GetValue(int ordinal)
    {
        return dataReader.GetValue(ordinal);
    }

    public bool Read()
    {
        return dataReader.Read();
    }

    public object? GetValue(Column column)
    {
        var ordinal = GetOrdinal(column.DbName);
        var value = GetValue(ordinal);

        if (value is DBNull)
            return null;
        else if (column.ValueProperty.CsType == typeof(Guid) || column.ValueProperty.CsType == typeof(Guid?))
        {
            var dbType = column.DbTypes.FirstOrDefault(x => x.DatabaseType == DatabaseType.MySQL) ?? column.DbTypes.FirstOrDefault(); //SqlFromMetadataFactory.GetDbType(column);
            if (value is byte[] bytes && dbType?.Name == "binary" && dbType?.Length == 16)
                return new Guid(bytes);
        }
        else if (column.ValueProperty.CsType == typeof(DateOnly))
            return GetDateOnly(ordinal);
        else if (column.ValueProperty.CsType.IsEnum && value is string stringValue)
            return Enum.ToObject(column.ValueProperty.CsType, column.ValueProperty.EnumProperty.Value.EnumValues.Single(x => x.name.Equals(stringValue, StringComparison.OrdinalIgnoreCase)).value);
        else if (column.ValueProperty.CsType.IsEnum)
            return Enum.ToObject(column.ValueProperty.CsType, value);
        else if (column.ValueProperty.CsNullable)
            return Convert.ChangeType(value, TypeUtils.GetNullableConversionType(column.ValueProperty.CsType));
        else if (value.GetType() != column.ValueProperty.CsType)
            return Convert.ChangeType(value, column.ValueProperty.CsType);

        return value;
    }
}
