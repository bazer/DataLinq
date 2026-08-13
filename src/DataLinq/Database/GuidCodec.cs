using System;
using DataLinq.Attributes;

namespace DataLinq;

/// <summary>
/// Converts canonical <see cref="Guid"/> values to and from deterministic
/// physical UUID representations. Nullable handling and provider context are
/// owned by callers.
/// </summary>
internal static class GuidCodec
{
    internal static object ToPhysicalValue(
        Guid canonicalValue,
        GuidStorageFormat format) => format switch
    {
        GuidStorageFormat.NativeUuid or GuidStorageFormat.Text36 =>
            canonicalValue.ToString("D"),
        GuidStorageFormat.Text32 => canonicalValue.ToString("N"),
        GuidStorageFormat.Binary16LittleEndian => canonicalValue.ToByteArray(),
        GuidStorageFormat.Binary16Rfc4122 => canonicalValue.ToByteArray(bigEndian: true),
        _ => throw CreateUnknownFormatException(format)
    };

    internal static Guid FromPhysicalValue(
        object physicalValue,
        GuidStorageFormat format)
    {
        ArgumentNullException.ThrowIfNull(physicalValue);

        return format switch
        {
            GuidStorageFormat.NativeUuid => DecodeNativeUuid(physicalValue, format),
            GuidStorageFormat.Text36 => DecodeText(physicalValue, format, "D"),
            GuidStorageFormat.Text32 => DecodeText(physicalValue, format, "N"),
            GuidStorageFormat.Binary16LittleEndian =>
                DecodeBinary(physicalValue, format, bigEndian: false),
            GuidStorageFormat.Binary16Rfc4122 =>
                DecodeBinary(physicalValue, format, bigEndian: true),
            _ => throw CreateUnknownFormatException(format)
        };
    }

    private static Guid DecodeNativeUuid(object physicalValue, GuidStorageFormat format) =>
        physicalValue switch
        {
            Guid guid => guid,
            string text => ParseExact(text, format, "D"),
            _ => throw new InvalidCastException(
                $"UUID storage format '{format}' requires physical CLR type " +
                $"'{typeof(Guid).FullName}' or '{typeof(string).FullName}', but received " +
                $"'{physicalValue.GetType().FullName}'.")
        };

    private static Guid DecodeText(
        object physicalValue,
        GuidStorageFormat format,
        string formatSpecifier) =>
        ParseExact(
            RequirePhysicalType<string>(physicalValue, format),
            format,
            formatSpecifier);

    private static Guid ParseExact(
        string text,
        GuidStorageFormat format,
        string formatSpecifier)
    {
        var expectedLength = formatSpecifier == "D" ? 36 : 32;
        if (text.Length == expectedLength &&
            Guid.TryParseExact(text, formatSpecifier, out var value))
            return value;

        throw new FormatException(
            $"Physical UUID text for storage format '{format}' must use exact " +
            $"'{formatSpecifier}' format.");
    }

    private static Guid DecodeBinary(
        object physicalValue,
        GuidStorageFormat format,
        bool bigEndian)
    {
        var bytes = RequirePhysicalType<byte[]>(physicalValue, format);
        if (bytes.Length != 16)
        {
            throw new FormatException(
                $"Physical UUID bytes for storage format '{format}' must have length 16, " +
                $"but received length {bytes.Length}.");
        }

        return new Guid(bytes, bigEndian);
    }

    private static T RequirePhysicalType<T>(object physicalValue, GuidStorageFormat format)
    {
        if (physicalValue is T typedValue)
            return typedValue;

        throw new InvalidCastException(
            $"UUID storage format '{format}' requires physical CLR type " +
            $"'{typeof(T).FullName}', but received '{physicalValue.GetType().FullName}'.");
    }

    private static ArgumentOutOfRangeException CreateUnknownFormatException(
        GuidStorageFormat format) =>
        new(
            nameof(format),
            format,
            "The UUID storage format is not supported.");
}
