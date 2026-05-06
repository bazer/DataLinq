using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Shared;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public class ProviderColumnMetadataFailureTests
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

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.ToString()!).Contains($"Unsupported {providerName} column type 'geometry'");
        await Assert.That(failure.ToString()!).Contains("items.shape");
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

    private static TableDefinition CreateTable()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var table = new TableDefinition("items");
        _ = new TableModel("Items", database, table, "Item");

        return table;
    }

    private sealed class ExposedMetadataFromSqlFactory(MetadataFromDatabaseFactoryOptions options, DatabaseType databaseType)
        : MetadataFromSqlFactory(options, databaseType)
    {
        public ExposedMetadataFromSqlFactory(DatabaseType databaseType)
            : this(new MetadataFromDatabaseFactoryOptions(), databaseType)
        {
        }

        public Option<bool, IDLOptionFailure> TryImportColumnForTest(TableDefinition table, ICOLUMNS columns)
        {
            if (!ParseColumn(table, columns).TryUnwrap(out var columnImport, out var failure))
                return failure;

            return columnImport.Column is not null;
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
