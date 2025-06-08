using System;
using System.Linq;
using DataLinq;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Tests;
using MySqlConnector;
using Xunit;

// A dedicated fixture to set up and tear down a database with a known schema for filtering tests.
public class MySqlFilteringTestFixture : IDisposable
{
    public DataLinqDatabaseConnection TestConnection { get; }
    public string TestDatabaseName { get; }

    public MySqlFilteringTestFixture()
    {
        var mysqlConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.Single(c => c.Type == DatabaseType.MySQL);

        TestDatabaseName = $"datalinq_filter_tests_{Guid.NewGuid().ToString("N")[..10]}";

        var serverConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = "" }.ConnectionString;

        using (var connection = new MySqlConnection(serverConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{TestDatabaseName}`;";
            command.ExecuteNonQuery();
        }

        var testConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = TestDatabaseName }.ConnectionString;

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

        TestConnection = new DataLinqDatabaseConnection(mysqlConfig.DatabaseConfig, new ConfigFileDatabaseConnection
        {
            Type = "MySQL",
            DataSourceName = TestDatabaseName,
            ConnectionString = testConnectionString
        });
    }

    public void Dispose()
    {
        var serverConnectionString = new MySqlConnectionStringBuilder(TestConnection.ConnectionString.Original) { Database = "" }.ConnectionString;
        using var connection = new MySqlConnection(serverConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{TestDatabaseName}`;";
        command.ExecuteNonQuery();
    }
}

[Collection("MySQL Filtering Tests")]
public class MySqlMetadataFilteringTests : IClassFixture<MySqlFilteringTestFixture>
{
    private readonly DataLinqDatabaseConnection _testConnection;
    private readonly string _dbName;

    public MySqlMetadataFilteringTests(MySqlFilteringTestFixture fixture)
    {
        _testConnection = fixture.TestConnection;
        _dbName = fixture.TestDatabaseName;
    }

    private DatabaseDefinition ParseSchema(MetadataFromDatabaseFactoryOptions options)
    {
        var factory = new MetadataFromMySqlFactory(options);
        return factory.ParseDatabase(
            _dbName, "TestDb", "TestNs", _dbName, _testConnection.ConnectionString.Original).Value;
    }

    [Fact]
    public void Filtering_NullIncludeList_ReturnsAllItems()
    {
        // Arrange: options.Include is null by default
        var options = new MetadataFromDatabaseFactoryOptions();

        // Act
        var dbDefinition = ParseSchema(options);

        // Assert
        Assert.Equal(4, dbDefinition.TableModels.Length); // 2 tables + 2 views
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table1");
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view1");
    }

    [Fact]
    public void Filtering_EmptyIncludeList_ReturnsAllItems()
    {
        // Arrange: An explicitly empty list should also mean "include all"
        var options = new MetadataFromDatabaseFactoryOptions { Include = [] };

        // Act
        var dbDefinition = ParseSchema(options);

        // Assert
        Assert.Equal(4, dbDefinition.TableModels.Length);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table2");
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view2");
    }

    [Fact]
    public void Filtering_IncludeListWithOneTable_ReturnsOnlyThatTable()
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["table1"] };

        // Act
        var dbDefinition = ParseSchema(options);

        // Assert
        Assert.Single(dbDefinition.TableModels);
        var tableModel = dbDefinition.TableModels[0];
        Assert.Equal("table1", tableModel.Table.DbName);
        Assert.Equal(TableType.Table, tableModel.Table.Type);
    }

    [Fact]
    public void Filtering_IncludeListWithOneView_ReturnsOnlyThatView()
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["view2"] };

        // Act
        var dbDefinition = ParseSchema(options);

        // Assert
        Assert.Single(dbDefinition.TableModels);
        var tableModel = dbDefinition.TableModels[0];
        Assert.Equal("view2", tableModel.Table.DbName);
        Assert.Equal(TableType.View, tableModel.Table.Type);
    }

    [Fact]
    public void Filtering_IncludeListWithSpecificTablesAndViews_ReturnsOnlyThose()
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["table2", "view1"] };

        // Act
        var dbDefinition = ParseSchema(options);

        // Assert
        Assert.Equal(2, dbDefinition.TableModels.Length);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table2" && tm.Table.Type == TableType.Table);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view1" && tm.Table.Type == TableType.View);
    }

    [Fact]
    public void Filtering_IncludeListWithNonExistentItem_ReturnsNothing()
    {
        // Arrange
        var options = new MetadataFromDatabaseFactoryOptions { Include = ["non_existent_table"] };

        // Act
        var dbDefinition = ParseSchema(options);

        // Assert
        Assert.Empty(dbDefinition.TableModels);
    }
}