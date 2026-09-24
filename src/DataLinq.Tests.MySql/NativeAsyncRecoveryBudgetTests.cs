using System;
using System.Data;
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

public sealed class NativeAsyncRecoveryBudgetTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RecoveryBudgetExpiryRetainsOwnershipUntilNativeRollbackAndCleanupSettle(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(RecoveryBudgetExpiryRetainsOwnershipUntilNativeRollbackAndCleanupSettle),
            "CREATE TABLE departments (dept_no CHAR(4) PRIMARY KEY, dept_name VARCHAR(40) NOT NULL) ENGINE=InnoDB",
            "INSERT INTO departments VALUES ('w213', 'before rollback')");
        foreach (var expire in new[] { false, true })
        foreach (var loseResponse in new[] { false, true })
        {
            var builder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString);
            await using var relay = new NativeCompletionProxy(builder.Server, checked((int)builder.Port), "rollback");
            builder.Server = "127.0.0.1";
            builder.Port = checked((uint)relay.Port);
            builder.Pooling = true;
            builder.MaximumPoolSize = 1;
            builder.ConnectionTimeout = 5;
            builder.DefaultCommandTimeout = 30;
            builder.CancellationTimeout = 30;
            builder.SslMode = MySqlSslMode.Disabled;
            builder.UseCompression = false;
            builder.AllowPublicKeyRetrieval = true;
            using SqlProvider<EmployeesDb> provider = descriptor.DatabaseType == DatabaseType.MySQL
                ? new MySqlProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName)
                : new MariaDBProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName);
            var mutable = (await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w213"], provider.ReadOnlyAccess))!.Mutate();
            mutable.Name = "must roll back";
            using var request = new CancellationTokenSource();
            var primary = new OperationCanceledException("The callback request was canceled.", request.Token);
            var clock = new RecoveryClock();
            var settings = new RecoveryRollbackSettings(TimeSpan.FromMilliseconds(250));
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            var work = transaction.RunCallbackAsyncCore<int>(async token =>
            {
                await Assert.That(token).IsEqualTo(request.Token);
                await transaction.UpdateAsyncCore(mutable, token);
                relay.Arm();
                request.Cancel();
                throw primary;
            }, settings, request.Token, clock);
            try
            {
                await relay.WaitForSuccessfulResponseAsync();
                await Assert.That(request.IsCancellationRequested).IsTrue();
                await Assert.That(clock.Created).IsEqualTo(1);
                await Assert.That(clock.DueTime).IsEqualTo(settings.RecoveryRollbackTimeout);
                await Assert.That(relay.CompletionCommands).IsEqualTo(1);
                if (expire) clock.Timer!.Fire();
                await Assert.That(work.IsCompleted).IsFalse();
                await Assert.That(transaction.IsDisposed).IsFalse();
                await Assert.That(async () => { await transaction.DisposeAsyncCore(); }).Throws<InvalidOperationException>();
                // Even after budget expiry, the owned connection cannot be
                // returned to the one-slot pool while its native work is pending.
                using (var waiting = new CancellationTokenSource())
                {
                    var poolWait = provider.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1", waiting.Token);
                    await Assert.That(poolWait.IsCompleted).IsFalse();
                    waiting.Cancel();
                    await Assert.That(async () => { await poolWait; }).Throws<OperationCanceledException>();
                }
                if (loseResponse) relay.LoseResponse();
                else relay.ForwardResponse();
                var failure = await Assert.That(async () => { await work.WaitAsync(TimeSpan.FromSeconds(20)); }).Throws<OperationCanceledException>();
                await Assert.That(failure).IsSameReferenceAs(primary);
                await Assert.That(failure!.CancellationToken).IsEqualTo(request.Token);
                var context = ExecutionFailureContexts.Get(failure)!;
                await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
                await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.TransactionCallback);
                await Assert.That(context.Completion).IsEqualTo(loseResponse ? ExecutionCompletion.Unknown : ExecutionCompletion.RolledBack);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
                await Assert.That(context.HasCleanupFailure).IsFalse();
                await Assert.That(context.SecondaryFailures.Count).IsEqualTo(loseResponse ? 1 : 0);
                if (loseResponse)
                {
                    var rollback = context.SecondaryFailures[0];
                    await Assert.That(rollback.Operation).IsEqualTo(ExecutionOperationKind.Rollback);
                    if (rollback.Exception is OperationCanceledException canceled)
                    {
                        await Assert.That(expire).IsTrue();
                        await Assert.That(canceled.CancellationToken).IsNotEqualTo(request.Token);
                        await Assert.That(rollback.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
                    }
                    else
                    {
                        await Assert.That(rollback.Exception).IsTypeOf<MySqlException>();
                        await Assert.That(rollback.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
                    }
                }
                await Assert.That(transaction.IsDisposed).IsTrue();
                await Assert.That(clock.Timer!.IsDisposed).IsTrue();
                await Assert.That(((IMutableLifecycle)mutable).Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
                await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w213"], provider.ReadOnlyAccess))!.Name)
                    .IsEqualTo("before rollback");
            }
            finally
            {
                relay.LoseResponse();
                try { await work.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (OperationCanceledException) when (work.IsCanceled) { }
                finally { await transaction.DisposeAsyncCore(); }
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ExpiredRecoveryPreservesPrimaryAndNativeDisposalFailureWhileReleasingConnection(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ExpiredRecoveryPreservesPrimaryAndNativeDisposalFailureWhileReleasingConnection),
            "CREATE TABLE items (id INT PRIMARY KEY) ENGINE=InnoDB", "INSERT INTO items VALUES (1)");
        foreach (var cancelCallback in new[] { false, true })
        foreach (var loseResponse in new[] { false, true })
        {
            var builder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString);
            await using var relay = new NativeCompletionProxy(builder.Server, checked((int)builder.Port), "rollback");
            builder.Server = "127.0.0.1";
            builder.Port = checked((uint)relay.Port);
            builder.Pooling = true;
            builder.MaximumPoolSize = 1;
            builder.ConnectionTimeout = 5;
            builder.DefaultCommandTimeout = 30;
            builder.SslMode = MySqlSslMode.Disabled;
            builder.UseCompression = false;
            builder.AllowPublicKeyRetrieval = true;
            using SqlProvider<EmployeesDb> provider = descriptor.DatabaseType == DatabaseType.MySQL
                ? new MySqlProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName)
                : new MariaDBProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName);
            using var request = new CancellationTokenSource();
            var canceled = new OperationCanceledException("The callback request was canceled.", request.Token);
            Exception? primary = null;
            MySqlConnection? connection = null;
            // Expire before native recovery dispatch. The driver's separate,
            // uncancelable DisposeAsync still performs its implicit rollback.
            var clock = new RecoveryClock(fireOnCreation: true);
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            var work = transaction.RunCallbackAsyncCore<int>(async token =>
            {
                await transaction.DatabaseAccess.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (2)", token);
                connection = ((MySqlTransaction)transaction.DatabaseAccess.DbTransaction!).Connection;
                relay.Arm();
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
            try
            {
                await relay.WaitForSuccessfulResponseAsync();
                await Assert.That(work.IsCompleted).IsFalse();
                await Assert.That(clock.Created).IsEqualTo(1);
                await Assert.That(clock.Timer!.IsDisposed).IsTrue();
                await Assert.That(relay.CompletionCommands).IsEqualTo(1);
                await Assert.That(async () => { await transaction.DisposeAsyncCore(); }).Throws<InvalidOperationException>();
                using (var waiting = new CancellationTokenSource())
                {
                    var poolWait = provider.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1", waiting.Token);
                    await Assert.That(poolWait.IsCompleted).IsFalse();
                    waiting.Cancel();
                    await Assert.That(async () => { await poolWait; }).Throws<OperationCanceledException>();
                }
                if (loseResponse) relay.LoseResponse();
                else relay.ForwardResponse();
                Exception? failure = cancelCallback
                    ? await Assert.That(async () => { await work.WaitAsync(TimeSpan.FromSeconds(20)); }).Throws<OperationCanceledException>()
                    : await Assert.That(async () => { await work.WaitAsync(TimeSpan.FromSeconds(20)); }).Throws<MySqlException>();
                await Assert.That(failure).IsSameReferenceAs(primary);
                if (cancelCallback) await Assert.That(failure).IsSameReferenceAs(canceled);
                else await Assert.That(((MySqlException)failure!).ErrorCode).IsEqualTo(MySqlErrorCode.DuplicateKeyEntry);
                var context = ExecutionFailureContexts.Get(failure!)!;
                await Assert.That(context.Cause).IsEqualTo(cancelCallback ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.ProviderError);
                await Assert.That(context.Operation).IsEqualTo(cancelCallback ? ExecutionOperationKind.TransactionCallback : ExecutionOperationKind.RawCommand);
                await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
                await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
                await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
                await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
                await Assert.That(context.HasCleanupFailure).IsEqualTo(loseResponse);
                await Assert.That(context.SecondaryFailures.Count).IsEqualTo(loseResponse ? 2 : 1);
                var recovery = context.SecondaryFailures[0];
                await Assert.That(recovery.Exception).IsTypeOf<OperationCanceledException>();
                await Assert.That(((OperationCanceledException)recovery.Exception).CancellationToken).IsNotEqualTo(request.Token);
                await Assert.That(recovery.Operation).IsEqualTo(ExecutionOperationKind.Rollback);
                await Assert.That(recovery.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
                if (loseResponse)
                {
                    var cleanup = context.SecondaryFailures[1];
                    await Assert.That(cleanup.Exception).IsTypeOf<MySqlException>();
                    await Assert.That(cleanup.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
                    await Assert.That(cleanup.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
                    await Assert.That(cleanup.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
                }
                await Assert.That(connection!.State).IsEqualTo(ConnectionState.Closed);
                await Assert.That(transaction.IsDisposed).IsTrue();
                await transaction.DisposeAsyncCore();
                await Assert.That(relay.CompletionCommands).IsEqualTo(1);
                await Assert.That(Convert.ToInt32(await provider.DatabaseAccess.ExecuteScalarAsyncCore("SELECT COUNT(*) FROM items"))).IsEqualTo(1);
            }
            finally
            {
                relay.LoseResponse();
                try { await work.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (Exception failure) when (work.IsCompleted && failure is MySqlException or OperationCanceledException) { }
                finally { await transaction.DisposeAsyncCore(); }
            }
        }
    }

    // Fire the real CTS timer callback at the selected recovery boundary:
    // before driver entry or while its response is held. Provider work stays native.
    private sealed class RecoveryClock(bool fireOnCreation = false) : TimeProvider
    {
        internal int Created;
        internal TimeSpan DueTime;
        internal RecoveryTimer? Timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Created++;
            DueTime = dueTime;
            Timer = new(callback, state);
            if (fireOnCreation) Timer.Fire();
            return Timer;
        }
        internal sealed class RecoveryTimer(TimerCallback callback, object? state) : ITimer
        {
            private int disposed;
            internal bool IsDisposed => Volatile.Read(ref disposed) != 0;
            internal void Fire() { if (!IsDisposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !IsDisposed;
            public void Dispose() => Interlocked.Exchange(ref disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
