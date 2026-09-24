using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.SQLite;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public sealed class SQLiteNativeAsyncMetadataTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ImportMatchesSynchronousDigestWarningsAndSqlRoundtrip(bool memory)
    {
        using var fixture = new Fixture(memory, Schema);
        var warnings = new List<string>();
        var factory = new MetadataFromSQLiteFactory(new() { CapitaliseNames = true, Log = warnings.Add });
        var expected = factory.ParseDatabase("MetadataDb", "MetadataDb", "Tests", fixture.Name, fixture.ConnectionString).ValueOrException();
        var expectedWarnings = warnings.ToArray();
        warnings.Clear();
        var actual = (await fixture.Import(factory)).ValueOrException();
        await Assert.That(actual.IsFrozen).IsTrue();
        await Assert.That(MetadataEquivalenceDigest.CreateText(actual)).IsEqualTo(MetadataEquivalenceDigest.CreateText(expected));
        await Assert.That(((ViewDefinition)actual.TableModels.Single(x => x.Table.DbName == "header_view").Table).Definition)
            .IsEqualTo("SELECT tenant, number, title FROM \"order-header\"");
        await Assert.That(warnings.OrderBy(x => x).ToArray()).IsEquivalentTo(expectedWarnings.OrderBy(x => x).ToArray());
        await Assert.That(warnings.Count).IsGreaterThanOrEqualTo(4);
        var sql = new SqlFromSQLiteFactory().GetCreateTables(actual, false).ValueOrException();
        using var recreated = new Fixture(memory, sql.Text);
        var reread = (await recreated.Import(factory)).ValueOrException();
        await Assert.That(MetadataRoundtripComparison.CompareSupportedSubset(actual, reread, DatabaseType.SQLite)).IsEmpty();
        var sourceRoundtrip = MetadataSourceRoundtrip.ParseGeneratedModelSource(actual);
        await Assert.That(MetadataEquivalenceDigest.CreateText(sourceRoundtrip, includeDatabaseStorageName: false))
            .IsEqualTo(MetadataEquivalenceDigest.CreateText(actual, includeDatabaseStorageName: false));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CapturedImportSettingsRemainIndependentAndModelFailuresStayExplicit(bool memory)
    {
        using var fixture = new Fixture(memory, "CREATE TABLE included(id INTEGER PRIMARY KEY); CREATE TABLE excluded(id INTEGER PRIMARY KEY); CREATE TABLE unsupported(shape GEOMETRY)");
        var include = new List<string> { "included" };
        var options = new MetadataFromDatabaseFactoryOptions { Include = include };
        var factory = new MetadataFromSQLiteFactory(options);
        var settings = MetadataReadSettings.Import(options);
        var plan = ((IAsyncMetadataFactory)factory).CaptureImport(new("MetadataDb", "MetadataDb", "Tests", fixture.Name, fixture.ConnectionString, settings));
        include[0] = "excluded";
        options.CapitaliseNames = true;
        var captured = (await ReadPlan(plan, settings)).ValueOrException();
        await Assert.That(captured.TableModels.Single().Table.DbName).IsEqualTo("included");
        await Assert.That(captured.TableModels.Single().Model.CsType.Name).IsEqualTo("included");
        await Assert.That((await fixture.Import(factory)).ValueOrException().TableModels.Single().Table.DbName).IsEqualTo("excluded");
        include[0] = "missing";
        await Assert.That((await fixture.Import(factory)).TryUnwrap(out _, out var missing)).IsFalse();
        await Assert.That(missing.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        include[0] = "unsupported";
        await Assert.That((await fixture.Import(factory)).TryUnwrap(out _, out var unsupported)).IsFalse();
        await Assert.That(unsupported.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(unsupported.ToString()!).Contains("geometry");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeReadsEffectiveIdentityFreshAndAllowsExistingEmptyDatabase(bool memory)
    {
        using var fixture = new Fixture(memory);
        var empty = (await fixture.Provider.ReadValidationMetadataAsyncCore()).ValueOrException();
        await Assert.That(empty.IsFrozen).IsTrue();
        await Assert.That(empty.DbName).IsEqualTo(fixture.Name);
        await Assert.That(empty.TableModels).IsEmpty();
        await Assert.That((await fixture.Import(new(new()))).TryUnwrap(out _, out _)).IsFalse();
        fixture.Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE right_database(id INTEGER PRIMARY KEY)");
        var fresh = (await fixture.Provider.ReadValidationMetadataAsyncCore()).ValueOrException();
        await Assert.That(fresh.TableModels.Single().Table.DbName).IsEqualTo("right_database");
        await Assert.That(empty.TableModels).IsEmpty();
        // Metadata execution must neither release the root's keeper nor rewrite
        // the named-memory identity from its configured, different data source.
        fixture.ClearPools();
        await Assert.That(await fixture.Provider.TableExistsAsyncCore("right_database")).IsTrue();
        await Assert.That(async () => { await fixture.Provider.ReadValidationMetadataAsyncCore(TimeSpan.FromSeconds(-1), token: new(true)); }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => { await fixture.Provider.ReadValidationMetadataAsyncCore(token: new(true)); }).Throws<OperationCanceledException>();
        await ((IAsyncRootDisposal)fixture.Provider).DisposeAsyncCore();
        await Assert.That(async () => { await fixture.Provider.ReadValidationMetadataAsyncCore(token: new(true)); }).Throws<ObjectDisposedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EveryNativeMetadataCommandReceivesCapturedTimeoutAtDispatch(bool memory)
    {
        using var fixture = new Fixture(memory, Schema);
        foreach (var timeout in new TimeSpan?[] { null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1250) })
        {
            var settings = MetadataReadSettings.Runtime(timeout, null);
            var plan = ((IAsyncProviderMetadataSource)fixture.Provider).CaptureValidationMetadata(settings);
            var session = plan.CreateSession();
            var access = new RecordingAccess(session.Access);
            fixture.Logger.Callback = access.RecordDispatch;
            var context = new MetadataReadContext(access, session.Commands, settings.CommandTimeoutSeconds, default);
            var failures = new ExecutionFailures();
            try
            {
                await session.OpenAsync(default);
                _ = (await plan.ReadAsync(context, default)).ValueOrException();
            }
            finally
            {
                fixture.Logger.Callback = null;
                await context.CloseAsync(failures);
                await session.DisposeAsync();
            }
            failures.ThrowIfAny();
            await Assert.That(access.Commands.Count).IsGreaterThanOrEqualTo(10);
            await Assert.That(access.Scalars).IsEqualTo(1);
            await Assert.That(access.Commands[0].Sql).IsEqualTo("PRAGMA read_uncommitted = false;");
            foreach (var command in access.Commands)
            {
                // Captured after connection binding, before native dispatch.
                // After settlement SQLite detaches commands, changing defaults.
                await Assert.That(command.Timeout).IsEqualTo(timeout is null ? 7 : timeout == TimeSpan.Zero ? 0 : 2);
                await Assert.That(command.Connection.State).IsEqualTo(ConnectionState.Closed);
            }
            await Assert.That(access.Commands.Select(command => command.Connection).Distinct().Count()).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MetadataScalarAndReaderHonorNativeLockTimeoutAndSettleSession(bool memory)
    {
        using var fixture = new Fixture(memory, "CREATE TABLE blocked_row(id INTEGER PRIMARY KEY, value INTEGER); INSERT INTO blocked_row VALUES(1, 1)");
        await fixture.Provider.SetJournalModeAsyncCore(SQLiteJournalMode.DELETE);
        foreach (var scalar in new[] { true, false })
        {
            var settings = MetadataReadSettings.Runtime(TimeSpan.FromSeconds(1), null);
            var session = ((IAsyncProviderMetadataSource)fixture.Provider).CaptureValidationMetadata(settings).CreateSession();
            var context = new MetadataReadContext(session.Access, session.Commands, settings.CommandTimeoutSeconds, default);
            var failures = new ExecutionFailures();
            using var blocker = new SqliteConnection(fixture.ConnectionString);
            blocker.Open();
            try
            {
                await session.OpenAsync(default);
                using (var lockCommand = new SqliteCommand("BEGIN EXCLUSIVE; UPDATE blocked_row SET value=2 WHERE id=1", blocker)) lockCommand.ExecuteNonQuery();
                var elapsed = Stopwatch.StartNew();
                var failure = await Assert.That(async () =>
                {
                    if (scalar) await context.ExecuteScalarAsync(new Sql("SELECT value FROM blocked_row WHERE id=1"));
                    else await context.ReadAsync(new Sql("SELECT value FROM blocked_row WHERE id=1"), row => row.GetInt32(0));
                }).Throws<SqliteException>();
                await Assert.That(failure!.SqliteErrorCode is 5 or 6).IsTrue();
                await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
                await Assert.That(elapsed.ElapsedMilliseconds).IsGreaterThanOrEqualTo(750);
            }
            finally
            {
                await context.CloseAsync(failures);
                await session.DisposeAsync();
                using var rollback = new SqliteCommand("ROLLBACK", blocker);
                rollback.ExecuteNonQuery();
            }
            await Assert.That(failures.Primary).IsNotNull();
            await Assert.That((await fixture.Provider.ReadValidationMetadataAsyncCore()).ValueOrException().TableModels.Length).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationAndLoggerFailurePublishNoMetadataAndDoNotBorrowTransaction(bool memory)
    {
        using var fixture = new Fixture(memory, Schema);
        using var transaction = fixture.Provider.StartTransaction();
        await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 7");
        var transactionConnection = transaction.DatabaseAccess.DbTransaction!.Connection!;
        using var cancellation = new CancellationTokenSource();
        var warned = false;
        await Assert.That(async () =>
        {
            await fixture.Provider.ReadValidationMetadataAsyncCore(log: _ => { warned = true; cancellation.Cancel(); }, token: cancellation.Token);
        }).Throws<OperationCanceledException>();
        await Assert.That(warned).IsTrue();
        var expected = new InvalidOperationException("Metadata warning failed.");
        var failure = Failure(await fixture.Provider.ReadValidationMetadataAsyncCore(log: _ => throw expected));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That((await fixture.Provider.ReadValidationMetadataAsyncCore()).ValueOrException().TableModels.Length).IsEqualTo(3);
        await Assert.That(transactionConnection.State).IsEqualTo(ConnectionState.Open);
        await Assert.That(Convert.ToInt64(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 8"))).IsEqualTo(8L);
        await transaction.RollbackAsyncCore();
    }

    [Test]
    public async Task MissingFileNeverBecomesAnEmptyOrRecreatedDatabase()
    {
        using var fixture = new Fixture(false);
        fixture.ClearPools();
        File.Delete(fixture.Path!);
        await Assert.That(async () => { await fixture.Import(new(new()), new(true)); }).Throws<OperationCanceledException>();
        var failure = Failure(await fixture.Provider.ReadValidationMetadataAsyncCore());
        await Assert.That(failure).IsTypeOf<SqliteException>();
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(Failure(await fixture.Import(new(new())))).IsTypeOf<SqliteException>();
        await Assert.That(File.Exists(fixture.Path)).IsFalse();
    }

    [Test]
    public async Task DerivedImportFactoryIsRejectedBeforeCancellationAndConnectionParsing()
    {
        await Assert.That(async () => { await new DerivedFactory().ParseDatabaseAsyncCore("Db", "Db", "Tests", "db", "invalid connection string", new(true)); })
            .Throws<NotSupportedException>();
    }

    private sealed class DerivedFactory() : MetadataFromSQLiteFactory(new());

    private static async Task<Option<DatabaseDefinition, IDLOptionFailure>> ReadPlan(IAsyncMetadataReadPlan plan, MetadataReadSettings settings)
    {
        plan.Validate();
        await using var session = plan.CreateSession();
        var context = new MetadataReadContext(session.Access, session.Commands, settings.CommandTimeoutSeconds, default);
        var failures = new ExecutionFailures();
        try
        {
            await session.OpenAsync(default);
            return await plan.ReadAsync(context, default);
        }
        finally { await context.CloseAsync(failures); failures.ThrowIfAny(); }
    }

    private static Exception Failure(Option<DatabaseDefinition, IDLOptionFailure> result) =>
        !result.TryUnwrap(out _, out var failure) && failure.FailureValue is Exception exception ? exception : throw new InvalidOperationException("Expected operational failure.");

    private sealed class RecordingAccess(IAsyncDatabaseAccess inner) : IAsyncDatabaseAccess
    {
        private IDbCommand? current;
        internal readonly List<(string Sql, int Timeout, IDbConnection Connection)> Commands = [];
        internal int Scalars;
        internal void RecordDispatch()
        {
            var command = current ?? throw new InvalidOperationException("A metadata command escaped its context.");
            Commands.Add((command.CommandText, command.CommandTimeout, command.Connection ?? throw new InvalidOperationException("Missing native connection.")));
        }
        public void ValidateCommand(IDbCommand command, AsyncCommandKind kind) => inner.ValidateCommand(command, kind);
        public void ValidateReader(IDbCommand command) => inner.ValidateReader(command);
        public async Task<IAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken token)
        { current = command; try { return await inner.ExecuteReaderAsync(command, token); } finally { current = null; } }
        public async Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken token)
        { current = command; Scalars++; try { return await inner.ExecuteScalarAsync(command, token); } finally { current = null; } }
        public Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken token) => throw new InvalidOperationException("Metadata must remain observational.");
    }

    private sealed class CallbackLogger : ILoggerFactory, ILogger
    {
        internal Action? Callback;
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Callback?.Invoke();
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly string Name = $"w2_metadata_{Guid.NewGuid():N}";
        internal string? Path { get; }
        internal CallbackLogger Logger { get; } = new();
        internal SQLiteProvider<EmployeesDb> Provider { get; }
        internal string ConnectionString => Provider.ConnectionString;
        internal Fixture(bool memory, string? schema = null)
        {
            Path = memory ? null : System.IO.Path.Combine(System.IO.Path.GetTempPath(), Name + ".db");
            var options = new SqliteConnectionStringBuilder
            {
                DataSource = Path ?? "configured_" + Name, Mode = memory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
                Cache = memory ? SqliteCacheMode.Shared : SqliteCacheMode.Private, Pooling = true, DefaultTimeout = 7
            };
            Provider = new(options.ConnectionString, Name, new(Logger));
            try { if (schema is not null) Provider.DatabaseAccess.ExecuteNonQuery(schema); }
            catch { Dispose(); throw; }
        }
        internal Task<Option<DatabaseDefinition, IDLOptionFailure>> Import(MetadataFromSQLiteFactory factory, CancellationToken token = default) =>
            factory.ParseDatabaseAsyncCore("MetadataDb", "MetadataDb", "Tests", Name, ConnectionString, token);
        internal void ClearPools()
        {
            using var pool = new SqliteConnection(ConnectionString);
            SqliteConnection.ClearPool(pool);
            if (Path is null) return;
            using var readOnlyPool = new SqliteConnection(new SqliteConnectionStringBuilder(ConnectionString) { Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
            SqliteConnection.ClearPool(readOnlyPool);
        }
        public void Dispose()
        {
            Logger.Callback = null;
            Provider.Dispose(); ClearPools(); Logger.Dispose();
            if (Path is null) return;
            File.Delete(Path); File.Delete(Path + "-wal"); File.Delete(Path + "-shm");
        }
    }

    private const string Schema = """
        CREATE TABLE "order-header" (
            "tenant" INTEGER NOT NULL, "number" INTEGER NOT NULL, "title" TEXT NOT NULL DEFAULT 'anonymous',
            "amount" REAL NOT NULL DEFAULT 1.25, "payload" BLOB DEFAULT X'0123', "created_at" TEXT DEFAULT CURRENT_TIMESTAMP,
            PRIMARY KEY("tenant", "number"), UNIQUE("title")
        );
        CREATE TABLE "order_line" (
            "id" INTEGER PRIMARY KEY AUTOINCREMENT, "tenant" INTEGER NOT NULL, "number" INTEGER NOT NULL,
            "note" TEXT DEFAULT 'a''b',
            FOREIGN KEY("tenant", "number") REFERENCES "order-header"("tenant", "number") ON UPDATE CASCADE ON DELETE RESTRICT
        );
        CREATE INDEX "ix_line" ON "order_line"("tenant", "number");
        CREATE INDEX "ix_partial" ON "order_line"("tenant") WHERE tenant > 0;
        CREATE INDEX "ix_expression" ON "order-header"(lower(title));
        CREATE INDEX "ix_descending" ON "order-header"(title DESC);
        CREATE VIEW "header_view" AS SELECT tenant, number, title FROM "order-header";
        """;
}
