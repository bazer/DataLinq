using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Testing;

public sealed class EmployeesTestDatabase : IDisposable
{
    private readonly PodmanTestEnvironmentSettings _settings;
    private readonly bool _ownsUnderlyingStore;
    private readonly Action? _releaseLease;
    private bool _disposed;

    private EmployeesTestDatabase(
        TestProviderDescriptor provider,
        TestConnectionDefinition connection,
        Database<EmployeesDb> database,
        PodmanTestEnvironmentSettings settings,
        bool ownsUnderlyingStore,
        Action? releaseLease = null)
    {
        Provider = provider;
        Connection = connection;
        Database = database;
        _settings = settings;
        _ownsUnderlyingStore = ownsUnderlyingStore;
        _releaseLease = releaseLease;
    }

    public TestProviderDescriptor Provider { get; }
    public TestConnectionDefinition Connection { get; }
    public Database<EmployeesDb> Database { get; }

    public static EmployeesTestDatabase Create(
        TestProviderDescriptor provider,
        string scenarioName,
        EmployeesFixtureProfile profile,
        PodmanTestEnvironmentSettings? settings = null)
        => CreateIsolated(provider, scenarioName, profile, settings);

    public static EmployeesTestDatabase CreateIsolated(
        TestProviderDescriptor provider,
        string scenarioName,
        EmployeesFixtureProfile profile,
        PodmanTestEnvironmentSettings? settings = null)
    {
        ProviderRegistration.EnsureRegistered();

        var resolvedSettings = settings ?? PodmanTestEnvironmentSettings.FromEnvironment();

        if (provider.ServerTarget is not null)
        {
            var lease = IsolatedEmployeesDatabasePool.Rent(
                provider,
                scenarioName,
                profile,
                resolvedSettings);
            try
            {
                var leasedDatabase = CreateDatabase(lease.Connection);
                return new EmployeesTestDatabase(
                    provider,
                    lease.Connection,
                    leasedDatabase,
                    resolvedSettings,
                    ownsUnderlyingStore: false,
                    releaseLease: lease.Release);
            }
            catch
            {
                lease.Release();
                throw;
            }
        }

        var logicalDatabaseName = $"{scenarioName}_{provider.Name}_{Guid.NewGuid():N}";
        var connection = resolvedSettings.CreateConnection(provider, logicalDatabaseName);

        var database = CreateDatabase(connection);

        EnsureSchema(database, connection);
        EnsureSeedData(database, profile);

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

        var databaseScope = CreateIsolated(provider, scenarioName, EmployeesFixtureProfile.SchemaOnly, settings);
        EmployeesBogusSeeder.Seed(databaseScope.Database, employeeCount);
        return databaseScope;
    }

    public static EmployeesTestDatabase OpenSharedSeeded(
        TestProviderDescriptor provider,
        string scenarioName,
        EmployeesFixtureProfile profile,
        PodmanTestEnvironmentSettings? settings = null)
    {
        ProviderRegistration.EnsureRegistered();

        var resolvedSettings = settings ?? PodmanTestEnvironmentSettings.FromEnvironment();
        var store = SharedEmployeesDatabaseCatalog.GetOrCreate(provider, profile, resolvedSettings);
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

    internal static void EnsureSeedData(Database<EmployeesDb> database, EmployeesFixtureProfile profile)
    {
        if (profile == EmployeesFixtureProfile.SchemaOnly || database.Query().Employees.Any())
            return;

        EmployeesBogusSeeder.Seed(
            database,
            profile == EmployeesFixtureProfile.TinySeeded
                ? EmployeesBogusSeeder.TinyEmployeeCount
                : EmployeesBogusSeeder.DefaultEmployeeCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Exception? failure = null;
        try
        {
            Database.Dispose();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            if (_releaseLease is not null)
            {
                _releaseLease();
            }
            else if (_ownsUnderlyingStore && Provider.ServerTarget is not null)
            {
                TestDatabaseLifecycle.DropServerDatabase(Provider.ServerTarget, Connection, _settings);
            }
            else if (_ownsUnderlyingStore && Provider.Kind == TestProviderKind.SQLiteFile)
            {
                TestDatabaseLifecycle.DeleteSqliteFile(Connection.ConnectionString);
            }
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        _disposed = true;
        if (failure is not null)
            throw failure;
    }
}
