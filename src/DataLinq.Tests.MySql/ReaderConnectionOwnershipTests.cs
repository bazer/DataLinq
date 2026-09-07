using System;
using System.Data;
using System.Threading.Tasks;
using DataLinq.Logging;
using DataLinq.MySql;
using DataLinq.Testing;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public class ReaderConnectionOwnershipTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task LoggingFailureReturnsConnectionToPoolAndPreservesCallerCommand(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(LoggingFailureReturnsConnectionToPoolAndPreservesCallerCommand));
        var connectionString = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString)
        {
            MaximumPoolSize = 1,
            ConnectionTimeout = 2
        };
        using var source = new MySqlDataSourceBuilder(connectionString.ConnectionString).Build();
        using var logger = new ThrowingLoggerFactory();
        var access = new SqlDbAccess(source, new DataLinqLoggingConfiguration(logger));
        using var command = new MySqlCommand("SELECT 1");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            logger.ThrowOnLog = true;
            await Assert.That(() => access.ExecuteReader(command)).Throws<InvalidOperationException>();
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);

            logger.ThrowOnLog = false;
            // A one-connection pool would time out here if the failed call leaked.
            // Reusing the same command also checks that caller ownership is retained.
            using (var reader = access.ExecuteReader(command))
            {
                await Assert.That(reader.ReadNextRow()).IsTrue();
                await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
                await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Open);
            }
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task SqlExecutionFailureClosesTheOwnedConnection(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(SqlExecutionFailureClosesTheOwnedConnection));
        using var source = new MySqlDataSourceBuilder(schema.Connection.ConnectionString).Build();
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        using var command = new MySqlCommand("SELECT invalid syntax for ownership test");

        await Assert.That(() => access.ExecuteReader(command)).Throws<MySqlException>();
        await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
    }

    private sealed class ThrowingLoggerFactory : ILoggerFactory, ILogger
    {
        internal bool ThrowOnLog { get; set; } = true;
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnLog)
                throw new InvalidOperationException("Injected logging failure.");
        }
    }
}
