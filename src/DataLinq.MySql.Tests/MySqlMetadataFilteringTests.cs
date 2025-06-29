using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using MySqlConnector;
using ThrowAway;
using Xunit;

namespace DataLinq.MySql.Tests;

// A dedicated fixture to set up and tear down a database with a known schema for filtering tests.
public class MySqlFilteringTestFixture() : IDisposable
{
    public static (string dbName, DataLinqDatabaseConnection connection) GetTestDatabase(DatabaseType databaseType)
    {
        var mysqlConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.Single(c => c.Type == databaseType);

        var databaseName = $"datalinq_filter_tests_{Guid.NewGuid().ToString("N")[..10]}";

        var serverConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = "" }.ConnectionString;

        using (var connection = new MySqlConnection(serverConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{databaseName}`;";
            command.ExecuteNonQuery();
        }

        var testConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = databaseName }.ConnectionString;

        // Create the schema
        using (var connection = new MySqlConnection(testConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE table1 (id INT PRIMARY KEY);
                CREATE TABLE table2 (id INT PRIMARY KEY);
                CREATE VIEW view1 AS SELECT * FROM table1;
                CREATE VIEW view2 AS SELECT * FROM table2;";
            command.ExecuteNonQuery();
        }

        var testConnection = new DataLinqDatabaseConnection(mysqlConfig.DatabaseConfig, new ConfigFileDatabaseConnection
        {
            Type = databaseType == DatabaseType.MySQL ? "MySQL" : "MariaDB",
            DataSourceName = databaseName,
            ConnectionString = testConnectionString
        });

        return (databaseName, testConnection);
    }

    public static void DropTestDatabase(DataLinqDatabaseConnection testConnection, string dbName)
    {
        using (var connection = new MySqlConnection(testConnection.ConnectionString.Original))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS `{dbName}`;";
            command.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        foreach (var (dbName, testConnection) in DatabaseFixture.SupportedDatabaseTypes.Select(GetTestDatabase))
            DropTestDatabase(testConnection, dbName);
    }
}

[Collection("MySQL Filtering Tests")]
public class MySqlMetadataFilteringTests : IClassFixture<MySqlFilteringTestFixture>
{
    //private readonly DataLinqDatabaseConnection _testConnection;
    //private readonly string _dbName;

    public static IEnumerable<object[]> DatabaseTypes()
    {
        foreach (var type in DatabaseFixture.SupportedDatabaseTypes)
            yield return new object[] { type };
    }

    public MySqlMetadataFilteringTests(MySqlFilteringTestFixture fixture)
    {
        //_testConnection = fixture.TestConnection;
        //_dbName = fixture.TestDatabaseName;
    }

    private Option<DatabaseDefinition, IDLOptionFailure> ParseSchema(MetadataFromDatabaseFactoryOptions options, DatabaseType databaseType)
    {
        var (dbName, testConnection) = MySqlFilteringTestFixture.GetTestDatabase(databaseType);
        var factory = MetadataFromSqlFactory.GetSqlFactory(options, databaseType);
        return factory.ParseDatabase(
            dbName, "TestDb", "TestNs", dbName, testConnection.ConnectionString.Original);
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public void Filtering_NullIncludeList_ReturnsAllItems(DatabaseType databaseType)
    {
        // Arrange: options.Include is null by default
        var options = new MetadataFromDatabaseFactoryOptions();

        // Act
        var dbDefinition = ParseSchema(options, databaseType).Value;

        // Assert
        Assert.Equal(4, dbDefinition.TableModels.Length); // 2 tables + 2 views
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table1");
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view1");
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public void Filtering_EmptyIncludeList_ReturnsAllItems(DatabaseType databaseType)
    {
        // Arrange: An explicitly empty list should also mean "include all"
        var options = new MetadataFromDatabaseFactoryOptions { Include = [] };

        // Act
        var dbDefinition = ParseSchema(options, databaseType).Value;

        // Assert
        Assert.Equal(4, dbDefinition.TableModels.Length);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table2");
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view2");
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public void Filtering_IncludeListWithOneTable_ReturnsOnlyThatTable(DatabaseType databaseType)
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["table1"] };

        // Act
        var dbDefinition = ParseSchema(options, databaseType).Value;

        // Assert
        Assert.Single(dbDefinition.TableModels);
        var tableModel = dbDefinition.TableModels[0];
        Assert.Equal("table1", tableModel.Table.DbName);
        Assert.Equal(TableType.Table, tableModel.Table.Type);
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public void Filtering_IncludeListWithOneView_ReturnsOnlyThatView(DatabaseType databaseType)
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["view2"] };

        // Act
        var dbDefinition = ParseSchema(options, databaseType).Value;

        // Assert
        Assert.Single(dbDefinition.TableModels);
        var tableModel = dbDefinition.TableModels[0];
        Assert.Equal("view2", tableModel.Table.DbName);
        Assert.Equal(TableType.View, tableModel.Table.Type);
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public void Filtering_IncludeListWithSpecificTablesAndViews_ReturnsOnlyThose(DatabaseType databaseType)
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["table2", "view1"] };

        // Act
        var dbDefinition = ParseSchema(options, databaseType).Value;

        // Assert
        Assert.Equal(2, dbDefinition.TableModels.Length);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table2" && tm.Table.Type == TableType.Table);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view1" && tm.Table.Type == TableType.View);
    }

    [Theory]
    [MemberData(nameof(DatabaseTypes))]
    public void Filtering_IncludeListWithNonExistentItem_ReturnsFailure(DatabaseType databaseType)
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["non_existent_table"] };

        // Act
        var dbDefinition = ParseSchema(options, databaseType);

        // Assert
        Assert.True(dbDefinition.HasFailed);
        Assert.Equal(DLFailureType.InvalidModel, dbDefinition.Failure.Value.FailureType);
        Assert.Contains("Could not find the specified tables or views: non_existent_table", dbDefinition.Failure.Value.Message);
    }
}