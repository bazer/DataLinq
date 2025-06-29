using System;
using System.Linq;
using DataLinq.Config;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Serilog;
using Xunit;

namespace DataLinq.MySql.Tests.MariaDB;

public class MariaDBTypeMappingFixture : IDisposable
{
    public string TestDatabaseName { get; }
    public DataLinqDatabaseConnection TestConnection { get; }
    public bool IsMariaDb107OrNewer { get; }
    public ILoggerFactory TestLoggerFactory { get; }

    public MariaDBTypeMappingFixture()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("D:\\git\\DataLinq\\logs\\MariaDBTypeMappingFixture.txt", rollingInterval: RollingInterval.Day, flushToDiskInterval: TimeSpan.FromSeconds(1))
            .CreateLogger();

        // Set up logging with Serilog
        TestLoggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog();
        });

        var mariaDbConfig = DatabaseFixture.DataLinqConfig.Databases
            .Single(x => x.Name == "employees")
            .Connections.SingleOrDefault(c => c.Type == DatabaseType.MariaDB);

        Assert.NotNull(mariaDbConfig); //"MariaDB connection configuration not found in datalinq.json

        TestDatabaseName = $"datalinq_mariadb_tests_{Guid.NewGuid().ToString("N")[..10]}";

        var serverConnectionString = new MySqlConnectionStringBuilder(mariaDbConfig.ConnectionString.Original) { Database = "" }.ConnectionString;

        // Check version before creating the database
        using (var connection = new MySqlConnection(serverConnectionString))
        {
            connection.Open();
            var versionString = new MySqlCommand("SELECT @@version", connection).ExecuteScalar() as string;
            if (versionString != null && versionString.Contains("MariaDB"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(versionString, @"(\d+\.\d+(\.\d+)?)");
                if (match.Success && Version.TryParse(match.Groups[1].Value, out var version))
                {
                    if (version >= new Version(10, 7))
                    {
                        IsMariaDb107OrNewer = true;
                    }
                }
            }
        }

        Assert.True(IsMariaDb107OrNewer, "MariaDB version 10.7 or newer is required for this test.");

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