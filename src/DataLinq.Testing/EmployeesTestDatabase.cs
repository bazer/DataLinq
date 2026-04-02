using System;
using System.IO;
using System.Linq;
using System.Threading;
using MySqlConnector;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Testing;

public sealed class EmployeesTestDatabase : IDisposable
{
    private readonly PodmanTestEnvironmentSettings _settings;
    private bool _disposed;

    private EmployeesTestDatabase(
        TestProviderDescriptor provider,
        TestConnectionDefinition connection,
        Database<EmployeesDb> database,
        PodmanTestEnvironmentSettings settings)
    {
        Provider = provider;
        Connection = connection;
        Database = database;
        _settings = settings;
    }

    public TestProviderDescriptor Provider { get; }
    public TestConnectionDefinition Connection { get; }
    public Database<EmployeesDb> Database { get; }

    public static EmployeesTestDatabase Create(
        TestProviderDescriptor provider,
        string scenarioName,
        EmployeesSeedMode seedMode = EmployeesSeedMode.None,
        PodmanTestEnvironmentSettings? settings = null)
    {
        ProviderRegistration.EnsureRegistered();

        var resolvedSettings = settings ?? PodmanTestEnvironmentSettings.FromEnvironment();
        var logicalDatabaseName = $"{scenarioName}_{provider.Name}_{Guid.NewGuid():N}";
        var connection = resolvedSettings.CreateConnection(provider, logicalDatabaseName);

        if (provider.ServerTarget is not null)
            EnsureServerDatabaseReady(provider.ServerTarget, connection, resolvedSettings);

        var database = CreateDatabase(connection);

        EnsureSchema(database, connection);
        EnsureSeedData(database, seedMode);

        return new EmployeesTestDatabase(provider, connection, database, resolvedSettings);
    }

    private static Database<EmployeesDb> CreateDatabase(TestConnectionDefinition connection)
    {
        var creator = PluginHook.DatabaseProviders.Single(x => x.Key == connection.DatabaseType).Value;
        return creator.GetDatabaseProvider<EmployeesDb>(connection.ConnectionString, connection.DataSourceName);
    }

    private static void EnsureSchema(Database<EmployeesDb> database, TestConnectionDefinition connection)
    {
        if (database.FileOrServerExists() && database.DatabaseExists() && database.TableExists("employees"))
            return;

        var result = connection.DatabaseType.CreateDatabaseFromMetadata(
            database.Provider.Metadata,
            connection.DataSourceName,
            connection.ConnectionString,
            true);

        if (result.HasFailed)
            throw new InvalidOperationException($"Failed to create employees test database '{connection.DataSourceName}': {result.Failure}");
    }

    private static void EnsureSeedData(Database<EmployeesDb> database, EmployeesSeedMode seedMode)
    {
        if (seedMode == EmployeesSeedMode.None || database.Query().Employees.Any())
            return;

        EmployeesBogusSeeder.Seed(database);
    }

    private static void EnsureServerDatabaseReady(
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

            using var createUser = adminConnection.CreateCommand();
            createUser.CommandText = $"CREATE USER IF NOT EXISTS '{EscapeSqlLiteral(settings.ApplicationUser)}'@'%' IDENTIFIED BY '{EscapeSqlLiteral(settings.ApplicationPassword)}';";
            createUser.ExecuteNonQuery();

            using var grantPrivileges = adminConnection.CreateCommand();
            grantPrivileges.CommandText = $"GRANT ALL PRIVILEGES ON {QuoteIdentifier(connection.LogicalDatabaseName)}.* TO '{EscapeSqlLiteral(settings.ApplicationUser)}'@'%';";
            grantPrivileges.ExecuteNonQuery();
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(BuildServerSetupErrorMessage(target, settings, exception), exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Database.Dispose();

        if (Provider.ServerTarget is not null)
        {
            DropServerDatabase(Provider.ServerTarget, Connection, _settings);
        }
        else if (Provider.Kind == TestProviderKind.SQLiteFile)
        {
            DeleteSqliteFile(Connection.ConnectionString);
        }

        _disposed = true;
    }

    private static void DropServerDatabase(
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

    private static void DeleteSqliteFile(string connectionString)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            return;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                if (!File.Exists(builder.DataSource))
                    return;

                File.Delete(builder.DataSource);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static string QuoteIdentifier(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

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
