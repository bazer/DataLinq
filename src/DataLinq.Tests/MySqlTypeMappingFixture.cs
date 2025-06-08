using System;
using System.Linq;
using DataLinq;
using DataLinq.Config;
using DataLinq.MySql;
using DataLinq.Tests;
using MySqlConnector;
using Xunit;

public class MySqlTypeMappingFixture : IDisposable
{
    public DataLinqDatabaseConnection TestConnection { get; }
    public string TestDatabaseName { get; }

    public MySqlTypeMappingFixture()
    {
        // Get the base connection details from the main DatabaseFixture config
        var mysqlConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.Single(c => c.Type == DatabaseType.MySQL);

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