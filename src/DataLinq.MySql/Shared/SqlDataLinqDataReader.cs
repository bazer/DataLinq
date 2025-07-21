using System;
using System.Buffers;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Utils;
using MySqlConnector;

namespace DataLinq.MySql;

public struct SqlDataLinqDataReader : IDataLinqDataReader, IDisposable
{
    public SqlDataLinqDataReader(MySqlDataReader dataReader)
    {
        this.dataReader = dataReader;
    }

    private readonly MySqlDataReader dataReader;

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
        return dataReader.GetDateOnly(ordinal);
    }

    public DateTime GetDateTime(int ordinal)
    {
        return dataReader.GetDateTime(ordinal);
    }

    public TimeOnly GetTimeOnly(int ordinal)
    {
        // MySqlConnector returns TimeSpan for TIME columns.
        var timeSpan = dataReader.GetTimeSpan(ordinal);
        return TimeOnly.FromTimeSpan(timeSpan);
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

    //public bool CanReadBytes(Type type)
    //{
    //    return type == typeof(byte[]) || type == typeof(byte?[]) ||
    //            type == typeof(Guid) || type == typeof(Guid?);
    //}

    //public void ReadPrimaryKeys(ReadOnlySpan<Column> primaryKeyColumns, Span<byte> buffer, Span<int> lengths)
    //{
    //    int bufferOffset = 0;

    //    for (int i = 0; i < primaryKeyColumns.Length; i++)
    //    {
    //        int bytesRead;
    //        if (CanReadBytes(primaryKeyColumns[i].ValueProperty.CsType))
    //        {
    //            bytesRead = (int)GetBytes(i, buffer.Slice(bufferOffset));
    //        }
    //        else
    //        {
    //            var span = DataReader.ConvertTypeToBytes(ReadColumn(primaryKeyColumns[i], i), primaryKeyColumns[i].ValueProperty);
    //            if (bufferOffset + span.Length > buffer.Length)
    //            {
    //                throw new ArgumentException("Buffer is too small to hold all the data.");
    //            }
    //            span.CopyTo(buffer.Slice(bufferOffset));
    //            bytesRead = span.Length;
    //        }

    //        lengths[i] = bufferOffset + bytesRead;
    //        bufferOffset += bytesRead;
    //    }
    //}

    public T? GetValue<T>(ColumnDefinition column)
    {
        return GetValue<T>(column, GetOrdinal(column.DbName));
    }

    public T? GetValue<T>(ColumnDefinition column, int ordinal)
    {
        if (IsDbNull(ordinal))
            return default;

        var csType = column.ValueProperty.CsType.Type;

        if (csType == typeof(Guid) || csType == typeof(Guid?))
            return (T?)(object)GetGuid(ordinal);

        if (csType == typeof(string))
            return (T?)(object)GetString(ordinal);

        if (csType == typeof(int) || csType == typeof(int?))
            return (T?)(object)GetInt32(ordinal);

        if (csType == typeof(DateOnly) || csType == typeof(DateOnly?))
            return (T?)(object)GetDateOnly(ordinal);

        if (csType == typeof(DateTime) || csType == typeof(DateTime?))
            return (T?)(object)GetDateTime(ordinal);

        if (csType == typeof(TimeOnly) || csType == typeof(TimeOnly?))
            return (T?)(object)GetTimeOnly(ordinal);

        if (csType?.IsEnum == true)
        {
            var enumValue = GetValue(ordinal);
            if (enumValue is string stringValue)
                return (T?)Enum.ToObject(csType, column.ValueProperty.EnumProperty.Value.DbValuesOrCsValues.Single(x => x.name.Equals(stringValue, StringComparison.OrdinalIgnoreCase)).value);
            else
                return (T?)Enum.ToObject(csType, enumValue);
        }

        var value = GetValue(ordinal);
        if (column.ValueProperty.CsNullable)
            return (T?)Convert.ChangeType(value, TypeUtils.GetNullableConversionType(csType));
        else if (value.GetType() != csType)
            return (T?)Convert.ChangeType(value, csType);

        return (T?)value;
    }

    //public object? ReadColumn(Column column, int ordinal)
    //{
    //    //var ordinal = GetOrdinal(column.DbName);
    //    var value = GetValue(ordinal);

    //    if (value is DBNull)
    //        return null;
    //    else if (column.ValueProperty.CsType == typeof(Guid) || column.ValueProperty.CsType == typeof(Guid?))
    //    {
    //        //var dbType = column.GetDbTypeFor(DatabaseType.MySQL); // column.DbTypes.FirstOrDefault(x => x.DatabaseType == DatabaseType.MySQL) ?? column.DbTypes.FirstOrDefault(); //SqlFromMetadataFactory.GetDbType(column);
    //        if (value is byte[] bytes && column.GetDbTypeFor(DatabaseType.MySQL)?.Length == 16 && column.GetDbTypeFor(DatabaseType.MySQL)?.Name == "binary")
    //            return new Guid(bytes);
    //    }
    //    else if (column.ValueProperty.CsType == typeof(DateOnly))
    //        return GetDateOnly(GetOrdinal(column.DbName));
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
