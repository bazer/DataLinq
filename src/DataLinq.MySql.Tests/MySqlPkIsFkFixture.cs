// In: src/DataLinq.MySql.Tests/MySqlPkIsFkFixture.cs (New File)
using System;
using System.Linq;
using DataLinq.Config;
using MySqlConnector;

namespace DataLinq.MySql.Tests;

public class MySqlPkIsFkFixture : IDisposable
{
    public DataLinqDatabaseConnection TestConnection { get; }
    public string TestDatabaseName { get; }

    public MySqlPkIsFkFixture()
    {
        var mysqlConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.Single(c => c.Type == DatabaseType.MySQL);

        TestDatabaseName = $"datalinq_pk_is_fk_tests_{Guid.NewGuid():N}";

        var serverConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = "" }.ConnectionString;

        using (var connection = new MySqlConnection(serverConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{TestDatabaseName}`;";
            command.ExecuteNonQuery();
        }

        var testConnectionString = new MySqlConnectionStringBuilder(mysqlConfig.ConnectionString.Original) { Database = TestDatabaseName }.ConnectionString;

        using (var connection = new MySqlConnection(testConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE user (
                    id INT PRIMARY KEY AUTO_INCREMENT,
                    username VARCHAR(255) NOT NULL
                );
                CREATE TABLE user_profile (
                    user_id INT PRIMARY KEY,
                    bio TEXT,
                    CONSTRAINT FK_Profile_User FOREIGN KEY (user_id) REFERENCES user(id)
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