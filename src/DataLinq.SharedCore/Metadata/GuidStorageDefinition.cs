using DataLinq.Attributes;

namespace DataLinq.Metadata;

/// <summary>
/// Describes the resolved physical UUID storage format for one database provider.
/// </summary>
/// <param name="DatabaseType">The concrete database provider this definition applies to.</param>
/// <param name="Format">The resolved physical UUID storage format.</param>
/// <param name="IsExplicit">
/// <see langword="true"/> when the format came from an explicit model declaration;
/// otherwise, <see langword="false"/>.
/// </param>
public sealed record GuidStorageDefinition(
    DatabaseType DatabaseType,
    GuidStorageFormat Format,
    bool IsExplicit);
