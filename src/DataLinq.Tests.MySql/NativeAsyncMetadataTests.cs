using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Execution;
using DataLinq.Metadata;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.Query;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using MySqlConnector;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Tests.MySql;

public sealed class NativeAsyncMetadataTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task AsyncImportMatchesSynchronousMetadataAndSqlRoundtrip(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(AsyncImportMatchesSynchronousMetadataAndSqlRoundtrip), Schema);
        var warnings = new List<string>();
        var options = new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true, DeclareEnumsInClass = true, Log = warnings.Add };
        var factory = MetadataFromSqlFactory.GetSqlFactory(options, descriptor.DatabaseType);
        var expected = factory.ParseDatabase("MetadataDb", "MetadataDb", "Tests", schema.Connection.DataSourceName, schema.Connection.ConnectionString).ValueOrException();
        var expectedWarnings = warnings.ToArray();
        warnings.Clear();
        var actual = (await Import(factory, schema)).ValueOrException();
        await Assert.That(MetadataEquivalenceDigest.CreateText(actual)).IsEqualTo(MetadataEquivalenceDigest.CreateText(expected));
        await Assert.That(warnings.OrderBy(x => x).ToArray()).IsEquivalentTo(expectedWarnings.OrderBy(x => x).ToArray());
        await Assert.That(actual.IsFrozen).IsTrue();
        var sql = SqlFromMetadataFactory.GetFactoryFromDatabaseType(descriptor.DatabaseType).GetCreateTables(actual, false).ValueOrException();
        using var roundtrip = ServerSchemaDatabase.Create(descriptor, nameof(AsyncImportMatchesSynchronousMetadataAndSqlRoundtrip) + "_roundtrip", sql.Text);
        var reread = (await Import(factory, roundtrip)).ValueOrException();
        await Assert.That(MetadataRoundtripComparison.CompareSupportedSubset(actual, reread, descriptor.DatabaseType)).IsEmpty();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ImportCapturesIncludeAndRetainsMissingObjectAndUnsupportedTypeFailures(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ImportCapturesIncludeAndRetainsMissingObjectAndUnsupportedTypeFailures),
            "CREATE TABLE included (id INT PRIMARY KEY); CREATE TABLE excluded (id INT PRIMARY KEY); CREATE TABLE unsupported (shape GEOMETRY)");
        var include = new List<string> { "included" };
        var factory = MetadataFromSqlFactory.GetSqlFactory(new() { Include = include }, descriptor.DatabaseType);
        var pending = Import(factory, schema);
        include.Clear();
        include.Add("excluded");
        await Assert.That((await pending).ValueOrException().TableModels.Single().Table.DbName).IsEqualTo("included");
        await Assert.That((await Import(factory, schema)).ValueOrException().TableModels.Single().Table.DbName).IsEqualTo("excluded");
        include[0] = "missing";
        var missing = await Import(factory, schema);
        await Assert.That(missing.TryUnwrap(out _, out var missingFailure)).IsFalse();
        await Assert.That(missingFailure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        include[0] = "unsupported";
        var unsupported = await Import(factory, schema);
        await Assert.That(unsupported.TryUnwrap(out _, out var unsupportedFailure)).IsFalse();
        await Assert.That(unsupportedFailure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(unsupportedFailure.ToString()!).Contains("geometry");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RuntimeReadsEffectiveSchemaFreshAndAllowsExistingEmptyDatabase(TestProviderDescriptor descriptor)
    {
        using var configured = ServerSchemaDatabase.Create(descriptor, nameof(RuntimeReadsEffectiveSchemaFreshAndAllowsExistingEmptyDatabase), "CREATE TABLE wrong_database (id INT PRIMARY KEY)");
        using var effective = ServerSchemaDatabase.Create(descriptor, nameof(RuntimeReadsEffectiveSchemaFreshAndAllowsExistingEmptyDatabase) + "_effective");
        using var provider = Provider(descriptor, configured.Connection.ConnectionString, effective.Connection.DataSourceName);
        var empty = (await provider.ReadValidationMetadataAsyncCore()).ValueOrException();
        await Assert.That(empty.IsFrozen).IsTrue();
        await Assert.That(empty.DbName).IsEqualTo(effective.Connection.DataSourceName);
        await Assert.That(empty.TableModels).IsEmpty();
        var importEmpty = await Import(MetadataFromSqlFactory.GetSqlFactory(new(), descriptor.DatabaseType), effective);
        await Assert.That(importEmpty.TryUnwrap(out _, out _)).IsFalse();
        effective.ExecuteNonQuery("CREATE TABLE right_database (id INT PRIMARY KEY)");
        var live = (await provider.ReadValidationMetadataAsyncCore()).ValueOrException();
        await Assert.That(live.TableModels.Single().Table.DbName).IsEqualTo("right_database");
        await Assert.That(empty.TableModels).IsEmpty();
        effective.ExecuteNonQuery($"DROP DATABASE {SqlIdentifier.Quote(effective.Connection.DataSourceName, "`")}");
        var missing = await provider.ReadValidationMetadataAsyncCore();
        await Assert.That(missing.TryUnwrap(out _, out _)).IsFalse();
        await Assert.That(await provider.DatabaseExistsAsyncCore()).IsFalse();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task EveryNativeMetadataCommandReceivesCapturedTimeout(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(EveryNativeMetadataCommandReceivesCapturedTimeout), Schema);
        var builder = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString) { DefaultCommandTimeout = 7 };
        using var provider = Provider(descriptor, builder.ConnectionString, schema.Connection.DataSourceName);
        foreach (var timeout in new TimeSpan?[] { null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1250) })
        {
            var settings = MetadataReadSettings.Runtime(timeout, null);
            var plan = ((IAsyncProviderMetadataSource)provider).CaptureValidationMetadata(settings);
            await using var session = plan.CreateSession();
            var access = new RecordingAccess(session.Access);
            var context = new MetadataReadContext(access, session.Commands, settings.CommandTimeoutSeconds, default);
            var failures = new ExecutionFailures();
            try
            {
                await session.OpenAsync(default);
                _ = (await plan.ReadAsync(context, default)).ValueOrException();
            }
            finally { await context.CloseAsync(failures); }
            failures.ThrowIfAny();
            await Assert.That(access.Commands.Count).IsGreaterThanOrEqualTo(10);
            await Assert.That(access.Scalars).IsEqualTo(1);
            foreach (var command in access.Commands)
            {
                // Read after actual dispatch: null must inherit the native connection's default.
                await Assert.That(command.CommandTimeout).IsEqualTo(timeout is null ? 7 : timeout == TimeSpan.Zero ? 0 : 2);
                await Assert.That(command.Connection).IsNotNull();
            }
            await Assert.That(access.Commands.Select(command => command.Connection).Distinct().Count()).IsEqualTo(1);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task MetadataSessionTimeoutInterruptsNativeScalarAndReaderAndReleasesPool(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(MetadataSessionTimeoutInterruptsNativeScalarAndReaderAndReleasesPool),
            "CREATE TABLE blocked_row (id INT PRIMARY KEY, value INT); INSERT INTO blocked_row VALUES (1, 1)");
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        await using var blocker = new MySqlConnection(schema.Connection.ConnectionString);
        await blocker.OpenAsync();
        await using var locked = await blocker.BeginTransactionAsync();
        await using (var update = new MySqlCommand("UPDATE blocked_row SET value=2 WHERE id=1", blocker, locked)) await update.ExecuteNonQueryAsync();
        foreach (var scalar in new[] { true, false })
        {
            var settings = MetadataReadSettings.Runtime(TimeSpan.FromSeconds(1), null);
            var session = ((IAsyncProviderMetadataSource)provider).CaptureValidationMetadata(settings).CreateSession();
            var context = new MetadataReadContext(session.Access, session.Commands, settings.CommandTimeoutSeconds, default,
                new(ExecutionOperationKind.MetadataRead, provider.TelemetryInstanceId));
            var failures = new ExecutionFailures();
            try
            {
                await session.OpenAsync(default);
                var sql = new Sql("SELECT value FROM blocked_row WHERE id=1 FOR UPDATE");
                var failure = await Assert.That(async () =>
                {
                    if (scalar) await context.ExecuteScalarAsync(sql);
                    else await context.ReadAsync(sql, row => row.GetInt32(0));
                }).Throws<MySqlException>();
                await Assert.That(failure!.ErrorCode).IsEqualTo(MySqlErrorCode.CommandTimeoutExpired);
                await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
            }
            finally { await context.CloseAsync(failures); await session.DisposeAsync(); }
            await Assert.That(failures.Primary).IsNotNull();
            await Assert.That((await provider.ReadValidationMetadataAsyncCore()).ValueOrException().TableModels.Length).IsEqualTo(1);
        }
        await locked.RollbackAsync();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CancellationDuringPoolWaitAndParsingDoesNotPublishOrBorrowTransaction(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(CancellationDuringPoolWaitAndParsingDoesNotPublishOrBorrowTransaction), Schema);
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        var transaction = provider.StartTransaction();
        try
        {
            await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 1");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pending = provider.ReadValidationMetadataAsyncCore(token: cancellation.Token);
            await Assert.That(pending.IsCompleted).IsFalse();
            cancellation.Cancel();
            await Assert.That(async () => { await pending; }).Throws<OperationCanceledException>();
            await Assert.That(Convert.ToInt32(await transaction.DatabaseAccess.ExecuteScalarAsyncCore("SELECT 2"))).IsEqualTo(2);
        }
        finally { await transaction.DisposeAsyncCore(); }
        using var parsingCancellation = new CancellationTokenSource();
        var observedWarning = false;
        await Assert.That(async () => { await provider.ReadValidationMetadataAsyncCore(log: _ => { observedWarning = true; parsingCancellation.Cancel(); }, token: parsingCancellation.Token); }).Throws<OperationCanceledException>();
        await Assert.That(observedWarning).IsTrue();
        await Assert.That((await provider.ReadValidationMetadataAsyncCore()).ValueOrException().TableModels.Length).IsEqualTo(3);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task OperationalAndLoggerFailuresRetainOriginalCauseAndReleaseOwnedSession(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(OperationalAndLoggerFailuresRetainOriginalCauseAndReleaseOwnedSession), Schema);
        using var provider = Provider(descriptor, schema.Connection.ConnectionString, schema.Connection.DataSourceName);
        var expected = new InvalidOperationException("Metadata warning observer failed.");
        var failure = Failure(await provider.ReadValidationMetadataAsyncCore(log: _ => throw expected));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That((await provider.ReadValidationMetadataAsyncCore()).ValueOrException().TableModels.Length).IsEqualTo(3);
        var invalid = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString) { UserID = "w2_metadata_missing_user", Password = "invalid" };
        var factory = MetadataFromSqlFactory.GetSqlFactory(new(), descriptor.DatabaseType);
        var auth = Failure(await factory.ParseDatabaseAsyncCore("MetadataDb", "MetadataDb", "Tests", schema.Connection.DataSourceName, invalid.ConnectionString));
        await Assert.That(auth).IsTypeOf<MySqlException>();
        await Assert.That(ExecutionFailureContexts.Get(auth)!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(ExecutionFailureContexts.Get(auth)!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
    }

    [Test]
    public async Task InvalidTimeoutAndDisposedProviderPrecedeCancellation()
    {
        using var provider = new MySqlProvider<EmployeesDb>("Server=127.0.0.1;Port=1;User ID=unused;Pooling=false", "unused");
        await Assert.That(async () => { await provider.ReadValidationMetadataAsyncCore(TimeSpan.FromSeconds(-1), token: new(true)); }).Throws<ArgumentOutOfRangeException>();
        await Assert.That(async () => { await provider.ReadValidationMetadataAsyncCore(token: new(true)); }).Throws<OperationCanceledException>();
        provider.Dispose();
        await Assert.That(async () => { await provider.ReadValidationMetadataAsyncCore(token: new(true)); }).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task DerivedImportFactoryIsRejectedBeforeCancellationOrConnectionParsing()
    {
        foreach (IMetadataFromSqlFactory factory in new IMetadataFromSqlFactory[] { new CustomMySqlFactory(), new CustomMariaDbFactory() })
            await Assert.That(async () => { await factory.ParseDatabaseAsyncCore("Db", "Db", "Tests", "db", "invalid connection string", new(true)); })
                .Throws<NotSupportedException>();
    }

    private sealed class CustomMySqlFactory() : MetadataFromMySqlFactory(new())
    {
        public override Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) =>
            throw new InvalidOperationException("A synchronous override must never be used as an async fallback.");
    }

    private sealed class CustomMariaDbFactory() : MetadataFromMariaDBFactory(new())
    {
        public override Option<DatabaseDefinition, IDLOptionFailure> ParseDatabase(string name, string csTypeName, string csNamespace, string dbName, string connectionString) =>
            throw new InvalidOperationException("A synchronous override must never be used as an async fallback.");
    }

    private static Task<Option<DatabaseDefinition, IDLOptionFailure>> Import(IMetadataFromSqlFactory factory, ServerSchemaDatabase schema) =>
        factory.ParseDatabaseAsyncCore("MetadataDb", "MetadataDb", "Tests", schema.Connection.DataSourceName, schema.Connection.ConnectionString);
    private static Exception Failure(Option<DatabaseDefinition, IDLOptionFailure> result) =>
        !result.TryUnwrap(out _, out var failure) && failure.FailureValue is Exception exception ? exception : throw new InvalidOperationException("Expected an operational failure.");
    private static SqlProvider<EmployeesDb> Provider(TestProviderDescriptor descriptor, string connectionString, string database) => descriptor.DatabaseType == DatabaseType.MySQL
        ? new MySqlProvider<EmployeesDb>(Pool(connectionString), database)
        : new MariaDBProvider<EmployeesDb>(Pool(connectionString), database);
    private static string Pool(string connectionString) => new MySqlConnectionStringBuilder(connectionString) { Pooling = true, MaximumPoolSize = 1, ConnectionTimeout = 5 }.ConnectionString;

    private sealed class RecordingAccess(IAsyncDatabaseAccess inner) : IAsyncDatabaseAccess
    {
        internal readonly List<IDbCommand> Commands = [];
        internal int Scalars;
        public void ValidateCommand(IDbCommand command, AsyncCommandKind kind) => inner.ValidateCommand(command, kind);
        public void ValidateReader(IDbCommand command) => inner.ValidateReader(command);
        public async Task<IAsyncDataReader> ExecuteReaderAsync(IDbCommand command, CancellationToken token)
        { var result = await inner.ExecuteReaderAsync(command, token); Commands.Add(command); return result; }
        public async Task<object?> ExecuteScalarAsync(IDbCommand command, CancellationToken token)
        { var result = await inner.ExecuteScalarAsync(command, token); Commands.Add(command); Scalars++; return result; }
        public Task<int> ExecuteNonQueryAsync(IDbCommand command, CancellationToken token) => throw new InvalidOperationException("Metadata must remain observational.");
    }

    private static readonly string[] Schema =
    [
        """
        CREATE TABLE `order-header` (
            `tenant` INT NOT NULL, `number` INT NOT NULL, `title` VARCHAR(80) NOT NULL DEFAULT 'anonymous',
            `status` ENUM('active','hold') NOT NULL DEFAULT 'active', `public_id` BINARY(16) NULL,
            `amount` DECIMAL(10,2) NOT NULL DEFAULT 1.25, `created` TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
            `computed` INT GENERATED ALWAYS AS (`number` + 1) VIRTUAL,
            PRIMARY KEY (`tenant`, `number`), UNIQUE INDEX `uq_header_title` (`title`),
            INDEX `ix_header_prefix` (`title`(5)), CONSTRAINT `ck_amount` CHECK (`amount` >= 0)
        ) COMMENT='header comment'
        """,
        """
        CREATE TABLE `order_line` (
            `id` INT PRIMARY KEY AUTO_INCREMENT, `tenant` INT NOT NULL, `number` INT NOT NULL,
            `payload` VARBINARY(8) NULL, `date` DATE NULL,
            CONSTRAINT `fk_line_header` FOREIGN KEY (`tenant`, `number`) REFERENCES `order-header` (`tenant`, `number`) ON UPDATE CASCADE ON DELETE RESTRICT
        )
        """,
        "CREATE VIEW `header_view` AS SELECT tenant, number, title FROM `order-header`"
    ];
}
