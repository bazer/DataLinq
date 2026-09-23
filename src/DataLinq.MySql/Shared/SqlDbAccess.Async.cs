using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Query;
using MySqlConnector;

namespace DataLinq.MySql;

// W2 standalone native binding. Transaction adapters and the public surface are
// separate integrations; synchronous access keeps its direct implementation.
public partial class SqlDbAccess : IAsyncEagerCommandFactory, IAsyncSqlReaderFactory, IAsyncBorrowedReaderFactory
{
    AsyncEagerCommand IAsyncEagerCommandFactory.BindCommand(string sql) =>
        new(new NativeCommands(this), new NativeCommandFactory(CapturedSql.Capture(new Sql(sql))));

    AsyncEagerCommand IAsyncEagerCommandFactory.BindCommand(IDbCommand command) =>
        new(new NativeCommands(this), command);

    AsyncEagerScalarInvocation<T> IAsyncEagerCommandFactory.BindScalar<T>(string sql) =>
        new(((IAsyncEagerCommandFactory)this).BindCommand(sql), static value => (T)(value ?? default(T)!));

    AsyncEagerScalarInvocation<T> IAsyncEagerCommandFactory.BindScalar<T>(IDbCommand command) =>
        new(((IAsyncEagerCommandFactory)this).BindCommand(command), static value => (T)(value ?? default(T)!));

    IAsyncSqlReaderFactory IAsyncSqlReaderFactory.CaptureInvocation() => this;

    IAsyncReaderSource IAsyncSqlReaderFactory.BindReader(CapturedSql sql) =>
        new OwnedCommandExecution(new NativeCommands(this), new NativeCommandFactory(sql));

    IAsyncReaderSource IAsyncBorrowedReaderFactory.BindBorrowedReader(IDbCommand command) =>
        new BorrowedCommandReaderSource(new NativeCommands(this), command);

    private sealed class NativeCommandFactory(CapturedSql sql) : IAsyncOwnedCommandFactory
    {
        private readonly Sql statement = sql.ToSql();

        public void Validate(AsyncCommandKind kind)
        {
            if (kind is not (AsyncCommandKind.Reader or AsyncCommandKind.Scalar or AsyncCommandKind.NonQuery))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ValidateSql(statement.Text);
            foreach (var parameter in statement.Parameters)
                if (parameter.ProviderParameter is not null and not MySqlParameter)
                    throw new NotSupportedException("Captured MySQL/MariaDB parameters must be MySqlConnector parameters.");
        }

