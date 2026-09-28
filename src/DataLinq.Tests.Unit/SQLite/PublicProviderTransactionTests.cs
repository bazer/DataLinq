using System;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class PublicProviderTransactionTests
{
    [Test]
    public async Task StandaloneOwnedReaderRetainsAdmissionThroughCommandCleanup()
    {
        await using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:");
        await using var transaction = provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        var command = new SqliteCommand();
        var rejected = false;
        command.Disposed += (_, _) =>
        {
            try { transaction.Commit(); }
            catch (InvalidOperationException) { rejected = true; }
        };
        using (var reader = StandaloneTransactionReader.Open(transaction, command, true,
            _ => provider.DatabaseAccess.ExecuteReader("SELECT 1")))
            await Assert.That(reader.ReadNextRow()).IsTrue();
        await Assert.That(rejected).IsTrue();
        await transaction.CommitAsync();
    }

    [Test]
    public async Task UnusedStandaloneCompletionNeverInitializesAndDefaultDisposalIsIdempotent()
    {
        foreach (var mode in new[] { "commit", "rollback", "dispose" })
        {
            DatabaseTransaction transaction = new SQLiteDatabaseTransaction("invalid", TransactionType.ReadAndWrite, DataLinqLoggingConfiguration.NullConfiguration);
            if (mode == "commit") await transaction.CommitAsync();
            if (mode == "rollback") await transaction.RollbackAsync();
            await ((IAsyncDisposable)transaction).DisposeAsync();
            transaction.Dispose();
            await transaction.DisposeAsync();
            await Assert.That(transaction.DbTransaction).IsNull();
        }
    }

    [Test]
    public async Task StandaloneReaderOwnsTheSlotAcrossMovesAndCompletionRejectsWithoutClosingIt()
    {
        await using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:");
        DatabaseTransaction transaction = provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        try
        {
            using (var reader = transaction.ExecuteReader("SELECT 1 UNION ALL SELECT 2"))
            {
                await Assert.That(reader.ReadNextRow()).IsTrue();
                var error = await Assert.That(() => transaction.CommitAsync()).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.RollbackAsync()).Throws<InvalidOperationException>();
                await Assert.That(async () => await transaction.DisposeAsync()).Throws<InvalidOperationException>();
                await Assert.That(transaction.Commit).Throws<InvalidOperationException>();
                await Assert.That(transaction.Dispose).Throws<InvalidOperationException>();
                await Assert.That(() => transaction.ExecuteScalar("SELECT 3")).Throws<InvalidOperationException>();
                var context = ExecutionFailureContexts.Get(error!)!;
                await Assert.That(context.TransactionId).IsNull();
                await Assert.That(context.ActiveOperation).IsEqualTo(ExecutionOperationKind.RawCommand);
                await Assert.That(reader.ReadNextRow()).IsTrue();
                await Assert.That(reader.GetInt32(0)).IsEqualTo(2);
                await Assert.That(reader.ReadNextRow()).IsFalse();
                // EOF alone does not dispose a directly acquired reader.
                await Assert.That(transaction.Commit).Throws<InvalidOperationException>();
            }
            await transaction.CommitAsync();
            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        }
        finally { await transaction.DisposeAsync(); }
    }

    [Test]
    public async Task ManagedProviderHandleRejectsDirectAsyncCompletionAndLeavesOwnerUsable()
    {
        await using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:");
        await using var managed = provider.StartTransaction();
        DatabaseTransaction handle = managed.DatabaseAccess;
        await Assert.That(() => handle.CommitAsync()).Throws<InvalidOperationException>();
        await Assert.That(() => handle.RollbackAsync()).Throws<InvalidOperationException>();
        await Assert.That(async () => await handle.DisposeAsync()).Throws<InvalidOperationException>();
        await Assert.That(handle.DbTransaction).IsNull();
        await managed.CommitAsync();
        await Assert.That(managed.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
    }

    [Test]
    public async Task CancellationBeforeStandaloneCommitLeavesWorkAvailableForRollback()
    {
        await using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:");
        provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE public_t05 (value INTEGER)");
        await using var transaction = provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        transaction.ExecuteNonQuery("INSERT INTO public_t05 VALUES (1)");
        await Assert.That(() => transaction.CommitAsync(new(true))).Throws<OperationCanceledException>();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await transaction.RollbackAsync();
        await Assert.That(provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM public_t05")).IsEqualTo(0L);
    }

    [Test]
    public async Task ConfirmedStandaloneCommitPreservesOutcomeWhenStatusObserverThrows()
    {
        await using var provider = new SQLiteProvider<EmployeesDb>("Data Source=:memory:");
        provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE public_t05 (value INTEGER)");
        await using var transaction = provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite);
        transaction.ExecuteNonQuery("INSERT INTO public_t05 VALUES (1)");
        var expected = new Exception("observer");
        transaction.OnStatusChanged += (_, _) => throw expected;
        var error = await Assert.That(() => transaction.CommitAsync()).Throws<Exception>();
        await Assert.That(error).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM public_t05")).IsEqualTo(1L);
    }
}
