using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Query;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteNativeAsyncCommandTests
{
    [Test]
    public async Task ValidationPrecedesCancellationAndDoesNotCreateDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w2_validation_{Guid.NewGuid():N}.db");
        var access = new SQLiteDbAccess(new SqliteConnectionStringBuilder { DataSource = path }.ConnectionString, DataLinqLoggingConfiguration.NullConfiguration);
        using var empty = new SqliteCommand();
        using var derived = new DerivedCommand { CommandText = "SELECT 1" };
        using var valid = new SqliteCommand("SELECT 1");
        await Assert.That(() => access.ExecuteScalarAsyncCore(empty, new(true))).Throws<InvalidOperationException>();
        await Assert.That(() => access.ExecuteScalarAsyncCore(derived, new(true))).Throws<NotSupportedException>();
        await Assert.That(() => access.ExecuteScalarAsyncCore(valid, new(true))).Throws<OperationCanceledException>();
        await Assert.That(valid.Connection).IsNull();
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ScalarAndNonQueryPreserveCastingNullsAndBorrowedCommandOwnership(bool memory)
    {
        using var fixture = new Fixture(memory);
        var access = fixture.Access;
        using var command = new SqliteCommand("UPDATE items SET value=@value WHERE id=1") { CommandTimeout = 7 };
        command.Parameters.AddWithValue("@value", 19);
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        await Assert.That(await access.ExecuteNonQueryAsyncCore(command)).IsEqualTo(1);
        await Assert.That(command.Connection).IsNull();
        await Assert.That(command.CommandTimeout).IsEqualTo(7);
        await Assert.That(disposals).IsEqualTo(0);
        command.Parameters[0].Value = 23;
        await Assert.That(await access.ExecuteNonQueryAsyncCore(command)).IsEqualTo(1);
        await Assert.That(await access.ExecuteScalarAsyncCore<long>("SELECT value FROM items WHERE id=1")).IsEqualTo(access.ExecuteScalar<long>("SELECT value FROM items WHERE id=1"));
        await Assert.That(await access.ExecuteScalarAsyncCore("SELECT NULL")).IsSameReferenceAs(DBNull.Value);
        await Assert.That(await access.ExecuteScalarAsyncCore("SELECT value FROM items WHERE id=99")).IsNull();
        await Assert.That(async () => { await access.ExecuteScalarAsyncCore<int>("SELECT value FROM items WHERE id=1"); }).Throws<InvalidCastException>();
        await Assert.That(async () => { await access.ExecuteScalarAsyncCore<int>("SELECT value FROM items WHERE id=99"); }).Throws<NullReferenceException>();
        await Assert.That(disposals).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EarlyAndCanceledReadersReleaseConnectionWithoutDisposingBorrowedCommand(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var command = new SqliteCommand("SELECT 1 UNION ALL SELECT 2");
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        for (var mode = 0; mode < 3; mode++)
        {
            var reader = await fixture.Access.ExecuteReaderAsyncCore(command);
            var connection = command.Connection!;
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
            if (mode == 0) reader.Dispose();
            else if (mode == 1) await reader.DisposeAsync();
            else await Assert.That(() => reader.ReadNextRowAsync(new(true))).Throws<OperationCanceledException>();
            await reader.DisposeAsync();
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Closed);
            await Assert.That(command.Connection).IsNull();
            await Assert.That(disposals).IsEqualTo(0);
        }
        var sequence = fixture.Access.ReadReaderAsyncCore(command);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            await foreach (var row in sequence) { await Assert.That(row.GetInt32(0)).IsEqualTo(1); break; }
            await Assert.That(command.Connection).IsNull();
        }
        await Assert.That(disposals).IsEqualTo(0);
        await Assert.That(await fixture.Access.ExecuteScalarAsyncCore<long>("SELECT 7")).IsEqualTo(7L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CapturedBinaryValuesAndReturnedBuffersRemainIndependent(bool memory)
    {
        using var fixture = new Fixture(memory);
        byte[] input = [1, 2, 3];
        var identifier = Guid.NewGuid();
        var captured = CapturedSql.Capture(new Sql("SELECT @value, X'', NULL, @identifier")
            .AddParameter("@value", input).AddParameter("@identifier", identifier));
        input[0] = 99;
        for (var repeat = 0; repeat < 2; repeat++)
        {
            var source = ((IAsyncSqlReaderFactory)fixture.Access).BindReader(captured);
            await using var reader = await source.OpenReaderAsync(default);
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            var first = ((IDataLinqOwnedBinaryBufferReader)reader).TakeOwnedBytes(0)!;
            first[1] = 99;
            await Assert.That(reader.GetBytes(0)).IsEquivalentTo(new byte[] { 1, 2, 3 });
            await Assert.That(reader.GetBytes(1)!).IsEmpty();
            await Assert.That(reader.GetBytes(2)).IsNull();
            await Assert.That(reader.GetString(3)).IsEqualTo(identifier.ToString("D"));
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeAndLoggerFailuresRetainIdentityAndLeaveCommandReusable(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var command = new SqliteCommand("SELECT missing_column FROM items");
        var native = await Assert.That(() => fixture.Access.ExecuteScalarAsyncCore(command)).Throws<SqliteException>();
        var context = ExecutionFailureContexts.Get(native!)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.CommandDispatch!.Dispatched).IsTrue();
        await Assert.That(command.Connection).IsNull();
        command.CommandText = "SELECT 7";
        var expected = new InvalidOperationException("SQL logger failed.");
        using var logger = new CallbackLogger(() => throw expected);
        var loggingAccess = new SQLiteDbAccess(fixture.ConnectionString, new(logger));
        var logged = await Assert.That(() => loggingAccess.ExecuteScalarAsyncCore(command)).Throws<InvalidOperationException>();
        await Assert.That(logged).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(logged!)!.CommandDispatch!.Dispatched).IsFalse();
        await Assert.That(ExecutionFailureContexts.Get(logged!)!.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(command.Connection).IsNull();
        await Assert.That(await fixture.Access.ExecuteScalarAsyncCore<long>(command)).IsEqualTo(7L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReaderCompletionObserverFailureSettlesUnreturnedResources(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var command = new SqliteCommand("SELECT 1 UNION ALL SELECT 2");
        using var parent = new Activity("sqlite-native-reader-observer").Start();
        var expected = new InvalidOperationException("Reader observer failed.");
        SqliteConnection? acquired = null;
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        using (var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataLinq",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                // The setup PRAGMA is separate and runs before this command is attached.
                if (activity.OperationName == "datalinq.db.command" && activity.ParentSpanId == parent.SpanId && command.Connection is { } connection)
                { acquired = connection; throw expected; }
            }
        })
        {
            ActivitySource.AddActivityListener(listener);
            var failure = await Assert.That(async () => { await fixture.Access.ExecuteReaderAsyncCore(command); }).Throws<InvalidOperationException>();
            await Assert.That(failure).IsSameReferenceAs(expected);
            await Assert.That(ExecutionFailureContexts.Get(failure!)!.CommandDispatch!.Dispatched).IsTrue();
        }
        await Assert.That(acquired!.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(command.Connection).IsNull();
        await Assert.That(disposals).IsEqualTo(0);
        await Assert.That(await fixture.Access.ExecuteScalarAsyncCore<long>(command)).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationInsideNativeStatementDoesNotUndoConfirmedWriteOrOffloadCall(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var cancellation = new CancellationTokenSource();
        using var command = new SqliteCommand("UPDATE items SET value=w2_cancel() WHERE id=1");
        var callerThread = Environment.CurrentManagedThreadId;
        var functionThread = 0;
        using var logger = new CallbackLogger(() => command.Connection!.CreateFunction("w2_cancel", () =>
        {
            functionThread = Environment.CurrentManagedThreadId;
            cancellation.Cancel();
            return 42;
        }));
        var access = new SQLiteDbAccess(fixture.ConnectionString, new(logger));
        var pending = access.ExecuteNonQueryAsyncCore(command, cancellation.Token);
        await Assert.That(pending.IsCompleted).IsTrue();
        await Assert.That(functionThread).IsEqualTo(callerThread);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(await pending).IsEqualTo(1);
        await Assert.That(await fixture.Access.ExecuteScalarAsyncCore<long>("SELECT value FROM items WHERE id=1")).IsEqualTo(42L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BusyWaitBlocksCallerAndPreservesNativeLockFailureDespiteCancellation(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var blocker = new SqliteConnection(fixture.ConnectionString);
        blocker.Open();
        using var held = blocker.BeginTransaction();
        using (var takeLock = new SqliteCommand("UPDATE items SET value=99 WHERE id=1", blocker, held)) takeLock.ExecuteNonQuery();
        using var cancellation = new CancellationTokenSource();
        using var command = new SqliteCommand("UPDATE items SET value=8 WHERE id=1") { CommandTimeout = 1 };
        var cancelThread = new Thread(() => { Thread.Sleep(100); cancellation.Cancel(); }) { IsBackground = true };
        var started = false;
        using var logger = new CallbackLogger(() => { started = true; cancelThread.Start(); });
        var access = new SQLiteDbAccess(fixture.ConnectionString, new(logger));
        try
        {
            var clock = Stopwatch.StartNew();
            var pending = access.ExecuteNonQueryAsyncCore(command, cancellation.Token);
            var blockedFor = clock.Elapsed;
            await Assert.That(pending.IsCompleted).IsTrue();
            await Assert.That(blockedFor >= TimeSpan.FromMilliseconds(800)).IsTrue();
            var failure = await Assert.That(() => pending).Throws<SqliteException>();
            await Assert.That(cancellation.IsCancellationRequested).IsTrue();
            await Assert.That(failure!.SqliteErrorCode is 5 or 6).IsTrue();
            await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
            await Assert.That(command.Connection).IsNull();
        }
        finally { if (started) cancelThread.Join(); }
        held.Rollback();
        await Assert.That(await fixture.Access.ExecuteScalarAsyncCore<long>("SELECT value FROM items WHERE id=1")).IsEqualTo(7L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AttachedTransactionIsRejectedBeforeCancellationAndRemainsOwnedByCaller(bool memory)
    {
        using var fixture = new Fixture(memory);
        using var connection = new SqliteConnection(fixture.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = new SqliteCommand("SELECT 7", connection, transaction);
        await Assert.That(() => fixture.Access.ExecuteScalarAsyncCore(command, new(true))).Throws<InvalidOperationException>();
        await Assert.That(command.Connection).IsSameReferenceAs(connection);
        await Assert.That(command.Transaction).IsSameReferenceAs(transaction);
        await Assert.That(command.ExecuteScalar()).IsEqualTo(7L);
        transaction.Rollback();
    }

    [Test]
    public async Task FailedOpenPreservesExistingCallerConnectionAndCommand()
    {
        var path = Path.Combine(Path.GetTempPath(), $"w2_missing_{Guid.NewGuid():N}.db");
        var access = new SQLiteDbAccess(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ConnectionString, DataLinqLoggingConfiguration.NullConfiguration);
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = new SqliteCommand("SELECT 7", connection);
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        var failure = await Assert.That(() => access.ExecuteScalarAsyncCore(command)).Throws<SqliteException>();
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.CommandDispatch!.Dispatched).IsFalse();
        await Assert.That(command.Connection).IsSameReferenceAs(connection);
        await Assert.That(command.ExecuteScalar()).IsEqualTo(7L);
        await Assert.That(disposals).IsEqualTo(0);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SetupObserverFailureOrCancellationDoesNotDispatchBusinessCommand(bool memory, bool cancel)
    {
        using var fixture = new Fixture(memory);
        using var command = new SqliteCommand("UPDATE items SET value=8 WHERE id=1");
        using var cancellation = new CancellationTokenSource();
        using var parent = new Activity("sqlite-native-setup").Start();
        var expected = new InvalidOperationException("Setup observer failed.");
        var observed = 0;
        using (var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DataLinq",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName != "datalinq.db.command" || activity.ParentSpanId != parent.SpanId) return;
                observed++;
                if (cancel) cancellation.Cancel();
                else throw expected;
            }
        })
        {
            ActivitySource.AddActivityListener(listener);
            Exception failure;
            if (cancel) failure = (await Assert.That(() => fixture.Access.ExecuteNonQueryAsyncCore(command, cancellation.Token)).Throws<OperationCanceledException>())!;
            else
            {
                failure = (await Assert.That(() => fixture.Access.ExecuteNonQueryAsyncCore(command)).Throws<InvalidOperationException>())!;
                await Assert.That(failure).IsSameReferenceAs(expected);
            }
            await Assert.That(ExecutionFailureContexts.Get(failure)!.CommandDispatch!.Dispatched).IsFalse();
            await Assert.That(command.Connection).IsNull();
        }
        await Assert.That(observed).IsEqualTo(1);
        await Assert.That(await fixture.Access.ExecuteScalarAsyncCore<long>("SELECT value FROM items WHERE id=1")).IsEqualTo(7L);
    }

    private sealed class DerivedCommand : SqliteCommand;

    private sealed class CallbackLogger(Action action) : ILoggerFactory, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => action();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string? path;
        private readonly SqliteConnection keeper;
        internal string ConnectionString { get; }
        internal SQLiteDbAccess Access { get; }

        internal Fixture(bool memory)
        {
            var name = $"w2_native_{Guid.NewGuid():N}";
            path = memory ? null : Path.Combine(Path.GetTempPath(), name + ".db");
            ConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path ?? name, Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
                Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private, Pooling = true, DefaultTimeout = 2
            }.ConnectionString;
            keeper = new SqliteConnection(ConnectionString);
            keeper.Open();
            using var create = new SqliteCommand("CREATE TABLE items (id INTEGER PRIMARY KEY, value INTEGER NOT NULL); INSERT INTO items VALUES (1,7)", keeper);
            create.ExecuteNonQuery();
            Access = new(ConnectionString, DataLinqLoggingConfiguration.NullConfiguration);
        }

        public void Dispose()
        {
            keeper.Dispose();
            SqliteConnection.ClearPool(keeper);
            if (path is not null)
            {
                File.Delete(path);
                File.Delete(path + "-wal");
                File.Delete(path + "-shm");
            }
        }
    }
}
