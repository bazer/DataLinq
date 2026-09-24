using System;
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

public sealed class NativeAsyncCommitAcknowledgementTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task LostSuccessfulCommitAcknowledgementKeepsOutcomeUnknownAndEvictsStaleCache(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(LostSuccessfulCommitAcknowledgementKeepsOutcomeUnknownAndEvictsStaleCache),
            "CREATE TABLE departments (dept_no CHAR(4) PRIMARY KEY, dept_name VARCHAR(40) NOT NULL) ENGINE=InnoDB",
            "INSERT INTO departments VALUES ('w212', 'before commit')");
        var builder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString);
        await using var relay = new NativeCompletionProxy(builder.Server, checked((int)builder.Port));
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
        var original = (await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w212"], provider.ReadOnlyAccess))!;
        var mutable = original.Mutate();
        mutable.Name = "committed without acknowledgement";
        var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        Task? completion = null;
        try
        {
            await transaction.UpdateAsyncCore(mutable);
            await Assert.That(((IMutableLifecycle)mutable).Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
            relay.Arm();
            completion = transaction.CommitAsyncCore();
            await relay.WaitForSuccessfulResponseAsync();
            await Assert.That(completion.IsCompleted).IsFalse();
            // This connection bypasses the relay. The driver awaiting COMMIT has
            // not received the successful server packet, but the row is durable.
            await using (var observer = new MySqlConnection(schema.Connection.ConnectionString))
            {
                await observer.OpenAsync();
                await using var query = new MySqlCommand("SELECT dept_name FROM departments WHERE dept_no='w212'", observer);
                await Assert.That(await query.ExecuteScalarAsync()).IsEqualTo("committed without acknowledgement");
            }
            relay.LoseResponse();
            var failure = await Assert.That(async () => { await completion.WaitAsync(TimeSpan.FromSeconds(20)); }).Throws<MySqlException>();
            var context = ExecutionFailureContexts.Get(failure!)!;
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
            await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
            await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
            await Assert.That(((IMutableLifecycle)mutable).Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
            await Assert.That(((IMutableLifecycle)mutable).Lifecycle.InvalidationReason).IsEqualTo(MutableInvalidationReason.CommitOutcomeUnknown);
            await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
            await Assert.That(async () => { await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1"); }).Throws<InvalidOperationException>();
        }
        finally
        {
            relay.LoseResponse();
            try
            {
                if (completion is not null)
                    try { await completion.WaitAsync(TimeSpan.FromSeconds(20)); }
                    catch (MySqlException) when (completion.IsFaulted) { }
            }
            finally { await transaction.DisposeAsyncCore(); }
        }
        await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        var fresh = (await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w212"], provider.ReadOnlyAccess))!;
        await Assert.That(fresh).IsNotSameReferenceAs(original);
        await Assert.That(fresh.Name).IsEqualTo("committed without acknowledgement");
        await Assert.That(relay.CompletionCommands).IsEqualTo(1);
    }
}
