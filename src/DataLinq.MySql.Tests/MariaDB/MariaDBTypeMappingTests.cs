using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.MariaDB;
using DataLinq.Metadata;
using DataLinq.Mutation;
using MySqlConnector;
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

        var builder = new MySqlConnectionStringBuilder(_fixture.TestConnection.ConnectionString.Original);
        builder.Remove("GuidFormat"); // Ensure the setting is not present
        var connectionStringWithoutFormat = builder.ConnectionString;

        // We need a proper Database<T> object to test the runtime
        var db = new MariaDBDatabase<UuidTestDatabase>(_fixture.TestConnection.ConnectionString.Original, _fixture.TestDatabaseName, _fixture.TestLoggerFactory);

        // Generate the SQL to create our test table
        var createSql = db.Provider.GetCreateSql().Text;
        db.Provider.DatabaseAccess.ExecuteNonQuery(createSql);

        // Create the table
        //db.Provider.DatabaseAccess.ExecuteNonQuery(createSql.Text);

        var testId = Guid.NewGuid();
        var newModel = new MutableUuidModel { Id = testId };

        // Act: Insert the data using DataLinq's runtime
        var inserted = db.Insert(newModel);

        db.Provider.State.ClearCache();
        // Read the data back from the database
        var retrieved = db.Query().UuidModels.SingleOrDefault(x => x.Id == testId);

        // Assert
        Assert.NotNull(inserted);
        Assert.NotNull(retrieved);
        Assert.Equal(testId, inserted.Id);
        Assert.Equal(testId, retrieved.Id);

        // Cleanup
        db.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE uuid_test;");
    }

    [Fact(Skip ="All variants of Guid handling is not implemented correctly for MySqlConnector")]
    public void EndToEnd_DataHandling_For_MySql_Binary16_Guid()
    {
        // Arrange: Create a connection string that DEFINITELY does not have the fix.
        var builder = new MySqlConnectionStringBuilder(_fixture.TestConnection.ConnectionString.Original);
        builder.Remove("GuidFormat"); // Ensure the setting is not present
        var connectionStringWithoutFormat = builder.ConnectionString;

        // We need a proper Database<T> object to test the runtime
        var db = new MariaDBDatabase<BinaryGuidTestDatabase>(connectionStringWithoutFormat, _fixture.TestDatabaseName, _fixture.TestLoggerFactory);

        // Generate the SQL to create our test table
        var createSql = db.Provider.GetCreateSql().Text;
        db.Provider.DatabaseAccess.ExecuteNonQuery(createSql);

        var originalId = Guid.NewGuid();
        var newModel = new MutableBinaryGuidModel { Id = originalId };

        // Act: Insert the data
        var inserted = db.Insert(newModel);

        // VERY IMPORTANT: Clear the cache to ensure we are reading from the database
        // and not just getting back the in-memory object.
        db.Provider.State.ClearCache();

        // Read the data back from the database
        var retrieved = db.Query().BinaryGuids.SingleOrDefault(x => x.Id == originalId);

        // Assert
        Assert.NotNull(inserted);
        Assert.NotNull(retrieved);

        // This is the assertion that will FAIL before our fix.
        Assert.Equal(originalId, inserted.Id);
        Assert.Equal(originalId, retrieved.Id);

        // Cleanup
        db.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE binary_guid_test;");
    }

    [Fact]
    public void Linq_Contains_With_Native_UUID_Column()
    {
        // This test ensures .Contains() works with native UUID columns WITHOUT any special GuidFormat.
        // Skip test if not applicable
        if (!_fixture.IsMariaDb107OrNewer) return;

        // Arrange: Use a connection string WITHOUT GuidFormat.
        var builder = new MySqlConnectionStringBuilder(_fixture.TestConnection.ConnectionString.Original);
        builder.Remove("GuidFormat");
        var connectionString = builder.ConnectionString;

        var db = new MariaDBDatabase<UuidTestDatabase>(connectionString, _fixture.TestDatabaseName, _fixture.TestLoggerFactory);
        var createSql = db.Provider.GetCreateSql().Text;
        db.Provider.DatabaseAccess.ExecuteNonQuery(createSql);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid(); // This one won't be in our search list

        db.Insert(new MutableUuidModel { Id = id1 });
        db.Insert(new MutableUuidModel { Id = id2 });
        db.Insert(new MutableUuidModel { Id = id3 });
        db.Provider.State.ClearCache();

        var idsToFind = new List<Guid> { id1, id2 };

        // Act
        var results = db.Query().UuidModels.Where(x => idsToFind.Contains(x.Id)).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, x => x.Id == id1);
        Assert.Contains(results, x => x.Id == id2);
        Assert.DoesNotContain(results, x => x.Id == id3);

        // Cleanup
        db.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE uuid_test;");
    }

    [Fact]
    public void Linq_Contains_With_Binary16_Column()
    {
        // This test proves that .Contains() ONLY works with BINARY(16) columns
        // WHEN the connection string is correctly configured.
        if (!_fixture.IsMariaDb107OrNewer) return;

        // Arrange: Use a connection string WITH the required GuidFormat.
        var builder = new MySqlConnectionStringBuilder(_fixture.TestConnection.ConnectionString.Original);
        builder.GuidFormat = MySqlConnector.MySqlGuidFormat.LittleEndianBinary16;
        var connectionString = builder.ConnectionString;

        var db = new MariaDBDatabase<BinaryGuidTestDatabase>(connectionString, _fixture.TestDatabaseName, _fixture.TestLoggerFactory);
        var createSql = db.Provider.GetCreateSql().Text;
        db.Provider.DatabaseAccess.ExecuteNonQuery(createSql);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        db.Insert(new MutableBinaryGuidModel { Id = id1 });
        db.Insert(new MutableBinaryGuidModel { Id = id2 });
        db.Insert(new MutableBinaryGuidModel { Id = id3 });
        db.Provider.State.ClearCache();

        var idsToFind = new List<Guid> { id1, id2 };

        // Act
        var results = db.Query().BinaryGuids.Where(x => idsToFind.Contains(x.Id)).ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, x => x.Id == id1);
        Assert.Contains(results, x => x.Id == id2);

        // For demonstration, prove it fails without the correct format
        builder.Remove("GuidFormat");
        var dbFails = new MariaDBDatabase<BinaryGuidTestDatabase>(builder.ConnectionString, _fixture.TestDatabaseName, _fixture.TestLoggerFactory);
        var emptyResults = dbFails.Query().BinaryGuids.Where(x => idsToFind.Contains(x.Id)).ToList();
        Assert.Empty(emptyResults);

        // Cleanup
        db.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE binary_guid_test;");
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

public partial class BinaryGuidTestDatabase(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<BinaryGuidModel> BinaryGuids { get; } = new(dataSource);
}

[Table("binary_guid_test")]
public abstract partial class BinaryGuidModel(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<BinaryGuidModel, BinaryGuidTestDatabase>(rowData, dataSource), ITableModel<BinaryGuidTestDatabase>
{
    [PrimaryKey, Column("id")]
    [Type(DatabaseType.MySQL, "binary", 16)] // Explicitly use BINARY(16)
    public abstract Guid Id { get; }
}