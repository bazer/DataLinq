using System;
using System.Data;
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

public sealed class NativeReviewRegressionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectCompletionRejectsFailedInitializationButAllowsUnusedTransactions(bool rollback)
    {
        using var provider = new MySqlProvider<EmployeesDb>("Server=127.0.0.1;Port=1;User ID=unused;Pooling=false;Connection Timeout=1", "unused");
        using var failed = provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        await Assert.That(() => failed.ExecuteScalar("SELECT 1")).Throws<MySqlException>();
        await Assert.That(() => Complete(failed, rollback)).Throws<InvalidOperationException>();
        await Assert.That(failed.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await Assert.That(failed.DbTransaction).IsNull();
        await Assert.That(((IAsyncTransactionCompletion)failed).InitializationState).IsEqualTo(TransactionInitializationState.Failed);
        failed.Dispose();
        failed.Dispose();

        using var unused = provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        Complete(unused, rollback);
        await Assert.That(unused.Status).IsEqualTo(rollback ? DatabaseTransactionStatus.RolledBack : DatabaseTransactionStatus.Committed);
        await Assert.That(unused.DbTransaction).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task BorrowedCommandsRemainReusableAcrossCompletedTransactions(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(BorrowedCommandsRemainReusableAcrossCompletedTransactions),
            "CREATE TABLE items (id INT PRIMARY KEY)");
        using var provider = CreateProvider(schema);
        foreach (var asyncCommand in new[] { false, true })
        foreach (var kind in new[] { "scalar", "non_query", "reader" })
        foreach (var completion in new[] { "commit", "rollback", "dispose" })
        {
            using var command = new MySqlCommand(kind == "non_query" ? "UPDATE items SET id = id" : "SELECT 7 UNION ALL SELECT 8");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
                try
                {
                    await Execute(transaction, command, kind, asyncCommand);
                    await Assert.That(command.Transaction).IsNull();
                    await Assert.That(command.Connection).IsNull();
                    await Assert.That(transaction.DatabaseAccess.DbTransaction!.Connection!.State).IsEqualTo(ConnectionState.Open);
                    if (completion == "commit")
                    {
                        if (asyncCommand) await transaction.CommitAsyncCore(); else transaction.Commit();
                    }
                    if (completion == "rollback")
                    {
                        if (asyncCommand) await transaction.RollbackAsyncCore(); else transaction.Rollback();
                    }
                }
                finally
                {
                    if (asyncCommand) await transaction.DisposeAsyncCore(); else transaction.Dispose();
                }
                await Assert.That(command.CommandText).IsEqualTo(kind == "non_query" ? "UPDATE items SET id = id" : "SELECT 7 UNION ALL SELECT 8");
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task FailedCommandsReleaseBindingsAndLiveForeignTransactionsAreRejected(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(FailedCommandsReleaseBindingsAndLiveForeignTransactionsAreRejected));
        using var provider = CreateProvider(schema);
        foreach (var asyncCommand in new[] { false, true })
        foreach (var kind in new[] { "scalar", "non_query", "reader" })
        {
            using var command = new MySqlCommand("SELECT * FROM w2_missing_table");
            var failed = new Transaction(provider, TransactionType.ReadAndWrite);
            try
            {
                await Assert.That(() => Execute(failed, command, kind, asyncCommand)).Throws<MySqlException>();
                await Assert.That(command.Transaction).IsNull();
                await Assert.That(command.Connection).IsNull();
            }
            finally { await failed.DisposeAsyncCore(); }
            command.CommandText = "SELECT 7";
            using var next = new Transaction(provider, TransactionType.ReadAndWrite);
            await Execute(next, command, kind, asyncCommand);
        }

        using var first = new Transaction(provider, TransactionType.ReadAndWrite);
        first.DatabaseAccess.ExecuteScalar("SELECT 1");
        using var second = new Transaction(provider, TransactionType.ReadAndWrite);
        var native = (MySqlTransaction)first.DatabaseAccess.DbTransaction!;
        using var foreign = new MySqlCommand("SELECT 7", native.Connection, native);
        foreach (var asyncCommand in new[] { false, true })
        foreach (var kind in new[] { "scalar", "non_query", "reader" })
        {
            await Assert.That(() => Execute(second, foreign, kind, asyncCommand)).Throws<InvalidOperationException>();
            await Assert.That(foreign.Transaction).IsSameReferenceAs(native);
            await Assert.That(foreign.Connection).IsSameReferenceAs(native.Connection);
            await Assert.That(second.DatabaseAccess.DbTransaction).IsNull();
        }
    }

    private static async Task Execute(Transaction transaction, MySqlCommand command, string kind, bool asyncCommand)
    {
        var access = transaction.DatabaseAccess;
        if (kind == "scalar")
        {
            var value = asyncCommand ? await access.ExecuteScalarAsyncCore(command) : access.ExecuteScalar(command);
            await Assert.That(Convert.ToInt32(value)).IsEqualTo(7);
        }
        else if (kind == "non_query")
        {
            if (asyncCommand) await access.ExecuteNonQueryAsyncCore(command); else access.ExecuteNonQuery(command);
        }
        else if (asyncCommand)
        {
            var reader = await access.ExecuteReaderAsyncCore(command);
            try
            {
                await Assert.That(command.Transaction).IsSameReferenceAs(access.DbTransaction);
                await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
                await Assert.That(reader.GetInt32(0)).IsEqualTo(7);
            }
            finally { await reader.DisposeAsync(); }
        }
        else
        {
            using var reader = access.ExecuteReader(command);
            await Assert.That(command.Transaction).IsSameReferenceAs(access.DbTransaction);
            await Assert.That(reader.ReadNextRow()).IsTrue();
            await Assert.That(reader.GetInt32(0)).IsEqualTo(7);
        }
    }

    private static void Complete(DatabaseTransaction transaction, bool rollback)
    {
        if (rollback) transaction.Rollback(); else transaction.Commit();
    }

    private static SqlProvider<EmployeesDb> CreateProvider(ServerSchemaDatabase schema) => schema.Provider.DatabaseType == DatabaseType.MySQL
        ? new MySqlProvider<EmployeesDb>(schema.Connection.ConnectionString, schema.Connection.DataSourceName, DataLinqLoggingConfiguration.NullConfiguration)
        : new MariaDBProvider<EmployeesDb>(schema.Connection.ConnectionString, schema.Connection.DataSourceName, DataLinqLoggingConfiguration.NullConfiguration);
}
