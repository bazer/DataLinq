using System;
using System.Globalization;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Decodes raw scalar results returned by provider-generated value expressions.
/// </summary>
internal static class GeneratedValueDecoder
{
    /// <summary>
    /// Normalizes a raw SQL auto-increment result into the column's canonical integral provider CLR type.
    /// </summary>
    internal static object DecodeAutoIncrementValue(
        ColumnDefinition column,
        object? physicalValue,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(column);
        ProviderRowMaterializer.ValidateSourceName(sourceName);

        if (!column.AutoIncrement)
        {
            throw new ArgumentException(
                $"Column '{column.Table.DbName}.{column.DbName}' is not an auto-increment column.",
                nameof(column));
        }

        object? canonicalValue = null;
        var valueProduced = false;

        try
        {
            if (physicalValue is null || ReferenceEquals(physicalValue, DBNull.Value))
            {
                throw new InvalidOperationException(
                    $"Provider returned no auto-increment value for column '{column.Table.DbName}.{column.DbName}'.");
            }

            var providerType = column.ProviderClrType
                ?? throw new InvalidOperationException(
                    $"Column '{column.Table.DbName}.{column.DbName}' has no runtime canonical provider CLR type metadata.");
            providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;

            if (!CanDecodeAutoIncrementValue(column))
            {
                throw new NotSupportedException(
                    $"Auto-increment hydration supports integral canonical provider CLR types, but column '{column.Table.DbName}.{column.DbName}' uses '{providerType.FullName}'.");
            }

            var physicalType = physicalValue.GetType();
            if (!IsIntegralType(physicalType))
            {
                throw new InvalidOperationException(
                    $"Provider returned non-integral physical CLR type '{physicalType.FullName}' for auto-increment column '{column.Table.DbName}.{column.DbName}'.");
            }

            canonicalValue = physicalType == providerType
                ? physicalValue
                : Convert.ChangeType(physicalValue, providerType, CultureInfo.InvariantCulture);
            valueProduced = true;

            CanonicalProviderValueRow.ValidateCanonicalValue(
                column,
                canonicalValue,
                nameof(canonicalValue));
            return CanonicalProviderValueRow.CopyMutableValue(canonicalValue)!;
        }
        catch (Exception exception) when (
            exception is not GeneratedValueDecodingException and
            not OperationCanceledException and
            not OutOfMemoryException and
            not AccessViolationException)
        {
            throw new GeneratedValueDecodingException(
                column,
                sourceName,
                physicalValue,
                valueProduced,
                canonicalValue,
                exception);
        }
    }

    internal static bool CanDecodeAutoIncrementValue(ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!column.AutoIncrement)
            return false;

        var providerType = column.ProviderClrType;
        if (providerType is null)
            return false;

        providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;
        return IsIntegralType(providerType);
    }

    private static bool IsIntegralType(Type type) =>
        type == typeof(sbyte) ||
        type == typeof(byte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long) ||
        type == typeof(ulong);
}

internal sealed class GeneratedValueDecodingException : InvalidOperationException
{
    internal GeneratedValueDecodingException(
        ColumnDefinition column,
        string sourceName,
        object? physicalValue,
        bool valueProduced,
        object? canonicalValue,
        Exception innerException)
        : base(
            CreateMessage(
                column,
                sourceName,
                physicalValue,
                valueProduced,
                canonicalValue),
            innerException)
    {
        Column = column;
        SourceName = sourceName;
    }

    internal ColumnDefinition Column { get; }
    internal string SourceName { get; }

    private static string CreateMessage(
        ColumnDefinition column,
        string sourceName,
        object? physicalValue,
        bool valueProduced,
        object? canonicalValue)
    {
        var providerType = column.ProviderClrType?.FullName ?? column.ProviderCsType.Name;
        var canonicalContext = valueProduced
            ? DescribeValue(canonicalValue)
            : "not decoded";

        return
            $"Failed to decode generated physical value for column '{column.Table.DbName}.{column.DbName}' " +
            $"from source '{sourceName}' into canonical provider CLR type '{providerType}'. " +
            $"Physical value context: {DescribeValue(physicalValue)}. " +
            $"Decoded value context: {canonicalContext}.";
    }

    private static string DescribeValue(object? value) => value switch
    {
        null => "null",
        string text => $"CLR type '{typeof(string).FullName}', length {text.Length}",
        byte[] bytes => $"CLR type '{typeof(byte[]).FullName}', length {bytes.Length}",
        Array array => $"CLR type '{array.GetType().FullName}', length {array.Length}",
        _ when ReferenceEquals(value, DBNull.Value) => $"CLR type '{typeof(DBNull).FullName}'",
        _ => $"CLR type '{value.GetType().FullName}'"
    };
}
