using System;
using System.IO;
using System.Linq;
using System.Threading;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Testing;

internal static class TestDatabaseLifecycle
{
    public static Database<TDatabase> CreateDatabase<TDatabase>(TestConnectionDefinition connection)
        where TDatabase : class, IDatabaseModel
    {
        var creator = PluginHook.DatabaseProviders.Single(x => x.Key == connection.DatabaseType).Value;
        return creator.GetDatabaseProvider<TDatabase>(connection.ConnectionString, connection.DataSourceName);
    }

    public static void EnsureServerDatabaseReady(
        DatabaseServerTarget target,
        TestConnectionDefinition connection,
        PodmanTestEnvironmentSettings settings)
    {
        try
        {
            using var adminConnection = new MySqlConnection(settings.CreateAdminConnectionString(target));
            adminConnection.Open();

            using var createDatabase = adminConnection.CreateCommand();
            createDatabase.CommandText = $"CREATE DATABASE IF NOT EXISTS {QuoteIdentifier(connection.LogicalDatabaseName)} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            createDatabase.ExecuteNonQuery();
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(BuildServerSetupErrorMessage(target, settings, exception), exception);
        }
    }

    public static void DropServerDatabase(
        DatabaseServerTarget target,
        TestConnectionDefinition connection,
        PodmanTestEnvironmentSettings settings)
    {
        MySqlConnection.ClearAllPools();
        using var adminConnection = new MySqlConnection(settings.CreateAdminConnectionString(target));
        adminConnection.Open();

        using var dropDatabase = adminConnection.CreateCommand();
        dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(connection.LogicalDatabaseName)};";
        dropDatabase.ExecuteNonQuery();
    }

    public static void DeleteSqliteFile(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            return;

        const int attempts = 60;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                SqliteConnection.ClearAllPools();

                if (attempt % 5 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                if (!File.Exists(builder.DataSource))
                    return;

                File.Delete(builder.DataSource);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(250);
            }
        }
    }

    private static string QuoteIdentifier(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";
    private static string BuildServerSetupErrorMessage(
        DatabaseServerTarget target,
        PodmanTestEnvironmentSettings settings,
        MySqlException exception)
    {
        if (exception.Message.Contains("Access denied for user 'root'", StringComparison.OrdinalIgnoreCase))
        {
            return
                $"Could not connect to '{target.Id}' with the configured admin account '{settings.AdminUser}'. " +
                $"Recreate the Podman profile with '.\\test-infra\\podman\\reset.ps1 -Profile {settings.ActiveProfile.Id}' so the container startup can provision the host admin privileges correctly. " +
                $"Current admin endpoint: {settings.Host}:{settings.GetPort(target)}.";
        }

        if (exception.Message.Contains($"Access denied for user '{settings.AdminUser}'", StringComparison.OrdinalIgnoreCase))
        {
            return
                $"Could not connect to '{target.Id}' with the configured admin account '{settings.AdminUser}'. " +
                $"Recreate the Podman profile with '.\\test-infra\\podman\\reset.ps1 -Profile {settings.ActiveProfile.Id}' so the container startup can reapply the elevated grants for that user. " +
                $"Current admin endpoint: {settings.Host}:{settings.GetPort(target)}.";
        }

        if (exception.Message.Contains("Incorrect database name", StringComparison.OrdinalIgnoreCase))
        {
            return
                $"The generated test database name was rejected by '{target.Id}'. " +
                $"This usually means the provider-specific test identifier exceeded the server's identifier rules.";
        }

        return
            $"Could not reach the '{target.Id}' test server at {settings.Host}:{settings.GetPort(target)}. " +
            $"Start the active Podman profile with '.\\test-infra\\podman\\up.ps1 -Profile {settings.ActiveProfile.Id}' and wait for it to become ready before running server-backed TUnit tests.";
    }
}
