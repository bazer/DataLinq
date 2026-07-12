using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway;

namespace DataLinq.Core.Factories;

internal enum GuidStorageResolutionMode
{
    Model,
    ProviderSnapshot,
    DeferredSource
}

public static partial class MetadataFactory
{
    private static readonly DatabaseType[] GuidStorageProviders =
    [
        DatabaseType.MySQL,
        DatabaseType.MariaDB,
        DatabaseType.SQLite
    ];

    internal static Option<bool, IDLOptionFailure> ResolveGuidStorageDefinitionsCore(
        DatabaseDefinition database,
        GuidStorageResolutionMode mode)
    {
        foreach (var tableModel in database.TableModels.Where(static x => !x.IsStub))
        {
            foreach (var column in tableModel.Table.Columns)
            {
                var property = column.ValueProperty;
                if (property is null)
                    continue;

                if (mode == GuidStorageResolutionMode.DeferredSource)
                {
                    column.SetGuidStorageDefinitionsCore([]);
                    column.SetUnresolvedGuidStorageProvidersCore([]);
                    continue;
                }

                var declarations = property.Attributes
                    .OfType<GuidStorageAttribute>()
                    .ToArray();
                var unresolvedDeclarations = property.Attributes
                    .OfType<GuidStorageUnresolvedAttribute>()
                    .ToArray();
                var carriedDefinitions = column.GuidStorageDefinitions.ToArray();
                var unresolvedProviders = new List<DatabaseType>();

                if (unresolvedDeclarations.Length != 0)
                {
                    var unresolved = unresolvedDeclarations[0];
                    return CreateValuePropertyAttributeFailure(
                        property,
                        unresolved,
                        $"UUID storage for value property '{GetValuePropertyDisplayName(property)}' is unresolved for provider '{unresolved.DatabaseType}'. Replace the generated GuidStorageUnresolved marker with an explicit GuidStorage declaration selecting Binary16LittleEndian or Binary16Rfc4122.");
                }

                if (!column.IsGuidColumn)
                {
                    if (declarations.Length != 0)
                    {
                        var providerType = FormatCanonicalProviderType(column);
                        return CreateValuePropertyAttributeFailure(
                            property,
                            declarations[0],
                            $"Guid storage attribute on value property '{GetValuePropertyDisplayName(property)}' requires canonical provider type '{typeof(Guid).FullName}', but the resolved canonical provider type is '{providerType}'.");
                    }

                    if (carriedDefinitions.Length != 0)
                    {
                        return CreateColumnPropertyFailure(
                            column,
                            $"Column '{column.Table.DbName}.{column.DbName}' carries resolved UUID storage metadata, but its canonical provider type is '{FormatCanonicalProviderType(column)}' rather than '{typeof(Guid).FullName}'. Regenerate the DataLinq model metadata.");
                    }

                    column.SetGuidStorageDefinitionsCore([]);
                    column.SetUnresolvedGuidStorageProvidersCore([]);
                    continue;
                }

                var applicableProviders = GetApplicableGuidStorageProviders(column, declarations);
                var resolvedDefinitions = new List<GuidStorageDefinition>(applicableProviders.Count);
                foreach (var provider in applicableProviders)
                {
                    var exactDeclaration = declarations.FirstOrDefault(x => x.DatabaseType == provider);
                    var declaration = exactDeclaration ?? declarations.FirstOrDefault(x => x.DatabaseType == DatabaseType.Default);
                    var effectiveType = EffectiveColumnTypeResolver.Resolve(column, provider);

                    if (effectiveType is null)
                    {
                        if (mode == GuidStorageResolutionMode.ProviderSnapshot && declaration is null)
                        {
                            unresolvedProviders.Add(provider);
                            continue;
                        }

                        return CreateColumnPropertyFailure(
                            column,
                            $"Guid column '{column.Table.DbName}.{column.DbName}' has no effective database type for provider '{provider}'. Declare a compatible [Type] mapping before selecting a UUID storage format.");
                    }

                    if (declaration is not null)
                    {
                        if (!IsGuidStorageFormatCompatible(
                            provider,
                            effectiveType,
                            declaration.Format,
                            mode))
                        {
                            return CreateValuePropertyAttributeFailure(
                                property,
                                declaration,
                                $"Guid storage format '{declaration.Format}' on value property '{GetValuePropertyDisplayName(property)}' is incompatible with effective {provider} database type '{FormatDatabaseType(effectiveType)}'. {DescribeCompatibleGuidStorage(provider, effectiveType)}");
                        }

                        resolvedDefinitions.Add(new GuidStorageDefinition(provider, declaration.Format, IsExplicit: true));
                        continue;
                    }

                    var inferredFormat = InferGuidStorageFormat(provider, effectiveType, mode);
                    if (inferredFormat.HasValue)
                    {
                        if (mode == GuidStorageResolutionMode.ProviderSnapshot &&
                            inferredFormat.Value == GuidStorageFormat.Binary16LittleEndian &&
                            IsBinary16(provider, effectiveType))
                        {
                            // A live BINARY(16) schema does not reveal byte order. Model metadata
                            // deliberately keeps the historical DataLinq compatibility default,
                            // while provider snapshots remain unresolved until schema validation.
                            unresolvedProviders.Add(provider);
                            continue;
                        }

                        resolvedDefinitions.Add(new GuidStorageDefinition(provider, inferredFormat.Value, IsExplicit: false));
                        continue;
                    }

                    if (mode == GuidStorageResolutionMode.ProviderSnapshot)
                    {
                        unresolvedProviders.Add(provider);
                        continue;
                    }

                    if (provider == DatabaseType.SQLite && IsSqliteBlob(effectiveType))
                    {
                        return CreateColumnPropertyFailure(
                            column,
                            $"Guid column '{column.Table.DbName}.{column.DbName}' uses SQLite type '{FormatDatabaseType(effectiveType)}', whose 16-byte UUID layout is ambiguous. Add [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16LittleEndian)] for the legacy .NET layout or Binary16Rfc4122 for RFC/string order.");
                    }

                    return CreateColumnPropertyFailure(
                        column,
                        $"Guid column '{column.Table.DbName}.{column.DbName}' uses unsupported effective {provider} database type '{FormatDatabaseType(effectiveType)}'. {DescribeCompatibleGuidStorage(provider, effectiveType)}");
                }

                var expectedDefinitions = resolvedDefinitions.ToArray();
                if (carriedDefinitions.Length != 0 &&
                    !carriedDefinitions.SequenceEqual(expectedDefinitions))
                {
                    return CreateColumnPropertyFailure(
                        column,
                        $"Column '{column.Table.DbName}.{column.DbName}' carries stale or inconsistent resolved UUID storage metadata. Regenerate the DataLinq model metadata with the current generator package.");
                }

                column.SetGuidStorageDefinitionsCore(expectedDefinitions);
                column.SetUnresolvedGuidStorageProvidersCore(unresolvedProviders);
            }
        }

        return true;
    }

