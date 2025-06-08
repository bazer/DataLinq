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
    public void Filtering_NoFilter_ReturnsAll()
    {
        var options = new MetadataFromDatabaseFactoryOptions();
        var dbDefinition = ParseSchema(options);

        Assert.Equal(4, dbDefinition.TableModels.Length); // 2 tables + 2 views
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table1");
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view1");
    }

    [Fact]
    public void Filtering_TablesOnly_ReturnsOnlySpecifiedTables()
    {
        var options = new MetadataFromDatabaseFactoryOptions { Tables = ["table1"], Views = [] };
        var dbDefinition = ParseSchema(options);

        Assert.Single(dbDefinition.TableModels);
        var tableModel = dbDefinition.TableModels[0];
        Assert.Equal("table1", tableModel.Table.DbName);
        Assert.Equal(TableType.Table, tableModel.Table.Type);
    }

    [Fact]
    public void Filtering_ViewsOnly_ReturnsOnlySpecifiedViews()
    {
        var options = new MetadataFromDatabaseFactoryOptions { Views = ["view2"], Tables = [] };
        var dbDefinition = ParseSchema(options);

        Assert.Single(dbDefinition.TableModels);
        var tableModel = dbDefinition.TableModels[0];
        Assert.Equal("view2", tableModel.Table.DbName);
        Assert.Equal(TableType.View, tableModel.Table.Type);
    }

    [Fact]
    public void Filtering_EmptyTableList_ReturnsNoTablesButAllViews()
    {
        // Here, Views is null, which means "all views". Tables is empty, meaning "zero tables".
        var options = new MetadataFromDatabaseFactoryOptions { Tables = [], Views = null };
        var dbDefinition = ParseSchema(options);

        Assert.Equal(2, dbDefinition.TableModels.Length);
        Assert.DoesNotContain(dbDefinition.TableModels, tm => tm.Table.Type == TableType.Table);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view1");
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view2");
    }

    [Fact]
    public void Filtering_EmptyViewList_ReturnsNoViewsButAllTables()
    {
        // Here, Tables is null, meaning "all tables". Views is empty, meaning "zero views".
        var options = new MetadataFromDatabaseFactoryOptions { Tables = null, Views = [] };
        var dbDefinition = ParseSchema(options);

        Assert.Equal(2, dbDefinition.TableModels.Length);
        Assert.DoesNotContain(dbDefinition.TableModels, tm => tm.Table.Type == TableType.View);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table1");
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table2");
    }

    [Fact]
    public void Filtering_SpecificTablesAndViews()
    {
        var options = new MetadataFromDatabaseFactoryOptions { Tables = ["table2"], Views = ["view1"] };
        var dbDefinition = ParseSchema(options);

        Assert.Equal(2, dbDefinition.TableModels.Length);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "table2" && tm.Table.Type == TableType.Table);
        Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "view1" && tm.Table.Type == TableType.View);
    }
}