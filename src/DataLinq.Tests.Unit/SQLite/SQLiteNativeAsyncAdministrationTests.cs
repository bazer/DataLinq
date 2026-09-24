using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteNativeAsyncAdministrationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProbesRetainEffectiveIdentityAndReadFreshNames(bool memory)
    {
        using var fixture = new Fixture(memory);
        var provider = fixture.Provider;
        provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE \"odd'name\" (id INTEGER); CREATE VIEW a_view AS SELECT * FROM \"odd'name\"");
        await Assert.That(await provider.FileOrServerExistsAsyncCore()).IsEqualTo(provider.FileOrServerExists());
        await Assert.That(await provider.DatabaseExistsAsyncCore("ignored_identity")).IsEqualTo(provider.DatabaseExists("ignored_identity"));
        await Assert.That(await provider.TableExistsAsyncCore("odd'name", "ignored_identity")).IsTrue();
        await Assert.That(await provider.TableExistsAsyncCore("' OR 1=1 --")).IsFalse();
        await Assert.That(await provider.TableExistsAsyncCore("a_view")).IsFalse();
        await Assert.That(await provider.TableExistsAsyncCore("created_later")).IsFalse();
        provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE created_later(id INTEGER)");
        await Assert.That(await provider.TableExistsAsyncCore("created_later")).IsTrue();
        provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE created_later");
        await Assert.That(await provider.TableExistsAsyncCore("created_later")).IsFalse();
    }

    [Test]
    public async Task MissingFileProbesNeverRecreateDatabase()
    {
        using var fixture = new Fixture(false);
        fixture.ClearPool();
        File.Delete(fixture.Path!);
        await Assert.That(await fixture.Provider.FileOrServerExistsAsyncCore()).IsFalse();
        await Assert.That(await fixture.Provider.DatabaseExistsAsyncCore()).IsFalse();
        var failure = await Assert.That(async () => { await fixture.Provider.TableExistsAsyncCore("items"); }).Throws<SqliteException>();
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Operation).IsEqualTo(ExecutionOperationKind.ExistenceCheck);
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(File.Exists(fixture.Path)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidationAndRootLifetimePrecedeCancellation(bool memory)
    {
        using var fixture = new Fixture(memory);
        var provider = fixture.Provider;
        var access = provider.DatabaseAccess;
        await Assert.That(async () => { await provider.TableExistsAsyncCore("", token: new(true)); }).Throws<ArgumentNullException>();
        await Assert.That(async () => { await provider.SetJournalModeAsyncCore((SQLiteJournalMode)999, new(true)); }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => { await provider.DatabaseExistsAsyncCore(token: new(true)); }).Throws<OperationCanceledException>();
        await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
        provider.Dispose();
        await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
        await Assert.That(async () => { await provider.DatabaseExistsAsyncCore(token: new(true)); }).Throws<ObjectDisposedException>();
        await Assert.That(async () => { await provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL, new(true)); }).Throws<ObjectDisposedException>();
        await Assert.That(() => access.ExecuteScalarAsyncCore("SELECT 1", new(true))).Throws<ObjectDisposedException>();
        await Assert.That(() => provider.GetNewDatabaseTransaction(TransactionType.ReadAndWrite)).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task JournalSetterConfirmsExecutionWithoutPromisingEffectiveMode(bool memory)
    {
        using var fixture = new Fixture(memory);
        var provider = fixture.Provider;
        foreach (var mode in new[] { SQLiteJournalMode.DELETE, SQLiteJournalMode.WAL })
        {
            await provider.SetJournalModeAsyncCore(mode);
            var actual = provider.DatabaseAccess.ExecuteScalar<string>("PRAGMA journal_mode");
            await Assert.That(actual).IsEqualTo(memory ? "memory" : mode.ToString().ToLowerInvariant());
        }
        await Assert.That(async () => { await provider.SetJournalModeAsyncCore(SQLiteJournalMode.DELETE, new(true)); }).Throws<OperationCanceledException>();
        await Assert.That(provider.DatabaseAccess.ExecuteScalar<string>("PRAGMA journal_mode")).IsEqualTo(memory ? "memory" : "wal");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LoggerFailuresRemainFailuresAndSettleResources(bool memory)
    {
        using var logger = new CallbackLogger();
        using var fixture = new Fixture(memory, new(logger));
        var expected = new InvalidOperationException("Administrative logger failed.");
        logger.Callback = _ => throw expected;
        var probe = await Assert.That(async () => { await fixture.Provider.TableExistsAsyncCore("items"); }).Throws<InvalidOperationException>();
        await Assert.That(probe).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(probe!)!.Operation).IsEqualTo(ExecutionOperationKind.ExistenceCheck);
        await Assert.That(ExecutionFailureContexts.Get(probe!)!.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        var setter = await Assert.That(async () => { await fixture.Provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL); }).Throws<InvalidOperationException>();
        await Assert.That(setter).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(setter!)!.Operation).IsEqualTo(ExecutionOperationKind.ProviderConfiguration);
        logger.Callback = null;
        await Assert.That(await fixture.Provider.TableExistsAsyncCore("items")).IsTrue();
        await fixture.Provider.SetJournalModeAsyncCore(SQLiteJournalMode.WAL);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionKeepsConnectionButDoesNotConsumeBorrowedCommands(bool memory)
    {
        using var fixture = new Fixture(memory);
        var plan = ((IAsyncExistenceProbeSource)fixture.Provider).CaptureExistenceProbe(new(ExistenceProbeKind.Table, null, "items"));
        var session = plan.CreateSession!();
        using var command = new SqliteCommand("SELECT 1 UNION ALL SELECT 2");
        var disposals = 0;
        command.Disposed += (_, _) => disposals++;
        SqliteConnection? connection = null;
        try
        {
            await session.OpenAsync(default);
            await Assert.That(await session.Access.ExecuteScalarAsync(command, default)).IsEqualTo(1L);
            await Assert.That(command.Connection).IsNull();
            await using (var reader = await session.Access.ExecuteReaderAsync(command, default))
            {
                connection = command.Connection!;
                await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            }
            await Assert.That(command.Connection).IsNull();
            await Assert.That(connection!.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(await session.Access.ExecuteScalarAsync(command, default)).IsEqualTo(1L);
        }
        finally { await session.DisposeAsync(); }
        await Assert.That(connection!.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(disposals).IsEqualTo(0);
        await Assert.That(await fixture.Provider.DatabaseAccess.ExecuteScalarAsyncCore<long>(command)).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RootDisposalDoesNotDrainDependentTransactionOrReader(bool memory)
    {
        using var fixture = new Fixture(memory);
        var transaction = new Transaction(fixture.Provider, TransactionType.ReadAndWrite);
        var reader = await fixture.Provider.DatabaseAccess.ExecuteReaderAsyncCore("SELECT 1 UNION ALL SELECT 2");
        try
        {
            await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 7");
            var connection = transaction.DatabaseAccess.DbTransaction!.Connection!;
            await ((IAsyncRootDisposal)fixture.Provider).DisposeAsyncCore();
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            await transaction.RollbackAsyncCore();
        }
        finally { await reader.DisposeAsync(); await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProvisioningPreservesCapturedScriptAndPartialDdl(bool memory)
    {
        var name = $"w2_provision_{Guid.NewGuid():N}";
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name + ".db");
        var options = new SqliteConnectionStringBuilder
        {
            DataSource = memory ? name : path, Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWrite,
            Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private, Pooling = true
        };
        var factory = new SqlFromSQLiteFactory();
        var sql = new Sql("CREATE TABLE created(id INTEGER); INSERT INTO created VALUES(1)");
        try
        {
            await Assert.That(async () => { await factory.CreateDatabaseAsyncCore(sql, name, options.ConnectionString, true, new(true)); }).Throws<OperationCanceledException>();
            await Assert.That(File.Exists(path)).IsFalse();
            var plan = ((IAsyncSqlProvisioningFactory)factory).CaptureProvisioning(new(sql.Text, name, options.ConnectionString, true));
            sql.AddText("; INVALID SQL");
            await using (var session = plan.CreateSession())
            {
                plan.Validate();
                await session.InitializeAsync(default);
                await new OwnedCommandExecution(session.Access, session.CommandFactory).ExecuteNonQueryAsync(default);
            }
            // With no provider yet, successful memory provisioning must retain a
            // fallback keeper until a real owning root adopts that lifetime.
            using var provider = new SQLiteProvider<EmployeesDb>(options.ConnectionString, name);
            await Assert.That(await provider.TableExistsAsyncCore("created")).IsTrue();
            await Assert.That(provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM created")).IsEqualTo(1L);
            var failure = await Assert.That(async () =>
            {
                await factory.CreateDatabaseAsyncCore(new Sql("CREATE TABLE kept_prefix(id INTEGER); INSERT INTO missing VALUES(1); CREATE TABLE skipped_suffix(id INTEGER)"),
                    name, options.ConnectionString, true);
            }).Throws<SqliteException>();
            await Assert.That(ExecutionFailureContexts.Get(failure!)!.Operation).IsEqualTo(ExecutionOperationKind.Provisioning);
            await Assert.That(await provider.TableExistsAsyncCore("kept_prefix")).IsTrue();
            await Assert.That(await provider.TableExistsAsyncCore("skipped_suffix")).IsFalse();
            await ((IAsyncRootDisposal)provider).DisposeAsyncCore();
            using var pool = new SqliteConnection(options.ConnectionString);
            SqliteConnection.ClearPool(pool);
            if (memory)
            {
                pool.Open();
                using var probe = new SqliteCommand("SELECT COUNT(*) FROM sqlite_master WHERE name='created'", pool);
                await Assert.That(probe.ExecuteScalar()).IsEqualTo(0L);
            }
        }
        finally
        {
            ClearExecutionPools(options.ConnectionString);
            File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
        }
    }

    [Test]
    public async Task SharedKeepersReleaseOnlyAfterLastOwnerInMixedDisposalModes()
    {
        using var fixture = new Fixture(true);
        using var second = new SQLiteProvider<EmployeesDb>(fixture.Provider.ConnectionString);
        await ((IAsyncRootDisposal)fixture.Provider).DisposeAsyncCore();
        fixture.ClearPool();
        await Assert.That(await second.TableExistsAsyncCore("items")).IsTrue();
        second.Dispose();
        await ((IAsyncRootDisposal)second).DisposeAsyncCore();
        fixture.ClearPool();
        using var probe = new SqliteConnection(fixture.Provider.ConnectionString);
        probe.Open();
        using var command = new SqliteCommand("SELECT COUNT(*) FROM sqlite_master WHERE name='items'", probe);
        await Assert.That(command.ExecuteScalar()).IsEqualTo(0L);
    }

    [Test]
    public async Task ConcurrentLastReleaseAndReacquisitionSettleEveryKeeper()
    {
        var options = new SqliteConnectionStringBuilder
        {
            DataSource = $"w2_keeper_race_{Guid.NewGuid():N}", Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared, Pooling = true
        };
        // Either generation may win when the last owner releases. The acquired
        // generation must stay owned, and no orphan keeper may survive its root.
        for (var iteration = 0; iteration < 32; iteration++)
        {
            using var first = new SQLiteProvider<EmployeesDb>(options.ConnectionString);
            using var start = new ManualResetEventSlim();
            var release = Task.Run(async () => { start.Wait(); await ((IAsyncRootDisposal)first).DisposeAsyncCore(); });
            var acquire = Task.Run(() => { start.Wait(); return new SQLiteProvider<EmployeesDb>(options.ConnectionString); });
            start.Set();
            using var second = await acquire;
            await release;
            second.DatabaseAccess.ExecuteNonQuery("CREATE TABLE marker(id INTEGER)");
            ClearExecutionPools(options.ConnectionString);
            await Assert.That(await second.TableExistsAsyncCore("marker")).IsTrue();
            await ((IAsyncRootDisposal)second).DisposeAsyncCore();
            ClearExecutionPools(options.ConnectionString);
            using var probe = new SqliteConnection(options.ConnectionString);
            probe.Open();
            using var command = new SqliteCommand("SELECT COUNT(*) FROM sqlite_master WHERE name='marker'", probe);
            await Assert.That(command.ExecuteScalar()).IsEqualTo(0L);
        }
        ClearExecutionPools(options.ConnectionString);
    }

    private sealed class CallbackLogger : ILoggerFactory, ILogger
    {
        internal Action<string>? Callback;
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Callback?.Invoke(formatter(state, exception));
    }

    private static void ClearExecutionPools(string connectionString)
    {
        using var pool = new SqliteConnection(connectionString);
        SqliteConnection.ClearPool(pool);
        var options = new SqliteConnectionStringBuilder(connectionString);
        if (options.Mode == SqliteOpenMode.Memory) return;
        // Read-only probes use a different pool key. Clear both fixture-owned
        // pools before deleting the file; pooling remains enabled throughout.
        options.Mode = SqliteOpenMode.ReadOnly;
        using var readOnlyPool = new SqliteConnection(options.ConnectionString);
        SqliteConnection.ClearPool(readOnlyPool);
    }

    private sealed class Fixture : IDisposable
    {
        internal string? Path { get; }
        internal SQLiteProvider<EmployeesDb> Provider { get; }
        internal Fixture(bool memory, DataLinqLoggingConfiguration? logging = null)
        {
            var name = $"w2_admin_{Guid.NewGuid():N}";
            Path = memory ? null : System.IO.Path.Combine(System.IO.Path.GetTempPath(), name + ".db");
            Provider = new(new SqliteConnectionStringBuilder
            {
                DataSource = Path ?? name, Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
                Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private, Pooling = true, DefaultTimeout = 2
            }.ConnectionString, logging);
            Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE items(id INTEGER)");
        }
        internal void ClearPool() => ClearExecutionPools(Provider.ConnectionString);
        public void Dispose()
        {
            Provider.Dispose(); ClearPool();
            if (Path is null) return;
            File.Delete(Path); File.Delete(Path + "-wal"); File.Delete(Path + "-shm");
        }
    }
}
