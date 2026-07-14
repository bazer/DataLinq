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

    private static DatabaseColumnType GetCanonicalIntType(DatabaseType databaseType) =>
        databaseType == DatabaseType.SQLite
            ? new DatabaseColumnType(databaseType, "integer")
            : new DatabaseColumnType(databaseType, "int", signed: true);

    private static DatabaseColumnType GetDefaultCanonicalIntType(DatabaseType databaseType) =>
        databaseType == DatabaseType.SQLite
            ? new DatabaseColumnType(DatabaseType.Default, "integer")
            : new DatabaseColumnType(DatabaseType.Default, "integer", signed: true);

    private static DatabaseColumnType GetTextType(DatabaseType databaseType) =>
        databaseType == DatabaseType.SQLite
            ? new DatabaseColumnType(databaseType, "text")
            : new DatabaseColumnType(databaseType, "varchar", 255);

    private sealed class SchemaScalarStorageRow;
    private readonly record struct SchemaTypedId(int Value);

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
}
