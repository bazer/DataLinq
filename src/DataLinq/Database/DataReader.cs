using System;
using System.Text;
using DataLinq.Metadata;

namespace DataLinq;

public interface IDataLinqDataReader : IDisposable
{
    object GetValue(int ordinal);
    int GetOrdinal(string name);
    string GetString(int ordinal);
    bool GetBoolean(int ordinal);
    int GetInt32(int ordinal);
    DateOnly GetDateOnly(int ordinal);
    Guid GetGuid(int ordinal);
    byte[]? GetBytes(int ordinal);
    long GetBytes(int ordinal, Span<byte> buffer);
    T? GetValue<T>(Column column);
    T? GetValue<T>(Column column, int ordinal);
    bool ReadNextRow();
    bool IsDbNull(int ordinal);
}

public static class DataReader
{
    //public static T? ReadColumn<T>(this IDataLinqDataReader reader, Column column)
    //{
    //    return reader.GetValue<T>(column);
    //}

    //public static T? ReadColumn<T>(this IDataLinqDataReader reader, Column column, int ordinal)
    //{
    //    return reader.GetValue<T>(column, ordinal);
    //}

    //TODO: Move to IDataLinqDataReader, the byte encoding might differ between databases
    public static object ConvertBytesToType(ReadOnlySpan<byte> bytes, ValueProperty property)
    {
        // Convert the byte array to the specified type
        if (property.CsType == typeof(int))
        {
            return BitConverter.ToInt32(bytes);
        }
        else if (property.CsType == typeof(short))
        {
            return BitConverter.ToInt16(bytes);
        }
        else if (property.CsType == typeof(long))
        {
            return BitConverter.ToInt64(bytes);
        }
        else if (property.CsType == typeof(float))
        {
            return BitConverter.ToSingle(bytes);
        }
        else if (property.CsType == typeof(double))
        {
            return BitConverter.ToDouble(bytes);
        }
        else if (property.CsType == typeof(bool))
        {
            return BitConverter.ToBoolean(bytes);
        }
        else if (property.CsType == typeof(Guid))
        {
            return new Guid(bytes);
        }
        else if (property.CsType == typeof(string))
        {
            return Encoding.UTF8.GetString(bytes);
        }
        else if (property.CsType == typeof(DateTime))
        {
            return new DateTime(BitConverter.ToInt64(bytes));
        }
        else if (property.CsType == typeof(DateOnly))
        {
            return DateOnly.FromDateTime(new DateTime(BitConverter.ToInt64(bytes)));
        }
        else
        {
            throw new NotSupportedException($"Type {property.CsType} is not supported.");
        }
    }

    //TODO: Move to IDataLinqDataReader, the byte encoding might differ between databases
    public static ReadOnlySpan<byte> ConvertTypeToBytes(object? value, ValueProperty property)
    {
        if (value == null)
            return new ReadOnlySpan<byte> { };
        if (property == null)
            throw new ArgumentNullException(nameof(property));

        // Convert the object to byte array based on the type
        if (property.CsType == typeof(int))
        {
            if (value is int intValue)
                return BitConverter.GetBytes(intValue);
            else if (value is long longValue)
                return BitConverter.GetBytes((int)longValue);
            else if (value is short shortValue)
                return BitConverter.GetBytes((int)shortValue);
            else
                throw new ArgumentException($"Value {value} is not an integer.", nameof(value));
        }
        else if (property.CsType == typeof(short))
        {
            return BitConverter.GetBytes((short)value);
        }
        else if (property.CsType == typeof(long))
        {
            return BitConverter.GetBytes((long)value);
        }
        else if (property.CsType == typeof(float))
        {
            return BitConverter.GetBytes((float)value);
        }
        else if (property.CsType == typeof(double))
        {
            return BitConverter.GetBytes((double)value);
        }
        else if (property.CsType == typeof(bool))
        {
            return BitConverter.GetBytes((bool)value);
        }
        else if (property.CsType == typeof(Guid))
        {
            return ((Guid)value).ToByteArray();
        }
        else if (property.CsType == typeof(string))
        {
            return Encoding.UTF8.GetBytes((string)value);
        }
        else if (property.CsType == typeof(DateTime))
        {
            return BitConverter.GetBytes(((DateTime)value).Ticks);
        }
        else if (property.CsType == typeof(DateOnly))
        {
            return BitConverter.GetBytes(((DateOnly)value).ToDateTime(TimeOnly.MinValue).Ticks);
        }
        else
        {
            throw new NotSupportedException($"Type {property.CsType} is not supported.");
        }
    }

}