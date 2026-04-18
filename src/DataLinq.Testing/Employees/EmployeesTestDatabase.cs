using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Testing;

public sealed class EmployeesTestDatabase : IDisposable
{
    private readonly PodmanTestEnvironmentSettings _settings;
    private readonly bool _ownsUnderlyingStore;
    private bool _disposed;

    private EmployeesTestDatabase(
        TestProviderDescriptor provider,
        TestConnectionDefinition connection,
        Database<EmployeesDb> database,
        PodmanTestEnvironmentSettings settings,
        bool ownsUnderlyingStore)
    {
        Provider = provider;
        Connection = connection;
        Database = database;
        _settings = settings;
        _ownsUnderlyingStore = ownsUnderlyingStore;
    }

    public TestProviderDescriptor Provider { get; }
    public TestConnectionDefinition Connection { get; }
    public Database<EmployeesDb> Database { get; }

    public static EmployeesTestDatabase Create(
        TestProviderDescriptor provider,
        string scenarioName,
        EmployeesSeedMode seedMode = EmployeesSeedMode.None,
        PodmanTestEnvironmentSettings? settings = null)
        => CreateIsolated(provider, scenarioName, seedMode, settings);

    public static EmployeesTestDatabase CreateIsolated(
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
            TestDatabaseLifecycle.EnsureServerDatabaseReady(provider.ServerTarget, connection, resolvedSettings);

        var database = CreateDatabase(connection);

        EnsureSchema(database, connection);
        EnsureSeedData(database, seedMode);

        return new EmployeesTestDatabase(provider, connection, database, resolvedSettings, ownsUnderlyingStore: true);
    }

    public static EmployeesTestDatabase CreateIsolatedBogus(
        TestProviderDescriptor provider,
        string scenarioName,
        int employeeCount,
        PodmanTestEnvironmentSettings? settings = null)
    {
        if (employeeCount < 1)
            throw new ArgumentOutOfRangeException(nameof(employeeCount), "The employee count must be at least 1.");

        var databaseScope = CreateIsolated(provider, scenarioName, EmployeesSeedMode.None, settings);
        EmployeesBogusSeeder.Seed(databaseScope.Database, employeeCount);
        return databaseScope;
    }

    public static EmployeesTestDatabase OpenSharedSeeded(
        TestProviderDescriptor provider,
        string scenarioName,
        EmployeesSeedMode seedMode = EmployeesSeedMode.Bogus,
        PodmanTestEnvironmentSettings? settings = null)
    {
        ProviderRegistration.EnsureRegistered();

        var resolvedSettings = settings ?? PodmanTestEnvironmentSettings.FromEnvironment();
        var store = SharedEmployeesDatabaseCatalog.GetOrCreate(provider, seedMode, resolvedSettings);
        var database = CreateDatabase(store.Connection);

        return new EmployeesTestDatabase(provider, store.Connection, database, resolvedSettings, ownsUnderlyingStore: false);
    }

    private static Database<EmployeesDb> CreateDatabase(TestConnectionDefinition connection)
        => TestDatabaseLifecycle.CreateDatabase<EmployeesDb>(connection);

    internal static void EnsureSchema(Database<EmployeesDb> database, TestConnectionDefinition connection)
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

    internal static void EnsureSeedData(Database<EmployeesDb> database, EmployeesSeedMode seedMode)
    {
        if (seedMode == EmployeesSeedMode.None || database.Query().Employees.Any())
            return;

        EmployeesBogusSeeder.Seed(database);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Database.Dispose();

        if (_ownsUnderlyingStore && Provider.ServerTarget is not null)
        {
            TestDatabaseLifecycle.DropServerDatabase(Provider.ServerTarget, Connection, _settings);
        }
        else if (_ownsUnderlyingStore && Provider.Kind == TestProviderKind.SQLiteFile)
        {
            TestDatabaseLifecycle.DeleteSqliteFile(Connection.ConnectionString);
        }

        _disposed = true;
    }
}
