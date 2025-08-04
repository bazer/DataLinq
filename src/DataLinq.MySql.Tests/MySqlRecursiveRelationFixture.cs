// In: src/DataLinq.MySql.Tests/MySqlRecursiveRelationFixture.cs (New File)
using System;
using System.Linq;
using DataLinq.Config;
using MySqlConnector;
using Xunit;

namespace DataLinq.MySql.Tests;

public class MySqlRecursiveRelationFixture : IDisposable
{
    public DataLinqDatabaseConnection TestConnection { get; }
    public string TestDatabaseName { get; }

    public MySqlRecursiveRelationFixture()
    {
        var mysqlConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.Single(c => c.Type == DatabaseType.MySQL);

        TestDatabaseName = $"datalinq_recursive_tests_{Guid.NewGuid():N}";

        var serverConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = "" }.ConnectionString;

        using (var connection = new MySqlConnection(serverConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{TestDatabaseName}`;";
            command.ExecuteNonQuery();
        }

        var testConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = TestDatabaseName }.ConnectionString;

        // Create the schema with a recursive FK
        using (var connection = new MySqlConnection(testConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE employee (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    name VARCHAR(255),
                    manager_id INT NULL,
                    CONSTRAINT FK_Employee_Manager FOREIGN KEY (manager_id) REFERENCES employee(id)
                );";
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