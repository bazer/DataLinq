using System;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Testing;

internal static class SharedEmployeesDatabaseCatalog
{
    private static readonly ConcurrentDictionary<string, Lazy<SharedEmployeesDatabaseStore>> Stores = new(StringComparer.Ordinal);

    public static SharedEmployeesDatabaseStore GetOrCreate(
        TestProviderDescriptor provider,
        EmployeesSeedMode seedMode,
        PodmanTestEnvironmentSettings settings)
    {
        var key = $"{provider.Name}:{seedMode}";
        return Stores.GetOrAdd(
            key,
            _ => new Lazy<SharedEmployeesDatabaseStore>(
                () => CreateStore(provider, seedMode, settings),
                isThreadSafe: true)).Value;
    }

    private static SharedEmployeesDatabaseStore CreateStore(
        TestProviderDescriptor provider,
        EmployeesSeedMode seedMode,
        PodmanTestEnvironmentSettings settings)
    {
        ProviderRegistration.EnsureRegistered();

        var logicalDatabaseName = $"shared_employees_{seedMode}_{provider.Name}_v1";
        var connection = settings.CreateConnection(provider, logicalDatabaseName);
        SqliteConnection? keepAliveConnection = null;

        if (provider.Kind == TestProviderKind.SQLiteInMemory)
        {
            keepAliveConnection = new SqliteConnection(connection.ConnectionString);
            keepAliveConnection.Open();
        }

        if (provider.ServerTarget is not null)
            TestDatabaseLifecycle.EnsureServerDatabaseReady(provider.ServerTarget, connection, settings);

        using var database = TestDatabaseLifecycle.CreateDatabase<EmployeesDb>(connection);
        EmployeesTestDatabase.EnsureSchema(database, connection);
        EmployeesTestDatabase.EnsureSeedData(database, seedMode);

        return new SharedEmployeesDatabaseStore(connection, keepAliveConnection);
    }
}

internal sealed class SharedEmployeesDatabaseStore : IDisposable
{
    private readonly SqliteConnection? _keepAliveConnection;
    private bool _disposed;

    public SharedEmployeesDatabaseStore(TestConnectionDefinition connection, SqliteConnection? keepAliveConnection)
    {
        Connection = connection;
        _keepAliveConnection = keepAliveConnection;
    }

    public TestConnectionDefinition Connection { get; }

    public void Dispose()
    {
        if (_disposed)
            return;

        _keepAliveConnection?.Dispose();
        _disposed = true;
    }
}
