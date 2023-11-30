using System;
using DataLinq.Metadata;

namespace DataLinq;

/// <summary>
/// Interface for writing data to a database.
/// </summary>
public interface IDataLinqDataWriter
{
    /// <summary>
    /// Converts the value of a column to the appropriate database type.
    /// </summary>
    /// <param name="column">The column to convert.</param>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    object? ConvertValue(Column column, object? value);
}

/// <summary>
/// Provides extension methods for converting data to be written to the database.
/// </summary>
public static class DataWriter
{
    /// <summary>
    /// Converts the value of a column to the appropriate database type.
    /// </summary>
    /// <param name="writer">The IDataLinqDataWriter instance.</param>
    /// <param name="column">The column to convert.</param>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when the column is null.</exception>
    public static object? ConvertColumnValue(this IDataLinqDataWriter writer, Column column, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(column);

        return writer.ConvertValue(column, value);
    }
}