using System;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class PublicStandaloneCommandTests
{
    private static SQLiteDatabaseTransaction Create(string connection = "Data Source=:memory:")
        => new(connection, TransactionType.ReadAndWrite, DataLinqLoggingConfiguration.NullConfiguration);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StandaloneAsyncCommandsShareNativeStateWithSynchronousCommandsAndBorrowReaders(bool borrowed)
    {
        await using var transaction = Create();
        IDatabaseAccess access = transaction;
        await access.ExecuteNonQueryAsync("CREATE TABLE items (id INTEGER)");
        await access.ExecuteNonQueryAsync("INSERT INTO items VALUES (1), (2)");
        await Assert.That(transaction.ExecuteScalar<long>("SELECT COUNT(*) FROM items")).IsEqualTo(2L);
        using var command = new SqliteCommand("SELECT id FROM items ORDER BY id");
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        await using var reader = await (borrowed ? access.ExecuteReaderAsync(command) : access.ExecuteReaderAsync(command.CommandText));
        await Assert.That(await reader.ReadNextRowAsync()).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
        await Assert.That(() => transaction.ExecuteScalar("SELECT 1")).Throws<InvalidOperationException>();
        await Assert.That(async () => await transaction.CommitAsync()).Throws<InvalidOperationException>();
        await Assert.That(await reader.ReadNextRowAsync()).IsTrue();
        await Assert.That(await reader.ReadNextRowAsync()).IsFalse();
        await Assert.That(async () => await transaction.DisposeAsync()).Throws<InvalidOperationException>();
        await reader.DisposeAsync();
        await Assert.That(disposals).IsEqualTo(0);
        await Assert.That(await access.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM items")).IsEqualTo(2L);
        await transaction.CommitAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StandaloneRawCancellationRejectsSyncAndAsyncReuseButPermitsRollback(bool sequence)
    {
        await using var transaction = Create();
        await transaction.ExecuteNonQueryAsync("CREATE TABLE items (id INTEGER)");
        await transaction.ExecuteNonQueryAsync("INSERT INTO items VALUES (1), (2)");
        Exception failure;
        if (sequence)
        {
            using var cancellation = new System.Threading.CancellationTokenSource();
            await using var rows = transaction.ReadReaderAsync("SELECT id FROM items", cancellation.Token).GetAsyncEnumerator();
            await Assert.That(await rows.MoveNextAsync()).IsTrue();
            await Assert.That(() => rows.Current.Dispose()).Throws<InvalidOperationException>();
            cancellation.Cancel();
            failure = (await Assert.That(async () => await rows.MoveNextAsync()).Throws<OperationCanceledException>())!;
        }
        else
        {
            await using var reader = await transaction.ExecuteReaderAsync("SELECT id FROM items");
            await Assert.That(await reader.ReadNextRowAsync()).IsTrue();
            failure = (await Assert.That(() => reader.ReadNextRowAsync(new(true))).Throws<OperationCanceledException>())!;
        }
        var context = DataLinqFailure.GetContext(failure)!;
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.Operation).IsEqualTo(DataLinqOperationKind.RawCommand);
        await Assert.That(context.RecoveryActions.HasFlag(DataLinqRecoveryActions.Rollback)).IsTrue();
        await Assert.That(context.RecoveryActions.HasFlag(DataLinqRecoveryActions.Continue)).IsFalse();
        await Assert.That(() => transaction.ExecuteScalar("SELECT 1")).Throws<InvalidOperationException>();
        await Assert.That(async () => await transaction.ExecuteScalarAsync("SELECT 1")).Throws<InvalidOperationException>();
        await Assert.That(() => transaction.Commit()).Throws<InvalidOperationException>();
        await Assert.That(async () => await transaction.CommitAsync()).Throws<InvalidOperationException>();
        await transaction.RollbackAsync();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
    }

    [Test]
    public async Task StandalonePreCancellationAndUnusedSequenceNeverInitializeResources()
    {
        await using var transaction = Create("invalid");
        await using (var unused = transaction.ReadReaderAsync("SELECT 1").GetAsyncEnumerator()) { }
        await Assert.That(() => transaction.ExecuteScalarAsync("SELECT 1", new(true))).Throws<OperationCanceledException>();
        await Assert.That(transaction.DbTransaction).IsNull();
    }
}
