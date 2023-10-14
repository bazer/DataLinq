using DataLinq.Metadata;
using System;

namespace DataLinq
{
    public interface IDataLinqDataWriter
    {
        object? ConvertValue(Column column, object? value);
    }

    public static class DataWriter
    {
        public static object? WriteColumn(this IDataLinqDataWriter writer, Column column, object? value)
        {
            return writer.ConvertValue(column, value);
        }
    }
}