using System;

namespace DataLinq.Attributes;

/// <summary>
/// Defines the physical database representation used for a canonical
/// <see cref="Guid"/> value.
/// </summary>
public enum GuidStorageFormat
{
    /// <summary>
    /// A provider-native UUID type.
    /// </summary>
    NativeUuid,

    /// <summary>A lowercase dashed 36-character UUID string.</summary>
    Text36,

    /// <summary>A lowercase undashed 32-character UUID string.</summary>
    Text32,

    /// <summary>
    /// The legacy .NET mixed-endian 16-byte layout produced by
    /// <see cref="Guid.ToByteArray()"/>.
    /// </summary>
    Binary16LittleEndian,

    /// <summary>The RFC/string-order 16-byte UUID layout.</summary>
    Binary16Rfc4122
}

/// <summary>
/// Declares a physical UUID storage format for a mapped value property.
/// Multiple provider-scoped declarations may be attached to one property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
public sealed class GuidStorageAttribute : Attribute
{
    /// <summary>Creates a declaration scoped to <see cref="DatabaseType.Default"/>.</summary>
    public GuidStorageAttribute(GuidStorageFormat format)
        : this(DatabaseType.Default, format)
    {
    }

    /// <summary>Creates a declaration scoped to <paramref name="databaseType"/>.</summary>
    public GuidStorageAttribute(DatabaseType databaseType, GuidStorageFormat format)
    {
        DatabaseType = databaseType;
        Format = format;
    }

    /// <summary>Gets the provider scope, or <see cref="DatabaseType.Default"/>.</summary>
    public DatabaseType DatabaseType { get; }

    /// <summary>Gets the declared physical UUID format.</summary>
    public GuidStorageFormat Format { get; }
}
