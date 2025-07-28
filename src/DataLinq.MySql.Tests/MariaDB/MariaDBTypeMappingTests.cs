using System;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.Mutation;
using Xunit;

namespace DataLinq.MySql.Tests.MariaDB;

[Collection("MariaDB Type Mapping")]
public class MariaDBTypeMappingTests : IClassFixture<MariaDBTypeMappingFixture>
{
    private readonly MariaDBTypeMappingFixture _fixture;

    public MariaDBTypeMappingTests(MariaDBTypeMappingFixture fixture)
    {
        _fixture = fixture;
    }

    // Helper to create a simple in-memory DatabaseDefinition for SQL generation tests
    private DatabaseDefinition CreateTestDatabaseDefinition()
    {
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "TestNs", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("UuidModel", "TestNs", ModelCsType.Class));
        model.SetInterfaces(new[] { new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface) });

        var table = new TableDefinition("uuid_test");
        var tableModel = new TableModel("UuidModels", db, model, table);

        var guidProp = new ValueProperty("Id", new CsTypeDeclaration(typeof(Guid)), model, new Attribute[] { new PrimaryKeyAttribute(), new ColumnAttribute("id") });
        model.AddProperty(guidProp);
        table.SetColumns(new[] { table.ParseColumn(guidProp) });

        db.SetTableModels(new[] { tableModel });
        return db;
    }

    [Fact]
    public void Test_1_MetadataParsing_From_MariaDB_UUID_Type()
    {
        // Skip this test if the MariaDB version is not supported
        if (!_fixture.IsMariaDb107OrNewer)
        {
            return;
        }

        // Arrange: Create a table with a native UUID column
        using var transaction = new SqlDatabaseTransaction(
            new MySqlConnector.MySqlDataSourceBuilder(_fixture.TestConnection.ConnectionString.Original).Build(),
            TransactionType.ReadAndWrite, _fixture.TestDatabaseName, DataLinq.Logging.DataLinqLoggingConfiguration.NullConfiguration);

        transaction.ExecuteNonQuery("CREATE TABLE uuid_test (id UUID PRIMARY KEY);");

        // Act: Parse the database schema
        var options = new MetadataFromDatabaseFactoryOptions { Include = new() { "uuid_test" } };
        var factory = new MetadataFromMariaDBFactory(options);
        var dbDefinition = factory.ParseDatabase("TestDb", "TestDb", "TestNs", _fixture.TestDatabaseName, _fixture.TestConnection.ConnectionString.Original).Value;

        transaction.ExecuteNonQuery("DROP TABLE uuid_test;");

        // Assert
        var column = dbDefinition.TableModels.Single().Table.Columns.Single();
        Assert.Equal("id", column.DbName);
        Assert.Equal("Guid", column.ValueProperty.CsType.Name); // Should map to C# Guid

        var dbType = column.GetDbTypeFor(DatabaseType.MariaDB); // Factory assigns MySQL as it's the base
        Assert.NotNull(dbType);
        Assert.Equal("uuid", dbType.Name, ignoreCase: true); // Should recognize the 'uuid' type
    }

    [Fact]
    public void Test_2_SqlGeneration_For_MariaDB_UUID_Type()
    {
        // Skip test if not applicable
        if (!_fixture.IsMariaDb107OrNewer) return;

        // Arrange
        var dbDefinition = CreateTestDatabaseDefinition();
        var sqlFactory = new SqlFromMariaDBFactory();

        // Act
        var sqlResult = sqlFactory.GetCreateTables(dbDefinition, true).Value;

        // Assert
        Assert.Contains("CREATE TABLE IF NOT EXISTS `uuid_test`", sqlResult.Text);
        Assert.Contains("`id` UUID NOT NULL", sqlResult.Text); // Assert it generates UUID
        Assert.DoesNotContain("`id` BINARY(16) NOT NULL", sqlResult.Text); // Assert it does NOT generate BINARY(16)
    }

    [Fact]
    public void Test_3_EndToEnd_DataHandling_For_MariaDB_UUID()
    {
        // Skip test if not applicable
        if (!_fixture.IsMariaDb107OrNewer) return;

        // Arrange: Define a simple model and create a table with a UUID column
        var sqlFactory = new SqlFromMariaDBFactory();
        var dbDefinition = CreateTestDatabaseDefinition();
        var createSql = sqlFactory.GetCreateTables(dbDefinition, true).Value;

        // We need a proper Database<T> object to test the runtime
        var db = new MariaDBDatabase<UuidTestDatabase>(_fixture.TestConnection.ConnectionString.Original, _fixture.TestDatabaseName, _fixture.TestLoggerFactory);

        // Create the table
        db.Provider.DatabaseAccess.ExecuteNonQuery(createSql.Text);

        var testId = Guid.NewGuid();
        var newModel = new MutableUuidModel { Id = testId };

        // Act: Insert the data using DataLinq's runtime
        var inserted = db.Insert(newModel);

        // Read the data back from the database
        var retrieved = db.Query().UuidModels.SingleOrDefault(x => x.Id == testId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(testId, inserted.Id);
        Assert.Equal(testId, retrieved.Id);

        // Cleanup
        db.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE uuid_test;");
    }
}

// Dummy database and model classes required for the end-to-end test
public partial class UuidTestDatabase(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<UuidModel> UuidModels { get; } = new(dataSource);
}

[Table("uuid_test")]
public abstract partial class UuidModel(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<UuidModel, UuidTestDatabase>(rowData, dataSource), ITableModel<UuidTestDatabase>
{
    [PrimaryKey, Column("id")]
    [Type(DatabaseType.MariaDB, "uuid")]
    [Type(DatabaseType.MySQL, "binary", 16)]
    public abstract Guid Id { get; }
}