using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteNativeAsyncRecoveryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedNativeCommitRemainsUnknownAfterSuccessfulRecovery(bool memory)
    {
        using var fixture = new Fixture(memory);
        var transaction = fixture.NewTransaction();
        var policy = new CompletionPolicy("COMMIT");
        try
        {
            var failure = await Assert.That(async () =>
            {
                await transaction.RunCallbackAsyncCore(async token =>
                {
                    await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (2)", token);
                    policy.Install(((SqliteTransaction)transaction.DatabaseAccess.DbTransaction!).Connection!);
                    return 42;
                }, new RecoveryRollbackSettings());
            }).Throws<SqliteException>();
            await Assert.That(failure!.SqliteErrorCode).IsEqualTo(raw.SQLITE_AUTH);
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
            // The adapter does not infer commit certainty from the engine's
            // rejection code. Later successful recovery cannot rewrite it.
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
            await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
            await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
            await Assert.That(context.SecondaryFailures.Count).IsEqualTo(0);
            await Assert.That(context.HasCleanupFailure).IsFalse();
            await Assert.That(policy.Commits).IsEqualTo(1);
            await Assert.That(policy.Rollbacks).IsEqualTo(1);
            await Assert.That(transaction.IsDisposed).IsTrue();
            await Assert.That(await fixture.Provider.DatabaseAccess.ExecuteScalarAsyncCore<long>("SELECT COUNT(*) FROM items")).IsEqualTo(1L);
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredRecoveryRetainsPrimaryAndNativeDisposalFailure(bool memory)
    {
        foreach (var cancelCallback in new[] { false, true })
        {
            using var fixture = new Fixture(memory);
            var transaction = fixture.NewTransaction();
            var policy = new CompletionPolicy("ROLLBACK");
            using var request = new CancellationTokenSource();
            var canceled = new OperationCanceledException("The callback request was canceled.", request.Token);
            Exception? primary = null;
            SqliteConnection? connection = null;
            var clock = new ExpiredRecoveryClock();
            try
            {
                var work = transaction.RunCallbackAsyncCore<int>(async token =>
                {
                    await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (2)", token);
                    connection = ((SqliteTransaction)transaction.DatabaseAccess.DbTransaction!).Connection!;
                    policy.Install(connection);
                    try
                    {
                        if (cancelCallback)
                        {
                            request.Cancel();
                            throw canceled;
                        }
                        return await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1)", token);
                    }
                    catch (Exception failure) { primary = failure; throw; }
                }, new RecoveryRollbackSettings(TimeSpan.FromMilliseconds(250)), request.Token, clock);
                Exception? failure = cancelCallback
                    ? await Assert.That(async () => { await work; }).Throws<OperationCanceledException>()
                    : await Assert.That(async () => { await work; }).Throws<SqliteException>();
                await Assert.That(failure).IsSameReferenceAs(primary);
                if (cancelCallback) await Assert.That(failure).IsSameReferenceAs(canceled);
                else await Assert.That(((SqliteException)failure!).SqliteErrorCode).IsEqualTo(raw.SQLITE_CONSTRAINT);
                var context = ExecutionFailureContexts.Get(failure!)!;
                await Assert.That(context.Cause).IsEqualTo(cancelCallback ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.ProviderError);
                await Assert.That(context.Operation).IsEqualTo(cancelCallback ? ExecutionOperationKind.TransactionCallback : ExecutionOperationKind.RawCommand);
                await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
                await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
                await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
                await Assert.That(context.HasCleanupFailure).IsTrue();
                await Assert.That(context.SecondaryFailures.Count).IsEqualTo(2);
                var recovery = context.SecondaryFailures[0];
                await Assert.That(recovery.Exception).IsTypeOf<OperationCanceledException>();
                await Assert.That(((OperationCanceledException)recovery.Exception).CancellationToken).IsNotEqualTo(request.Token);
                await Assert.That(recovery.Operation).IsEqualTo(ExecutionOperationKind.Rollback);
                await Assert.That(recovery.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
                var cleanup = context.SecondaryFailures[1];
                await Assert.That(cleanup.Exception).IsTypeOf<SqliteException>();
                await Assert.That(((SqliteException)cleanup.Exception).SqliteErrorCode).IsEqualTo(raw.SQLITE_AUTH);
                await Assert.That(cleanup.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
                await Assert.That(cleanup.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
                await Assert.That(cleanup.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
                // Closed verifies independent ADO cleanup, not pooled-handle
                // integrity. The separate raw-driver rollback-denial probe
                // records 10.0.11's unsafe file-pool reuse; acceptance stays open.
                await Assert.That(connection!.State).IsEqualTo(ConnectionState.Closed);
                await Assert.That(transaction.IsDisposed).IsTrue();
                await Assert.That(clock.Created).IsEqualTo(1);
                await Assert.That(clock.Timer!.IsDisposed).IsTrue();
                await Assert.That(policy.Rollbacks).IsEqualTo(1);
                await transaction.DisposeAsyncCore();
                await Assert.That(policy.Rollbacks).IsEqualTo(1);
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
    }

    // Reject only the selected completion at the real engine prepare boundary.
    // No exception or transaction result is synthesized by the fixture, and the
    // callback never modifies its connection. Its isolated pool is torn down last.
    private sealed class CompletionPolicy(string deniedCommand)
    {
        internal int Commits;
        internal int Rollbacks;
        internal void Install(SqliteConnection connection)
        {
            var result = raw.sqlite3_set_authorizer(connection.Handle!, (strdelegate_authorizer)Authorize, null);
            SqliteException.ThrowExceptionForRC(result, connection.Handle);
        }
        private int Authorize(object state, int action, string first, string second, string database, string source)
        {
            if (action != raw.SQLITE_TRANSACTION) return raw.SQLITE_OK;
            if (first == "COMMIT") Commits++;
            if (first == "ROLLBACK") Rollbacks++;
            return first == deniedCommand ? raw.SQLITE_DENY : raw.SQLITE_OK;
        }
    }

    private sealed class ExpiredRecoveryClock : TimeProvider
    {
        internal int Created;
        internal RecoveryTimer? Timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Created++;
            Timer = new();
            callback(state);
            return Timer;
        }
        internal sealed class RecoveryTimer : ITimer
        {
            internal bool IsDisposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;
            public void Dispose() => IsDisposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? path;
        internal SQLiteProvider<EmployeesDb> Provider { get; }
        internal Transaction NewTransaction() => new(Provider, TransactionType.ReadAndWrite);
        internal Fixture(bool memory)
        {
            var name = $"w2_recovery_{Guid.NewGuid():N}";
            path = memory ? null : Path.Combine(Path.GetTempPath(), name + ".db");
            Provider = new SQLiteProvider<EmployeesDb>(new SqliteConnectionStringBuilder
            {
                DataSource = path ?? name, Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
                Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private, Pooling = true, DefaultTimeout = 2
            }.ConnectionString);
            Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE items (id INTEGER PRIMARY KEY); INSERT INTO items VALUES (1)");
        }
        public void Dispose()
        {
            try { Provider.Dispose(); }
            finally
            {
                using var pool = new SqliteConnection(Provider.ConnectionString);
                SqliteConnection.ClearPool(pool);
                if (path is not null)
                {
                    File.Delete(path);
                    File.Delete(path + "-wal");
                    File.Delete(path + "-shm");
                }
            }
        }
    }
}
