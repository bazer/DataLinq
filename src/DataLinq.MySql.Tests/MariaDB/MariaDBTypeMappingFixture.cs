using System;
using System.Linq;
using DataLinq;
using DataLinq.Config;
using DataLinq.MySql;
using DataLinq.Tests;
using MySqlConnector;
using Xunit;

public class MariaDBTypeMappingFixture : IDisposable
{
    public DataLinqDatabaseConnection TestConnection { get; }
    public string TestDatabaseName { get; }
    public bool IsMariaDb107OrNewer { get; }

    public MariaDBTypeMappingFixture()
    {
        var mariaDbConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.SingleOrDefault(c => c.Type == DatabaseType.MariaDB);

        // If no specific MariaDB connection is defined, we can't run these tests.
        if (mariaDbConfig == null)
        {
            // Set a flag to skip tests if MariaDB is not configured.
            IsMariaDb107OrNewer = false;
            return;
        }

        TestDatabaseName = $"datalinq_mariadb_tests_{Guid.NewGuid().ToString("N")[..10]}";

        var serverConnectionString = new MySqlConnectionStringBuilder(mariaDbConfig.ConnectionString.Original) { Database = "" }.ConnectionString;

        // Check version before creating the database
        using (var connection = new MySqlConnection(serverConnectionString))
        {
            connection.Open();
            var versionString = new MySqlCommand("SELECT @@version", connection).ExecuteScalar() as string;
            if (versionString != null && versionString.Contains("MariaDB"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(versionString, @"(\d+\.\d+)");
                if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double version))
                {
                    if (version >= 10.7)
                    {
                        IsMariaDb107OrNewer = true;
                    }
                }
            }
        }

        if (!IsMariaDb107OrNewer) return; // Stop if the version is not supported

        // Create the temporary database
        using (var connection = new MySqlConnection(serverConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{TestDatabaseName}`;";
            command.ExecuteNonQuery();
        }

        var testConnectionString = new MySqlConnectionStringBuilder(mariaDbConfig.ConnectionString.Original) { Database = TestDatabaseName }.ConnectionString;

        TestConnection = new DataLinqDatabaseConnection(mariaDbConfig.DatabaseConfig, new ConfigFileDatabaseConnection
        {
            Type = "MariaDB",
            DataSourceName = TestDatabaseName,
            ConnectionString = testConnectionString
        });
    }

    public void Dispose()
    {
        if (!IsMariaDb107OrNewer) return;

        var serverConnectionString = new MySqlConnectionStringBuilder(TestConnection.ConnectionString.Original) { Database = "" }.ConnectionString;
        using var connection = new MySqlConnection(serverConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS `{TestDatabaseName}`;";
        command.ExecuteNonQuery();
    }
}