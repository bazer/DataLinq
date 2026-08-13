using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class SchemaComparerScalarStorageTests
{
    [Test]
    public async Task Compare_ConverterBackedCanonicalInt_UsesProviderPhysicalFallback()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(int)),
                [GetCanonicalIntType(databaseType)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences).IsEmpty();
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalInt_NormalizesServerIntegerMetadata()
    {
        foreach (var databaseType in ServerProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(int)),
                [new DatabaseColumnType(databaseType, "INTEGER", 11)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences).IsEmpty();
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ServerIntegerFamilies_NormalizeDisplayWidthAndSignedDefault()
    {
        foreach (var databaseType in ServerProviders)
        {
            foreach (var typeName in ServerIntegerTypeNames)
            {
                var model = BuildModel(
                    new CsTypeDeclaration(typeof(int)),
                    [new DatabaseColumnType(databaseType, typeName, 3, signed: true)]);
                var database = BuildProviderSnapshot(
                    new CsTypeDeclaration(typeof(int)),
                    [new DatabaseColumnType(databaseType, typeName, 11)]);

                var differences = SchemaComparer.Compare(model, database, databaseType);

                await Assert.That(differences).IsEmpty();
            }
        }
    }

    [Test]
    public async Task Compare_DefaultPhysicalDeclaration_TranslatesToConcreteProviderType()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [GetDefaultCanonicalIntType(databaseType)],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(int)),
                [GetCanonicalIntType(databaseType)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences).IsEmpty();
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalInt_PreservesPhysicalTypeMismatch()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(string)),
                [GetTextType(databaseType)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnTypeMismatch]);
            await Assert.That(differences.Single().Message).Contains(databaseType.ToString());
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_MatchingCanonicalTypes_DoNotHideDeclaredPhysicalTypeMismatch()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [GetTextType(databaseType)],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(int)),
                [GetCanonicalIntType(databaseType)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnTypeMismatch]);
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalInt_RejectsMatchingTextStorage()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var textType = GetTextType(databaseType);
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [textType],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(string)),
                [textType]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await AssertCanonicalIntMismatch(differences, databaseType, textType.Name);
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalInt_RejectsMatchingUnsignedIntStorage()
    {
        foreach (var databaseType in ServerProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var unsignedInt = new DatabaseColumnType(databaseType, "int", signed: false);
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [unsignedInt],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(uint)),
                [unsignedInt]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await AssertCanonicalIntMismatch(differences, databaseType, "int unsigned");
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalInt_RejectsOtherMatchingSignedIntegerFamilies()
    {
        foreach (var databaseType in ServerProviders)
        {
            foreach (var typeName in new[] { "mediumint", "bigint" })
            {
                var converter = new SchemaTypedIdConverter();
                var physicalType = new DatabaseColumnType(databaseType, typeName, signed: true);
                var model = BuildModel(
                    new CsTypeDeclaration(typeof(SchemaTypedId)),
                    dbTypes: [physicalType],
                    CreateScalarConverter(converter));
                var database = BuildProviderSnapshot(
                    new CsTypeDeclaration(typeof(int)),
                    [physicalType]);

                var differences = SchemaComparer.Compare(model, database, databaseType);

                await AssertCanonicalIntMismatch(differences, databaseType, typeName);
                await Assert.That(converter.Calls).IsEqualTo(0);
            }
        }
    }

    [Test]
    public async Task Compare_CanonicalSignedIntAgainstUnsignedDatabase_RemainsPhysicalMismatchOnly()
    {
        foreach (var databaseType in ServerProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [],
                CreateScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(uint)),
                [new DatabaseColumnType(databaseType, "int", 11, signed: false)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnTypeMismatch]);
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalLong_UsesProviderPhysicalFallback()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaLongTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaLongTypedId)),
                dbTypes: [],
                CreateLongScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(long)),
                [GetCanonicalLongType(databaseType)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences).IsEmpty();
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalLong_NormalizesImportedServerBigInt()
    {
        foreach (var databaseType in ServerProviders)
        {
            var converter = new SchemaLongTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaLongTypedId)),
                dbTypes: [],
                CreateLongScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(long)),
                [new DatabaseColumnType(databaseType, "BIGINT", 20)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences).IsEmpty();
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalLong_RejectsMatchingTextStorage()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaLongTypedIdConverter();
            var textType = GetTextType(databaseType);
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaLongTypedId)),
                dbTypes: [textType],
                CreateLongScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(string)),
                [textType]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await AssertCanonicalLongMismatch(differences, databaseType, textType.Name);
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ConverterBackedCanonicalLong_RejectsMatchingNarrowOrUnsignedServerStorage()
    {
        foreach (var databaseType in ServerProviders)
        {
            var cases = new[]
            {
                new DatabaseColumnType(databaseType, "int", signed: true),
                new DatabaseColumnType(databaseType, "mediumint", signed: true),
                new DatabaseColumnType(databaseType, "bigint", signed: false)
            };

            foreach (var physicalType in cases)
            {
                var converter = new SchemaLongTypedIdConverter();
                var model = BuildModel(
                    new CsTypeDeclaration(typeof(SchemaLongTypedId)),
                    dbTypes: [physicalType],
                    CreateLongScalarConverter(converter));
                var database = BuildProviderSnapshot(
                    new CsTypeDeclaration(typeof(long)),
                    [physicalType]);

                var differences = SchemaComparer.Compare(model, database, databaseType);

                await AssertCanonicalLongMismatch(
                    differences,
                    databaseType,
                    physicalType.Signed == false ? "bigint unsigned" : physicalType.Name);
                await Assert.That(converter.Calls).IsEqualTo(0);
            }
        }
    }

    [Test]
    public async Task Compare_CanonicalLongAgainstNarrowServerDatabase_RemainsPhysicalMismatchOnly()
    {
        foreach (var databaseType in ServerProviders)
        {
            var converter = new SchemaLongTypedIdConverter();
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaLongTypedId)),
                dbTypes: [],
                CreateLongScalarConverter(converter));
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(int)),
                [new DatabaseColumnType(databaseType, "int", 11)]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                .IsEquivalentTo([SchemaDifferenceKind.ColumnTypeMismatch]);
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_UnresolvedCanonicalLongClrType_SkipsCompatibilityCheck()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaLongTypedIdConverter();
            var textType = GetTextType(databaseType);
            var unresolvedConverter = new MetadataScalarConverterDraft(
                new CsTypeDeclaration(typeof(SchemaLongTypedId)),
                new CsTypeDeclaration("Int64", "System", ModelCsType.Primitive),
                new CsTypeDeclaration(typeof(SchemaLongTypedIdConverter)),
                () => converter);
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaLongTypedId)),
                dbTypes: [textType],
                unresolvedConverter);
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(string)),
                [textType]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(model.TableModels.Single().Table.Columns.Single().ProviderClrType).IsNull();
            await Assert.That(differences).IsEmpty();
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_ServerNonIntegerModifiers_RemainPhysicalTypeDifferences()
    {
        foreach (var databaseType in ServerProviders)
        {
            var cases = new[]
            {
                (
                    ClrType: typeof(bool),
                    ModelType: new DatabaseColumnType(databaseType, "bit", 1),
                    DatabaseType: new DatabaseColumnType(databaseType, "bit", 2)),
                (
                    ClrType: typeof(string),
                    ModelType: new DatabaseColumnType(databaseType, "varchar", 32),
                    DatabaseType: new DatabaseColumnType(databaseType, "varchar", 64)),
                (
                    ClrType: typeof(byte[]),
                    ModelType: new DatabaseColumnType(databaseType, "binary", 16),
                    DatabaseType: new DatabaseColumnType(databaseType, "binary", 32)),
                (
                    ClrType: typeof(byte[]),
                    ModelType: new DatabaseColumnType(databaseType, "varbinary", 16),
                    DatabaseType: new DatabaseColumnType(databaseType, "varbinary", 32)),
                (
                    ClrType: typeof(decimal),
                    ModelType: new DatabaseColumnType(databaseType, "decimal", 18, 4),
                    DatabaseType: new DatabaseColumnType(databaseType, "decimal", 18, 2))
            };

            foreach (var item in cases)
            {
                var model = BuildModel(
                    new CsTypeDeclaration(item.ClrType),
                    [item.ModelType]);
                var database = BuildProviderSnapshot(
                    new CsTypeDeclaration(item.ClrType),
                    [item.DatabaseType]);

                var differences = SchemaComparer.Compare(model, database, databaseType);

                await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
                    .IsEquivalentTo([SchemaDifferenceKind.ColumnTypeMismatch]);
            }
        }
    }

    [Test]
    public async Task Compare_UnresolvedCanonicalClrType_SkipsCompatibilityCheck()
    {
        foreach (var databaseType in BuiltInProviders)
        {
            var converter = new SchemaTypedIdConverter();
            var textType = GetTextType(databaseType);
            var unresolvedConverter = new MetadataScalarConverterDraft(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                new CsTypeDeclaration("Int32", "System", ModelCsType.Primitive),
                new CsTypeDeclaration(typeof(SchemaTypedIdConverter)),
                () => converter);
            var model = BuildModel(
                new CsTypeDeclaration(typeof(SchemaTypedId)),
                dbTypes: [textType],
                unresolvedConverter);
            var database = BuildProviderSnapshot(
                new CsTypeDeclaration(typeof(string)),
                [textType]);

            var differences = SchemaComparer.Compare(model, database, databaseType);

            await Assert.That(model.TableModels.Single().Table.Columns.Single().ProviderClrType).IsNull();
            await Assert.That(differences).IsEmpty();
            await Assert.That(converter.Calls).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Compare_DatabaseSnapshotWithoutActiveProviderType_RemainsUnresolved()
    {
        var converter = new SchemaTypedIdConverter();
        var model = BuildModel(
            new CsTypeDeclaration(typeof(SchemaTypedId)),
            dbTypes: [],
            CreateScalarConverter(converter));
        var database = BuildProviderSnapshot(
            new CsTypeDeclaration(typeof(int)),
            [GetCanonicalIntType(DatabaseType.MariaDB)]);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        await Assert.That(differences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnTypeMismatch]);
        await Assert.That(differences.Single().Message).Contains("database: ''");
        await Assert.That(converter.Calls).IsEqualTo(0);
    }

    private static readonly DatabaseType[] BuiltInProviders =
    [
        DatabaseType.SQLite,
        DatabaseType.MySQL,
        DatabaseType.MariaDB
    ];

    private static readonly DatabaseType[] ServerProviders =
    [
        DatabaseType.MySQL,
        DatabaseType.MariaDB
    ];

    private static readonly string[] ServerIntegerTypeNames =
    [
        "tinyint",
        "smallint",
        "mediumint",
        "int",
        "bigint"
    ];

    private static DatabaseDefinition BuildModel(
        CsTypeDeclaration modelType,
        IReadOnlyList<DatabaseColumnType> dbTypes,
        MetadataScalarConverterDraft? scalarConverter = null) =>
        Build(modelType, dbTypes, scalarConverter, providerSnapshot: false);

    private static DatabaseDefinition BuildProviderSnapshot(
        CsTypeDeclaration modelType,
        IReadOnlyList<DatabaseColumnType> dbTypes) =>
        Build(modelType, dbTypes, scalarConverter: null, providerSnapshot: true);

    private static DatabaseDefinition Build(
        CsTypeDeclaration modelType,
        IReadOnlyList<DatabaseColumnType> dbTypes,
        MetadataScalarConverterDraft? scalarConverter,
        bool providerSnapshot)
    {
        var draft = new MetadataDatabaseDraft(
            "SchemaScalarStorageDb",
            new CsTypeDeclaration(typeof(SchemaComparerScalarStorageTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(SchemaScalarStorageRow)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                modelType,
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = dbTypes
                                })
                            {
                                ScalarConverter = scalarConverter
                            }
                        ]
                    },
                    new MetadataTableDraft("schema_scalar_storage_rows"))
            ]
        };
        var factory = new MetadataDefinitionFactory();

        return providerSnapshot
            ? factory.BuildProviderMetadata(draft).ValueOrException()
            : factory.Build(draft).ValueOrException();
    }

    private static MetadataScalarConverterDraft CreateScalarConverter(
        SchemaTypedIdConverter converter) =>
        new(
            new CsTypeDeclaration(typeof(SchemaTypedId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(SchemaTypedIdConverter)),
            () => converter);

    private static MetadataScalarConverterDraft CreateLongScalarConverter(
        SchemaLongTypedIdConverter converter) =>
        new(
            new CsTypeDeclaration(typeof(SchemaLongTypedId)),
            new CsTypeDeclaration(typeof(long)),
            new CsTypeDeclaration(typeof(SchemaLongTypedIdConverter)),
            () => converter);

    private static DatabaseColumnType GetCanonicalIntType(DatabaseType databaseType) =>
        databaseType == DatabaseType.SQLite
            ? new DatabaseColumnType(databaseType, "integer")
            : new DatabaseColumnType(databaseType, "int", signed: true);

    private static DatabaseColumnType GetDefaultCanonicalIntType(DatabaseType databaseType) =>
        databaseType == DatabaseType.SQLite
            ? new DatabaseColumnType(DatabaseType.Default, "integer")
            : new DatabaseColumnType(DatabaseType.Default, "integer", signed: true);

    private static DatabaseColumnType GetCanonicalLongType(DatabaseType databaseType) =>
        databaseType == DatabaseType.SQLite
            ? new DatabaseColumnType(databaseType, "integer")
            : new DatabaseColumnType(databaseType, "bigint", signed: true);

    private static DatabaseColumnType GetTextType(DatabaseType databaseType) =>
        databaseType == DatabaseType.SQLite
            ? new DatabaseColumnType(databaseType, "text")
            : new DatabaseColumnType(databaseType, "varchar", 255);

    private static async Task AssertCanonicalIntMismatch(
        IReadOnlyList<SchemaDifference> differences,
        DatabaseType databaseType,
        string observedType)
    {
        await Assert.That(differences).Count().IsEqualTo(1);

        var difference = differences.Single();
        await Assert.That(difference.Kind).IsEqualTo(SchemaDifferenceKind.ColumnCanonicalTypeMismatch);
        await Assert.That(difference.Severity).IsEqualTo(SchemaDifferenceSeverity.Error);
        await Assert.That(difference.Safety).IsEqualTo(SchemaDifferenceSafety.Ambiguous);
        await Assert.That(difference.Path).IsEqualTo("schema_scalar_storage_rows.id");
        await Assert.That(difference.Message).Contains(typeof(int).FullName!);
        await Assert.That(difference.Message).Contains(databaseType.ToString());
        await Assert.That(difference.Message).Contains(observedType);
    }

    private static async Task AssertCanonicalLongMismatch(
        IReadOnlyList<SchemaDifference> differences,
        DatabaseType databaseType,
        string observedType)
    {
        await Assert.That(differences).Count().IsEqualTo(1);

        var difference = differences.Single();
        await Assert.That(difference.Kind).IsEqualTo(SchemaDifferenceKind.ColumnCanonicalTypeMismatch);
        await Assert.That(difference.Severity).IsEqualTo(SchemaDifferenceSeverity.Error);
        await Assert.That(difference.Safety).IsEqualTo(SchemaDifferenceSafety.Ambiguous);
        await Assert.That(difference.Path).IsEqualTo("schema_scalar_storage_rows.id");
        await Assert.That(difference.Message).Contains(typeof(long).FullName!);
        await Assert.That(difference.Message).Contains(databaseType.ToString());
        await Assert.That(difference.Message).Contains(observedType);
    }

    private sealed class SchemaScalarStorageRow;
    private readonly record struct SchemaTypedId(int Value);
    private readonly record struct SchemaLongTypedId(long Value);

    private sealed class SchemaTypedIdConverter : DataLinqScalarConverter<SchemaTypedId, int>
    {
        public int Calls { get; private set; }

        public override int ToProvider(
            SchemaTypedId modelValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return modelValue.Value;
        }

        public override SchemaTypedId FromProvider(
            int providerValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return new SchemaTypedId(providerValue);
        }
    }

    private sealed class SchemaLongTypedIdConverter : DataLinqScalarConverter<SchemaLongTypedId, long>
    {
        public int Calls { get; private set; }

        public override long ToProvider(
            SchemaLongTypedId modelValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return modelValue.Value;
        }

        public override SchemaLongTypedId FromProvider(
            long providerValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return new SchemaLongTypedId(providerValue);
        }
    }
}
