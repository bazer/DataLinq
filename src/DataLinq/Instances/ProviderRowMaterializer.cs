using System;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Converts canonical provider CLR values into the public model-valued row representation.
/// </summary>
internal static class ProviderRowMaterializer
{
    /// <summary>
    /// Applies column scalar mappings to one complete canonical row and returns public model-valued row state.
    /// </summary>
    /// <param name="providerRow">The validated complete row of canonical provider CLR values.</param>
    /// <param name="sourceName">
    /// A stable, non-sensitive logical source label such as <c>sql</c> or <c>memory</c>, used only in diagnostics.
    /// </param>
    /// <returns>A model-valued row suitable for generated immutable instance construction.</returns>
    internal static RowData Materialize(
        CanonicalProviderValueRow providerRow,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(providerRow);
        ValidateSourceName(sourceName);

        var modelValues = new object?[providerRow.Count];
        for (var ordinal = 0; ordinal < providerRow.Count; ordinal++)
        {
            var column = providerRow.Table.Columns[ordinal];
            var providerValue = providerRow[ordinal];
            modelValues[ordinal] = MaterializeValueCore(column, providerValue, sourceName);
        }

        return RowData.CreateTrusted(providerRow, modelValues);
    }

    /// <summary>
    /// Converts one validated canonical provider CLR value into its public model representation.
    /// </summary>
    internal static object? MaterializeValue(
        ColumnDefinition column,
        object? providerValue,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(column);
        ValidateSourceName(sourceName);
        return MaterializeValueCore(column, providerValue, sourceName);
    }

    private static object? MaterializeValueCore(
        ColumnDefinition column,
        object? providerValue,
        string sourceName)
    {
        object? modelValue = null;
        var modelValueProduced = false;

        try
        {
            CanonicalProviderValueRow.ValidateCanonicalValue(
                column,
                providerValue,
                nameof(providerValue));

            if (providerValue is null || !column.HasScalarConverter)
            {
                modelValue = providerValue;
            }
            else
            {
                var converter = column.ScalarConverter
                    ?? throw new InvalidOperationException(
                        $"Scalar converter metadata for column '{column.Table.DbName}.{column.DbName}' is unresolved at runtime.");
                var context = new ScalarConversionContext(column);
                modelValue = converter.FromProviderObject(providerValue, in context);
            }

            modelValueProduced = true;
            CanonicalProviderValueRow.ValidateModelValue(column, modelValue, nameof(modelValue));
            return modelValue;
        }
        catch (Exception exception) when (
            exception is not ProviderValueMaterializationException and
            not OperationCanceledException and
            not OutOfMemoryException and
            not AccessViolationException)
        {
            throw new ProviderValueMaterializationException(
                column,
                sourceName,
                providerValue,
                modelValueProduced,
                modelValue,
                exception);
        }
    }

    internal static void ValidateSourceName(string? sourceName)
    {
        if (!IsDiagnosticSourceLabel(sourceName))
        {
            throw new ArgumentException(
                "Provider-value conversion requires a short, non-sensitive diagnostic source label containing only letters, digits, '.', '-', '_', or ':'.",
                nameof(sourceName));
        }
    }

    private static bool IsDiagnosticSourceLabel(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
            return false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_' and not ':')
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class ProviderValueMaterializationException : InvalidOperationException
{
    internal ProviderValueMaterializationException(
        ColumnDefinition column,
        string sourceName,
        object? providerValue,
        bool modelValueProduced,
        object? modelValue,
        Exception innerException)
        : base(
            CreateMessage(
                column,
                sourceName,
                providerValue,
                modelValueProduced,
                modelValue),
            innerException)
    {
        Column = column;
        ConverterType = column.ScalarMapping.ConverterClrType;
        SourceName = sourceName;
    }

    internal ColumnDefinition Column { get; }
    internal Type? ConverterType { get; }
    internal string SourceName { get; }

    private static string CreateMessage(
        ColumnDefinition column,
        string sourceName,
        object? providerValue,
        bool modelValueProduced,
        object? modelValue)
    {
        var providerType = column.ProviderClrType?.FullName ?? column.ProviderCsType.Name;
        var modelType = column.ModelClrType?.FullName ?? column.ModelCsType.Name;
        var scalarMapping = column.ScalarMapping.ConverterClrType is { } converterType
            ? $"converter '{converterType.FullName}'"
            : "identity mapping";
        var modelContext = modelValueProduced
            ? DescribeValue(modelValue)
            : "not produced";

        return
            $"Failed to materialize canonical provider value for column '{column.Table.DbName}.{column.DbName}' " +
            $"from source '{sourceName}'. " +
            $"Expected provider CLR type '{providerType}' and model CLR type '{modelType}'. " +
            $"Scalar mapping: {scalarMapping}. " +
            $"Provider value context: {DescribeValue(providerValue)}. " +
            $"Produced model value context: {modelContext}.";
    }

    private static string DescribeValue(object? value) => value switch
    {
        null => "null",
        string text => $"CLR type '{typeof(string).FullName}', length {text.Length}",
        byte[] bytes => $"CLR type '{typeof(byte[]).FullName}', length {bytes.Length}",
        Array array => $"CLR type '{array.GetType().FullName}', length {array.Length}",
        _ => $"CLR type '{value.GetType().FullName}'"
    };
}
