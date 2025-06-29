using System;
using System.Linq;
using DataLinq.Config;
using DataLinq.Core.Factories;
using MySqlConnector;
using Xunit;

namespace DataLinq.MySql.Tests;

public class MySqlTypeMappingFixture : IDisposable
{
    public DataLinqDatabaseConnection TestConnection { get; }
    public string TestDatabaseName { get; }

    public MySqlTypeMappingFixture()
    {
        // Get the base connection details from the main DatabaseFixture config
        var mysqlConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.Single(c => c.Type == DatabaseType.MariaDB);

        // Generate a unique name for our temporary database
        TestDatabaseName = $"datalinq_type_tests_{Guid.NewGuid().ToString("N")[..10]}";

        // Create a connection string that DOES NOT specify a database initially
        var serverConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original)
        {
            Database = "" // Important: connect to the server, not a specific DB
        }.ConnectionString;

        // Create the temporary database
        using var connection = new MySqlConnection(serverConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{TestDatabaseName}`;";
        command.ExecuteNonQuery();

        // Now create a connection config that points to our new test database
        var testConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original)
        {
            Database = TestDatabaseName
        }.ConnectionString;

        TestConnection = new DataLinqDatabaseConnection(mysqlConfig.DatabaseConfig, new ConfigFileDatabaseConnection
        {
            Type = "MySQL",
            DataSourceName = TestDatabaseName,
            ConnectionString = testConnectionString
        });
    }

    public void Dispose()
    {
        // Cleanup: drop the temporary database
        var serverConnectionString = new MySqlConnectionStringBuilder(TestConnection.ConnectionString.Original)
        {
            Database = ""
        }.ConnectionString;

        using var connection = new MySqlConnection(serverConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{TestDatabaseName}`;";
        command.ExecuteNonQuery();
    }
}

// We use a collection fixture to ensure these tests don't run in parallel,
// as they create and drop tables in the same database.
[Collection("MySQL Type Mapping")]
public class MySqlTypeMappingTests : IClassFixture<MySqlTypeMappingFixture>
{
    private readonly DataLinqDatabaseConnection _mySqlConnection;
    private readonly string _dbName;

    public MySqlTypeMappingTests(MySqlTypeMappingFixture fixture)
    {
        // --- CHANGE 2: Get connection info from the new fixture ---
        _mySqlConnection = fixture.TestConnection;
        _dbName = fixture.TestDatabaseName;
    }

    /// <summary>
    /// A helper method that creates a temporary table, parses it, and asserts the type.
    /// </summary>
    private void TestTypeMapping(string mysqlColumnDefinition, string expectedCSharpType, bool expectNullable = false)
    {
        var tableName = "type_test_table";

        // Create a new transaction for each test to ensure isolation
        using var transaction = new SqlDatabaseTransaction(
             new MySqlDataSourceBuilder(_mySqlConnection.ConnectionString.Original).Build(),
             Mutation.TransactionType.ReadAndWrite,
             _dbName, // Use the unique DB name
             Logging.DataLinqLoggingConfiguration.NullConfiguration);

        try
        {
            // 1. Create a temporary table with the specific column type
            // Add a dummy 'id' column with PRIMARY KEY
            string nullabilityClause = expectNullable ? "NULL" : "NOT NULL";
            var createTableSql = $"CREATE TABLE `{tableName}` (id INT PRIMARY KEY AUTO_INCREMENT, test_column {mysqlColumnDefinition} {nullabilityClause});";
            transaction.ExecuteNonQuery(createTableSql);

            // 2. Parse the schema for just this table
            var options = new MetadataFromDatabaseFactoryOptions { Include = [tableName], CapitaliseNames = true };
            var factory = MetadataFromSqlFactory.GetSqlFactory(options, DatabaseType.MariaDB);
            var dbDefinition = factory.ParseDatabase(
                _dbName,
                "TestDb",
                "TestNamespace",
                _dbName,
                _mySqlConnection.ConnectionString.Original).Value;

            // 3. Perform Assertions
            Assert.NotNull(dbDefinition);
            Assert.Single(dbDefinition.TableModels);
            var tableModel = dbDefinition.TableModels[0];
            // Expecting 2 columns: 'id' and 'test_column'
            Assert.Equal(2, tableModel.Table.Columns.Length);

            // Find the specific column we are testing, not the first one
            var column = tableModel.Table.Columns.SingleOrDefault(c => c.DbName == "test_column");
            Assert.NotNull(column); // Ensure the test column was found

            Assert.Equal("TestColumn", column.ValueProperty.PropertyName);
            Assert.Equal(expectNullable, column.Nullable);

            // Special handling for ENUM types
            if (expectedCSharpType == "enum")
            {
                // The factory should generate a specific enum name, e.g., "TestColumnValue"
                var expectedEnumName = "TestColumnValue";
                Assert.Equal(expectedEnumName, column.ValueProperty.CsType.Name);

                // Also, verify that the enum values were parsed correctly.
                Assert.NotNull(column.ValueProperty.EnumProperty);
                var enumValues = column.ValueProperty.EnumProperty.Value.DbEnumValues;
                Assert.Equal(2, enumValues.Count);
                Assert.Equal("a", enumValues[0].name);
                Assert.Equal(1, enumValues[0].value); // DataLinq assigns 1-based index by default
                Assert.Equal("b", enumValues[1].name);
                Assert.Equal(2, enumValues[1].value);
            }
            else
            {
                // Assertions for all other types
                Assert.Equal(expectedCSharpType, column.ValueProperty.CsType.Name);
                Assert.Equal(expectNullable, column.ValueProperty.CsNullable);
            }
        }
        finally
        {
            // 4. Drop the temporary table to ensure cleanup
            transaction.ExecuteNonQuery($"DROP TABLE IF EXISTS `{tableName}`;");
        }
    }

