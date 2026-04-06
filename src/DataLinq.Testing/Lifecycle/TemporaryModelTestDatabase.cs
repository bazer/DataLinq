using System;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Testing;

public sealed class TemporaryModelTestDatabase<TDatabase> : IDisposable
    where TDatabase : class, IDatabaseModel
{
    private readonly PodmanTestEnvironmentSettings _settings;
    private bool _disposed;

    private TemporaryModelTestDatabase(
        TestProviderDescriptor provider,
        TestConnectionDefinition connection,
        Database<TDatabase> database,
        PodmanTestEnvironmentSettings settings)
    {
        Provider = provider;
        Connection = connection;
        Database = database;
        _settings = settings;
    }

    public TestProviderDescriptor Provider { get; }
    public TestConnectionDefinition Connection { get; }
    public Database<TDatabase> Database { get; }

    public static TemporaryModelTestDatabase<TDatabase> Create(
        TestProviderDescriptor provider,
        string scenarioName,
        PodmanTestEnvironmentSettings? settings = null)
    {
        ProviderRegistration.EnsureRegistered();

        var resolvedSettings = settings ?? PodmanTestEnvironmentSettings.FromEnvironment();
        var logicalDatabaseName = $"{scenarioName}_{provider.Name}_{Guid.NewGuid():N}";
        var connection = resolvedSettings.CreateConnection(provider, logicalDatabaseName);

        if (provider.ServerTarget is not null)
            TestDatabaseLifecycle.EnsureServerDatabaseReady(provider.ServerTarget, connection, resolvedSettings);

        var database = TestDatabaseLifecycle.CreateDatabase<TDatabase>(connection);
        var createResult = connection.DatabaseType.CreateDatabaseFromMetadata(
            database.Provider.Metadata,
            connection.DataSourceName,
            connection.ConnectionString,
            true);

        if (createResult.HasFailed)
        {
            database.Dispose();
            throw new InvalidOperationException($"Failed to create temporary test database '{connection.DataSourceName}': {createResult.Failure}");
        }

        return new TemporaryModelTestDatabase<TDatabase>(provider, connection, database, resolvedSettings);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Database.Dispose();

        if (Provider.ServerTarget is not null)
        {
            TestDatabaseLifecycle.DropServerDatabase(Provider.ServerTarget, Connection, _settings);
        }
        else if (Provider.Kind == TestProviderKind.SQLiteFile)
        {
            TestDatabaseLifecycle.DeleteSqliteFile(Connection.ConnectionString);
        }

        _disposed = true;
    }
}
