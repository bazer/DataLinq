using System;
using DataLinq.Attributes;

namespace DataLinq.Metadata;

/// <summary>
/// Owns the built-in provider rules that connect one physical database type
/// to a canonical <see cref="Guid"/> storage format. Metadata construction and
/// schema comparison share this policy so neither path invents a second UUID
/// type matrix.
/// </summary>
internal static class GuidStoragePhysicalTypeResolver
{
    internal static GuidStorageFormat? InferCompatibilityDefault(
        DatabaseType provider,
        DatabaseColumnType type,
        bool allowSchemaModifiers)
    {
        if (provider == DatabaseType.MariaDB &&
            IsNativeUuidType(type, allowSchemaModifiers))
            return GuidStorageFormat.NativeUuid;

        if (provider is DatabaseType.MySQL or DatabaseType.MariaDB)
        {
            if (IsBinary16(provider, type))
                return GuidStorageFormat.Binary16LittleEndian;
            if (IsTextType(type, 36))
                return GuidStorageFormat.Text36;
            if (IsTextType(type, 32))
                return GuidStorageFormat.Text32;

            return null;
        }

        return provider == DatabaseType.SQLite && IsName(type, "text")
            ? GuidStorageFormat.Text36
            : null;
    }

    internal static GuidStorageFormat? InferSchemaObservableFormat(
        DatabaseType provider,
        DatabaseColumnType type,
        bool allowSchemaModifiers)
    {
        if (provider == DatabaseType.MariaDB &&
            IsNativeUuidType(type, allowSchemaModifiers))
            return GuidStorageFormat.NativeUuid;

        if (provider is DatabaseType.MySQL or DatabaseType.MariaDB)
        {
            if (IsTextType(type, 36))
                return GuidStorageFormat.Text36;
            if (IsTextType(type, 32))
                return GuidStorageFormat.Text32;
        }

        // BINARY(16), SQLite BLOB, and SQLite TEXT do not encode UUID byte
        // order or text shape in the schema. Those formats require trusted
        // metadata rather than a model-side compatibility default.
        return null;
    }

    internal static bool IsCompatible(
        DatabaseType provider,
        DatabaseColumnType type,
        GuidStorageFormat format,
        bool allowSchemaModifiers) => format switch
    {
        GuidStorageFormat.NativeUuid =>
            provider == DatabaseType.MariaDB &&
            IsNativeUuidType(type, allowSchemaModifiers),
        GuidStorageFormat.Text36 =>
            provider == DatabaseType.SQLite
                ? IsName(type, "text")
                : provider is DatabaseType.MySQL or DatabaseType.MariaDB && IsTextType(type, 36),
        GuidStorageFormat.Text32 =>
            provider == DatabaseType.SQLite
                ? IsName(type, "text")
                : provider is DatabaseType.MySQL or DatabaseType.MariaDB && IsTextType(type, 32),
        GuidStorageFormat.Binary16LittleEndian or GuidStorageFormat.Binary16Rfc4122 =>
            provider == DatabaseType.SQLite
                ? IsName(type, "blob")
                : provider is DatabaseType.MySQL or DatabaseType.MariaDB && IsBinary16(provider, type),
        _ => false
    };

    internal static bool HasAmbiguousBinaryLayout(
        DatabaseType provider,
        DatabaseColumnType type) =>
        provider == DatabaseType.SQLite
            ? IsName(type, "blob")
            : provider is DatabaseType.MySQL or DatabaseType.MariaDB && IsBinary16(provider, type);

    private static bool IsTextType(DatabaseColumnType type, ulong length) =>
        (IsName(type, "char") || IsName(type, "varchar")) &&
        type.Length == length &&
        !type.Decimals.HasValue &&
        !type.Signed.HasValue;

    private static bool IsBinary16(DatabaseType provider, DatabaseColumnType type) =>
        provider is DatabaseType.MySQL or DatabaseType.MariaDB &&
        IsName(type, "binary") &&
        type.Length == 16 &&
        !type.Decimals.HasValue &&
        !type.Signed.HasValue;

    private static bool IsNativeUuidType(
        DatabaseColumnType type,
        bool allowSchemaModifiers) =>
        IsName(type, "uuid") &&
        (allowSchemaModifiers ||
         ((!type.Length.HasValue || type.Length == 0) &&
          (!type.Decimals.HasValue || type.Decimals == 0) &&
          !type.Signed.HasValue));

    private static bool IsName(DatabaseColumnType type, string expected) =>
        string.Equals(type.Name, expected, StringComparison.OrdinalIgnoreCase);
}
