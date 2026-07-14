using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Testing;
using DataLinq.Tools;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class SchemaComparerGuidStorageTests
{
    [Test]
    public async Task Compare_DeferredDirectGuid_UsesRawTextAndNativeDeclarations()
    {
        var cases = new[]
        {
            new GuidStorageCase(
                DatabaseType.MySQL,
                new DatabaseColumnType(DatabaseType.MySQL, "char", 36),
                GuidStorageFormat.Text36,
                typeof(string)),
            new GuidStorageCase(
                DatabaseType.MariaDB,
                new DatabaseColumnType(DatabaseType.MariaDB, "varchar", 32),
                GuidStorageFormat.Text32,
                typeof(string)),
            new GuidStorageCase(
                DatabaseType.MariaDB,
                new DatabaseColumnType(DatabaseType.MariaDB, "uuid"),
                GuidStorageFormat.NativeUuid,
                typeof(Guid))
        };

        foreach (var testCase in cases)
        {
            var model = BuildDatabase(
                DeferredGuidSyntax,
                testCase.PhysicalType,
                testCase.Format,
                MetadataBuildMode.DeferredSource);
            var modelColumn = model.TableModels.Single().Table.Columns.Single();
            var database = BuildDatabase(
                new CsTypeDeclaration(testCase.DatabaseClrType),
                testCase.PhysicalType,
                guidStorageFormat: null,
                MetadataBuildMode.ProviderSnapshot);

            var differences = SchemaComparer.Compare(model, database, testCase.Provider);

            await Assert.That(modelColumn.GuidStorageDefinitions).IsEmpty();
            await Assert.That(differences).IsEmpty();
        }
    }

    [Test]
    public async Task Compare_DeferredBareGuidSyntax_DoesNotAssumeAssemblyIdentityMapping()
    {
        var cases = new[]
        {
            new GuidStorageCase(
                DatabaseType.MySQL,
                new DatabaseColumnType(DatabaseType.MySQL, "char", 36),
                GuidStorageFormat.Text36,
                typeof(string)),
            new GuidStorageCase(
                DatabaseType.MariaDB,
                new DatabaseColumnType(DatabaseType.MariaDB, "uuid"),
                GuidStorageFormat.NativeUuid,
                typeof(Guid))
        };

        foreach (var testCase in cases)
        {
            var model = BuildDatabase(
                DeferredGuidSyntax,
                testCase.PhysicalType,
                guidStorageFormat: null,
                MetadataBuildMode.DeferredSource);
            var database = BuildDatabase(
                new CsTypeDeclaration(testCase.DatabaseClrType),
                testCase.PhysicalType,
                guidStorageFormat: null,
                MetadataBuildMode.ProviderSnapshot);

            var differences = SchemaComparer.Compare(model, database, testCase.Provider);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
            await Assert.That(differences.Single().Message).Contains("Model UUID storage intent is unresolved");
            await Assert.That(differences.Single().Message).Contains("assembly-registered scalar converter");
        }
    }

    [Test]
    public async Task Compare_DeferredGlobalGuidSyntax_PreservesRawAndBareIntent()
    {
        var physicalType = new DatabaseColumnType(DatabaseType.MySQL, "char", 36);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(string)),
            physicalType,
            guidStorageFormat: null,
            MetadataBuildMode.ProviderSnapshot);
        var declaredModel = BuildDatabase(
            DeferredGlobalGuidSyntax,
            physicalType,
            GuidStorageFormat.Text36,
            MetadataBuildMode.DeferredSource);
        var bareModel = BuildDatabase(
            DeferredGlobalGuidSyntax,
            physicalType,
            guidStorageFormat: null,
            MetadataBuildMode.DeferredSource);

        var declaredDifferences = SchemaComparer.Compare(declaredModel, database, DatabaseType.MySQL);
        var bareDifferences = SchemaComparer.Compare(bareModel, database, DatabaseType.MySQL);

        await Assert.That(declaredDifferences).IsEmpty();
        await Assert.That(bareDifferences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
        await Assert.That(bareDifferences.Single().Message).Contains("Model UUID storage intent is unresolved");
    }

    [Test]
    public async Task Compare_UnhintedSqliteText_DoesNotInventTextShape()
    {
        foreach (var format in new[] { GuidStorageFormat.Text36, GuidStorageFormat.Text32 })
        {
            var physicalType = new DatabaseColumnType(DatabaseType.SQLite, "TEXT");
            var model = BuildDatabase(
                new CsTypeDeclaration(typeof(Guid)),
                physicalType,
                format,
                MetadataBuildMode.Model);
            var database = BuildDatabase(
                new CsTypeDeclaration(typeof(string)),
                physicalType,
                guidStorageFormat: null,
                MetadataBuildMode.ProviderSnapshot);

            var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
            await Assert.That(differences.Single().Message).Contains(format.ToString());
            await Assert.That(differences.Single().Message).Contains("does not encode its UUID representation");
        }
    }

    [Test]
    public async Task Compare_TrustedSqliteTextMetadata_DetectsKnownShapeChange()
    {
        var physicalType = new DatabaseColumnType(DatabaseType.SQLite, "TEXT");
        var model = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            GuidStorageFormat.Text32,
            MetadataBuildMode.Model);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            GuidStorageFormat.Text36,
            MetadataBuildMode.Model);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatMismatch]);
        await Assert.That(differences.Single().Message).Contains(nameof(GuidStorageFormat.Text32));
        await Assert.That(differences.Single().Message).Contains(nameof(GuidStorageFormat.Text36));
        await Assert.That(differences.Single().Message).Contains("manual data migration");
    }

    [Test]
    public async Task Compare_DefaultNewUuidVersions_AreSemanticallyDistinct()
    {
        var physicalType = new DatabaseColumnType(DatabaseType.SQLite, "TEXT");
        var version4 = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            GuidStorageFormat.Text36,
            MetadataBuildMode.Model,
            defaultAttribute: new DefaultNewUUIDAttribute(UUIDVersion.Version4));
        var version7 = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            GuidStorageFormat.Text36,
            MetadataBuildMode.Model,
            defaultAttribute: new DefaultNewUUIDAttribute(UUIDVersion.Version7));

        var schemaDifferences = SchemaComparer.Compare(version4, version7, DatabaseType.SQLite);
        var roundtripDifferences = MetadataRoundtripComparison.CompareSupportedSubset(
            version4,
            version7,
            DatabaseType.SQLite);
        var version4Digest = MetadataEquivalenceDigest.CreateText(version4);
        var version7Digest = MetadataEquivalenceDigest.CreateText(version7);

        await Assert.That(schemaDifferences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnDefaultMismatch]);
        await Assert.That(roundtripDifferences.Count).IsEqualTo(1);
        await Assert.That(roundtripDifferences.Single()).Contains(".default");
        await Assert.That(roundtripDifferences.Single()).Contains(UUIDVersion.Version4.ToString());
        await Assert.That(roundtripDifferences.Single()).Contains(UUIDVersion.Version7.ToString());
        await Assert.That(version4Digest).Contains(UUIDVersion.Version4.ToString());
        await Assert.That(version7Digest).Contains(UUIDVersion.Version7.ToString());
        await Assert.That(version4Digest).IsNotEqualTo(version7Digest);
    }

    [Test]
    public async Task Compare_SameFormat_IgnoresDeclarationProvenance()
    {
        var physicalType = new DatabaseColumnType(DatabaseType.MySQL, "char", 36);
        var model = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            GuidStorageFormat.Text36,
            MetadataBuildMode.Model);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            guidStorageFormat: null,
            MetadataBuildMode.ProviderSnapshot);
        var modelDefinition = model.TableModels.Single().Table.Columns.Single()
            .GetGuidStorageFor(DatabaseType.MySQL)!;
        var databaseDefinition = database.TableModels.Single().Table.Columns.Single()
            .GetGuidStorageFor(DatabaseType.MySQL)!;

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        await Assert.That(modelDefinition.IsExplicit).IsTrue();
        await Assert.That(databaseDefinition.IsExplicit).IsFalse();
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Compare_DeferredDirectGuid_UsesDefaultStorageDeclaration()
    {
        var physicalType = new DatabaseColumnType(DatabaseType.MySQL, "char", 36);
        var model = BuildDatabase(
            DeferredGuidSyntax,
            physicalType,
            GuidStorageFormat.Text36,
            MetadataBuildMode.DeferredSource,
            guidStorageProvider: DatabaseType.Default);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(string)),
            physicalType,
            guidStorageFormat: null,
            MetadataBuildMode.ProviderSnapshot);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Compare_AmbiguousBinaryProviderMetadata_ReturnsUnresolvedFormat()
    {
        foreach (var testCase in BinaryCases)
        {
            var model = BuildDatabase(
                new CsTypeDeclaration(typeof(Guid)),
                testCase.PhysicalType,
                GuidStorageFormat.Binary16Rfc4122,
                MetadataBuildMode.Model);
            var database = BuildDatabase(
                new CsTypeDeclaration(testCase.DatabaseClrType),
                testCase.PhysicalType,
                guidStorageFormat: null,
                MetadataBuildMode.ProviderSnapshot);

            var differences = SchemaComparer.Compare(model, database, testCase.Provider);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
            await Assert.That(differences.Single().Severity).IsEqualTo(SchemaDifferenceSeverity.Error);
            await Assert.That(differences.Single().Safety).IsEqualTo(SchemaDifferenceSafety.Ambiguous);
            await Assert.That(differences.Single().Message).Contains("Binary16Rfc4122");
            await Assert.That(differences.Single().Message).Contains("does not encode its UUID representation");
        }
    }

    [Test]
    public async Task Compare_KnownDifferentBinaryFormats_RequiresManualMigration()
    {
        foreach (var testCase in BinaryCases)
        {
            var model = BuildDatabase(
                new CsTypeDeclaration(typeof(Guid)),
                testCase.PhysicalType,
                GuidStorageFormat.Binary16Rfc4122,
                MetadataBuildMode.Model);
            var database = BuildDatabase(
                new CsTypeDeclaration(typeof(Guid)),
                testCase.PhysicalType,
                GuidStorageFormat.Binary16LittleEndian,
                MetadataBuildMode.Model);

            var differences = SchemaComparer.Compare(model, database, testCase.Provider);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatMismatch]);
            await Assert.That(differences.Single().Message).Contains("Binary16Rfc4122");
            await Assert.That(differences.Single().Message).Contains("Binary16LittleEndian");
            await Assert.That(differences.Single().Message).Contains("manual data migration");
            await Assert.That(differences.Single().Message).Contains("will not generate an automatic rewrite");
        }
    }

    [Test]
    public async Task Compare_PhysicalTypeMismatch_SuppressesFormatNoise()
    {
        var model = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            new DatabaseColumnType(DatabaseType.MySQL, "binary", 16),
            GuidStorageFormat.Binary16Rfc4122,
            MetadataBuildMode.Model);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(string)),
            new DatabaseColumnType(DatabaseType.MySQL, "char", 36),
            guidStorageFormat: null,
            MetadataBuildMode.ProviderSnapshot);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnTypeMismatch]);
    }

    [Test]
    public async Task Compare_GuidBackedTypedId_UsesSameUnresolvedFormatBoundaryWithoutConversion()
    {
        var converter = new SchemaGuidIdConverter();
        var physicalType = new DatabaseColumnType(DatabaseType.MySQL, "binary", 16);
        var model = BuildDatabase(
            new CsTypeDeclaration(typeof(SchemaGuidId)),
            physicalType,
            GuidStorageFormat.Binary16Rfc4122,
            MetadataBuildMode.Model,
            converter);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            guidStorageFormat: null,
            MetadataBuildMode.ProviderSnapshot);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
        await Assert.That(converter.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task Compare_NonGuidBinaryColumn_DoesNotReceiveUuidDiagnostics()
    {
        var physicalType = new DatabaseColumnType(DatabaseType.SQLite, "BLOB");
        var model = BuildDatabase(
            new CsTypeDeclaration(typeof(byte[])),
            physicalType,
            guidStorageFormat: null,
            MetadataBuildMode.Model);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(byte[])),
            physicalType,
            guidStorageFormat: null,
            MetadataBuildMode.ProviderSnapshot);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task DiffScript_FormatDifferencesRemainReviewOnlyComments()
    {
        var physicalType = new DatabaseColumnType(DatabaseType.MySQL, "binary", 16);
        var model = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            GuidStorageFormat.Binary16Rfc4122,
            MetadataBuildMode.Model);
        var database = BuildDatabase(
            new CsTypeDeclaration(typeof(Guid)),
            physicalType,
            GuidStorageFormat.Binary16LittleEndian,
            MetadataBuildMode.Model);
        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MySQL, differences);

        await Assert.That(script).Contains("REVIEW REQUIRED Error/Ambiguous ColumnGuidStorageFormatMismatch");
        await Assert.That(script).Contains("No SQL generated: ambiguous change.");
        await Assert.That(script).DoesNotContain("ALTER TABLE");
    }

    private static readonly BinaryGuidStorageCase[] BinaryCases =
    [
        new(
            DatabaseType.MySQL,
            new DatabaseColumnType(DatabaseType.MySQL, "binary", 16),
            typeof(Guid)),
        new(
            DatabaseType.MariaDB,
            new DatabaseColumnType(DatabaseType.MariaDB, "binary", 16),
            typeof(Guid)),
        new(
            DatabaseType.SQLite,
            new DatabaseColumnType(DatabaseType.SQLite, "BLOB"),
            typeof(byte[]))
    ];

    private static readonly CsTypeDeclaration DeferredGuidSyntax =
        new(nameof(Guid), "DataLinq.Tests.Schema", ModelCsType.Class);

    private static readonly CsTypeDeclaration DeferredGlobalGuidSyntax =
        new("global::System.Guid", "DataLinq.Tests.Schema", ModelCsType.Class);

    private static DatabaseDefinition BuildDatabase(
        CsTypeDeclaration modelType,
        DatabaseColumnType physicalType,
        GuidStorageFormat? guidStorageFormat,
        MetadataBuildMode mode,
        IDataLinqScalarConverter? converter = null,
        DatabaseType? guidStorageProvider = null,
        DefaultAttribute? defaultAttribute = null)
    {
        var attributes = new List<Attribute>();
        if (guidStorageFormat.HasValue)
        {
            attributes.Add(new GuidStorageAttribute(
                guidStorageProvider ?? physicalType.DatabaseType,
                guidStorageFormat.Value));
        }

        if (defaultAttribute != null)
            attributes.Add(defaultAttribute);

        var draft = new MetadataDatabaseDraft(
            "SchemaGuidStorageDb",
            new CsTypeDeclaration(typeof(SchemaComparerGuidStorageTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(SchemaGuidStorageRow)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                modelType,
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [physicalType]
                                })
                            {
                                Attributes = attributes,
                                ScalarConverter = converter is null
                                    ? null
                                    : new MetadataScalarConverterDraft(
                                        new CsTypeDeclaration(converter.ModelType),
                                        new CsTypeDeclaration(converter.ProviderType),
                                        new CsTypeDeclaration(converter.GetType()),
                                        () => converter)
                            }
                        ]
                    },
                    new MetadataTableDraft("schema_guid_storage_rows"))
            ]
        };
        var factory = new MetadataDefinitionFactory();

        return mode switch
        {
            MetadataBuildMode.Model => factory.Build(draft).ValueOrException(),
            MetadataBuildMode.ProviderSnapshot => factory.BuildProviderMetadata(draft).ValueOrException(),
            MetadataBuildMode.DeferredSource => factory.BuildDeferredSourceMetadata(draft).ValueOrException(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }

    private enum MetadataBuildMode
    {
        Model,
        ProviderSnapshot,
        DeferredSource
    }

    private sealed record GuidStorageCase(
        DatabaseType Provider,
        DatabaseColumnType PhysicalType,
        GuidStorageFormat Format,
        Type DatabaseClrType);

    private sealed record BinaryGuidStorageCase(
        DatabaseType Provider,
        DatabaseColumnType PhysicalType,
        Type DatabaseClrType);

    private sealed class SchemaGuidStorageRow;
    private readonly record struct SchemaGuidId(Guid Value);

    private sealed class SchemaGuidIdConverter : DataLinqScalarConverter<SchemaGuidId, Guid>
    {
        public int Calls { get; private set; }

        public override Guid ToProvider(
            SchemaGuidId modelValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return modelValue.Value;
        }

        public override SchemaGuidId FromProvider(
            Guid providerValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return new SchemaGuidId(providerValue);
        }
    }
}