    private static IReadOnlyList<DatabaseType> GetApplicableGuidStorageProviders(
        ColumnDefinition column,
        IReadOnlyList<GuidStorageAttribute> declarations)
    {
        var concreteTypes = new HashSet<DatabaseType>(column.DbTypes
            .Where(static x => x.DatabaseType is DatabaseType.MySQL or DatabaseType.MariaDB or DatabaseType.SQLite)
            .Select(static x => x.DatabaseType));
        var providerAgnosticType = column.DbTypes.Count == 0 ||
            column.DbTypes.Any(static x => x.DatabaseType == DatabaseType.Default);

        if (providerAgnosticType)
        {
            foreach (var provider in GuidStorageProviders)
                concreteTypes.Add(provider);
        }

        foreach (var declaration in declarations)
        {
            if (declaration.DatabaseType is DatabaseType.MySQL or DatabaseType.MariaDB or DatabaseType.SQLite)
                concreteTypes.Add(declaration.DatabaseType);
        }

        return GuidStorageProviders.Where(concreteTypes.Contains).ToArray();
    }

    private static GuidStorageFormat? InferGuidStorageFormat(
        DatabaseType provider,
        DatabaseColumnType type,
        GuidStorageResolutionMode mode)
    {
        if (provider == DatabaseType.MariaDB &&
            IsNativeUuidType(type, allowSchemaModifiers: mode == GuidStorageResolutionMode.ProviderSnapshot))
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

        if (provider == DatabaseType.SQLite && IsName(type, "text"))
            return GuidStorageFormat.Text36;

        return null;
    }

    private static bool IsGuidStorageFormatCompatible(
        DatabaseType provider,
        DatabaseColumnType type,
        GuidStorageFormat format,
        GuidStorageResolutionMode mode) => format switch
    {
        GuidStorageFormat.NativeUuid =>
            provider == DatabaseType.MariaDB &&
            IsNativeUuidType(type, allowSchemaModifiers: mode == GuidStorageResolutionMode.ProviderSnapshot),
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
                ? IsSqliteBlob(type)
                : provider is DatabaseType.MySQL or DatabaseType.MariaDB && IsBinary16(provider, type),
        _ => false
    };

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

    private static bool IsSqliteBlob(DatabaseColumnType type) => IsName(type, "blob");

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

    private static string FormatCanonicalProviderType(ColumnDefinition column) =>
        column.ProviderClrType?.FullName ??
        (string.IsNullOrWhiteSpace(column.ProviderCsType.Namespace)
            ? column.ProviderCsType.Name
            : $"{column.ProviderCsType.Namespace}.{column.ProviderCsType.Name}");

    private static string FormatDatabaseType(DatabaseColumnType type) => type.Length.HasValue
        ? $"{type.Name}({type.Length.Value})"
        : type.Name;

    private static string DescribeCompatibleGuidStorage(
        DatabaseType provider,
        DatabaseColumnType type)
    {
        _ = type;
        return provider switch
        {
            DatabaseType.MySQL =>
                "MySQL UUID storage requires BINARY(16), CHAR(36), VARCHAR(36), CHAR(32), or VARCHAR(32) with the matching GuidStorageFormat.",
            DatabaseType.MariaDB =>
                "MariaDB UUID storage requires an unmodified UUID type, or unmodified BINARY(16), CHAR(36), VARCHAR(36), CHAR(32), or VARCHAR(32) with the matching GuidStorageFormat.",
            DatabaseType.SQLite =>
                "SQLite UUID storage requires TEXT for Text36/Text32 or BLOB with an explicit binary GuidStorageFormat.",
            _ => "Declare a provider-compatible UUID storage type and format."
        };
    }
}
