using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public class GuidStorageStaticDefaultTests
{
    private static readonly Guid KnownGuid =
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    [Test]
    public async Task GetCreateTables_StaticGuidDefaults_RoundTripExactPhysicalLayouts()
    {
        var database = Build(
            CreateIdColumn(),
            CreateGuidDefaultColumn("Text36", "text36", "TEXT", GuidStorageFormat.Text36),
            CreateGuidDefaultColumn("Text32", "text32", "TEXT", GuidStorageFormat.Text32),
            CreateGuidDefaultColumn("BinaryLittle", "binary_little", "BLOB", GuidStorageFormat.Binary16LittleEndian),
            CreateGuidDefaultColumn("BinaryRfc", "binary_rfc", "BLOB", GuidStorageFormat.Binary16Rfc4122));
        var createSql = new SqlFromSQLiteFactory()
            .GetCreateTables(database, foreignKeyRestrict: false)
            .ValueOrException()
            .Text;

        await Assert.That(createSql).Contains("DEFAULT '00112233-4455-6677-8899-aabbccddeeff'");
        await Assert.That(createSql).Contains("DEFAULT '00112233445566778899aabbccddeeff'");
        await Assert.That(createSql).Contains("DEFAULT X'33221100554477668899AABBCCDDEEFF'");
        await Assert.That(createSql).Contains("DEFAULT X'00112233445566778899AABBCCDDEEFF'");

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = createSql;
            create.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO \"guid_defaults\" DEFAULT VALUES;";
            insert.ExecuteNonQuery();
        }

        using var select = connection.CreateCommand();
        select.CommandText =
            "SELECT \"text36\", \"text32\", hex(\"binary_little\"), hex(\"binary_rfc\") FROM \"guid_defaults\";";
        using var reader = select.ExecuteReader();

        await Assert.That(reader.Read()).IsTrue();
        await Assert.That(reader.GetString(0)).IsEqualTo("00112233-4455-6677-8899-aabbccddeeff");
        await Assert.That(reader.GetString(1)).IsEqualTo("00112233445566778899aabbccddeeff");
        await Assert.That(reader.GetString(2)).IsEqualTo("33221100554477668899AABBCCDDEEFF");
        await Assert.That(reader.GetString(3)).IsEqualTo("00112233445566778899AABBCCDDEEFF");
        await Assert.That(reader.Read()).IsFalse();
    }

    [Test]
    public async Task GetDefaultValue_AmbiguousBlobMetadata_IsRejected()
    {
        var database = new MetadataDefinitionFactory()
            .BuildProviderMetadata(CreateDatabaseDraft(
                CreateIdColumn(),
                new MetadataValuePropertyDraft(
                    "Ambiguous",
                    new CsTypeDeclaration(typeof(Guid)),
                    new MetadataColumnDraft("ambiguous")
                    {
                        DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "BLOB")]
                    })
                {
                    Attributes =
                    [
                        new ColumnAttribute("ambiguous"),
                        new DefaultAttribute(KnownGuid)
                    ]
                }))
            .ValueOrException();
        var column = database.TableModels.Single().Table.Columns.Single(x => x.DbName == "ambiguous");
        InvalidOperationException? exception = null;

        try
        {
            _ = new SqlFromSQLiteFactory().GetDefaultValue(column);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("unresolved SQLite UUID storage metadata");
        await Assert.That(exception.Message).Contains("guid_defaults.ambiguous");
    }

    [Test]
    public async Task GetDefaultValue_ConverterBackedGuidDefault_IsRejectedWithoutConversion()
    {
        var converter = new RecordingTypedGuidConverter();
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(TypedGuidId)),
            new CsTypeDeclaration(typeof(Guid)),
            new CsTypeDeclaration(typeof(RecordingTypedGuidConverter)),
            () => converter);
        var database = Build(
            CreateIdColumn(),
            new MetadataValuePropertyDraft(
                "Converted",
                new CsTypeDeclaration(typeof(TypedGuidId)),
                new MetadataColumnDraft("converted")
                {
                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "TEXT")]
                })
            {
                Attributes =
                [
                    new ColumnAttribute("converted"),
                    new DefaultAttribute(
                        new TypedGuidId(KnownGuid),
                        "new TypedGuidId(Guid.Parse(\"00112233-4455-6677-8899-aabbccddeeff\"))"),
                    new GuidStorageAttribute(DatabaseType.SQLite, GuidStorageFormat.Text36)
                ],
                ScalarConverter = scalarConverter
            });
        var column = database.TableModels.Single().Table.Columns.Single(x => x.DbName == "converted");
        InvalidOperationException? exception = null;

        try
        {
            _ = new SqlFromSQLiteFactory().GetDefaultValue(column);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("direct canonical Guid mapping");
        await Assert.That(exception.Message).Contains("guid_defaults.converted");
        await Assert.That(converter.ToProviderCalls).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
    }

    private static DatabaseDefinition Build(params MetadataValuePropertyDraft[] columns) =>
        new MetadataDefinitionFactory()
            .Build(CreateDatabaseDraft(columns))
            .ValueOrException();

    private static MetadataDatabaseDraft CreateDatabaseDraft(
        params MetadataValuePropertyDraft[] columns) =>
        new(
            "GuidDefaultsDb",
            new CsTypeDeclaration("GuidDefaultsDb", "DataLinq.Tests", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "GuidDefaultModels",
                    new MetadataModelDraft(
                        new CsTypeDeclaration("GuidDefaultModel", "DataLinq.Tests", ModelCsType.Class))
                    {
                        ValueProperties = columns
                    },
                    new MetadataTableDraft("guid_defaults"))
            ]
        };

    private static MetadataValuePropertyDraft CreateIdColumn() =>
        new(
            "Id",
            new CsTypeDeclaration(typeof(int)),
            new MetadataColumnDraft("id")
            {
                PrimaryKey = true,
                AutoIncrement = true
            })
        {
            Attributes = [new ColumnAttribute("id")],
            CsNullable = true
        };

    private static MetadataValuePropertyDraft CreateGuidDefaultColumn(
        string propertyName,
        string columnName,
        string dbTypeName,
        GuidStorageFormat storageFormat) =>
        new(
            propertyName,
            new CsTypeDeclaration(typeof(Guid)),
            new MetadataColumnDraft(columnName)
            {
                DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, dbTypeName)]
            })
        {
            Attributes =
            [
                new ColumnAttribute(columnName),
                new DefaultAttribute(KnownGuid),
                new GuidStorageAttribute(DatabaseType.SQLite, storageFormat)
            ]
        };

    private readonly record struct TypedGuidId(Guid Value);

    private sealed class RecordingTypedGuidConverter : DataLinqScalarConverter<TypedGuidId, Guid>
    {
        public int ToProviderCalls { get; private set; }
        public int FromProviderCalls { get; private set; }

        public override Guid ToProvider(
            TypedGuidId modelValue,
            in ScalarConversionContext context)
        {
            ToProviderCalls++;
            return modelValue.Value;
        }

        public override TypedGuidId FromProvider(
            Guid providerValue,
            in ScalarConversionContext context)
        {
            FromProviderCalls++;
            return new TypedGuidId(providerValue);
        }
    }
}
