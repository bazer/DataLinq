﻿using System;
using System.Buffers;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Utils;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public class SQLiteDataLinqDataReader : IDataLinqDataReader
{
    public SQLiteDataLinqDataReader(SqliteDataReader dataReader)
    {
        this.dataReader = dataReader;
    }

    protected SqliteDataReader dataReader;

    public void Dispose()
    {
        dataReader.Dispose();
    }

    public bool IsDbNull(int ordinal)
    {
        return dataReader.IsDBNull(ordinal);
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
        var date = dataReader.GetDateTime(ordinal);
        return new DateOnly(date.Year, date.Month, date.Day);
    }

    public Guid GetGuid(int ordinal)
    {
        return dataReader.GetGuid(ordinal);
    }

    public int GetOrdinal(string name)
    {
        return dataReader.GetOrdinal(name);
    }

    public object GetValue(int ordinal)
    {
        return dataReader.GetValue(ordinal);
    }

    public long GetByteLength(int ordinal)
    {
        return dataReader.GetBytes(ordinal, 0, null, 0, 0);
    }

    public byte[]? GetBytes(int ordinal)
    {
        if (GetByteLength(ordinal) == 0)
            return null;

        var buffer = new byte[GetByteLength(ordinal)];
        if (GetBytes(ordinal, buffer) == 0)
            throw new Exception($"Unexpectedly read 0 bytes from column ordinal {ordinal}");

        return buffer;
    }

    public long GetBytes(int ordinal, Span<byte> buffer)
    {
        byte[] tempBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);

        try
        {
            long bytesRead = dataReader.GetBytes(ordinal, 0, tempBuffer, 0, buffer.Length);
            new ReadOnlySpan<byte>(tempBuffer, 0, (int)bytesRead).CopyTo(buffer);
            return bytesRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
        }
    }

    public bool ReadNextRow()
    {
        return dataReader.Read();
    }


    public T? GetValue<T>(ColumnDefinition column)
    {
        return GetValue<T>(column, GetOrdinal(column.DbName));
    }

    public T? GetValue<T>(ColumnDefinition column, int ordinal)
    {
        if (IsDbNull(ordinal))
            return default;

        else if (column.ValueProperty.CsType.Type == typeof(Guid) || column.ValueProperty.CsType.Type == typeof(Guid?))
        {
            //var dbType = column.GetDbTypeFor(DatabaseType.MySQL); // column.DbTypes.FirstOrDefault(x => x.DatabaseType == DatabaseType.MySQL) ?? column.DbTypes.FirstOrDefault(); //SqlFromMetadataFactory.GetDbType(column);
            //if (value is byte[] bytes && column.GetDbTypeFor(DatabaseType.MySQL)?.Length == 16 && column.GetDbTypeFor(DatabaseType.MySQL)?.Name == "binary")
            //    return new Guid(bytes);

            return (T?)(object)GetGuid(ordinal);
        }
        else if (column.ValueProperty.CsType.Type == typeof(string))
            return (T?)(object)GetString(ordinal);
        else if (column.ValueProperty.CsType.Type == typeof(int) || column.ValueProperty.CsType.Type == typeof(int?))
            return (T?)(object)GetInt32(ordinal);
        else if (column.ValueProperty.CsType.Type == typeof(DateOnly) || column.ValueProperty.CsType.Type == typeof(DateOnly?))
            return (T?)(object)GetDateOnly(ordinal);
        else if (column.ValueProperty.CsType.Type?.IsEnum == true)
        {
            var enumValue = GetValue(ordinal);
            if (enumValue is string stringValue)
                return (T?)Enum.ToObject(column.ValueProperty.CsType.Type, column.ValueProperty.EnumProperty.Value.EnumValues.Single(x => x.name.Equals(stringValue, StringComparison.OrdinalIgnoreCase)).value);
            else
                return (T?)Enum.ToObject(column.ValueProperty.CsType.Type, enumValue);
        }

        var value = GetValue(ordinal);
        if (column.ValueProperty.CsNullable)
            return (T?)Convert.ChangeType(value, TypeUtils.GetNullableConversionType(column.ValueProperty.CsType.Type));
        else if (value.GetType() != column.ValueProperty.CsType.Type)
            return (T?)Convert.ChangeType(value, column.ValueProperty.CsType.Type);

        return (T?)value;
    }

    //public T? GetValue<T>(Column column)
    //{
    //    return (T?)GetValue(column, GetOrdinal(column.DbName));
    //}

    //public T? GetValue<T>(Column column, int ordinal)
    //{
    //    return (T?)GetValue(column, ordinal);
    //}

    //public object? GetValue(Column column)
    //{
    //    return GetValue(column, GetOrdinal(column.DbName));
    //}

    //public object? GetValue(Column column, int ordinal)
    //{
    //    var value = GetValue(ordinal);

    //    if (value is DBNull)
    //        return null;
    //    else if (column.ValueProperty.CsType == typeof(Guid) || column.ValueProperty.CsType == typeof(Guid?))
    //    {
    //        var dbType = SqlFromMetadataFactory.GetDbType(column);
    //        if (value is byte[] bytes && dbType.Name == "binary" && dbType.Length == 16)
    //            return new Guid(bytes);
    //    }
    //    else if (column.ValueProperty.CsType == typeof(DateOnly))
    //        return GetDateOnly(ordinal);
    //    else if (column.ValueProperty.CsType.IsEnum && value is string stringValue)
    //        return Enum.ToObject(column.ValueProperty.CsType, column.ValueProperty.EnumProperty.Value.EnumValues.Single(x => x.name.Equals(stringValue, StringComparison.OrdinalIgnoreCase)).value);
    //    else if (column.ValueProperty.CsType.IsEnum)
    //        return Enum.ToObject(column.ValueProperty.CsType, value);
    //    else if (column.ValueProperty.CsNullable)
    //        return Convert.ChangeType(value, TypeUtils.GetNullableConversionType(column.ValueProperty.CsType));
    //    else if (value.GetType() != column.ValueProperty.CsType)
    //        return Convert.ChangeType(value, column.ValueProperty.CsType);

    //    return value;
    //}




}
