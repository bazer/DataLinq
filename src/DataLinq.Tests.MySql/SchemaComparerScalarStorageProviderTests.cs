using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Testing;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed class SchemaComparerScalarStorageProviderTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task Compare_LiveCanonicalIntSchema_AcceptsSignedIntAndRejectsMatchingVarchar(
        TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(Compare_LiveCanonicalIntSchema_AcceptsSignedIntAndRejectsMatchingVarchar),
            """
            CREATE TABLE `schema_scalar_storage_rows` (
                `id` INT NOT NULL PRIMARY KEY,
                `invalid_id` VARCHAR(255) NOT NULL
            );
            """);
        var converter = new ProviderSchemaTypedIdConverter();
        var model = BuildModel(provider.DatabaseType, converter);
        var database = schema.ParseDatabase(
            "SchemaScalarStorageDb",
            "SchemaScalarStorageDb",
            "DataLinq.Tests.MySql");
        var intType = database.TableModels.Single()
            .Table.Columns.Single(column => column.DbName == "id")
            .GetDbTypeFor(provider.DatabaseType)!;

        var differences = SchemaComparer.Compare(model, database, provider.DatabaseType);

        await Assert.That(intType.Name).IsEqualTo("int");
        await Assert.That(intType.Signed).IsNull();

        await Assert.That(differences).Count().IsEqualTo(1);
        var difference = differences.Single();
        await Assert.That(difference.Kind).IsEqualTo(SchemaDifferenceKind.ColumnCanonicalTypeMismatch);
        await Assert.That(difference.Severity).IsEqualTo(SchemaDifferenceSeverity.Error);
        await Assert.That(difference.Safety).IsEqualTo(SchemaDifferenceSafety.Ambiguous);
        await Assert.That(difference.Path).IsEqualTo("schema_scalar_storage_rows.invalid_id");
        await Assert.That(difference.Message).Contains(typeof(int).FullName!);
        await Assert.That(difference.Message).Contains(provider.DatabaseType.ToString());
        await Assert.That(converter.Calls).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task Compare_LiveCanonicalLongSchema_AcceptsSignedBigIntAndRejectsMatchingInt(
        TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(Compare_LiveCanonicalLongSchema_AcceptsSignedBigIntAndRejectsMatchingInt),
            """
            CREATE TABLE `schema_scalar_long_storage_rows` (
                `id` BIGINT NOT NULL PRIMARY KEY,
                `invalid_id` INT NOT NULL
            );
            """);
        var converter = new ProviderSchemaLongTypedIdConverter();
        var model = BuildLongModel(provider.DatabaseType, converter);
        var database = schema.ParseDatabase(
            "SchemaScalarLongStorageDb",
            "SchemaScalarLongStorageDb",
            "DataLinq.Tests.MySql");
        var bigIntType = database.TableModels.Single()
            .Table.Columns.Single(column => column.DbName == "id")
            .GetDbTypeFor(provider.DatabaseType)!;

        var differences = SchemaComparer.Compare(model, database, provider.DatabaseType);

        await Assert.That(bigIntType.Name).IsEqualTo("bigint");
        await Assert.That(bigIntType.Signed).IsNull();

        await Assert.That(differences).Count().IsEqualTo(1);
        var difference = differences.Single();
        await Assert.That(difference.Kind).IsEqualTo(SchemaDifferenceKind.ColumnCanonicalTypeMismatch);
        await Assert.That(difference.Severity).IsEqualTo(SchemaDifferenceSeverity.Error);
        await Assert.That(difference.Safety).IsEqualTo(SchemaDifferenceSafety.Ambiguous);
        await Assert.That(difference.Path).IsEqualTo("schema_scalar_long_storage_rows.invalid_id");
        await Assert.That(difference.Message).Contains(typeof(long).FullName!);
        await Assert.That(difference.Message).Contains(provider.DatabaseType.ToString());
        await Assert.That(converter.Calls).IsEqualTo(0);
    }

    private static DatabaseDefinition BuildModel(
        DatabaseType databaseType,
        ProviderSchemaTypedIdConverter converter)
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(ProviderSchemaTypedId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(ProviderSchemaTypedIdConverter)),
            () => converter);
        var draft = new MetadataDatabaseDraft(
            "SchemaScalarStorageDb",
            new CsTypeDeclaration(typeof(SchemaComparerScalarStorageProviderTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(ProviderSchemaRow)))
                    {
                        ValueProperties =
                        [
                            CreateProperty(
                                "Id",
                                "id",
                                dbTypes: [],
                                scalarConverter,
                                primaryKey: true),
                            CreateProperty(
                                "InvalidId",
                                "invalid_id",
                                [new DatabaseColumnType(databaseType, "varchar", 255)],
                                scalarConverter)
                        ]
                    },
                    new MetadataTableDraft("schema_scalar_storage_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static DatabaseDefinition BuildLongModel(
        DatabaseType databaseType,
        ProviderSchemaLongTypedIdConverter converter)
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(ProviderSchemaLongTypedId)),
            new CsTypeDeclaration(typeof(long)),
            new CsTypeDeclaration(typeof(ProviderSchemaLongTypedIdConverter)),
            () => converter);
        var draft = new MetadataDatabaseDraft(
            "SchemaScalarLongStorageDb",
            new CsTypeDeclaration(typeof(SchemaComparerScalarStorageProviderTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(ProviderSchemaLongRow)))
                    {
                        ValueProperties =
                        [
                            CreateLongProperty(
                                "Id",
                                "id",
                                dbTypes: [],
                                scalarConverter,
                                primaryKey: true),
                            CreateLongProperty(
                                "InvalidId",
                                "invalid_id",
                                [new DatabaseColumnType(databaseType, "int", 11)],
                                scalarConverter)
                        ]
                    },
                    new MetadataTableDraft("schema_scalar_long_storage_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static MetadataValuePropertyDraft CreateProperty(
        string propertyName,
        string columnName,
        DatabaseColumnType[] dbTypes,
        MetadataScalarConverterDraft scalarConverter,
        bool primaryKey = false) =>
        new(
            propertyName,
            new CsTypeDeclaration(typeof(ProviderSchemaTypedId)),
            new MetadataColumnDraft(columnName)
            {
                PrimaryKey = primaryKey,
                DbTypes = dbTypes
            })
        {
            ScalarConverter = scalarConverter
        };

    private static MetadataValuePropertyDraft CreateLongProperty(
        string propertyName,
        string columnName,
        DatabaseColumnType[] dbTypes,
        MetadataScalarConverterDraft scalarConverter,
        bool primaryKey = false) =>
        new(
            propertyName,
            new CsTypeDeclaration(typeof(ProviderSchemaLongTypedId)),
            new MetadataColumnDraft(columnName)
            {
                PrimaryKey = primaryKey,
                DbTypes = dbTypes
            })
        {
            ScalarConverter = scalarConverter
        };

    private sealed class ProviderSchemaRow;
    private sealed class ProviderSchemaLongRow;
    private readonly record struct ProviderSchemaTypedId(int Value);
    private readonly record struct ProviderSchemaLongTypedId(long Value);

    private sealed class ProviderSchemaTypedIdConverter : DataLinqScalarConverter<ProviderSchemaTypedId, int>
    {
        public int Calls { get; private set; }

        public override int ToProvider(
            ProviderSchemaTypedId modelValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return modelValue.Value;
        }

        public override ProviderSchemaTypedId FromProvider(
            int providerValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return new ProviderSchemaTypedId(providerValue);
        }
    }

    private sealed class ProviderSchemaLongTypedIdConverter
        : DataLinqScalarConverter<ProviderSchemaLongTypedId, long>
    {
        public int Calls { get; private set; }

        public override long ToProvider(
            ProviderSchemaLongTypedId modelValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return modelValue.Value;
        }

        public override ProviderSchemaLongTypedId FromProvider(
            long providerValue,
            in ScalarConversionContext context)
        {
            Calls++;
            return new ProviderSchemaLongTypedId(providerValue);
        }
    }
}
