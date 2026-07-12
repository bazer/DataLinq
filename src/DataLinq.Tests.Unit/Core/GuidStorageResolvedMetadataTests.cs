using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.Testing;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class GuidStorageResolvedMetadataTests
{
    [Test]
    public async Task Build_NoTypeDeclarations_ResolvesDeterministicProviderDefaults()
    {
        var column = Build(CreateDraft(new CsTypeDeclaration(typeof(Guid))));
        var definitions = column.GuidStorageDefinitions.ToArray();

        await Assert.That(definitions.Length).IsEqualTo(3);
        await AssertDefinition(
            definitions[0],
            DatabaseType.MySQL,
            GuidStorageFormat.Binary16LittleEndian,
            isExplicit: false);
        await AssertDefinition(
            definitions[1],
            DatabaseType.MariaDB,
            GuidStorageFormat.NativeUuid,
            isExplicit: false);
        await AssertDefinition(
            definitions[2],
            DatabaseType.SQLite,
            GuidStorageFormat.Text36,
            isExplicit: false);
    }

    [Test]
    public async Task Build_ExactDeclarationOverridesDefaultForApplicableProviders()
    {
        var column = Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes:
            [
                new DatabaseColumnType(DatabaseType.MySQL, "binary", 16),
                new DatabaseColumnType(DatabaseType.SQLite, "TEXT")
            ],
            guidStorage:
            [
                new GuidStorageAttribute(GuidStorageFormat.Text36),
                new GuidStorageAttribute(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Binary16Rfc4122)
            ]));
        var definitions = column.GuidStorageDefinitions.ToArray();

        await Assert.That(definitions.Length).IsEqualTo(2);
        await AssertDefinition(
            definitions[0],
            DatabaseType.MySQL,
            GuidStorageFormat.Binary16Rfc4122,
            isExplicit: true);
        await AssertDefinition(
            definitions[1],
            DatabaseType.SQLite,
            GuidStorageFormat.Text36,
            isExplicit: true);
        await Assert.That(column.GetGuidStorageFor(DatabaseType.MariaDB)).IsNull();
    }

    [Test]
    public async Task Build_MySqlBinary16WithoutDeclaration_PreservesLegacyCompatibilityFormat()
    {
        var column = Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "BINARY", 16)]));

        await Assert.That(column.GuidStorageDefinitions.Count).IsEqualTo(1);
        await AssertDefinition(
            column.GuidStorageDefinitions[0],
            DatabaseType.MySQL,
            GuidStorageFormat.Binary16LittleEndian,
            isExplicit: false);
    }

    [Test]
    public async Task Build_TextLengthsAndNativeUuid_InferTheBoundedFormats()
    {
        var column = Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes:
            [
                new DatabaseColumnType(DatabaseType.MySQL, "CHAR", 32),
                new DatabaseColumnType(DatabaseType.MariaDB, "varchar", 36),
                new DatabaseColumnType(DatabaseType.SQLite, "text")
            ]));
        var definitions = column.GuidStorageDefinitions.ToArray();

        await AssertDefinition(
            definitions[0],
            DatabaseType.MySQL,
            GuidStorageFormat.Text32,
            isExplicit: false);
        await AssertDefinition(
            definitions[1],
            DatabaseType.MariaDB,
            GuidStorageFormat.Text36,
            isExplicit: false);
        await AssertDefinition(
            definitions[2],
            DatabaseType.SQLite,
            GuidStorageFormat.Text36,
            isExplicit: false);

        var nativeColumn = Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MariaDB, "UUID")],
            guidStorage:
            [
                new GuidStorageAttribute(
                    DatabaseType.MariaDB,
                    GuidStorageFormat.NativeUuid)
            ]));
        await AssertDefinition(
            nativeColumn.GuidStorageDefinitions.Single(),
            DatabaseType.MariaDB,
            GuidStorageFormat.NativeUuid,
            isExplicit: true);
    }

    [Test]
    public async Task Build_MariaDbNativeUuid_RejectsSqlTypeModifiers()
    {
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MariaDB, "uuid", 16)]));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("uuid(16)");
        await Assert.That(failure.Message).Contains("unsupported");
    }

    [Test]
    public async Task Build_BinaryAndTextUuidTypes_RejectSqlModifiers()
    {
        DatabaseColumnType[] invalidTypes =
        [
            new(DatabaseType.MySQL, "binary", 16, signed: false),
            new(DatabaseType.MySQL, "char", 36, decimals: 2)
        ];

        foreach (var invalidType in invalidTypes)
        {
            var result = new MetadataDefinitionFactory().Build(CreateDraft(
                new CsTypeDeclaration(typeof(Guid)),
                dbTypes: [invalidType]));

            await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
            await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
            await Assert.That(failure.Message).Contains("unsupported");
        }
    }

    [Test]
    public async Task Build_SqliteBlobWithoutDeclaration_RejectsAmbiguousByteOrder()
    {
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.SQLite, "BLOB")]));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("layout is ambiguous");
        await Assert.That(failure.Message).Contains(nameof(GuidStorageFormat.Binary16Rfc4122));
    }

    [Test]
    public async Task Build_SqliteBlobWithExplicitBinaryFormat_Resolves()
    {
        var column = Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.SQLite, "blob")],
            guidStorage:
            [
                new GuidStorageAttribute(
                    DatabaseType.SQLite,
                    GuidStorageFormat.Binary16Rfc4122)
            ]));

        await AssertDefinition(
            column.GuidStorageDefinitions.Single(),
            DatabaseType.SQLite,
            GuidStorageFormat.Binary16Rfc4122,
            isExplicit: true);
    }

    [Test]
    public async Task Build_IncompatibleExplicitFormat_ReportsPhysicalType()
    {
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)],
            guidStorage:
            [
                new GuidStorageAttribute(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Text36)
            ]));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("incompatible");
        await Assert.That(failure.Message).Contains("binary(16)");
    }

    [Test]
    public async Task Build_TypedIdBackedByGuid_IsEligible()
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(TypedGuidId)),
            new CsTypeDeclaration(typeof(Guid)),
            new CsTypeDeclaration(typeof(TypedGuidIdConverter)),
            static () => new TypedGuidIdConverter());
        var column = Build(CreateDraft(
            new CsTypeDeclaration(typeof(TypedGuidId)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)],
            guidStorage:
            [
                new GuidStorageAttribute(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Binary16Rfc4122)
            ],
            scalarConverter: scalarConverter));

        await Assert.That(column.IsGuidColumn).IsTrue();
        await Assert.That(column.ModelClrType).IsEqualTo(typeof(TypedGuidId));
        await Assert.That(column.ProviderClrType).IsEqualTo(typeof(Guid));
        await AssertDefinition(
            column.GuidStorageDefinitions.Single(),
            DatabaseType.MySQL,
            GuidStorageFormat.Binary16Rfc4122,
            isExplicit: true);
    }

    [Test]
    public async Task Build_GuidModelConvertedToString_IsNotEligible()
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(Guid)),
            new CsTypeDeclaration(typeof(string)),
            new CsTypeDeclaration(typeof(GuidStringConverter)),
            static () => new GuidStringConverter());
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "char", 36)],
            guidStorage: [new GuidStorageAttribute(GuidStorageFormat.Text36)],
            scalarConverter: scalarConverter));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("canonical provider type");
        await Assert.That(failure.Message).Contains(typeof(string).FullName!);
    }

    [Test]
    public async Task BuildProviderMetadata_BareBinary16_DoesNotInventByteOrder()
    {
        var database = new MetadataDefinitionFactory()
            .BuildProviderMetadata(CreateDraft(
                new CsTypeDeclaration(typeof(Guid)),
                dbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)]))
            .ValueOrException();
        var column = database.TableModels.Single().Table.Columns.Single();

        await Assert.That(column.IsGuidColumn).IsTrue();
        await Assert.That(column.GuidStorageDefinitions).IsEmpty();
        await Assert.That(column.GetGuidStorageFor(DatabaseType.MySQL)).IsNull();
        await Assert.That(column.IsGuidStorageUnresolvedFor(DatabaseType.MySQL)).IsTrue();
        await Assert.That(column.IsGuidStorageUnresolvedFor(DatabaseType.MariaDB)).IsFalse();
        await Assert.That(column.UnresolvedGuidStorageProviders.ToArray())
            .IsEquivalentTo([DatabaseType.MySQL]);

        var modelSource = string.Join(
            Environment.NewLine,
            new ModelFileFactory(new ModelFileFactoryOptions())
                .CreateModelFiles(database)
                .Select(static file => file.contents));
        await Assert.That(modelSource).Contains("#error DATALINQ_UUID_STORAGE_UNRESOLVED");
        await Assert.That(modelSource).Contains("[GuidStorageUnresolved(DatabaseType.MySQL)]");
        await Assert.That(modelSource).Contains("Binary16LittleEndian or Binary16Rfc4122");

        var reparsed = MetadataSourceRoundtrip.ParseGeneratedModelSource(database);
        var reparsedColumn = reparsed.TableModels.Single().Table.Columns.Single();

        await Assert.That(reparsedColumn.ValueProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()
            .Select(static x => x.DatabaseType)
            .ToArray()).IsEquivalentTo([DatabaseType.MySQL]);
        await Assert.That(MetadataEquivalenceDigest.CreateText(reparsed))
            .IsEqualTo(MetadataEquivalenceDigest.CreateText(database));
    }

    [Test]
    public async Task BuildProviderMetadata_MariaBinaryAndSqliteBlob_DoNotInventByteOrder()
    {
        var mariaColumn = new MetadataDefinitionFactory()
            .BuildProviderMetadata(CreateDraft(
                new CsTypeDeclaration(typeof(Guid)),
                dbTypes: [new DatabaseColumnType(DatabaseType.MariaDB, "binary", 16)]))
            .ValueOrException()
            .TableModels.Single().Table.Columns.Single();
        var sqliteColumn = new MetadataDefinitionFactory()
            .BuildProviderMetadata(CreateDraft(
                new CsTypeDeclaration(typeof(Guid)),
                dbTypes: [new DatabaseColumnType(DatabaseType.SQLite, "BLOB")]))
            .ValueOrException()
            .TableModels.Single().Table.Columns.Single();

        await Assert.That(mariaColumn.GuidStorageDefinitions).IsEmpty();
        await Assert.That(sqliteColumn.GuidStorageDefinitions).IsEmpty();
        await Assert.That(mariaColumn.UnresolvedGuidStorageProviders.ToArray())
            .IsEquivalentTo([DatabaseType.MariaDB]);
        await Assert.That(sqliteColumn.UnresolvedGuidStorageProviders.ToArray())
            .IsEquivalentTo([DatabaseType.SQLite]);
    }

    [Test]
    public async Task BuildProviderMetadata_NativeUuidModifiers_AreNormalizedInGeneratedModelSource()
    {
        var database = new MetadataDefinitionFactory()
            .BuildProviderMetadata(CreateDraft(
                new CsTypeDeclaration(typeof(Guid)),
                dbTypes:
                [
                    new DatabaseColumnType(
                        DatabaseType.MariaDB,
                        "uuid",
                        length: 36,
                        decimals: 2,
                        signed: false)
                ]))
            .ValueOrException();
        var column = database.TableModels.Single().Table.Columns.Single();
        var modelSource = string.Join(
            Environment.NewLine,
            new ModelFileFactory(new ModelFileFactoryOptions())
                .CreateModelFiles(database)
                .Select(static file => file.contents));

        await AssertDefinition(
            column.GuidStorageDefinitions.Single(),
            DatabaseType.MariaDB,
            GuidStorageFormat.NativeUuid,
            isExplicit: false);
        await Assert.That(modelSource)
            .Contains("[Type(DatabaseType.MariaDB, \"uuid\")]");
        await Assert.That(modelSource).DoesNotContain("\"uuid\", 36");
        await Assert.That(modelSource).DoesNotContain("#error DATALINQ_UUID_STORAGE_UNRESOLVED");

        var reparsed = MetadataSourceRoundtrip.ParseGeneratedModelSource(database);
        var reparsedType = reparsed.TableModels.Single().Table.Columns.Single().DbTypes.Single();

        await Assert.That(reparsedType.Name).IsEqualTo("uuid");
        await Assert.That(reparsedType.Length).IsNull();
        await Assert.That(reparsedType.Decimals).IsNull();
        await Assert.That(reparsedType.Signed).IsNull();
    }

    [Test]
    public async Task Build_MariaBinary16WithoutDeclaration_PreservesLegacyCompatibilityFormat()
    {
        var column = Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MariaDB, "binary", 16)]));

        await AssertDefinition(
            column.GuidStorageDefinitions.Single(),
            DatabaseType.MariaDB,
            GuidStorageFormat.Binary16LittleEndian,
            isExplicit: false);
    }

    [Test]
    public async Task Build_RejectsStaleCarriedResolvedDefinitions()
    {
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(Guid)),
            dbTypes: [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)],
            carriedDefinitions:
            [
                new GuidStorageDefinition(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Binary16Rfc4122,
                    IsExplicit: false)
            ]));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("stale or inconsistent");
    }

    [Test]
    public async Task Build_RejectsGuidStorageOnRelationProperty()
    {
        var relationProperty = new MetadataRelationPropertyDraft(
            "Children",
            new CsTypeDeclaration(typeof(object)))
        {
            Attributes = [new GuidStorageAttribute(GuidStorageFormat.Text36)]
        };
        var result = new MetadataDefinitionFactory().Build(CreateDraft(
            new CsTypeDeclaration(typeof(int)),
            relationProperties: [relationProperty]));

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("relation property");
        await Assert.That(failure.Message).Contains("mapped value properties");
    }

    private static ColumnDefinition Build(MetadataDatabaseDraft draft) =>
        new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels.Single()
            .Table.Columns.Single();

    private static async Task AssertDefinition(
        GuidStorageDefinition definition,
        DatabaseType databaseType,
        GuidStorageFormat format,
        bool isExplicit)
    {
        await Assert.That(definition.DatabaseType).IsEqualTo(databaseType);
        await Assert.That(definition.Format).IsEqualTo(format);
        await Assert.That(definition.IsExplicit).IsEqualTo(isExplicit);
    }

    private static MetadataDatabaseDraft CreateDraft(
        CsTypeDeclaration propertyType,
        IReadOnlyList<DatabaseColumnType>? dbTypes = null,
        IReadOnlyList<GuidStorageAttribute>? guidStorage = null,
        MetadataScalarConverterDraft? scalarConverter = null,
        IReadOnlyList<GuidStorageDefinition>? carriedDefinitions = null,
        IReadOnlyList<MetadataRelationPropertyDraft>? relationProperties = null)
    {
        var attributes = (guidStorage ?? [])
            .Cast<Attribute>()
            .Prepend(new PrimaryKeyAttribute())
            .Prepend(new ColumnAttribute("id"))
            .ToArray();

        return new MetadataDatabaseDraft(
            "ResolvedGuidStorageDb",
            new CsTypeDeclaration(typeof(GuidStorageResolvedMetadataTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(
                        new CsTypeDeclaration(typeof(GuidStorageResolvedMetadataRow)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                propertyType,
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = dbTypes ?? [],
                                    GuidStorageDefinitions = carriedDefinitions ?? []
                                })
                            {
                                Attributes = attributes,
                                ScalarConverter = scalarConverter
                            }
                        ],
                        RelationProperties = relationProperties ?? []
                    },
                    new MetadataTableDraft("resolved_guid_storage_rows"))
            ]
        };
    }

    private readonly record struct TypedGuidId(Guid Value);

    private sealed class TypedGuidIdConverter : DataLinqScalarConverter<TypedGuidId, Guid>
    {
        public override Guid ToProvider(
            TypedGuidId modelValue,
            in ScalarConversionContext context) => modelValue.Value;

        public override TypedGuidId FromProvider(
            Guid providerValue,
            in ScalarConversionContext context) => new(providerValue);
    }

    private sealed class GuidStringConverter : DataLinqScalarConverter<Guid, string>
    {
        public override string ToProvider(
            Guid modelValue,
            in ScalarConversionContext context) => modelValue.ToString("D");

        public override Guid FromProvider(
            string providerValue,
            in ScalarConversionContext context) => Guid.ParseExact(providerValue, "D");
    }

    private sealed class GuidStorageResolvedMetadataRow;
}
