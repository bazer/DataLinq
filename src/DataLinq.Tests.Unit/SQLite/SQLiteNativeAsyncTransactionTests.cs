using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteNativeAsyncTransactionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnusedCompletionDoesNotInitializeNativeTransaction(bool memory)
    {
        using var fixture = new Fixture(memory);
        foreach (var mode in new[] { "commit", "rollback", "dispose" })
        {
            var transaction = fixture.NewTransaction();
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Unused);
            if (mode == "commit") await transaction.CommitAsyncCore();
            if (mode == "rollback") await transaction.RollbackAsyncCore();
            await transaction.DisposeAsyncCore();
            await transaction.DisposeAsyncCore();
            transaction.Dispose();
            await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Disposed);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FirstUseAndCompletionSwitchBetweenSyncAndAsync(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var command = new SqliteCommand("INSERT INTO items VALUES (@id, 10)");
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        command.Parameters.AddWithValue("@id", 0);
        for (var mode = 0; mode < 4; mode++)
        {
            var transaction = fixture.NewTransaction();
            try
            {
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Unused);
                command.Parameters[0].Value = mode;
                if ((mode & 1) == 0) transaction.DatabaseAccess.ExecuteNonQuery(command);
                else await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(command);
                await Assert.That(command.Connection).IsNull();
                await Assert.That(command.Transaction).IsNull();
                var native = transaction.DatabaseAccess.DbTransaction;
                await Assert.That(native!.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Ready);
                await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<long>($"SELECT value FROM items WHERE id={mode}")).IsEqualTo(10L);
                await Assert.That(transaction.DatabaseAccess.ExecuteScalar<long>($"SELECT value FROM items WHERE id={mode}")).IsEqualTo(10L);
                await Assert.That(transaction.DatabaseAccess.DbTransaction).IsSameReferenceAs(native);
                if ((mode & 2) == 0) await transaction.CommitAsyncCore(); else transaction.Commit();
                await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
            }
            finally { await transaction.DisposeAsyncCore(); }
            await Assert.That(disposals).IsEqualTo(0);
        }
        await Assert.That(fixture.Provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM items")).IsEqualTo(4L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreCancellationAndValidationLeaveUnusedWrapperReusable(bool memory)
    {
        using var fixture = new Fixture(memory);
        var transaction = fixture.NewTransaction();
        using var command = new SqliteCommand("SELECT 1");
        using var unsupported = new DerivedCommand { CommandText = "SELECT 1" };
        try
        {
            await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore(command, new(true))).Throws<OperationCanceledException>();
            await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore(unsupported, new(true))).Throws<NotSupportedException>();
            await Assert.That(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(" ", new(true))).Throws<InvalidOperationException>();
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Unused);
            await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
            await Assert.That(command.Connection).IsNull();
            await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<long>(command)).IsEqualTo(1L);
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReaderOwnsAdmissionAndReleasesBorrowedCommandWithoutClosingTransaction(bool memory)
    {
        using var fixture = new Fixture(memory);
        var transaction = fixture.NewTransaction();
        using var command = new SqliteCommand("SELECT 1 UNION ALL SELECT 2");
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        try
        {
            var reader = await transaction.DatabaseAccess.ExecuteReaderAsyncCore(command);
            var connection = command.Connection!;
            try
            {
                await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalar("SELECT 9")).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 9")).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.CommitAsyncCore()).Throws<InvalidOperationException>();
                await Assert.That(transaction.Dispose).Throws<InvalidOperationException>();
            }
            finally { await reader.DisposeAsync(); }
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(command.Connection).IsNull();
            using (var syncReader = transaction.DatabaseAccess.ExecuteReader(command))
                await Assert.That(syncReader.ReadNextRow()).IsTrue();
            await Assert.That(command.Connection).IsNull();
            await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<long>("SELECT 3")).IsEqualTo(3L);
        }
        finally { await transaction.DisposeAsyncCore(); }
        await Assert.That(disposals).IsEqualTo(0);
        await Assert.That(await fixture.Provider.DatabaseAccess.ExecuteScalarAsyncCore<long>(command)).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeferredSerializableBeginDoesNotTakeTheOtherWritersLock(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var blocker = new SqliteConnection(fixture.Provider.ConnectionString);
        blocker.Open();
        using var held = blocker.BeginTransaction();
        using (var write = new SqliteCommand("INSERT INTO items VALUES (10,10)", blocker, held)) write.ExecuteNonQuery();
        foreach (var asyncFirst in new[] { false, true })
        {
            var transaction = fixture.NewTransaction();
            try
            {
                var result = asyncFirst ? await transaction.DatabaseAccess.ExecuteScalarAsyncCore<long>("SELECT 7")
                    : transaction.DatabaseAccess.ExecuteScalar<long>("SELECT 7");
                await Assert.That(result).IsEqualTo(7L);
                await Assert.That(transaction.DatabaseAccess.DbTransaction!.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
                await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<long>("PRAGMA read_uncommitted")).IsEqualTo(0L);
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
        held.Rollback();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ScalarNullAndDirectCastMatchSynchronousSQLite(bool memory)
    {
        using var fixture = new Fixture(memory);
        var transaction = fixture.NewTransaction();
        try
        {
            await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT NULL")).IsSameReferenceAs(DBNull.Value);
            await Assert.That(transaction.DatabaseAccess.ExecuteScalar("SELECT NULL")).IsSameReferenceAs(DBNull.Value);
            await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT value FROM items")).IsNull();
            // Raw SQL retains unknown effects; even a later CLR cast failure
            // cannot authorize more business operations after dispatch.
            await Assert.That(async () => { await transaction.DatabaseAccess.ExecuteScalarAsyncCore<int>("SELECT 7"); }).Throws<InvalidCastException>();
            await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 7")).Throws<InvalidOperationException>();
            await transaction.RollbackAsyncCore();
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InitializationObserverFailureRejectsReentryAndCleansPrivateResources(bool memory)
    {
        using var fixture = new Fixture(memory);
        foreach (var asyncFirst in new[] { false, true })
        {
            var transaction = fixture.NewTransaction();
            var expected = new InvalidOperationException("Initialization observer failed.");
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
                await Assert.That(failure).IsSameReferenceAs(expected);
                await Assert.That(rejection).IsTypeOf<InvalidOperationException>();
                await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Failed);
                await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
                await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
                await Assert.That(ExecutionFailureContexts.Get(failure!)!.Cause).IsEqualTo(ExecutionFailureCause.ApplicationError);
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 2")).Throws<InvalidOperationException>();
            }
            finally { await transaction.DisposeAsyncCore(); }
            await fixture.Provider.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1,10); DELETE FROM items");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationAfterSetupIsTerminalWithoutPublishingNativeBegin(bool memory)
    {
        using var fixture = new Fixture(memory);
        var transaction = fixture.NewTransaction();
        using var cancellation = new CancellationTokenSource();
        using var parent = new Activity("sqlite-transaction-setup").Start();
        try
        {
            using (var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "DataLinq",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.OperationName == "datalinq.db.command" && activity.ParentSpanId == parent.SpanId) cancellation.Cancel();
                }
            })
            {
                ActivitySource.AddActivityListener(listener);
                await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1", cancellation.Token)).Throws<OperationCanceledException>();
            }
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(transaction.DatabaseAccess.DbTransaction).IsNull();
            await Assert.That(() => transaction.RollbackAsyncCore()).Throws<InvalidOperationException>();
            await Assert.That(() => transaction.DatabaseAccess.ExecuteScalar("SELECT 2")).Throws<InvalidOperationException>();
        }
        finally { await transaction.DisposeAsyncCore(); }
        await fixture.Provider.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1,10)");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AttachedTransactionPreservesIsolationAndConsumesItsConnection(bool memory)
    {
        using var fixture = new Fixture(memory);
        var options = new SqliteConnectionStringBuilder(fixture.Provider.ConnectionString) { Cache = SqliteCacheMode.Shared };
        using var connection = new SqliteConnection(options.ConnectionString);
        connection.Open();
        using var native = connection.BeginTransaction(IsolationLevel.ReadUncommitted, deferred: true);
        var transaction = new Transaction(fixture.Provider, native, TransactionType.ReadAndWrite);
        try
        {
            await Assert.That(State(transaction)).IsEqualTo(TransactionInitializationState.Ready);
            await Assert.That(await transaction.DatabaseAccess.ExecuteScalarAsyncCore<long>("PRAGMA read_uncommitted")).IsEqualTo(1L);
            await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1,10)");
            await Assert.That(transaction.DatabaseAccess.DbTransaction).IsSameReferenceAs(native);
            await Assert.That(native.IsolationLevel).IsEqualTo(IsolationLevel.ReadUncommitted);
            await transaction.RollbackAsyncCore();
        }
        finally { await transaction.DisposeAsyncCore(); }
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(fixture.Provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM items")).IsEqualTo(0L);
        SqliteConnection.ClearPool(connection);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisposalRollsBackAndConfirmedCompletionSurvivesObserverFailure(bool memory)
    {
        using var fixture = new Fixture(memory);
        foreach (var asyncDispose in new[] { false, true })
        {
            var transaction = fixture.NewTransaction();
            await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1,10)");
            if (asyncDispose) await transaction.DisposeAsyncCore(); else transaction.Dispose();
            await transaction.DisposeAsyncCore();
            await Assert.That(fixture.Provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM items")).IsEqualTo(0L);
        }
        foreach (var commit in new[] { false, true })
        {
            var transaction = fixture.NewTransaction();
            var expected = new InvalidOperationException("Completion observer failed.");
            await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1,10)");
            transaction.OnStatusChanged += (_, _) => throw expected;
            try
            {
                var failure = await Assert.That(() => commit ? transaction.CommitAsyncCore() : transaction.RollbackAsyncCore()).Throws<InvalidOperationException>();
                await Assert.That(failure).IsSameReferenceAs(expected);
                await Assert.That(ExecutionFailureContexts.Get(failure!)!.Completion).IsEqualTo(commit ? ExecutionCompletion.Committed : ExecutionCompletion.RolledBack);
            }
            finally { await transaction.DisposeAsyncCore(); }
            await Assert.That(fixture.Provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM items")).IsEqualTo(commit ? 1L : 0L);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CanceledCallbackRecoversIndependentlyAndRetainsSecondaryObserverFailure(bool memory)
    {
        using var fixture = new Fixture(memory);
        foreach (var failObserver in new[] { false, true })
        {
            var transaction = fixture.NewTransaction();
            using var cancellation = new CancellationTokenSource();
            var expected = new InvalidOperationException("Rollback observer failed.");
            transaction.OnStatusChanged += (_, args) =>
            {
                if (failObserver && args.Status == DatabaseTransactionStatus.RolledBack) throw expected;
            };
            try
            {
                var failure = await Assert.That(async () =>
                {
                    await transaction.RunCallbackAsyncCore<int>(async token =>
                    {
                        await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1,10)", token);
                        cancellation.Cancel();
                        token.ThrowIfCancellationRequested();
                        return 1;
                    }, new RecoveryRollbackSettings(), cancellation.Token);
                }).Throws<OperationCanceledException>();
                var context = ExecutionFailureContexts.Get(failure!)!;
                await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
                await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.TransactionCallback);
                await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
                await Assert.That(context.SecondaryFailures.Count).IsEqualTo(failObserver ? 1 : 0);
                if (failObserver) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(expected);
                await Assert.That(transaction.IsDisposed).IsTrue();
                await Assert.That(fixture.Provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM items")).IsEqualTo(0L);
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
    }

    private static TransactionInitializationState State(Transaction transaction) => ((IAsyncTransactionCompletion)transaction.DatabaseAccess).InitializationState;
    private sealed class DerivedCommand : SqliteCommand;

    private sealed class Fixture : IDisposable
    {
        private readonly string? path;
        internal SQLiteProvider<EmployeesDb> Provider { get; }
        internal Transaction NewTransaction() => new(Provider, TransactionType.ReadAndWrite);
        internal Fixture(bool memory)
        {
            var name = $"w2_tx_{Guid.NewGuid():N}";
            path = memory ? null : Path.Combine(Path.GetTempPath(), name + ".db");
            Provider = new SQLiteProvider<EmployeesDb>(new SqliteConnectionStringBuilder
            {
                DataSource = path ?? name, Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
                Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private, Pooling = true, DefaultTimeout = 2
            }.ConnectionString);
            Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE items (id INTEGER PRIMARY KEY, value INTEGER NOT NULL)");
        }
        public void Dispose()
        {
            Provider.Dispose();
            using var pool = new SqliteConnection(Provider.ConnectionString);
            SqliteConnection.ClearPool(pool);
            if (path is null) return;
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
        }
    }
}
