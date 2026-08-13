using System;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq;

/// <summary>
/// Interface for writing data to a database.
/// </summary>
public interface IDataLinqDataWriter
{
    /// <summary>
    /// Converts a canonical provider CLR value to the appropriate physical database value.
    /// </summary>
    /// <param name="column">The column to convert.</param>
    /// <param name="value">The canonical provider CLR value to encode.</param>
    /// <returns>The converted value.</returns>
    object? ConvertValue(ColumnDefinition column, object? value);
}

/// <summary>
/// Provides extension methods for converting data to be written to the database.
/// </summary>
public static class DataWriter
{
    /// <summary>
    /// Converts a canonical provider CLR value to the appropriate physical database value.
    /// </summary>
    /// <param name="writer">The IDataLinqDataWriter instance.</param>
    /// <param name="column">The column to convert.</param>
    /// <param name="value">The canonical provider CLR value to encode.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when the column is null.</exception>
    public static object? ConvertColumnValue(this IDataLinqDataWriter writer, ColumnDefinition column, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(column);

        return writer.ConvertValue(column, value);
    }

    /// <summary>
    /// Converts a public model value through the column's scalar mapping before applying provider physical encoding.
    /// </summary>
    internal static object? ConvertModelColumnValue(
        this IDataLinqDataWriter writer,
        ColumnDefinition column,
        object? modelValue,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(column);

        var canonicalProviderValue = ModelValueConverter.ToCanonicalProviderValue(
            column,
            modelValue,
            sourceName);
        return writer.ConvertValue(column, canonicalProviderValue);
    }
}
