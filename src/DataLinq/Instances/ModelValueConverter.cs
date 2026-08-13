using System;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Converts public model values into canonical provider CLR values before a backend applies physical encoding.
/// </summary>
internal static class ModelValueConverter
{
    internal static object? ToCanonicalProviderValue(
        ColumnDefinition column,
        object? modelValue,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(column);
        ProviderRowMaterializer.ValidateSourceName(sourceName);

        // MutationWriteSlot omission is decided before this conversion boundary. Any null
        // that reaches the converter is therefore an explicit provider null and passes through.
        if (modelValue is null)
            return null;

        object? providerValue = null;
        var providerValueProduced = false;

        try
        {
            CanonicalProviderValueRow.ValidateModelValue(column, modelValue, nameof(modelValue));

            if (!column.HasScalarConverter)
            {
                providerValue = modelValue;
            }
            else
            {
                var converter = column.ScalarConverter
                    ?? throw new InvalidOperationException(
                        $"Scalar converter metadata for column '{column.Table.DbName}.{column.DbName}' is unresolved at runtime.");
                var context = new ScalarConversionContext(column);
                providerValue = converter.ToProviderObject(modelValue, in context);
            }

            providerValueProduced = true;
            CanonicalProviderValueRow.ValidateCanonicalValue(column, providerValue, nameof(providerValue));
            return CanonicalProviderValueRow.CopyMutableValue(providerValue);
        }
        catch (Exception exception) when (
            exception is not ModelValueConversionException and
            not OperationCanceledException and
            not OutOfMemoryException and
            not AccessViolationException)
        {
            throw new ModelValueConversionException(
                column,
                sourceName,
                modelValue,
                providerValueProduced,
                providerValue,
                exception);
        }
    }
}

internal sealed class ModelValueConversionException : InvalidOperationException
{
    internal ModelValueConversionException(
        ColumnDefinition column,
        string sourceName,
        object? modelValue,
        bool providerValueProduced,
        object? providerValue,
        Exception innerException)
        : base(
            CreateMessage(
                column,
                sourceName,
                modelValue,
                providerValueProduced,
                providerValue),
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
        object? modelValue,
        bool providerValueProduced,
        object? providerValue)
    {
        var modelType = column.ModelClrType?.FullName ?? column.ModelCsType.Name;
        var providerType = column.ProviderClrType?.FullName ?? column.ProviderCsType.Name;
        var scalarMapping = column.ScalarMapping.ConverterClrType is { } converterType
            ? $"converter '{converterType.FullName}'"
            : "identity mapping";
        var providerContext = providerValueProduced
            ? DescribeValue(providerValue)
            : "not produced";

        return
            $"Failed to convert model value for column '{column.Table.DbName}.{column.DbName}' " +
            $"from source '{sourceName}' to a canonical provider CLR value. " +
            $"Expected model CLR type '{modelType}' and provider CLR type '{providerType}'. " +
            $"Scalar mapping: {scalarMapping}. " +
            $"Model value context: {DescribeValue(modelValue)}. " +
            $"Produced provider value context: {providerContext}.";
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
