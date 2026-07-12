using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Shared;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class ProviderMetadataFailureTests
{
    [Test]
    [Arguments(DatabaseType.MySQL, "MySQL")]
    [Arguments(DatabaseType.MariaDB, "MariaDB")]
    public async Task ParseColumn_UnsupportedSqlType_ReturnsInvalidModelFailure(DatabaseType databaseType, string providerName)
    {
        var table = CreateTable();
        var result = new ExposedMetadataFromSqlFactory(databaseType)
            .TryImportColumnForTest(table, new TestColumns
            {
                DATA_TYPE = "geometry",
                COLUMN_TYPE = "geometry",
                COLUMN_NAME = "shape"
            });

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.ToString()!).Contains($"Unsupported {providerName} column type 'geometry'");
        await Assert.That(failure.ToString()!).Contains("items.shape");
    }

    [Test]
    [Arguments(DatabaseType.MySQL, 8)]
    [Arguments(DatabaseType.MySQL, 32)]
    [Arguments(DatabaseType.MariaDB, 8)]
    [Arguments(DatabaseType.MariaDB, 32)]
    public async Task ParseCsType_NonUuidBinaryWidths_MapToByteArray(
        DatabaseType databaseType,
        int length)
    {
        var result = new ExposedMetadataFromSqlFactory(databaseType)
            .TryParseCsTypeForTest(new DatabaseColumnType(
                databaseType,
                "binary",
                (ulong)length));

        await Assert.That(result.ValueOrException()).IsEqualTo("byte[]");
    }

    [Test]
    [Arguments(DatabaseType.MySQL)]
    [Arguments(DatabaseType.MariaDB)]
    public async Task ParseCsType_Binary16_MapsToGuid(DatabaseType databaseType)
    {
        var result = new ExposedMetadataFromSqlFactory(databaseType)
            .TryParseCsTypeForTest(new DatabaseColumnType(databaseType, "binary", 16));

        await Assert.That(result.ValueOrException()).IsEqualTo("Guid");
    }

    [Test]
    public async Task ParseColumn_MariaDbNativeUuid_DiscardsInformationSchemaModifiers()
    {
        var result = new ExposedMetadataFromSqlFactory(
                DatabaseType.MariaDB,
                csTypeOverride: "Guid")
            .TryImportColumnTypeForTest(CreateTable(), new TestColumns
            {
                DATA_TYPE = "uuid",
                COLUMN_TYPE = "uuid unsigned",
                COLUMN_NAME = "public_id",
                CHARACTER_MAXIMUM_LENGTH = 36
            });
        var dbType = result.ValueOrException();

        await Assert.That(dbType.Name).IsEqualTo("uuid");
        await Assert.That(dbType.Length).IsNull();
        await Assert.That(dbType.Decimals).IsNull();
        await Assert.That(dbType.Signed).IsNull();
    }

    [Test]
    [Arguments(DatabaseType.MySQL)]
    [Arguments(DatabaseType.MariaDB)]
    public async Task ParseColumn_GeneratedSqlColumn_ReturnsSuccessWithoutImport(DatabaseType databaseType)
    {
        var table = CreateTable();
        var result = new ExposedMetadataFromSqlFactory(databaseType)
            .TryImportColumnForTest(table, new TestColumns
            {
                DATA_TYPE = "int",
                COLUMN_TYPE = "int",
                COLUMN_NAME = "computed_total",
                EXTRA = "VIRTUAL GENERATED"
            });

        await Assert.That(result.ValueOrException()).IsFalse();
    }

    [Test]
    [Arguments(DatabaseType.MySQL, "MySQL")]
    [Arguments(DatabaseType.MariaDB, "MariaDB")]
    public async Task ParseIndexType_UnsupportedSqlIndexType_ReturnsInvalidModelFailure(DatabaseType databaseType, string providerName)
    {
        var result = new ExposedMetadataFromSqlFactory(databaseType)
            .TryParseIndexTypeForTest("SPATIAL");

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.ToString()!).Contains($"Unsupported {providerName} index type 'SPATIAL'");
        await Assert.That(failure.ToString()!).Contains("items.ix_items_shape");
    }

    [Test]
    [Arguments(DatabaseType.MySQL, "MySQL")]
    [Arguments(DatabaseType.MariaDB, "MariaDB")]
    public async Task ParseForeignKeyReference_MalformedSqlRelationRow_ReturnsInvalidModelFailure(DatabaseType databaseType, string providerName)
    {
        var result = new ExposedMetadataFromSqlFactory(databaseType)
            .TryParseForeignKeyReferenceForTest(constraintName: null);

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.ToString()!).Contains($"Malformed {providerName} foreign-key metadata row");
        await Assert.That(failure.ToString()!).Contains("constraint name");
    }

    [Test]
    [Arguments(DatabaseType.MySQL)]
    [Arguments(DatabaseType.MariaDB)]
    public async Task ParseColumn_UnknownCsType_ReturnsInvalidModelFailure(DatabaseType databaseType)
    {
        var table = CreateTable();
        var result = new ExposedMetadataFromSqlFactory(databaseType, csTypeOverride: "MissingClrType")
            .TryImportColumnForTest(table, new TestColumns
            {
                DATA_TYPE = "int",
                COLUMN_TYPE = "int",
                COLUMN_NAME = "shape"
            });

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.ToString()!).Contains("Unsupported C# type 'MissingClrType'");
        await Assert.That(failure.ToString()!).Contains("items.shape");
    }

    private static TableDefinition CreateTable()
    {
        return new TableDefinition("items");
    }

    private sealed class ExposedMetadataFromSqlFactory(
        MetadataFromDatabaseFactoryOptions options,
        DatabaseType databaseType,
        string? csTypeOverride = null)
        : MetadataFromSqlFactory(options, databaseType)
    {
        public ExposedMetadataFromSqlFactory(DatabaseType databaseType, string? csTypeOverride = null)
            : this(new MetadataFromDatabaseFactoryOptions(), databaseType, csTypeOverride)
        {
        }

        public Option<bool, IDLOptionFailure> TryImportColumnForTest(TableDefinition table, ICOLUMNS columns)
        {
            if (!ParseColumn(new ProviderTableDraft(table.DbName, table.Type), columns).TryUnwrap(out var columnImport, out var failure))
                return failure;

            return columnImport.Property is not null;
        }

        public Option<DatabaseColumnType, IDLOptionFailure> TryImportColumnTypeForTest(
            TableDefinition table,
            ICOLUMNS columns)
        {
            if (!ParseColumn(new ProviderTableDraft(table.DbName, table.Type), columns)
                .TryUnwrap(out var columnImport, out var failure))
            {
                return failure;
            }

            if (columnImport.Property is null)
            {
                return DLOptionFailure.Fail(
                    DLFailureType.InvalidModel,
                    "Expected the test column to be imported.");
            }

            return columnImport.Property.Column.DbTypes[0];
        }

        public Option<string, IDLOptionFailure> TryParseCsTypeForTest(DatabaseColumnType dbType) =>
            ParseCsType(dbType, "items", "payload");

        public Option<bool, IDLOptionFailure> TryParseIndexTypeForTest(string indexType)
        {
            if (!ParseIndexType(indexType, "items", "ix_items_shape").TryUnwrap(out _, out var failure))
                return failure;

            return true;
        }

        public Option<bool, IDLOptionFailure> TryParseForeignKeyReferenceForTest(
            string? tableName = "items",
            string? columnName = "category_id",
            string? referencedTableName = "categories",
            string? referencedColumnName = "id",
            string? constraintName = "fk_items_categories")
        {
            if (!ParseForeignKeyReference(
                "testdb",
                tableName,
                columnName,
                referencedTableName,
                referencedColumnName,
                constraintName).TryUnwrap(out _, out var failure))
                return failure;

            return true;
        }

        protected override Option<string, IDLOptionFailure> ParseCsType(DatabaseColumnType dbType, string tableName, string columnName)
        {
            if (csTypeOverride != null)
                return csTypeOverride;

            return base.ParseCsType(dbType, tableName, columnName);
        }

        public override Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(
            string name,
            string csTypeName,
            string csNamespace,
            string dbName,
            string connectionString) =>
            DLOptionFailure.Fail(DLFailureType.NotImplemented, "Test factory does not parse databases.");
    }

    private sealed class TestColumns : ICOLUMNS
    {
        public string TABLE_SCHEMA { get; init; } = "test";
        public string TABLE_NAME { get; init; } = "items";
        public string DATA_TYPE { get; init; } = "int";
        public string COLUMN_TYPE { get; init; } = "int";
        public ulong? NUMERIC_PRECISION { get; init; }
        public ulong? NUMERIC_SCALE { get; init; }
        public ulong? CHARACTER_MAXIMUM_LENGTH { get; init; }
        public string IS_NULLABLE { get; init; } = "NO";
        public COLUMN_KEY COLUMN_KEY { get; init; } = COLUMN_KEY.Empty;
        public string EXTRA { get; init; } = "";
        public string? GENERATION_EXPRESSION { get; init; }
        public string COLUMN_DEFAULT { get; init; } = "";
        public string COLUMN_NAME { get; init; } = "id";
        public string COLUMN_COMMENT { get; init; } = "";
    }
}
