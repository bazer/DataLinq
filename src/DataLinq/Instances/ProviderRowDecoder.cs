using System;
using System.Globalization;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Decodes one complete backend reader row into canonical provider CLR values. Physical/provider
/// decoding ends here; provider-to-model scalar conversion remains in <see cref="ProviderRowMaterializer"/>.
/// </summary>
internal static class ProviderRowDecoder
{
    internal static CanonicalProviderValueRow DecodeFullRow(
        IDataLinqDataReader reader,
        TableDefinition table,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(table);
        ProviderRowMaterializer.ValidateSourceName(sourceName);

        var canonicalValues = new object?[table.ColumnCount];
        for (var ordinal = 0; ordinal < table.ColumnCount; ordinal++)
        {
            var column = table.Columns[ordinal];
            object? canonicalValue = null;
            var valueProduced = false;

            try
            {
                canonicalValue = reader.IsDbNull(ordinal)
                    ? null
                    : DecodeNonNullValue(reader, column, ordinal);
                valueProduced = true;
                CanonicalProviderValueRow.ValidateCanonicalValue(
                    column,
                    canonicalValue,
                    nameof(canonicalValue));
                canonicalValues[ordinal] = canonicalValue;
            }
            catch (Exception exception) when (
                exception is not ProviderValueDecodingException and
                not OperationCanceledException and
                not OutOfMemoryException and
                not AccessViolationException)
            {
                throw new ProviderValueDecodingException(
                    column,
                    sourceName,
                    valueProduced,
                    canonicalValue,
                    exception);
            }
        }

        return CanonicalProviderValueRow.Create(table, canonicalValues);
    }

    private static object DecodeNonNullValue(
        IDataLinqDataReader reader,
        ColumnDefinition column,
        int ordinal)
    {
        if (!column.HasScalarConverter)
        {
            return reader.GetValue<object>(column, ordinal)
                ?? throw new InvalidOperationException(
                    $"Reader returned null for non-NULL column '{column.Table.DbName}.{column.DbName}'.");
        }

        var providerType = column.ProviderClrType
            ?? throw new InvalidOperationException(
                $"Column '{column.Table.DbName}.{column.DbName}' has no runtime canonical provider CLR type metadata.");
        providerType = Nullable.GetUnderlyingType(providerType) ?? providerType;

        if (providerType == typeof(int))
            return reader.GetInt32(ordinal);
        if (providerType == typeof(long))
            return Convert.ToInt64(ReadRawValue(reader, column, ordinal), CultureInfo.InvariantCulture);
        if (providerType == typeof(Guid))
            return reader.GetGuid(ordinal);
        if (providerType == typeof(string))
            return reader.GetString(ordinal);
        if (providerType == typeof(bool))
            return reader.GetBoolean(ordinal);
        if (providerType == typeof(DateOnly))
            return reader.GetDateOnly(ordinal);
        if (providerType == typeof(byte[]))
        {
            return reader.GetBytes(ordinal)
                ?? throw new InvalidOperationException(
                    $"Reader returned null bytes for non-NULL column '{column.Table.DbName}.{column.DbName}'.");
        }

        var rawValue = ReadRawValue(reader, column, ordinal);
        if (providerType.IsInstanceOfType(rawValue))
            return rawValue;

        if (providerType.IsEnum)
        {
            var enumStorageType = Enum.GetUnderlyingType(providerType);
            var enumValue = Convert.ChangeType(rawValue, enumStorageType, CultureInfo.InvariantCulture);
            return Enum.ToObject(providerType, enumValue!);
        }

        return Convert.ChangeType(rawValue, providerType, CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException(
                $"Reader could not decode canonical provider type '{providerType.FullName}' for column '{column.Table.DbName}.{column.DbName}'.");
    }

    private static object ReadRawValue(
        IDataLinqDataReader reader,
        ColumnDefinition column,
        int ordinal)
    {
        var value = reader.GetValue(ordinal);
        if (value is null || ReferenceEquals(value, DBNull.Value))
        {
            throw new InvalidOperationException(
                $"Reader returned a null physical value for non-NULL column '{column.Table.DbName}.{column.DbName}'.");
        }

        return value;
    }
}

internal sealed class ProviderValueDecodingException : InvalidOperationException
{
    internal ProviderValueDecodingException(
        ColumnDefinition column,
        string sourceName,
        bool valueProduced,
        object? canonicalValue,
        Exception innerException)
        : base(
            CreateMessage(column, sourceName, valueProduced, canonicalValue),
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
        bool valueProduced,
        object? canonicalValue)
    {
        var providerType = column.ProviderClrType?.FullName ?? column.ProviderCsType.Name;
        var valueContext = valueProduced
            ? DescribeValue(canonicalValue)
            : "not decoded";

        return
            $"Failed to decode physical value for column '{column.Table.DbName}.{column.DbName}' " +
            $"from source '{sourceName}' into canonical provider CLR type '{providerType}'. " +
            $"Decoded value context: {valueContext}.";
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
