using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.Query;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed class NativeAsyncAdministrationTests
{
    [Test]
    public async Task ValidationAndDisposedRootPrecedePreCancellationWithoutIO()
    {
        using var provider = new MySqlProvider<EmployeesDb>("Server=127.0.0.1;Port=1;User ID=unused;Pooling=false", "unused");
        var access = provider.DatabaseAccess;
        var token = new CancellationToken(true);
        await Assert.That(async () => { await provider.TableExistsAsyncCore("", token: token); }).Throws<ArgumentNullException>();
        await Assert.That(async () => { await provider.DatabaseExistsAsyncCore(token: token); }).Throws<OperationCanceledException>();
        var factory = new SqlFromMySqlFactory();
        await Assert.That(async () => { await factory.CreateDatabaseAsyncCore(new Sql(""), "unused", "Not a connection string", true, token); }).Throws<ArgumentException>();
        await Assert.That(async () => { await factory.CreateDatabaseAsyncCore(new Sql(""), "unused", provider.ConnectionString, true, token); }).Throws<OperationCanceledException>();
        await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
        provider.Dispose();
        await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
        await Assert.That(async () => { await provider.DatabaseExistsAsyncCore(token: token); }).Throws<ObjectDisposedException>();
        await Assert.That(async () => { await access.ExecuteScalarAsyncCore("SELECT 1", token); }).Throws<ObjectDisposedException>();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ProbesPreserveNamesAndObserveFreshState(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ProbesPreserveNamesAndObserveFreshState), "CREATE TABLE `odd'name` (id INT PRIMARY KEY)");
        using var provider = CreateProvider(schema);
        await Assert.That(await provider.FileOrServerExistsAsyncCore()).IsTrue();
        await Assert.That(await provider.DatabaseExistsAsyncCore()).IsEqualTo(provider.DatabaseExists());
        await Assert.That(await provider.TableExistsAsyncCore("odd'name")).IsEqualTo(provider.TableExists("odd'name"));
        await Assert.That(await provider.TableExistsAsyncCore("' OR 1=1 --")).IsFalse();
        await Assert.That(await provider.DatabaseExistsAsyncCore("no_such_database_' OR 1=1")).IsFalse();
        await Assert.That(await provider.DatabaseExistsAsyncCore("INFORMATION_SCHEMA")).IsTrue();
        await Assert.That(await provider.TableExistsAsyncCore("tables", "INFORMATION_SCHEMA")).IsTrue();
        await Assert.That(await provider.TableExistsAsyncCore("created_later")).IsFalse();
        schema.ExecuteNonQuery("CREATE TABLE created_later (id INT PRIMARY KEY)");
        await Assert.That(await provider.TableExistsAsyncCore("created_later")).IsTrue();
        schema.ExecuteNonQuery("DROP TABLE created_later");
        await Assert.That(await provider.TableExistsAsyncCore("created_later")).IsFalse();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task OnlyAvailabilityMapsAuthenticationFailureToFalse(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(OnlyAvailabilityMapsAuthenticationFailureToFalse));
        var connection = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString) { UserID = "w2_nonexistent_user", Password = "invalid", ConnectionTimeout = 2 };
        using var provider = CreateProvider(schema, connection.ConnectionString);
        await Assert.That(await provider.FileOrServerExistsAsyncCore()).IsFalse();
        var failure = await Assert.That(async () => { await provider.DatabaseExistsAsyncCore(); }).Throws<MySqlException>();
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Operation).IsEqualTo(ExecutionOperationKind.ExistenceCheck);
        await Assert.That(async () => { await provider.TableExistsAsyncCore("items"); }).Throws<MySqlException>();
        await Assert.That(async () => { await provider.FileOrServerExistsAsyncCore(new(true)); }).Throws<OperationCanceledException>();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task AvailabilityPreservesLoggerFailureAndReleasesItsPoolSlot(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(AvailabilityPreservesLoggerFailureAndReleasesItsPoolSlot));
        using var logger = new FailingCommandLogger();
        using var provider = CreateProvider(schema, logging: new(logger));
        logger.Enabled = true;
        Exception? failure;
        try { failure = await Assert.That(async () => { await provider.FileOrServerExistsAsyncCore(); }).Throws<InvalidOperationException>(); }
        finally { logger.Enabled = false; }
        await Assert.That(failure).IsSameReferenceAs(logger.Failure);
        var context = ExecutionFailureContexts.Get(failure!)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.ExistenceCheck);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ApplicationError);
        await Assert.That(await provider.FileOrServerExistsAsyncCore()).IsTrue();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CanceledProbePoolWaitSettlesWithoutClosingTheBorrowedConnection(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(CanceledProbePoolWaitSettlesWithoutClosingTheBorrowedConnection));
        using var provider = CreateProvider(schema);
        var reader = await provider.DatabaseAccess.ExecuteReaderAsyncCore("SELECT 1 UNION ALL SELECT 2");
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pending = provider.FileOrServerExistsAsyncCore(cancellation.Token);
            await Assert.That(pending.IsCompleted).IsFalse();
            cancellation.Cancel();
            await Assert.That(async () => { await pending; }).Throws<OperationCanceledException>();
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        }
        finally { await reader.DisposeAsync(); }
        await Assert.That(await provider.FileOrServerExistsAsyncCore()).IsTrue();
        await Assert.That(await provider.DatabaseExistsAsyncCore()).IsTrue();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ProvisioningCreatesMissingSchemaAndRetainsPartialDdlOnFailure(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ProvisioningCreatesMissingSchemaAndRetainsPartialDdlOnFailure));
        var name = schema.Connection.DataSourceName;
        schema.ExecuteNonQuery($"DROP DATABASE {SqlIdentifier.Quote(name, "`")}");
        var connection = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString) { Database = "" };
        using var provider = CreateProvider(schema, connection.ConnectionString);
        var factory = SqlFromMetadataFactory.GetFactoryFromDatabaseType(descriptor.DatabaseType);
        var sql = new Sql("CREATE TABLE created (id INT PRIMARY KEY); INSERT INTO created VALUES (1)");
        await Assert.That(async () => { await factory.CreateDatabaseAsyncCore(sql, name, connection.ConnectionString, true, new(true)); }).Throws<OperationCanceledException>();
        await Assert.That(await provider.DatabaseExistsAsyncCore()).IsFalse();
        var pending = factory.CreateDatabaseAsyncCore(sql, name, connection.ConnectionString, true);
        sql.AddText("; INVALID SQL THAT MUST NOT BE CAPTURED LATE");
        _ = (await pending).ValueOrException();
        await Assert.That(await provider.DatabaseExistsAsyncCore()).IsTrue();
        await Assert.That(await provider.TableExistsAsyncCore("created")).IsTrue();
        var prefix = new Sql("CREATE TABLE kept_prefix (id INT PRIMARY KEY); INSERT INTO missing_table VALUES (1); CREATE TABLE skipped_suffix (id INT)");
        var failure = await Assert.That(async () => { await factory.CreateDatabaseAsyncCore(prefix, name, connection.ConnectionString, true); }).Throws<MySqlException>();
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Operation).IsEqualTo(ExecutionOperationKind.Provisioning);
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(await provider.TableExistsAsyncCore("kept_prefix")).IsTrue();
        await Assert.That(await provider.TableExistsAsyncCore("skipped_suffix")).IsFalse();
        await Assert.That(await provider.TableExistsAsyncCore("created")).IsTrue();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task AdministrativeSessionKeepsOneConnectionUntilOwnedCleanup(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(AdministrativeSessionKeepsOneConnectionUntilOwnedCleanup));
        using var provider = CreateProvider(schema);
        var session = ((IAsyncExistenceProbeSource)provider).CaptureExistenceProbe(new(ExistenceProbeKind.FileOrServer, null, null)).CreateSession!();
        using var command = new MySqlCommand("SELECT CONNECTION_ID()");
        try
        {
            await session.OpenAsync(default);
            var id = await session.Access.ExecuteScalarAsync(command, default);
            await Assert.That(await session.Access.ExecuteScalarAsync(command, default)).IsEqualTo(id);
            await using (var reader = await new OwnedCommandExecution(session.Access, session.CommandFactory).OpenReaderAsync(default))
            {
                await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            }
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Open);
            using var invalid = new MySqlCommand("SELECT * FROM nonexistent_table");
            await Assert.That(async () => { await session.Access.ExecuteScalarAsync(invalid, default); }).Throws<MySqlException>();
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Open);
        }
        finally { await session.DisposeAsync(); }
        await session.DisposeAsync();
        await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(await provider.FileOrServerExistsAsyncCore()).IsTrue();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RootDisposalIsSharedAndDoesNotCompleteDependentTransaction(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(RootDisposalIsSharedAndDoesNotCompleteDependentTransaction));
        foreach (var asyncFirst in new[] { false, true })
        {
            using var provider = CreateProvider(schema);
            var transaction = provider.StartTransaction();
            using var command = new MySqlCommand("SELECT 1");
            try
            {
                await transaction.DatabaseAccess.ExecuteScalarAsyncCore(command);
                var native = transaction.DatabaseAccess.DbTransaction;
                if (asyncFirst) await ((IAsyncRootDisposal)provider).DisposeAsyncCore(); else provider.Dispose();
                await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
                provider.Dispose();
                await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
                await Assert.That(transaction.DatabaseAccess.DbTransaction).IsSameReferenceAs(native);
                await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Open);
                await Assert.That(async () => { await provider.DatabaseExistsAsyncCore(); }).Throws<ObjectDisposedException>();
            }
            finally { await transaction.DisposeAsyncCore(); }
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        }
    }

    private static SqlProvider<EmployeesDb> CreateProvider(ServerSchemaDatabase schema, string? connectionString = null,
        DataLinqLoggingConfiguration? logging = null)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString ?? schema.Connection.ConnectionString) { Pooling = true, MaximumPoolSize = 1, ConnectionTimeout = 3 };
        return schema.Provider.DatabaseType == DatabaseType.MySQL
            ? new MySqlProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName, logging)
            : new MariaDBProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName, logging);
    }

    private sealed class FailingCommandLogger : ILoggerFactory, ILogger
    {
        internal readonly InvalidOperationException Failure = new("Injected SQL logger failure.");
        internal bool Enabled { get; set; }
        public ILogger CreateLogger(string categoryName) => categoryName == "DataLinq.SqlCommand" ? this : NullLogger.Instance;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Enabled) throw Failure;
        }
    }
}
