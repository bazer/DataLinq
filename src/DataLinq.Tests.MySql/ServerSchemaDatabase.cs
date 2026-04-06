using System;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.Testing;
using MySqlConnector;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

internal sealed class ServerSchemaDatabase : IDisposable
{
    private readonly PodmanTestEnvironmentSettings _settings;
    private bool _disposed;

    private ServerSchemaDatabase(
        TestProviderDescriptor provider,
        TestConnectionDefinition connection,
        PodmanTestEnvironmentSettings settings)
    {
        Provider = provider;
        Connection = connection;
        _settings = settings;
    }

    public TestProviderDescriptor Provider { get; }
    public TestConnectionDefinition Connection { get; }

    public static ServerSchemaDatabase Create(
        TestProviderDescriptor provider,
        string scenarioName,
        params string[] schemaStatements)
    {
        if (provider.ServerTarget is null)
            throw new InvalidOperationException($"Provider '{provider.Name}' is not a server-backed provider.");

        ProviderRegistration.EnsureRegistered();

        var settings = PodmanTestEnvironmentSettings.FromEnvironment();
        var logicalDatabaseName = $"{scenarioName}_{provider.Name}_{Guid.NewGuid():N}";
        var connection = settings.CreateConnection(provider, logicalDatabaseName);

        using (var adminConnection = new MySqlConnection(settings.CreateAdminConnectionString(provider.ServerTarget)))
        {
            adminConnection.Open();
            using var createDatabase = adminConnection.CreateCommand();
            createDatabase.CommandText = $"CREATE DATABASE IF NOT EXISTS {QuoteIdentifier(connection.LogicalDatabaseName)} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            createDatabase.ExecuteNonQuery();
        }

        using (var schemaConnection = new MySqlConnection(connection.ConnectionString))
        {
            schemaConnection.Open();
            foreach (var statement in schemaStatements)
            {
                using var command = schemaConnection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }

        return new ServerSchemaDatabase(provider, connection, settings);
    }

    public DatabaseDefinition ParseDatabase(
        string databaseName,
        string csTypeName,
        string csNamespace,
        MetadataFromDatabaseFactoryOptions? options = null)
    {
        var factory = MetadataFromSqlFactory.GetSqlFactory(
            options ?? new MetadataFromDatabaseFactoryOptions(),
            Provider.DatabaseType);

        return factory.ParseDatabase(
                databaseName,
                csTypeName,
                csNamespace,
                Connection.DataSourceName,
                Connection.ConnectionString)
            .ValueOrException();
    }

    public void ExecuteNonQuery(string commandText)
    {
        using var connection = new MySqlConnection(Connection.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (Provider.ServerTarget is not null)
        {
            MySqlConnection.ClearAllPools();
            using var adminConnection = new MySqlConnection(_settings.CreateAdminConnectionString(Provider.ServerTarget));
            adminConnection.Open();
            using var dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(Connection.LogicalDatabaseName)};";
            dropDatabase.ExecuteNonQuery();
        }

        _disposed = true;
    }

    private static string QuoteIdentifier(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";
}
