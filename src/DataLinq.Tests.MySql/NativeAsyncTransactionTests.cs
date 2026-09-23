using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public sealed class NativeAsyncTransactionTests
{
    [Test]
    public async Task UnusedCompletionAndDisposalDoNotOpenAConnection()
    {
        using var provider = new MySqlProvider<EmployeesDb>("Server=127.0.0.1;Port=1;User ID=unused;Pooling=false", "unused");
        foreach (var complete in new[] { "commit", "rollback", "dispose" })
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Unused);
            if (complete == "commit") await transaction.CommitAsyncCore();
            if (complete == "rollback") await transaction.RollbackAsyncCore();
            await transaction.DisposeAsyncCore();
            await transaction.DisposeAsyncCore();
            transaction.Dispose();
            await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Disposed);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task FirstUseAndCompletionCanSwitchBetweenSyncAndAsync(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(FirstUseAndCompletionCanSwitchBetweenSyncAndAsync),
            "CREATE TABLE items (id INT PRIMARY KEY, value INT NOT NULL)");
        using var provider = CreateProvider(schema);
        for (var mode = 0; mode < 4; mode++)
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            try
            {
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Unused);
                await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
                var sql = $"INSERT INTO items VALUES ({mode}, 10)";
                if ((mode & 1) == 0) transaction.DatabaseAccess.ExecuteNonQuery(sql);
                else await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(sql);
                var native = transaction.DatabaseAccess.DbTransaction;
                await Assert.That(native!.IsolationLevel).IsEqualTo(IsolationLevel.ReadCommitted);
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Ready);
                await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<int>($"SELECT value FROM items WHERE id = {mode}")).IsEqualTo(10);
                await Assert.That(transaction.DatabaseAccess.ExecuteScalar<int>($"SELECT value FROM items WHERE id = {mode}")).IsEqualTo(10);
                await Assert.That(ReferenceEquals(native, transaction.DatabaseAccess.DbTransaction)).IsTrue();
                if ((mode & 2) == 0) await transaction.CommitAsyncCore();
                else transaction.Commit();
                await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
        await Assert.That(Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT COUNT(*) FROM items"))).IsEqualTo(4);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task PreCancellationAndInvalidCommandsLeaveTransactionUnused(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(PreCancellationAndInvalidCommandsLeaveTransactionUnused));
        using var provider = CreateProvider(schema);
        var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        try
        {
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            using var command = new MySqlCommand("SELECT 1");
            await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore(command, canceled.Token)).Throws<OperationCanceledException>();
            await Assert.That(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(" ", canceled.Token)).Throws<InvalidOperationException>();
            await Assert.That(async () => { await transaction.DatabaseAccess.ExecuteReaderAsyncCore(command, canceled.Token); }).Throws<OperationCanceledException>();
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Unused);
            await Assert.That(command.Connection).IsNull();
            await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
            await Assert.That(Convert.ToInt32(await transaction.DatabaseAccess.ExecuteScalarAsyncCore(command))).IsEqualTo(1);
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ReaderOwnsAdmissionButBorrowsTheTransactionConnection(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ReaderOwnsAdmissionButBorrowsTheTransactionConnection));
        using var provider = CreateProvider(schema);
        var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        try
        {
            using var command = new MySqlCommand("SELECT 1 UNION ALL SELECT 2");
            var reader = await transaction.DatabaseAccess.ExecuteReaderAsyncCore(command);
            try
            {
                await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
                await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalar("SELECT 2")).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 2")).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.CommitAsyncCore()).Throws<InvalidOperationException>();
                await Assert.That(transaction.Dispose).Throws<InvalidOperationException>();
            }
            finally { await reader.DisposeAsync(); }
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(Convert.ToInt32(transaction.DatabaseAccess.ExecuteScalar("SELECT 3"))).IsEqualTo(3);
            using (var syncReader = transaction.DatabaseAccess.ExecuteReader(command))
                await Assert.That(syncReader.ReadNextRow()).IsTrue();
            await Assert.That(Convert.ToInt32(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 4"))).IsEqualTo(4);
        }
        finally { await transaction.DisposeAsyncCore(); }
        await Assert.That(Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT 5"))).IsEqualTo(5);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task NullScalarConversionMatchesExistingTransactionBehavior(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(NullScalarConversionMatchesExistingTransactionBehavior));
        using var provider = CreateProvider(schema);
        var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        try
        {
            await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT NULL")).IsNull();
            await Assert.That(transaction.DatabaseAccess.ExecuteScalar("SELECT NULL")).IsNull();
            await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<int>("SELECT NULL")).IsEqualTo(0);
            await Assert.That(transaction.DatabaseAccess.ExecuteScalar<int>("SELECT NULL")).IsEqualTo(0);
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task FailedSetupIsTerminalAndReleasesPartialNativeResources(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(FailedSetupIsTerminalAndReleasesPartialNativeResources));
        using var provider = CreateProvider(schema, databaseName: "w2_missing_" + Guid.NewGuid().ToString("N"));
        foreach (var asyncFirst in new[] { false, true })
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            try
            {
                var failure = asyncFirst
                    ? await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1")).Throws<MySqlException>()
                    : await Assert.That(() => transaction.DatabaseAccess.ExecuteScalar("SELECT 1")).Throws<MySqlException>();
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Failed);
                await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
                await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
                var context = ExecutionFailureContexts.Get(failure!)!;
                await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalar("SELECT 1")).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.RollbackAsyncCore()).Throws<InvalidOperationException>();
                await Assert.That(Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT 9"))).IsEqualTo(9);
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CanceledPoolWaitRejectsOverlapAndNeverPublishesPartialState(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(CanceledPoolWaitRejectsOverlapAndNeverPublishesPartialState));
        using var provider = CreateProvider(schema);
        var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        try
        {
            using (var blocker = provider.DatabaseAccess.ExecuteReader("SELECT 1"))
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var pending = transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 2", cancellation.Token);
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Initializing);
                await Assert.That(pending.IsCompleted).IsFalse();
                await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
                await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalar("SELECT 3")).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.DisposeAsyncCore().AsTask()).Throws<InvalidOperationException>();
                cancellation.Cancel();
                await Assert.That(() => pending).Throws<OperationCanceledException>();
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Failed);
                await Assert.That(blocker.ReadNextRow()).IsTrue();
            }
            await Assert.That(Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT 4"))).IsEqualTo(4);
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task InitializationObserverCannotReenterAndFailureCleansUpBeforePublication(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(InitializationObserverCannotReenterAndFailureCleansUpBeforePublication));
        using var provider = CreateProvider(schema);
        foreach (var asyncFirst in new[] { false, true })
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            var expected = new InvalidOperationException("startup observer");
            Exception? rejection = null;
            transaction.OnStatusChanged += (_, _) =>
            {
                try { transaction.DatabaseAccess.ExecuteScalar("SELECT 99"); }
                catch (Exception failure) { rejection = failure; }
                throw expected;
            };
            try
            {
                var failure = asyncFirst
                    ? await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1")).Throws<InvalidOperationException>()
                    : await Assert.That(() => transaction.DatabaseAccess.ExecuteScalar("SELECT 1")).Throws<InvalidOperationException>();
                await Assert.That(ReferenceEquals(failure, expected)).IsTrue();
                await Assert.That(rejection).IsTypeOf<InvalidOperationException>();
                await Assert.That(ExecutionFailureContexts.Get(failure!)!.Cause).IsEqualTo(ExecutionFailureCause.ApplicationError);
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Failed);
                await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
                await Assert.That(Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT 2"))).IsEqualTo(2);
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task AttachmentUsesExistingTransactionAndConsumesItsResources(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(AttachmentUsesExistingTransactionAndConsumesItsResources),
            "CREATE TABLE items (id INT PRIMARY KEY)");
        using var provider = CreateProvider(schema);
        await using var connection = new MySqlConnection(schema.Connection.ConnectionString);
        await connection.OpenAsync();
        await using var native = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var transaction = new Transaction(provider, native, TransactionType.ReadAndWrite);
        try
        {
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Ready);
            await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1)");
            await Assert.That(ReferenceEquals(native, transaction.DatabaseAccess.DbTransaction)).IsTrue();
            await Assert.That(native.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            await transaction.RollbackAsyncCore();
        }
        finally { await transaction.DisposeAsyncCore(); }
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT COUNT(*) FROM items"))).IsEqualTo(0);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task DisposalRollsBackAndPreservesSinglePoolCapacity(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(DisposalRollsBackAndPreservesSinglePoolCapacity),
            "CREATE TABLE items (id INT PRIMARY KEY)");
        using var provider = CreateProvider(schema);
        foreach (var asyncDispose in new[] { false, true })
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1)");
            if (asyncDispose) await transaction.DisposeAsyncCore(); else transaction.Dispose();
            await transaction.DisposeAsyncCore();
            transaction.Dispose();
            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
            await Assert.That(Convert.ToInt32(provider.DatabaseAccess.ExecuteScalar("SELECT COUNT(*) FROM items"))).IsEqualTo(0);
        }
    }

    private static TransactionInitializationState State(Transaction transaction) =>
        ((IAsyncTransactionCompletion)transaction.DatabaseAccess).InitializationState;

    private static SqlProvider<EmployeesDb> CreateProvider(ServerSchemaDatabase schema, string? databaseName = null)
    {
        var builder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString) { MaximumPoolSize = 1, ConnectionTimeout = 5 };
        return schema.Provider.DatabaseType == DatabaseType.MySQL
            ? new MySqlProvider<EmployeesDb>(builder.ConnectionString, databaseName ?? schema.Connection.DataSourceName, DataLinqLoggingConfiguration.NullConfiguration)
            : new MariaDBProvider<EmployeesDb>(builder.ConnectionString, databaseName ?? schema.Connection.DataSourceName, DataLinqLoggingConfiguration.NullConfiguration);
    }
}