        public IAsyncOwnedCommand Create()
        {
            var command = new MySqlCommand(statement.Text);
            try
            {
                foreach (var parameter in statement.Parameters)
                    command.Parameters.Add(parameter.ProviderParameter ??
                        new MySqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value));
                return new NativeOwnedCommand(command);
            }
            catch
            {
                command.Dispose();
                throw;
            }
        }
    }

    private sealed class NativeOwnedCommand(MySqlCommand command) : IAsyncOwnedCommand
    {
        public IDbCommand Command => command;
        public void Dispose() => command.Dispose();
        public ValueTask DisposeAsync() => command.DisposeAsync();
    }

    private sealed class NativeCommands(SqlDbAccess owner) : AsyncDatabaseAccess, IAsyncReadFailureEvidence
    {
        protected override void ValidateCommand(IDbCommand command, AsyncCommandKind kind)
        {
            if (command is not MySqlCommand native)
                throw new NotSupportedException("MySQL/MariaDB asynchronous execution requires a MySqlConnector command.");
            if (native.Transaction is not null)
                throw new InvalidOperationException("A standalone asynchronous command cannot execute an attached transaction.");
            ValidateSql(native.CommandText);
        }

        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => new(Cause: Classify(failure));

        protected override Task<object?> ExecuteScalarCoreAsync(IDbCommand command, CancellationToken cancellationToken) =>
            ExecuteEagerAsync(command, "scalar", cancellationToken, static (native, token) => native.ExecuteScalarAsync(token));

        protected override Task<int> ExecuteNonQueryCoreAsync(IDbCommand command, CancellationToken cancellationToken) =>
            ExecuteEagerAsync(command, "non_query", cancellationToken, static (native, token) => native.ExecuteNonQueryAsync(token));

        private async Task<T> ExecuteEagerAsync<T>(IDbCommand command, string kind, CancellationToken token,
            Func<MySqlCommand, CancellationToken, Task<T>> execute)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            MySqlConnection? connection = null;
            ExecutionFailures? failures = null;
            var stage = ExecutionFailureStage.Initialization;
            var dispatched = false;
            var result = default(T)!;
            try
            {
                connection = await owner.dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
                stage = ExecutionFailureStage.Validation;
                var native = (MySqlCommand)command;
                native.Connection = connection;
                stage = ExecutionFailureStage.Notification;
                using (ExecutionFailureScope.Begin()) Log.SqlCommand(owner.loggingConfiguration, command);
                stage = ExecutionFailureStage.CommandExecution;
                result = await owner.ExecuteCommandWithTelemetryAsync(command, kind, false, null, token, () =>
                {
                    dispatched = true;
                    return execute(native, token);
                }).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures = new();
                failures.AddReported(failure, stage, stage == ExecutionFailureStage.Notification
                    ? ExecutionFailureCause.ApplicationError : Classify(failure, token));
            }
            await DisposeConnectionAsync(connection, failures, command, dispatched).ConfigureAwait(false);
            return result;
        }

        protected override async Task<IAsyncDataReader> ExecuteReaderCoreAsync(IDbCommand command, CancellationToken token)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            MySqlConnection? connection = null;
            ExecutionFailures? failures = null;
            var stage = ExecutionFailureStage.Initialization;
            var dispatched = false;
            IAsyncDataReader? result = null;
            try
            {
                connection = await owner.dataSource.OpenConnectionAsync(token).ConfigureAwait(false);
                stage = ExecutionFailureStage.Validation;
                var native = (MySqlCommand)command;
                native.Connection = connection;
                stage = ExecutionFailureStage.Notification;
                using (ExecutionFailureScope.Begin()) Log.SqlCommand(owner.loggingConfiguration, command);
                stage = ExecutionFailureStage.CommandExecution;
                result = await owner.ExecuteReaderWithTelemetryAsync(command, false, null, token, async () =>
                {
                    dispatched = true;
                    var reader = await native.ExecuteReaderAsync(token).ConfigureAwait(false);
                    var owned = new SqlAsyncDataLinqDataReader(reader, connection, owner.databaseType,
                        owner.DiagnosticProviderInstanceId);
                    // Reporting now owns the unreturned reader, including its connection.
                    // If an observer throws, it must finish that cleanup before returning.
                    connection = null;
                    return owned;
                }).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures = new();
                failures.AddReported(failure, stage, stage == ExecutionFailureStage.Notification
                    ? ExecutionFailureCause.ApplicationError : Classify(failure, token));
            }
            await DisposeConnectionAsync(connection, failures, command, dispatched).ConfigureAwait(false);
            return result!;
        }

        private async ValueTask DisposeConnectionAsync(MySqlConnection? connection, ExecutionFailures? failures,
            IDbCommand command, bool dispatched)
        {
            if (connection is not null)
            {
                using var cleanup = ExecutionFailureScope.Begin();
                var occurrence = ExecutionFailureContexts.CaptureOccurrence();
                try { await connection.DisposeAsync().ConfigureAwait(false); }
                catch (Exception failure)
                {
                    ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
                    (failures ??= new()).AddCleanup(failure);
                }
            }
            if (failures?.Primary is not { } primary) return;
            var context = failures.Snapshot(new(Cause: Classify(primary)), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, ExecutionOperationKind.RawCommand,
                owner.DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true);
            CommandDispatchEvidence.Attach(primary, context, command, dispatched);
            failures.ThrowIfAny();
        }

        private static ExecutionFailureCause Classify(Exception failure, CancellationToken token = default) => failure switch
        {
            OperationCanceledException canceled when token.IsCancellationRequested && canceled.CancellationToken == token =>
                ExecutionFailureCause.Cancellation,
            MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired } => ExecutionFailureCause.Timeout,
            MySqlException => ExecutionFailureCause.ProviderError,
            _ => ExecutionFailureCause.Unknown
        };
    }

    private static void ValidateSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException("CommandText must be specified.");
    }
}
