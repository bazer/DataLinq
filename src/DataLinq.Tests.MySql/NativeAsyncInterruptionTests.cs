using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.MariaDB;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public sealed class NativeAsyncInterruptionTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task InterruptedMutationPoisonsTransactionAndPreservesOnlyValidRecovery(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(InterruptedMutationPoisonsTransactionAndPreservesOnlyValidRecovery),
            "CREATE TABLE departments (dept_no CHAR(4) PRIMARY KEY, dept_name VARCHAR(40) NOT NULL) ENGINE=InnoDB",
            "INSERT INTO departments VALUES ('w211', 'original')");
        await using var blocker = new MySqlConnection(schema.Connection.ConnectionString);
        await blocker.OpenAsync();
        await using var held = await blocker.BeginTransactionAsync();
        await using (var takeLock = new MySqlCommand("UPDATE departments SET dept_name='blocker' WHERE dept_no='w211'", blocker, held))
            await takeLock.ExecuteNonQueryAsync();
        await using var observer = new MySqlConnection(schema.Connection.ConnectionString);
        await observer.OpenAsync();

        foreach (var mode in new[] { "token", "soft_timeout", "hard_timeout" })
        {
            // Keep the statement timeout short, but give the driver's separate
            // KILL QUERY connection time to settle under the full concurrent suite.
            using var provider = Provider(schema, mode == "hard_timeout" ? -1 : 30, mode == "token" ? 15u : 2u);
            var original = (await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w211"], provider.ReadOnlyAccess))!;
            var mutable = original.Mutate();
            mutable.Name = "must not publish";
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            using var cancellation = new CancellationTokenSource();
            try
            {
                var connectionId = Convert.ToInt64(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT CONNECTION_ID()"));
                var native = (MySqlTransaction)transaction.DatabaseAccess.DbTransaction!;
                var connection = native.Connection!;
                var pending = transaction.UpdateAsyncCore(mutable, cancellation.Token);
                Exception failure;
                try
                {
                    await ObserveStatement(observer, connectionId, pending);
                    await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
                    if (mode == "token")
                    {
                        cancellation.Cancel();
                        var canceled = await Assert.That(async () => { await pending; }).Throws<OperationCanceledException>();
                        await Assert.That(canceled!.CancellationToken).IsEqualTo(cancellation.Token);
                        await Assert.That(canceled.InnerException).IsTypeOf<MySqlException>();
                        await Assert.That(((MySqlException)canceled.InnerException!).ErrorCode).IsEqualTo(MySqlErrorCode.QueryInterrupted);
                        failure = canceled;
                    }
                    else
                    {
                        var timedOut = await Assert.That(async () => { await pending; }).Throws<MySqlException>();
                        await Assert.That(timedOut!.ErrorCode).IsEqualTo(MySqlErrorCode.CommandTimeoutExpired);
                        if (mode == "soft_timeout")
                        {
                            await Assert.That(timedOut.InnerException).IsTypeOf<MySqlException>();
                            await Assert.That(((MySqlException)timedOut.InnerException!).ErrorCode).IsEqualTo(MySqlErrorCode.QueryInterrupted);
                        }
                        else await Assert.That(timedOut.InnerException is MySqlException { ErrorCode: MySqlErrorCode.QueryInterrupted }).IsFalse();
                        failure = timedOut;
                    }
                }
                finally
                {
                    cancellation.Cancel();
                    try { await pending; } catch (Exception) { }
                }
                var context = ExecutionFailureContexts.Get(failure)!;
                await Assert.That(context.Cause).IsEqualTo(mode == "token" ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Timeout);
                await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Update);
                await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
                await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
                await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
                await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
                await Assert.That(context.Recovery).IsEqualTo(mode == "hard_timeout" ? ExecutionRecoveryActions.Dispose
                    : ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
                await Assert.That(connection.State).IsEqualTo(mode == "hard_timeout" ? ConnectionState.Broken : ConnectionState.Open);
                await Assert.That(((IMutableLifecycle)mutable).Lifecycle.InvalidationReason).IsEqualTo(MutableInvalidationReason.MutationFailed);
                await Assert.That(async () => { await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1"); }).Throws<InvalidOperationException>();
                await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
                if (mode != "hard_timeout")
                {
                    await Assert.That(async () => { await transaction.RollbackAsyncCore(new(true)); }).Throws<OperationCanceledException>();
                    await transaction.RollbackAsyncCore();
                    await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
                }
            }
            finally { await transaction.DisposeAsyncCore(); }
            // A new operation must obtain the one-slot pool only after all owned work settles.
            await Assert.That(Convert.ToInt32(await provider.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 9"))).IsEqualTo(9);
            provider.State.ClearCache();
            await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w211"], provider.ReadOnlyAccess))!.Name).IsEqualTo("original");
        }
        await held.RollbackAsync();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task LostConnectionDuringCompletionRetainsUnknownOutcomeAndNativeCause(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(LostConnectionDuringCompletionRetainsUnknownOutcomeAndNativeCause),
            "CREATE TABLE items (id INT PRIMARY KEY)");
        using var provider = Provider(schema);
        foreach (var commit in new[] { true, false })
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            try
            {
                await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1)");
                var connectionId = Convert.ToInt64(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT CONNECTION_ID()"));
                await using var killer = new MySqlConnection(schema.Connection.ConnectionString);
                await killer.OpenAsync();
                await using (var kill = new MySqlCommand($"KILL CONNECTION {connectionId}", killer)) await kill.ExecuteNonQueryAsync();
                var failure = await Assert.That(async () =>
                {
                    if (commit) await transaction.CommitAsyncCore(); else await transaction.RollbackAsyncCore();
                }).Throws<MySqlException>();
                var context = ExecutionFailureContexts.Get(failure!)!;
                await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
                await Assert.That(context.Operation).IsEqualTo(commit ? ExecutionOperationKind.Commit : ExecutionOperationKind.Rollback);
                await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
                await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
                await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
                await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
                await Assert.That(async () => { await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1"); }).Throws<InvalidOperationException>();
            }
            finally { await transaction.DisposeAsyncCore(); }
            await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
            await Assert.That(Convert.ToInt32(await provider.DatabaseAccess.ExecuteScalarAsyncCore("SELECT COUNT(*) FROM items"))).IsEqualTo(0);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ConfirmedCompletionSurvivesLaterObserverFailure(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ConfirmedCompletionSurvivesLaterObserverFailure),
            "CREATE TABLE items (id INT PRIMARY KEY)");
        using var provider = Provider(schema);
        foreach (var commit in new[] { false, true })
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            var expected = new InvalidOperationException("Completion observer failed after native confirmation.");
            var completedStatus = commit ? DatabaseTransactionStatus.Committed : DatabaseTransactionStatus.RolledBack;
            transaction.OnStatusChanged += (_, args) => { if (args.Status == completedStatus) throw expected; };
            try
            {
                await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1)");
                var failure = await Assert.That(async () =>
                {
                    if (commit) await transaction.CommitAsyncCore(); else await transaction.RollbackAsyncCore();
                }).Throws<InvalidOperationException>();
                await Assert.That(failure).IsSameReferenceAs(expected);
                var context = ExecutionFailureContexts.Get(failure!)!;
                await Assert.That(context.Completion).IsEqualTo(commit ? ExecutionCompletion.Committed : ExecutionCompletion.RolledBack);
                await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
                await Assert.That(transaction.Status).IsEqualTo(completedStatus);
            }
            finally { await transaction.DisposeAsyncCore(); }
            await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(commit ? ExecutionCompletion.Committed : ExecutionCompletion.RolledBack);
            await Assert.That(Convert.ToInt32(await provider.DatabaseAccess.ExecuteScalarAsyncCore("SELECT COUNT(*) FROM items"))).IsEqualTo(commit ? 1 : 0);
        }
    }

    private static async Task ObserveStatement(MySqlConnection observer, long connectionId, Task pending)
    {
        using var probe = new MySqlCommand("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE ID=@id AND COMMAND='Query' AND INFO LIKE 'UPDATE %'", observer);
        probe.Parameters.AddWithValue("@id", connectionId);
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(5) && !pending.IsCompleted)
        {
            if (Convert.ToInt64(await probe.ExecuteScalarAsync()) == 1) return;
            await Task.Delay(20);
        }
        throw new InvalidOperationException("The native UPDATE was not observed while the row lock was held.");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CanceledCallbackUsesIndependentNativeRecoveryAndPreservesSecondaryFailure(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(CanceledCallbackUsesIndependentNativeRecoveryAndPreservesSecondaryFailure),
            "CREATE TABLE items (id INT PRIMARY KEY)");
        using var provider = Provider(schema);
        foreach (var failObserver in new[] { false, true })
        {
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            using var cancellation = new CancellationTokenSource();
            var observerFailure = new InvalidOperationException("Rollback notification failed after confirmation.");
            var sawRollback = false;
            transaction.OnStatusChanged += (_, args) =>
            {
                if (args.Status != DatabaseTransactionStatus.RolledBack) return;
                sawRollback = true;
                if (failObserver) throw observerFailure;
            };
            try
            {
                await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1");
                await Assert.That(async () => { await transaction.CommitAsyncCore(new(true)); }).Throws<OperationCanceledException>();
                var failure = await Assert.That(async () =>
                {
                    await transaction.RunCallbackAsyncCore<int>(async token =>
                    {
                        await Assert.That(token).IsEqualTo(cancellation.Token);
                        await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1)", token);
                        await Assert.That(async () => { await transaction.CommitAsyncCore(new(true)); }).Throws<InvalidOperationException>();
                        await Assert.That(Convert.ToInt32(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT COUNT(*) FROM items", token))).IsEqualTo(1);
                        cancellation.Cancel();
                        token.ThrowIfCancellationRequested();
                        return 1;
                    }, new RecoveryRollbackSettings(), cancellation.Token);
                }).Throws<OperationCanceledException>();
                await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
                var context = ExecutionFailureContexts.Get(failure)!;
                await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
                await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.TransactionCallback);
                await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
                await Assert.That(sawRollback).IsTrue();
                await Assert.That(transaction.IsDisposed).IsTrue();
                await Assert.That(context.SecondaryFailures.Count).IsEqualTo(failObserver ? 1 : 0);
                if (failObserver)
                {
                    var secondary = context.SecondaryFailures[0];
                    await Assert.That(secondary.Exception).IsSameReferenceAs(observerFailure);
                    await Assert.That(secondary.Operation).IsEqualTo(ExecutionOperationKind.Rollback);
                    await Assert.That(secondary.Stage).IsEqualTo(ExecutionFailureStage.Notification);
                }
                await Assert.That(Convert.ToInt32(await provider.DatabaseAccess.ExecuteScalarAsyncCore("SELECT COUNT(*) FROM items"))).IsEqualTo(0);
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
    }

    private static SqlProvider<EmployeesDb> Provider(ServerSchemaDatabase schema, int cancellationTimeout = 2, uint commandTimeout = 15)
    {
        var builder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString)
        {
            Pooling = true, MaximumPoolSize = 1, ConnectionTimeout = 5,
            CancellationTimeout = cancellationTimeout, DefaultCommandTimeout = commandTimeout
        };
        return schema.Provider.DatabaseType == DatabaseType.MySQL
            ? new MySqlProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName)
            : new MariaDBProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName);
    }
}
