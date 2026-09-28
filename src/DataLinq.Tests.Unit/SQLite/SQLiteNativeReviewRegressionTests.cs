using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteNativeReviewRegressionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectCompletionRejectsFailedInitializationButAllowsUnusedTransactions(bool rollback)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db")
        }.ConnectionString;
        using var failed = new SQLiteDatabaseTransaction(connectionString, TransactionType.ReadAndWrite, DataLinqLoggingConfiguration.NullConfiguration);
        await Assert.That(() => failed.ExecuteScalar("SELECT 1")).Throws<SqliteException>();
        await Assert.That(() => Complete(failed, rollback)).Throws<InvalidOperationException>();
        await Assert.That(failed.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await Assert.That(failed.DbTransaction).IsNull();
        await Assert.That(((IAsyncTransactionCompletion)failed).InitializationState).IsEqualTo(TransactionInitializationState.Failed);
        failed.Dispose();
        failed.Dispose();

        using var unused = new SQLiteDatabaseTransaction(connectionString, TransactionType.ReadAndWrite, DataLinqLoggingConfiguration.NullConfiguration);
        Complete(unused, rollback);
        await Assert.That(unused.Status).IsEqualTo(rollback ? DatabaseTransactionStatus.RolledBack : DatabaseTransactionStatus.Committed);
        await Assert.That(unused.DbTransaction).IsNull();
    }

    [Test]
    public async Task SynchronousTransactionInvokesCommandSubclassButAsyncStillRejectsIt()
    {
        using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:;Pooling=False");
        using var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        using var command = new CountingCommand { CommandText = "SELECT 7" };
        await Assert.That(() => transaction.DatabaseAccess.ExecuteScalarAsyncCore(command)).Throws<NotSupportedException>();
        await Assert.That(((IAsyncTransactionCompletion)transaction.DatabaseAccess).InitializationState).IsEqualTo(TransactionInitializationState.Unused);
        await Assert.That(command.ScalarCalls).IsEqualTo(0);
        await Assert.That(transaction.DatabaseAccess.ExecuteScalar<long>(command)).IsEqualTo(7L);
        await Assert.That(command.ScalarCalls).IsEqualTo(1);
        command.CommandText = "CREATE TABLE items (id INTEGER)";
        transaction.DatabaseAccess.ExecuteNonQuery(command);
        await Assert.That(command.NonQueryCalls).IsEqualTo(1);
        command.CommandText = "SELECT 8";
        using (var reader = transaction.DatabaseAccess.ExecuteReader(command))
        {
            await Assert.That(reader.ReadNextRow()).IsTrue();
            await Assert.That(reader.GetInt32(0)).IsEqualTo(8);
        }
        await Assert.That(command.ReaderCalls).IsEqualTo(1);
        await Assert.That(command.Connection).IsNull();
        await Assert.That(command.Transaction).IsNull();
        await Assert.That(() => transaction.DatabaseAccess.ExecuteNonQueryAsyncCore(command)).Throws<NotSupportedException>();
        await Assert.That(async () => { await transaction.DatabaseAccess.ExecuteReaderAsyncCore(command); }).Throws<NotSupportedException>();
        transaction.Commit();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedSynchronousAccessRejectsEveryCommandAfterRootDisposal(bool asyncDispose)
    {
        var path = Path.Combine(Path.GetTempPath(), $"w2_disposed_{Guid.NewGuid():N}.db");
        using var provider = new SQLiteProvider<EmployeesDb>(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        var access = provider.DatabaseAccess;
        if (asyncDispose) await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
        else provider.Dispose();
        File.Delete(path);
        try
        {
            using var command = new SqliteCommand("SELECT 1");
            await Assert.That(() => access.ExecuteScalar(command)).Throws<ObjectDisposedException>();
            await Assert.That(() => access.ExecuteScalar<long>(command)).Throws<ObjectDisposedException>();
            await Assert.That(() => access.ExecuteNonQuery(command)).Throws<ObjectDisposedException>();
            await Assert.That(() => { using var reader = access.ExecuteReader(command); }).Throws<ObjectDisposedException>();
            await Assert.That(() => access.ExecuteScalar("SELECT 1")).Throws<ObjectDisposedException>();
            await Assert.That(() => access.ExecuteScalar<long>("SELECT 1")).Throws<ObjectDisposedException>();
            await Assert.That(() => access.ExecuteNonQuery("CREATE TABLE items (id INTEGER)")).Throws<ObjectDisposedException>();
            await Assert.That(() => { using var reader = access.ExecuteReader("SELECT 1"); }).Throws<ObjectDisposedException>();
            await Assert.That(command.Connection).IsNull();
            await Assert.That(File.Exists(path)).IsFalse();
        }
        finally { File.Delete(path); }
    }

    private static void Complete(DatabaseTransaction transaction, bool rollback)
    {
        if (rollback) transaction.Rollback(); else transaction.Commit();
    }

    private sealed class CountingCommand : SqliteCommand
    {
        internal int ScalarCalls { get; private set; }
        internal int NonQueryCalls { get; private set; }
        internal int ReaderCalls { get; private set; }
        public override object? ExecuteScalar() { ScalarCalls++; return base.ExecuteScalar(); }
        public override int ExecuteNonQuery() { NonQueryCalls++; return base.ExecuteNonQuery(); }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            ReaderCalls++;
            return base.ExecuteDbDataReader(behavior);
        }
    }
}
