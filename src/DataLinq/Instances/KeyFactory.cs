using System;
using System.Collections.Generic;
using DataLinq.Metadata;
using DataLinq.Query;

namespace DataLinq.Instances;

public static class KeyFactory
{
    public static DataLinqKey CreateKeyFromValue<T>(T? value) =>
        DataLinqKey.FromValue(value);

    public static DataLinqKey CreateKeyFromValues(IEnumerable<object?> values) =>
        DataLinqKey.FromValues(values);

    /// <summary>
    /// Creates a canonical provider-domain key from one public model value and its column metadata.
    /// </summary>
    /// <param name="modelValue">The model-side key value.</param>
    /// <param name="column">The column whose scalar mapping owns the value.</param>
    public static DataLinqKey CreateKeyFromModelValue(
        object? modelValue,
        ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return CreateKeyFromValue(GetCanonicalModelKeyValue(
            column,
            modelValue,
            "key.model-value"));
    }

    /// <summary>
    /// Creates a canonical provider-domain key from ordered public model values and matching columns.
    /// </summary>
    /// <param name="modelValues">The ordered model-side key components.</param>
    /// <param name="columns">The matching ordered key columns.</param>
    public static DataLinqKey CreateKeyFromModelValues(
        IReadOnlyList<object?> modelValues,
        IReadOnlyList<ColumnDefinition> columns)
    {
        ArgumentNullException.ThrowIfNull(modelValues);
        ArgumentNullException.ThrowIfNull(columns);

        if (modelValues.Count != columns.Count)
        {
            throw new ArgumentException(
                $"Model key value count {modelValues.Count} does not match column count {columns.Count}.",
                nameof(modelValues));
        }

        return CreateKeyFromModelValuesCore(modelValues, columns, "key.model-values");
    }

    public static DataLinqKey GetKey(IDataLinqDataReader reader, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 1)
        {
            // The fast path
            var column = columns[0];
            if (column.HasScalarConverter)
            {
                return CreateKeyFromValue(
                    ProviderRowDecoder.DecodeCanonicalValue(
                        reader,
                        column,
                        ordinal: 0,
                        sourceName: "reader.key-selection"));
            }

            var columnType = column.ValueProperty.CsType.Type;
            if (columnType == typeof(int))
                return CreateKeyFromValue(reader.GetInt32(0));
            else if (columnType == typeof(Guid))
                return CreateKeyFromValue(reader.GetGuid(0));
            else if (columnType == typeof(string))
                return CreateKeyFromValue(reader.GetString(0));
            else
                return CreateKeyFromValue(reader.GetValue<object>(column, 0));
        }

        var values = new object?[columns.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = reader.GetValue<object>(columns[i]);

        return CreateKeyFromValues(values);
    }

    public static DataLinqKey GetKey(IRowData row, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 1)
            return CreateKeyFromModelValueCore(
                row.GetValue(columns[0]),
                columns[0],
                "key.row");

        var values = new object?[columns.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = row.GetValue(columns[i]);

        return CreateKeyFromModelValuesCore(values, columns, "key.row");
    }

    public static DataLinqKey GetKey(IModelInstance model, IReadOnlyList<ColumnDefinition> columns)
    {
        if (columns.Count == 1)
            return CreateKeyFromModelValueCore(
                model[columns[0]],
                columns[0],
                "key.model");

        var values = new object?[columns.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = model[columns[i]];

        return CreateKeyFromModelValuesCore(values, columns, "key.model");
    }

    private static DataLinqKey CreateKeyFromModelValueCore(
        object? modelValue,
        ColumnDefinition column,
        string sourceName) =>
        CreateKeyFromValue(GetCanonicalModelKeyValue(column, modelValue, sourceName));

    private static DataLinqKey CreateKeyFromModelValuesCore(
        IReadOnlyList<object?> modelValues,
        IReadOnlyList<ColumnDefinition> columns,
        string sourceName)
    {
        if (modelValues.Count == 0)
            return DataLinqKey.Null;

        if (modelValues.Count == 1)
            return CreateKeyFromModelValueCore(modelValues[0], columns[0], sourceName);

        var canonicalValues = new object?[modelValues.Count];
        for (var index = 0; index < canonicalValues.Length; index++)
        {
            canonicalValues[index] = GetCanonicalModelKeyValue(
                columns[index],
                modelValues[index],
                sourceName);
        }

        return CreateKeyFromValues(canonicalValues);
    }

    private static object? GetCanonicalModelKeyValue(
        ColumnDefinition column,
        object? modelValue,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(column);

        return column.HasScalarConverter
            ? ModelValueConverter.ToCanonicalProviderValue(column, modelValue, sourceName)
            : modelValue;
    }

    public static IEnumerable<DataLinqKey> GetKeys<T>(Select<T> select, IReadOnlyList<ColumnDefinition> columns)
    {
        foreach (var reader in select.ReadReader())
            yield return GetKey(reader, columns);
    }
}
