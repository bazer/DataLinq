using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.Query;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DataLinq.Tests.MySql;

public sealed class NativeAsyncCommandTests
{
    [Test]
    public async Task ValidationPrecedesCancellationWithoutOpeningOrMutatingCommands()
    {
        await using var source = new MySqlDataSourceBuilder("Server=127.0.0.1;Port=1;User ID=unused;Pooling=false").Build();
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var unsupported = new UnsupportedCommand();
        foreach (var kind in new[] { "scalar", "non_query", "reader" })
        {
            await Assert.That(() => Execute(access, unsupported, kind, canceled.Token)).Throws<NotSupportedException>();
            using var command = new MySqlCommand("SELECT 1");
            await Assert.That(() => Execute(access, command, kind, canceled.Token)).Throws<OperationCanceledException>();
            await Assert.That(command.Connection).IsNull();
            await Assert.That(command.CommandText).IsEqualTo("SELECT 1");
            using var empty = new MySqlCommand(" ");
            await Assert.That(() => Execute(access, empty, kind, canceled.Token)).Throws<InvalidOperationException>();
            await Assert.That(empty.Connection).IsNull();
        }
    }

    [Test]
    public async Task ForeignProviderParametersAreRejectedBeforeCancellationOrAcquisition()
    {
        await using var source = new MySqlDataSourceBuilder("Server=127.0.0.1;Port=1;User ID=unused;Pooling=false").Build();
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        var sql = CapturedSql.Capture(new Sql("SELECT @value", new CloneableForeignParameter { Value = 7 }));
        var bound = ((IAsyncSqlReaderFactory)access).BindReader(sql);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(bound.Validate).Throws<NotSupportedException>();
        await Assert.That(async () => { await bound.OpenReaderAsync(cancellation.Token); }).Throws<NotSupportedException>();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ScalarAndNonQueryMatchSynchronousResultsAndPreserveBorrowedCommands(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(ScalarAndNonQueryMatchSynchronousResultsAndPreserveBorrowedCommands),
            "CREATE TABLE items (id INT PRIMARY KEY, value INT NOT NULL)");
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        await Assert.That(await access.ExecuteNonQueryAsyncCore("INSERT INTO items VALUES (1, 10)")).IsEqualTo(1);
        using var update = new MySqlCommand("UPDATE items SET value = @value WHERE id = 1");
        update.Parameters.AddWithValue("@value", 20);
        await Assert.That(await access.ExecuteNonQueryAsyncCore(update)).IsEqualTo(1);
        await Assert.That(update.Connection!.State).IsEqualTo(ConnectionState.Closed);
        update.Parameters[0].Value = 30;
        await Assert.That(access.ExecuteNonQuery(update)).IsEqualTo(1);
        using var select = new MySqlCommand("SELECT value FROM items WHERE id = 1");
        await Assert.That(await access.ExecuteScalarAsyncCore<int>(select)).IsEqualTo(access.ExecuteScalar<int>(select));
        await Assert.That(await access.ExecuteScalarAsyncCore<int>("SELECT value FROM items WHERE id = 1")).IsEqualTo(30);
        await Assert.That(select.Connection!.State).IsEqualTo(ConnectionState.Closed);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task EarlyReaderDisposalReleasesSingleConnectionPoolAndKeepsCommandReusable(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(EarlyReaderDisposalReleasesSingleConnectionPoolAndKeepsCommandReusable));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        using var command = new MySqlCommand("SELECT 1 UNION ALL SELECT 2");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var reader = await access.ExecuteReaderAsyncCore(command);
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Open);
            if (attempt == 1) reader.Dispose();
            else await reader.DisposeAsync();
            await reader.DisposeAsync();
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
            await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore("SELECT 7"))).IsEqualTo(7);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CapturedBinaryParametersAndOwnedReaderPreserveIndependentBuffers(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(CapturedBinaryParametersAndOwnedReaderPreserveIndependentBuffers));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        byte[] input = [1, 2, 3];
        var sql = CapturedSql.Capture(new Sql("SELECT @value").AddParameter("@value", input));
        var bound = ((IAsyncSqlReaderFactory)access).CaptureInvocation().BindReader(sql);
        input[0] = 99;
        await using (var reader = await bound.OpenReaderAsync(default))
        {
            await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
            var first = ((IDataLinqOwnedBinaryBufferReader)reader).TakeOwnedBytes(0)!;
            first[1] = 99;
            var second = reader.GetBytes(0)!;
            await Assert.That(second[0]).IsEqualTo((byte)1);
            await Assert.That(second[1]).IsEqualTo((byte)2);
        }
        await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore("SELECT 7"))).IsEqualTo(7);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task DeferredReaderEnumerationCanRepeatAndBreakWithoutLeaking(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(DeferredReaderEnumerationCanRepeatAndBreakWithoutLeaking));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        using var command = new MySqlCommand("SELECT 1 UNION ALL SELECT 2");
        var sequence = access.ReadReaderAsyncCore(command);
        await Assert.That(command.Connection).IsNull();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await foreach (var row in sequence)
            {
                await Assert.That(row.GetInt32(0)).IsEqualTo(1);
                break;
            }
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        }
        var sum = 0;
        await foreach (var row in access.ReadReaderAsyncCore("SELECT 1 UNION ALL SELECT 2")) sum += row.GetInt32(0);
        await Assert.That(sum).IsEqualTo(3);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CancelingAPoolWaitDoesNotDispatchOrDisposeTheOtherReader(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(CancelingAPoolWaitDoesNotDispatchOrDisposeTheOtherReader));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        await using var reader = await access.ExecuteReaderAsyncCore("SELECT 1 UNION ALL SELECT 2");
        using var waiting = new MySqlCommand("SELECT 3");
        using var cancellation = new CancellationTokenSource();
        var operation = access.ExecuteScalarAsyncCore(waiting, cancellation.Token);
        await Assert.That(operation.IsCompleted).IsFalse();
        cancellation.Cancel();
        var failure = await Assert.That(async () => await operation).Throws<OperationCanceledException>();
        await Assert.That(waiting.Connection).IsNull();
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.CommandDispatch!.Dispatched).IsFalse();
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(1);
        await reader.DisposeAsync();
        await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore(waiting))).IsEqualTo(3);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CancellationBetweenRowsCleansUpBeforeReturningFailure(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(CancellationBetweenRowsCleansUpBeforeReturningFailure));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        using var command = new MySqlCommand("SELECT 1 UNION ALL SELECT 2");
        await using var reader = await access.ExecuteReaderAsyncCore(command);
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var failure = await Assert.That(() => reader.ReadNextRowAsync(cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(ExecutionFailureContexts.Get(failure!)!.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore("SELECT 7"))).IsEqualTo(7);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ProviderErrorsRetainClassificationAndReleaseConnections(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(ProviderErrorsRetainClassificationAndReleaseConnections));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        foreach (var kind in new[] { "scalar", "non_query", "reader" })
        {
            using var command = new MySqlCommand("SELECT invalid syntax for async execution");
            var failure = await Assert.That(() => Execute(access, command, kind)).Throws<MySqlException>();
            var context = ExecutionFailureContexts.Get(failure!)!;
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
            await Assert.That(context.TransactionId).IsNull();
            await Assert.That(context.ProviderInstanceId).IsNull();
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
            command.CommandText = "SELECT 7";
            await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore(command))).IsEqualTo(7);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task LoggingFailuresDoNotDispatchAndPreserveOriginalException(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(LoggingFailuresDoNotDispatchAndPreserveOriginalException));
        await using var source = CreateSource(schema);
        using var logger = new ThrowingLogger();
        var access = new SqlDbAccess(source, new DataLinqLoggingConfiguration(logger));
        foreach (var kind in new[] { "scalar", "non_query", "reader" })
        {
            using var command = new MySqlCommand("SELECT 7");
            logger.Enabled = true;
            var failure = await Assert.That(() => Execute(access, command, kind)).Throws<InvalidOperationException>();
            await Assert.That(ReferenceEquals(failure, logger.Failure)).IsTrue();
            var context = ExecutionFailureContexts.Get(failure!)!;
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ApplicationError);
            await Assert.That(context.CommandDispatch!.Dispatched).IsFalse();
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
            logger.Enabled = false;
            await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore(command))).IsEqualTo(7);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ObserverFailureAfterReaderAcquisitionCleansUpUnreturnedNativeResources(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(ObserverFailureAfterReaderAcquisitionCleansUpUnreturnedNativeResources));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        using var command = new MySqlCommand("SELECT 1 UNION ALL SELECT 2");
        using var parent = new Activity("native-async-observer-test").Start();
        var expected = new InvalidOperationException("Injected reader completion observer failure.");
        using (var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == "DataLinq",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "datalinq.db.command" && activity.ParentSpanId == parent.SpanId)
                    throw expected;
            }
        })
        {
            ActivitySource.AddActivityListener(listener);
            var failure = await Assert.That(async () => { await access.ExecuteReaderAsyncCore(command); }).Throws<InvalidOperationException>();
            await Assert.That(ReferenceEquals(failure, expected)).IsTrue();
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
            await Assert.That(ExecutionFailureContexts.Get(failure!)!.CommandDispatch!.Dispatched).IsTrue();
        }
        await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore(command))).IsEqualTo(1);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task CommandTimeoutRemainsDistinctFromCallerCancellation(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(CommandTimeoutRemainsDistinctFromCallerCancellation),
            "CREATE TABLE items (id INT PRIMARY KEY, value INT NOT NULL) ENGINE=InnoDB", "INSERT INTO items VALUES (1, 7)");
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        await using var blocker = new MySqlConnection(schema.Connection.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        using var takeLock = new MySqlCommand("SELECT value FROM items WHERE id = 1 FOR UPDATE", blocker, transaction);
        await takeLock.ExecuteScalarAsync();
        // Lock contention gives a real interrupted statement. SLEEP may instead
        // finish successfully with an interruption result on some server versions.
        using var command = new MySqlCommand(takeLock.CommandText) { CommandTimeout = 1 };
        var failure = await Assert.That(() => access.ExecuteScalarAsyncCore(command)).Throws<MySqlException>();
        await Assert.That(failure!.ErrorCode).IsEqualTo(MySqlErrorCode.CommandTimeoutExpired);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(command.CommandTimeout).IsEqualTo(1);
        await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore("SELECT 7"))).IsEqualTo(7);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task InFlightCancellationWaitsForNativeSettlementAndReleasesTheConnection(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(InFlightCancellationWaitsForNativeSettlementAndReleasesTheConnection),
            "CREATE TABLE items (id INT PRIMARY KEY, value INT NOT NULL) ENGINE=InnoDB", "INSERT INTO items VALUES (1, 7)");
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        var marker = "w2_cancel_" + Guid.NewGuid().ToString("N");
        await using var blocker = new MySqlConnection(schema.Connection.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        using var takeLock = new MySqlCommand("SELECT value FROM items WHERE id = 1 FOR UPDATE", blocker, transaction);
        await takeLock.ExecuteScalarAsync();
        using var command = new MySqlCommand($"{takeLock.CommandText} /*{marker}*/") { CommandTimeout = 15 };
        await using var observer = new MySqlConnection(schema.Connection.ConnectionString);
        await observer.OpenAsync();
        using var probe = observer.CreateCommand();
        probe.CommandText = "SELECT COUNT(*) FROM information_schema.PROCESSLIST WHERE INFO = @sql";
        probe.Parameters.AddWithValue("@sql", command.CommandText);
        using var cancellation = new CancellationTokenSource();
        var operation = access.ExecuteScalarAsyncCore(command, cancellation.Token);
        try
        {
            var started = Stopwatch.StartNew();
            var observed = false;
            while (started.Elapsed < TimeSpan.FromSeconds(5) && !operation.IsCompleted)
            {
                if (Convert.ToInt64(await probe.ExecuteScalarAsync()) > 0) { observed = true; break; }
                await Task.Delay(20);
            }
            await Assert.That(observed).IsTrue();
            await Assert.That(operation.IsCompleted).IsFalse();
            cancellation.Cancel();
            var failure = await Assert.That(async () => await operation).Throws<OperationCanceledException>();
            var context = ExecutionFailureContexts.Get(failure!)!;
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
            await Assert.That(context.CommandDispatch!.Dispatched).IsTrue();
            await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        }
        finally
        {
            cancellation.Cancel();
            // Even a failed readiness assertion must settle the command before disposing its source.
            try { await operation; } catch (Exception) { }
        }
        command.CommandText = "SELECT 7";
        await Assert.That(Convert.ToInt32(await access.ExecuteScalarAsyncCore(command))).IsEqualTo(7);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task AttachedTransactionIsRejectedBeforeCancellationAndRemainsUsable(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(AttachedTransactionIsRejectedBeforeCancellationAndRemainsUsable));
        await using var source = CreateSource(schema);
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        using var command = new MySqlCommand("SELECT 7", connection, transaction);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var kind in new[] { "scalar", "non_query", "reader" })
        {
            await Assert.That(() => Execute(access, command, kind, cancellation.Token)).Throws<InvalidOperationException>();
            await Assert.That(ReferenceEquals(command.Connection, connection)).IsTrue();
            await Assert.That(ReferenceEquals(command.Transaction, transaction)).IsTrue();
        }
        await Assert.That(Convert.ToInt32(await command.ExecuteScalarAsync())).IsEqualTo(7);
        await transaction.RollbackAsync();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ProviderBoundAccessPreservesIdentityAndSynchronousScalarConversion(TestProviderDescriptor descriptor)
    {
        using var schema = ServerSchemaDatabase.Create(descriptor, nameof(ProviderBoundAccessPreservesIdentityAndSynchronousScalarConversion));
        using SqlProvider<EmployeesDb> provider = descriptor.DatabaseType == DatabaseType.MySQL
            ? new MySqlProvider<EmployeesDb>(schema.Connection.ConnectionString, schema.Connection.DataSourceName, DataLinqLoggingConfiguration.NullConfiguration)
            : new MariaDBProvider<EmployeesDb>(schema.Connection.ConnectionString, schema.Connection.DataSourceName, DataLinqLoggingConfiguration.NullConfiguration);
        var access = provider.DatabaseAccess;
        var failure = await Assert.That(async () => { await access.ExecuteScalarAsyncCore<Guid>("SELECT 'text'"); }).Throws<InvalidCastException>();
        await Assert.That(() => access.ExecuteScalar<Guid>("SELECT 'text'")).Throws<InvalidCastException>();
        var context = ExecutionFailureContexts.Get(failure!)!;
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        using var command = new MySqlCommand("SELECT invalid syntax for provider attribution");
        var providerFailure = await Assert.That(() => access.ExecuteScalarAsyncCore(command)).Throws<MySqlException>();
        await Assert.That(ExecutionFailureContexts.Get(providerFailure!)!.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(command.Connection!.State).IsEqualTo(ConnectionState.Closed);
        await Assert.That(await access.ExecuteScalarAsyncCore("SELECT 7")).IsEqualTo(access.ExecuteScalar("SELECT 7"));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task FailedConnectionInitializationIsNotCommandDispatch(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(provider, nameof(FailedConnectionInitializationIsNotCommandDispatch));
        var connectionString = new MySqlConnectionStringBuilder(schema.Connection.ConnectionString)
        {
            Password = "invalid_" + Guid.NewGuid().ToString("N"),
            Pooling = false,
            ConnectionTimeout = 5
        };
        await using var source = new MySqlDataSourceBuilder(connectionString.ConnectionString).Build();
        var access = new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration);
        using var command = new MySqlCommand("SELECT 7");
        var failure = await Assert.That(() => access.ExecuteScalarAsyncCore(command)).Throws<MySqlException>();
        var context = ExecutionFailureContexts.Get(failure!)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.CommandDispatch!.Dispatched).IsFalse();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(command.Connection).IsNull();
    }

    private static MySqlDataSource CreateSource(ServerSchemaDatabase schema) =>
        new MySqlDataSourceBuilder(new MySqlConnectionStringBuilder(schema.Connection.ConnectionString)
        {
            MaximumPoolSize = 1,
            ConnectionTimeout = 5
        }.ConnectionString).Build();

    private static async Task Execute(SqlDbAccess access, IDbCommand command, string kind, CancellationToken token = default)
    {
        if (kind == "scalar") await access.ExecuteScalarAsyncCore(command, token);
        else if (kind == "non_query") await access.ExecuteNonQueryAsyncCore(command, token);
        else await (await access.ExecuteReaderAsyncCore(command, token)).DisposeAsync();
    }

    private sealed class ThrowingLogger : ILoggerFactory, ILogger
    {
        internal readonly InvalidOperationException Failure = new("Injected native SQL logging failure.");
        internal bool Enabled { get; set; }
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Enabled) throw Failure;
        }
    }

    private sealed class UnsupportedCommand : IDbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string CommandText { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public int CommandTimeout { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public CommandType CommandType { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public IDbConnection? Connection { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public IDataParameterCollection Parameters => throw new NotSupportedException();
        public IDbTransaction? Transaction { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public UpdateRowSource UpdatedRowSource { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public void Cancel() => throw new NotSupportedException();
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public int ExecuteNonQuery() => throw new NotSupportedException();
        public IDataReader ExecuteReader() => throw new NotSupportedException();
        public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
        public object? ExecuteScalar() => throw new NotSupportedException();
        public void Prepare() => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class CloneableForeignParameter : IDbDataParameter, ICloneable
    {
        public DbType DbType { get; set; }
        public ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public bool IsNullable => true;
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string ParameterName { get; set; } = "@value";
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public string SourceColumn { get; set; } = "";
        public DataRowVersion SourceVersion { get; set; }
        public object? Value { get; set; }
        public byte Precision { get; set; }
        public byte Scale { get; set; }
        public int Size { get; set; }
        public object Clone() => MemberwiseClone();
    }
}
