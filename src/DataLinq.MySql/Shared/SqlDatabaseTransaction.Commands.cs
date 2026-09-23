using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Query;
using MySqlConnector;

namespace DataLinq.MySql;

public partial class SqlDatabaseTransaction : IAsyncEagerCommandFactory, IAsyncSqlReaderFactory,
    IAsyncBorrowedReaderFactory, ISyncRawCommandFactory, IAsyncSqlScalarFactory, IAsyncMutationCommandFactory
{
    AsyncEagerCommand IAsyncEagerCommandFactory.BindCommand(string sql) =>
        new(new NativeCommands(this), new SqlDbAccess.NativeCommandFactory(CapturedSql.Capture(new Sql(sql))), AsyncResource);
    AsyncEagerCommand IAsyncEagerCommandFactory.BindCommand(IDbCommand command) => new(new NativeCommands(this), command, AsyncResource);
    AsyncEagerScalarInvocation<T> IAsyncEagerCommandFactory.BindScalar<T>(string sql) =>
        new(((IAsyncEagerCommandFactory)this).BindCommand(sql), static value => (T)(value ?? default(T)!));
    AsyncEagerScalarInvocation<T> IAsyncEagerCommandFactory.BindScalar<T>(IDbCommand command) =>
        new(((IAsyncEagerCommandFactory)this).BindCommand(command), static value => (T)(value ?? default(T)!));
    IAsyncSqlReaderFactory IAsyncSqlReaderFactory.CaptureInvocation() => this;
    IAsyncReaderSource IAsyncSqlReaderFactory.BindReader(CapturedSql sql) =>
        new InitializingTransactionReaderSource<NativeTransactionResource>(AsyncResource,
            new OwnedCommandExecution(new NativeCommands(this), new SqlDbAccess.NativeCommandFactory(sql)));
    IAsyncReaderSource IAsyncBorrowedReaderFactory.BindBorrowedReader(IDbCommand command) =>
        new InitializingTransactionReaderSource<NativeTransactionResource>(AsyncResource, new BorrowedCommandReaderSource(new NativeCommands(this), command));

    IAsyncScalarSource IAsyncSqlScalarFactory.BindScalar(CapturedSql sql) =>
        new InitializingTransactionScalarSource<NativeTransactionResource>(AsyncResource,
            new OwnedCommandExecution(new NativeCommands(this), new SqlDbAccess.NativeCommandFactory(sql), ManagedTransaction?.TransactionID));
    AsyncScalarInvocation<T> IAsyncSqlScalarFactory.BindScalar<T>(CapturedSql sql) =>
        new(((IAsyncSqlScalarFactory)this).BindScalar(sql), static value => (T)(value ?? default(T)!));
    AsyncEagerCommand IAsyncMutationCommandFactory.BindMutation(CapturedSql sql) =>
        new(new NativeCommands(this), new SqlDbAccess.NativeCommandFactory(sql), AsyncResource);

    SyncRawCommand ISyncRawCommandFactory.BindCommand(string sql) =>
        new(new NativeCommands(this), () => new MySqlCommand(sql), _ => SqlDbAccess.ValidateSql(sql), Resource);
    SyncRawCommand ISyncRawCommandFactory.BindCommand(IDbCommand command) => new(new NativeCommands(this), command, Resource);
    SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(string sql) =>
        new(((ISyncRawCommandFactory)this).BindCommand(sql), static value => (T)(value ?? default(T)!));
    SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(IDbCommand command) =>
        new(((ISyncRawCommandFactory)this).BindCommand(command), static value => (T)(value ?? default(T)!));

    private SyncRawCommand BindOwnedCommand(IDbCommand command, SyncCommandKind kind)
    {
        var bound = ((ISyncRawCommandFactory)this).BindCommand(command);
        bound.Reserve(kind, hasOwner: true);
        return bound;
    }
    internal override object? ExecuteScalarOwnedCore(IDbCommand command, TransactionOperationGate.Step owner) =>
        BindOwnedCommand(command, SyncCommandKind.Scalar).ExecuteScalar(owner);
    internal override T ExecuteScalarOwnedCore<T>(IDbCommand command, TransactionOperationGate.Step owner) =>
        (T)(ExecuteScalarOwnedCore(command, owner) ?? default(T)!);
    internal override int ExecuteNonQueryOwnedCore(IDbCommand command, TransactionOperationGate.Step owner) =>
        BindOwnedCommand(command, SyncCommandKind.NonQuery).ExecuteNonQuery(owner);
    internal override IDataLinqDataReader ExecuteReaderOwnedCore(IDbCommand command, TransactionOperationGate.Step owner) =>
        BindOwnedCommand(command, SyncCommandKind.Reader).ExecuteReader(owner);
    internal override IDataLinqDataReader ExecuteReaderOwnedCore(string query, TransactionOperationGate.Step owner)
    {
        var command = new MySqlCommand(query);
        try { return OwnedCommandDataReader.Create(ExecuteReaderOwnedCore(command, owner), command); }
        catch { command.Dispose(); throw; }
    }

    private void LogNativeCommand(IDbCommand command)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        try { Log.SqlCommand(loggingConfiguration, command); }
        catch (Exception failure)
        {
            var failures = new ExecutionFailures();
            failures.Add(failure, ExecutionFailureCause.ApplicationError, ExecutionFailureStage.Notification);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotAttempted,
                ExecutionRecoveryActions.Dispose, ManagedTransaction?.TransactionID,
                fallbackProviderInstanceId: DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true));
            throw;
        }
    }

    private static ExecutionFailureCause ClassifyNativeFailure(Exception failure, CancellationToken token = default) => failure switch
    {
        OperationCanceledException canceled when token.IsCancellationRequested && canceled.CancellationToken == token => ExecutionFailureCause.Cancellation,
        MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired } => ExecutionFailureCause.Timeout,
        MySqlException => ExecutionFailureCause.ProviderError,
        _ => ExecutionFailureCause.Unknown
    };

    private sealed class NativeCommands(SqlDatabaseTransaction owner) : AsyncDatabaseAccess, ISyncCommandAccess, IAsyncReadFailureEvidence
    {
        protected override void ValidateCommand(IDbCommand command, AsyncCommandKind kind)
        {
            owner.ValidateNativeTransactionCapability();
            Validate(command);
        }
        void ISyncCommandAccess.ValidateCommand(IDbCommand command, SyncCommandKind kind) => Validate(command);
        private void Validate(IDbCommand command)
        {
            owner.EnsureSynchronousResourceUsable();
            owner.Resource.Validate();
            if (owner.Status is DatabaseTransactionStatus.Committed or DatabaseTransactionStatus.RolledBack)
                throw new InvalidOperationException("The transaction has already completed.");
            if (command is not MySqlCommand native)
                throw new NotSupportedException("MySQL/MariaDB execution requires a MySqlConnector command.");
            if (native.Transaction is not null && !ReferenceEquals(native.Transaction, owner.DbTransaction))
                throw new InvalidOperationException("The command belongs to another transaction.");
            SqlDbAccess.ValidateSql(native.CommandText);
        }
        private MySqlCommand Prepare(IDbCommand command)
        {
            var resource = owner.Resource.PublishedResource ?? throw new InvalidOperationException("The native transaction is not initialized.");
            _ = owner.GetActiveProviderTransaction("execute a command");
            var native = (MySqlCommand)command;
            native.Connection = resource.Connection;
            native.Transaction = (MySqlTransaction)resource.Transaction!;
            owner.LogNativeCommand(command);
            return native;
        }

        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => new(Cause: ClassifyNativeFailure(failure),
            RollbackAvailable: owner.InitializationState == TransactionInitializationState.Ready && owner.NativeRollbackAvailable());

        protected override async Task<object?> ExecuteScalarCoreAsync(IDbCommand command, CancellationToken token)
        {
            var value = await ExecuteAsync(command, "scalar", token, static (native, token) => native.ExecuteScalarAsync(token)).ConfigureAwait(false);
            return value == DBNull.Value ? null : value;
        }
        protected override Task<int> ExecuteNonQueryCoreAsync(IDbCommand command, CancellationToken token) =>
            ExecuteAsync(command, "non_query", token, static (native, token) => native.ExecuteNonQueryAsync(token));
        private async Task<T> ExecuteAsync<T>(IDbCommand command, string kind, CancellationToken token,
            Func<MySqlCommand, CancellationToken, Task<T>> execute)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            var dispatched = false;
            try
            {
                var native = Prepare(command);
                return await owner.ExecuteCommandWithTelemetryAsync(command, kind, true, owner.Type, token, () =>
                {
                    dispatched = true;
                    return execute(native, token);
                }).ConfigureAwait(false);
            }
            catch (Exception failure) { Report(failure, command, dispatched, token); throw; }
        }
        protected override async Task<IAsyncDataReader> ExecuteReaderCoreAsync(IDbCommand command, CancellationToken token)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            var dispatched = false;
            try
            {
                var native = Prepare(command);
                return await owner.ExecuteReaderWithTelemetryAsync(command, true, owner.Type, token, async () =>
                {
                    dispatched = true;
                    return new SqlAsyncDataLinqDataReader(await native.ExecuteReaderAsync(token).ConfigureAwait(false),
                        null, owner.databaseType, owner.DiagnosticProviderInstanceId);
                }).ConfigureAwait(false);
            }
            catch (Exception failure) { Report(failure, command, dispatched, token); throw; }
        }

        object? ISyncCommandAccess.ExecuteScalar(IDbCommand command)
        {
            var result = Execute(command, "scalar", static native => native.ExecuteScalar());
            return result == DBNull.Value ? null : result;
        }
        int ISyncCommandAccess.ExecuteNonQuery(IDbCommand command) => Execute(command, "non_query", static native => native.ExecuteNonQuery());
        IDataLinqDataReader ISyncCommandAccess.ExecuteReader(IDbCommand command) => Execute(command, "reader",
            native => new SqlDataLinqDataReader(native.ExecuteReader(), owner.databaseType));

        private T Execute<T>(IDbCommand command, string kind, Func<MySqlCommand, T> execute)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            var dispatched = false;
            try
            {
                var native = Prepare(command);
                return owner.ExecuteCommandWithTelemetry(command, kind, true, owner.Type, () =>
                {
                    dispatched = true;
                    return execute(native);
                });
            }
            catch (Exception failure) { Report(failure, command, dispatched, default); throw; }
        }
        private void Report(Exception failure, IDbCommand command, bool dispatched, CancellationToken token)
        {
            var failures = new ExecutionFailures();
            failures.AddReported(failure, dispatched ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Validation,
                ClassifyNativeFailure(failure, token));
            CommandDispatchEvidence.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotAttempted,
                ExecutionRecoveryActions.Dispose, owner.ManagedTransaction?.TransactionID, ExecutionOperationKind.RawCommand,
                owner.DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true), command, dispatched);
        }
    }
}
