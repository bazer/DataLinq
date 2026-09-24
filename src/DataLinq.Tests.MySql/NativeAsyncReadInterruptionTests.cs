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

public sealed class NativeAsyncReadInterruptionTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task InterruptedGeneratedReadRequiresRecoveryEvenWhenNativeConnectionRemainsOpen(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(InterruptedGeneratedReadRequiresRecoveryEvenWhenNativeConnectionRemainsOpen),
            "CREATE TABLE departments (dept_no CHAR(4) PRIMARY KEY, dept_name VARCHAR(40) NOT NULL) ENGINE=InnoDB",
            "INSERT INTO departments VALUES ('w214', 'original')");
        await using var blocker = new MySqlConnection(schema.Connection.ConnectionString);
        await blocker.OpenAsync();
        await using var observer = new MySqlConnection(schema.Connection.ConnectionString);
        await observer.OpenAsync();
        foreach (var mode in new[] { "token", "soft_timeout", "hard_timeout" })
        {
            var builder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString)
            {
                Pooling = true, MaximumPoolSize = 1, ConnectionTimeout = 5,
                DefaultCommandTimeout = mode == "token" ? 15u : 2u,
                CancellationTimeout = mode == "hard_timeout" ? -1 : 30
            };
            using SqlProvider<EmployeesDb> provider = descriptor.DatabaseType == DatabaseType.MySQL
                ? new MySqlProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName)
                : new MariaDBProvider<EmployeesDb>(builder.ConnectionString, schema.Connection.DataSourceName);
            var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
            using var cancellation = new CancellationTokenSource();
            Task<Department?>? pending = null;
            try
            {
                var connectionId = Convert.ToInt64(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT CONNECTION_ID()"));
                var connection = ((MySqlTransaction)transaction.DatabaseAccess.DbTransaction!).Connection!;
                // No-dispatch cancellation is independently reusable. It must
                // not be confused with cancellation after a SELECT has entered SQL.
                await Assert.That(async () =>
                {
                    await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w214"], transaction, new(true));
                }).Throws<OperationCanceledException>();
                await Assert.That(Convert.ToInt32(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1"))).IsEqualTo(1);
                await using (var takeLock = new MySqlCommand("LOCK TABLES departments WRITE", blocker))
                    await takeLock.ExecuteNonQueryAsync();
                // Block an ordinary generated SELECT without adding FOR UPDATE,
                // stored functions or raw SQL whose effects would be unknown.
                pending = AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w214"], transaction, cancellation.Token);
                await ObserveRead(observer, connectionId, pending);
                Exception failure;
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
                    failure = timedOut;
                }
                var context = ExecutionFailureContexts.Get(failure)!;
                await Assert.That(context.Cause).IsEqualTo(mode == "token" ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Timeout);
                await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.KeyLookup);
                await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
                await Assert.That(context.HasCleanupFailure).IsFalse();
                await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
                await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
                await Assert.That(connection.State).IsEqualTo(mode == "hard_timeout" ? ConnectionState.Broken : ConnectionState.Open);
                await Assert.That(context.Recovery).IsEqualTo(mode == "hard_timeout" ? ExecutionRecoveryActions.Dispose
                    : ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
                await Assert.That(async () => { await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w214"], transaction); })
                    .Throws<InvalidOperationException>();
                await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
                if (mode != "hard_timeout") await transaction.RollbackAsyncCore();
            }
            finally
            {
                cancellation.Cancel();
                await using (var release = new MySqlCommand("UNLOCK TABLES", blocker)) await release.ExecuteNonQueryAsync();
                try
                {
                    if (pending is not null)
                        try { await pending; } catch (Exception failure) when (failure is MySqlException or OperationCanceledException) { }
                }
                finally { await transaction.DisposeAsyncCore(); }
            }
            // The canceled load cannot publish a partial model or retain the
            // connection. A fresh root read succeeds through the same one-slot pool.
            await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w214"], provider.ReadOnlyAccess))!.Name)
                .IsEqualTo("original");
        }
    }

    private static async Task ObserveRead(MySqlConnection observer, long connectionId, Task pending)
    {
        using var query = new MySqlCommand("SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE ID=@id AND COMMAND='Query' AND INFO LIKE 'SELECT %' AND INFO LIKE '%departments%'", observer);
        query.Parameters.AddWithValue("@id", connectionId);
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(5) && !pending.IsCompleted)
        {
            if (Convert.ToInt32(await query.ExecuteScalarAsync()) == 1) return;
            await Task.Delay(20);
        }
        throw new InvalidOperationException("The generated SELECT was not observed while the table lock was held.");
    }
}
