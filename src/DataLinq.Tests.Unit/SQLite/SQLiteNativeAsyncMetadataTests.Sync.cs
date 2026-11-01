using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;
using DataLinq.Testing;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public sealed partial class SQLiteNativeAsyncMetadataTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMetadata_ObservationalReadMatchesAsyncMetadataAndWarnings(bool memory)
    {
        using var fixture = new Fixture(memory, Schema);
        var warnings = new List<string>();
        var actual = fixture.Provider.ReadValidationMetadataCore(log: warnings.Add).ValueOrException();
        var syncWarnings = warnings.ToArray();
        warnings.Clear();
        var expected = (await fixture.Provider.ReadValidationMetadataAsyncCore(log: warnings.Add)).ValueOrException();
        await Assert.That(MetadataEquivalenceDigest.CreateText(actual)).IsEqualTo(MetadataEquivalenceDigest.CreateText(expected));
        await Assert.That(syncWarnings.SequenceEqual(warnings)).IsTrue();
        await Assert.That(actual.IsFrozen).IsTrue();
        await Assert.That(actual.DbName).IsEqualTo(fixture.Name);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMetadata_ExistingEmptyDatabaseAndFreshReadsPreserveRootIdentity(bool memory)
    {
        using var fixture = new Fixture(memory);
        var empty = fixture.Provider.ReadValidationMetadataCore().ValueOrException();
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE fresh_table(id INTEGER PRIMARY KEY)");
        var fresh = fixture.Provider.ReadValidationMetadataCore().ValueOrException();
        await Assert.That(empty.TableModels).IsEmpty();
        await Assert.That(fresh.TableModels.Single().Table.DbName).IsEqualTo("fresh_table");
        fixture.ClearPools();
        await Assert.That(fixture.Provider.TableExists("fresh_table")).IsTrue();
        fixture.Provider.Dispose();
        await Assert.That(() => fixture.Provider.ReadValidationMetadataCore()).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMetadata_EveryCommandUsesTimeoutAndOneOwnedConnection(bool memory)
    {
        using var fixture = new Fixture(memory, Schema);
        foreach (var timeout in new TimeSpan?[] { null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1250) })
        {
            var settings = MetadataReadSettings.Runtime(timeout, null);
            var plan = ((ISyncProviderMetadataSource)fixture.Provider).CaptureValidationMetadata(settings);
            using var session = new RecordingSyncSession(plan.CreateSession());
            fixture.Logger.Callback = session.RecordDispatch;
            var context = new SyncMetadataReadContext(session, settings.CommandTimeoutSeconds, fixture.Provider.TelemetryInstanceId);
            var failures = new ExecutionFailures();
            try
            {
                session.Open();
                _ = plan.Read(context).ValueOrException();
            }
            finally
            {
                fixture.Logger.Callback = null;
                context.Close(failures);
                session.Dispose();
            }
            failures.ThrowIfAny();
            await Assert.That(session.Commands.Count).IsGreaterThanOrEqualTo(10);
            await Assert.That(session.Commands[0].Sql).IsEqualTo("PRAGMA read_uncommitted = false;");
            await Assert.That(session.Commands.Select(command => command.Connection).Distinct().Count()).IsEqualTo(1);
            foreach (var command in session.Commands)
            {
                await Assert.That(command.Timeout).IsEqualTo(timeout is null ? 7 : timeout == TimeSpan.Zero ? 0 : 2);
                await Assert.That(command.Connection.State).IsEqualTo(ConnectionState.Closed);
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMetadata_CallbackFailureIsNotDriftAndDoesNotBorrowApplicationTransaction(bool memory)
    {
        using var fixture = new Fixture(memory, Schema);
        using var transaction = fixture.Provider.StartTransaction();
        _ = transaction.DatabaseAccess.ExecuteScalar("SELECT 7");
        var connection = transaction.DatabaseAccess.DbTransaction!.Connection!;
        var original = new InvalidOperationException("metadata callback");
        await Assert.That(Failure(fixture.Provider.ReadValidationMetadataCore(log: _ => throw original))).IsSameReferenceAs(original);
        await Assert.That(ExecutionFailureContexts.Get(original)!.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(Convert.ToInt64(transaction.DatabaseAccess.ExecuteScalar("SELECT 8"))).IsEqualTo(8L);
        await Assert.That(fixture.Provider.ReadValidationMetadataCore().ValueOrException().TableModels.Length).IsEqualTo(3);
        transaction.Rollback();
    }

    [Test]
    public async Task SyncMetadata_MissingFileIsNeverCreatedOrReportedEmpty()
    {
        using var fixture = new Fixture(false);
        fixture.ClearPools();
        File.Delete(fixture.Path!);
        var failure = Failure(fixture.Provider.ReadValidationMetadataCore());
        await Assert.That(failure).IsTypeOf<SqliteException>();
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(File.Exists(fixture.Path)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMetadata_NativeLockTimeoutSettlesReaderAndScalar(bool memory)
    {
        using var fixture = new Fixture(memory, "CREATE TABLE blocked_row(id INTEGER PRIMARY KEY, value INTEGER); INSERT INTO blocked_row VALUES(1,1)");
        fixture.Provider.SetJournalMode(DataLinq.SQLite.SQLiteJournalMode.DELETE);
        foreach (var scalar in new[] { true, false })
        {
            var settings = MetadataReadSettings.Runtime(TimeSpan.FromSeconds(1), null);
            using var session = ((ISyncProviderMetadataSource)fixture.Provider).CaptureValidationMetadata(settings).CreateSession();
            var context = new SyncMetadataReadContext(session, settings.CommandTimeoutSeconds, fixture.Provider.TelemetryInstanceId);
            var failures = new ExecutionFailures();
            using var blocker = new SqliteConnection(fixture.ConnectionString);
            blocker.Open();
            try
            {
                session.Open();
                using (var lockCommand = new SqliteCommand("BEGIN EXCLUSIVE; UPDATE blocked_row SET value=2 WHERE id=1", blocker)) lockCommand.ExecuteNonQuery();
                var elapsed = Stopwatch.StartNew();
                var failure = await Assert.That(() =>
                {
                    if (scalar) context.ExecuteScalar(new Sql("SELECT value FROM blocked_row WHERE id=1"));
                    else context.Read(new Sql("SELECT value FROM blocked_row WHERE id=1"), row => row.GetInt32(0));
                }).Throws<SqliteException>();
                await Assert.That(failure!.SqliteErrorCode is 5 or 6).IsTrue();
                await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
                await Assert.That(elapsed.ElapsedMilliseconds).IsGreaterThanOrEqualTo(750);
            }
            finally
            {
                context.Close(failures);
                session.Dispose();
                using var rollback = new SqliteCommand("ROLLBACK", blocker);
                rollback.ExecuteNonQuery();
            }
            await Assert.That(failures.Primary).IsNotNull();
            await Assert.That(fixture.Provider.ReadValidationMetadataCore().ValueOrException().TableModels.Length).IsEqualTo(1);
        }
    }

    private sealed class RecordingSyncSession(ISyncMetadataSession inner) : ISyncMetadataSession
    {
        private IDbCommand? current;
        private bool disposed;
        internal readonly List<(string Sql, int Timeout, IDbConnection Connection)> Commands = [];
        internal void RecordDispatch()
        {
            var command = current ?? throw new InvalidOperationException("A metadata command escaped its context.");
            Commands.Add((command.CommandText, command.CommandTimeout, command.Connection!));
        }
        public void Validate(int? seconds) => inner.Validate(seconds);
        public void Open() => inner.Open();
        public IDbCommand CreateCommand(CapturedSql sql) => inner.CreateCommand(sql);
        public IDataLinqDataReader ExecuteReader(IDbCommand command)
        { current = command; try { return inner.ExecuteReader(command); } finally { current = null; } }
        public object? ExecuteScalar(IDbCommand command)
        { current = command; try { return inner.ExecuteScalar(command); } finally { current = null; } }
        public void Dispose() { if (disposed) return; disposed = true; inner.Dispose(); }
    }
}
