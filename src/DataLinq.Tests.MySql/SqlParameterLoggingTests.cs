using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public sealed class SqlParameterLoggingTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task DatabaseAndTransactionCommandsHonorValueDisclosurePolicy(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(DatabaseAndTransactionCommandsHonorValueDisclosurePolicy));
        using var source = new MySqlDataSourceBuilder(schema.Connection.ConnectionString).Build();
        const string secret = "synthetic-server-secret";
        foreach (var includeValues in new[] { false, true })
        {
            using var loggerFactory = new RecordingLoggerFactory();
            var configuration = new DataLinqLoggingConfiguration(loggerFactory)
            {
                SqlParameters = new SqlParameterLoggingOptions { IncludeSensitiveValues = includeValues }
            };
            var access = new SqlDbAccess(source, configuration);
            using var command = new MySqlCommand("SELECT @value");
            command.Parameters.AddWithValue("@value", secret);
            await Assert.That(access.ExecuteScalar(command)).IsEqualTo(secret);
            access.ExecuteNonQuery(command);
            using (var reader = access.ExecuteReader(command))
                await Assert.That(reader.ReadNextRow()).IsTrue();

            using var transaction = new SqlDatabaseTransaction(source, TransactionType.ReadAndWrite, schema.Connection.DataSourceName, configuration);
            await Assert.That(transaction.ExecuteScalar(command)).IsEqualTo(secret);
            transaction.ExecuteNonQuery(command);
            using (var reader = transaction.ExecuteReader(command))
                await Assert.That(reader.ReadNextRow()).IsTrue();

            var commands = loggerFactory.Messages.Where(message => message.Contains("Parameters:", StringComparison.Ordinal)).ToArray();
            await Assert.That(commands.Length).IsEqualTo(6);
            await Assert.That(commands.All(message => message.Contains(secret, StringComparison.Ordinal) == includeValues)).IsTrue();
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