    // --- Integer Types ---
    [Theory]
    [InlineData("TINYINT", "sbyte")]
    [InlineData("TINYINT UNSIGNED", "byte")]
    [InlineData("SMALLINT", "short")]
    [InlineData("SMALLINT UNSIGNED", "ushort")]
    [InlineData("MEDIUMINT", "int")]
    [InlineData("MEDIUMINT UNSIGNED", "uint")]
    [InlineData("INT", "int")]
    [InlineData("INT UNSIGNED", "uint")]
    [InlineData("BIGINT", "long")]
    [InlineData("BIGINT UNSIGNED", "ulong")]
    public void TestIntegerTypeMappings(string mysqlType, string csharpType)
    {
        TestTypeMapping(mysqlType, csharpType);
    }

    // --- Floating Point and Decimal Types ---
    [Theory]
    [InlineData("FLOAT", "float")]
    [InlineData("DOUBLE", "double")]
    [InlineData("DECIMAL(10, 2)", "decimal")]
    public void TestFloatingPointTypeMappings(string mysqlType, string csharpType)
    {
        TestTypeMapping(mysqlType, csharpType);
    }

    // --- Date and Time Types ---
    [Theory]
    [InlineData("DATE", "DateOnly")]
    [InlineData("DATETIME", "DateTime")]
    [InlineData("TIMESTAMP", "DateTime")]
    [InlineData("TIME", "TimeOnly")]
    [InlineData("YEAR", "int")]
    public void TestDateTimeTypeMappings(string mysqlType, string csharpType)
    {
        TestTypeMapping(mysqlType, csharpType);
    }

    // --- String and Text Types ---
    [Theory]
    [InlineData("CHAR(10)", "string")]
    [InlineData("VARCHAR(255)", "string")]
    [InlineData("TINYTEXT", "string")]
    [InlineData("TEXT", "string")]
    [InlineData("MEDIUMTEXT", "string")]
    [InlineData("LONGTEXT", "string")]
    public void TestStringTypeMappings(string mysqlType, string csharpType)
    {
        TestTypeMapping(mysqlType, csharpType);
    }

    // --- Binary and Special Types ---
    [Theory]
    [InlineData("BINARY(16)", "Guid")] // Standard mapping for UUIDs
    [InlineData("VARBINARY(100)", "byte[]")]
    [InlineData("TINYBLOB", "byte[]")]
    [InlineData("BLOB", "byte[]")]
    [InlineData("MEDIUMBLOB", "byte[]")]
    [InlineData("LONGBLOB", "byte[]")]
    [InlineData("BIT(1)", "bool")]
    [InlineData("ENUM('a', 'b')", "enum")]
    public void TestSpecialTypeMappings(string mysqlType, string csharpType)
    {
        TestTypeMapping(mysqlType, csharpType);
    }

    // --- Nullability Tests ---
    [Theory]
    [InlineData("INT NULL", "int", true)]
    [InlineData("VARCHAR(50) NULL", "string", true)]
    [InlineData("DATETIME NULL", "DateTime", true)]
    public void TestNullableTypeMappings(string mysqlType, string csharpType, bool isNullable)
    {
        TestTypeMapping(mysqlType, csharpType, isNullable);
    }
}