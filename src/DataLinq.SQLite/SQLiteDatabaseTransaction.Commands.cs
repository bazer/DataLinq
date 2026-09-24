using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using DataLinq.Query;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public partial class SQLiteDatabaseTransaction : IAsyncEagerCommandFactory, IAsyncSqlReaderFactory,
    IAsyncBorrowedReaderFactory, ISyncRawCommandFactory, IAsyncSqlScalarFactory, IAsyncMutationCommandFactory
{
    AsyncEagerCommand IAsyncEagerCommandFactory.BindCommand(string sql) =>
        new(new NativeCommands(this), new SQLiteDbAccess.NativeCommandFactory(CapturedSql.Capture(new Sql(sql))), AsyncResource);
    AsyncEagerCommand IAsyncEagerCommandFactory.BindCommand(IDbCommand command) => new(new NativeCommands(this), command, AsyncResource);
    AsyncEagerScalarInvocation<T> IAsyncEagerCommandFactory.BindScalar<T>(string sql) =>
        new(((IAsyncEagerCommandFactory)this).BindCommand(sql), static value => (T)value!);
    AsyncEagerScalarInvocation<T> IAsyncEagerCommandFactory.BindScalar<T>(IDbCommand command) =>
        new(((IAsyncEagerCommandFactory)this).BindCommand(command), static value => (T)value!);
    IAsyncSqlReaderFactory IAsyncSqlReaderFactory.CaptureInvocation() => this;
    IAsyncReaderSource IAsyncSqlReaderFactory.BindReader(CapturedSql sql) =>
        new InitializingTransactionReaderSource<NativeTransactionResource>(AsyncResource,
            new OwnedCommandExecution(new NativeCommands(this), new SQLiteDbAccess.NativeCommandFactory(sql)));
    IAsyncReaderSource IAsyncBorrowedReaderFactory.BindBorrowedReader(IDbCommand command) =>
        new InitializingTransactionReaderSource<NativeTransactionResource>(AsyncResource, new BorrowedCommandReaderSource(new NativeCommands(this), command));

    IAsyncScalarSource IAsyncSqlScalarFactory.BindScalar(CapturedSql sql) =>
        new InitializingTransactionScalarSource<NativeTransactionResource>(AsyncResource,
            new OwnedCommandExecution(new NativeCommands(this), new SQLiteDbAccess.NativeCommandFactory(sql), ManagedTransaction?.TransactionID));
    AsyncScalarInvocation<T> IAsyncSqlScalarFactory.BindScalar<T>(CapturedSql sql) =>
        new(((IAsyncSqlScalarFactory)this).BindScalar(sql), static value => (T)value!);
    AsyncEagerCommand IAsyncMutationCommandFactory.BindMutation(CapturedSql sql) =>
        new(new NativeCommands(this), new SQLiteDbAccess.NativeCommandFactory(sql), AsyncResource);

    SyncRawCommand ISyncRawCommandFactory.BindCommand(string sql) =>
        new(new NativeCommands(this), () => new SqliteCommand(sql), _ => SQLiteDbAccess.ValidateSql(sql), Resource);
    SyncRawCommand ISyncRawCommandFactory.BindCommand(IDbCommand command) => new(new NativeCommands(this), command, Resource);
    SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(string sql) =>
        new(((ISyncRawCommandFactory)this).BindCommand(sql), static value => (T)value!);
    SyncRawScalarInvocation<T> ISyncRawCommandFactory.BindScalar<T>(IDbCommand command) =>
        new(((ISyncRawCommandFactory)this).BindCommand(command), static value => (T)value!);

    private SyncRawCommand BindOwnedCommand(IDbCommand command, SyncCommandKind kind)
    {
        var bound = ((ISyncRawCommandFactory)this).BindCommand(command);
        bound.Reserve(kind, hasOwner: true);
        return bound;
    }
    internal override object? ExecuteScalarOwnedCore(IDbCommand command, TransactionOperationGate.Step owner) =>
        BindOwnedCommand(command, SyncCommandKind.Scalar).ExecuteScalar(owner);
    internal override T ExecuteScalarOwnedCore<T>(IDbCommand command, TransactionOperationGate.Step owner) =>
        (T)ExecuteScalarOwnedCore(command, owner)!;
    internal override int ExecuteNonQueryOwnedCore(IDbCommand command, TransactionOperationGate.Step owner) =>
        BindOwnedCommand(command, SyncCommandKind.NonQuery).ExecuteNonQuery(owner);
    internal override IDataLinqDataReader ExecuteReaderOwnedCore(IDbCommand command, TransactionOperationGate.Step owner) =>
        BindOwnedCommand(command, SyncCommandKind.Reader).ExecuteReader(owner);
    internal override IDataLinqDataReader ExecuteReaderOwnedCore(string query, TransactionOperationGate.Step owner)
    {
        var command = new SqliteCommand(query);
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
        SqliteException => ExecutionFailureCause.ProviderError,
        _ => ExecutionFailureCause.Unknown
    };

    private sealed class NativeCommands(SQLiteDatabaseTransaction owner) : AsyncDatabaseAccess, ISyncCommandAccess, IAsyncReadFailureEvidence
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
            if (command.GetType() != typeof(SqliteCommand))
                throw new NotSupportedException("SQLite execution requires an unmodified Microsoft.Data.Sqlite command.");
            var native = (SqliteCommand)command;
            if (native.Transaction is not null && !ReferenceEquals(native.Transaction, owner.DbTransaction))
                throw new InvalidOperationException("The command belongs to another transaction.");
            SQLiteDbAccess.ValidateSql(native.CommandText);
        }
        private SqliteCommand Prepare(IDbCommand command)
        {
            var resource = owner.Resource.PublishedResource ?? throw new InvalidOperationException("The native transaction is not initialized.");
            _ = owner.GetActiveProviderTransaction("execute a command");
            var native = (SqliteCommand)command;
            native.Connection = resource.Connection;
            native.Transaction = (SqliteTransaction)resource.Transaction!;
            owner.LogNativeCommand(command);
            return native;
        }

        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => new(Cause: ClassifyNativeFailure(failure),
            RollbackAvailable: owner.InitializationState == TransactionInitializationState.Ready && owner.NativeRollbackAvailable());

        protected override Task<object?> ExecuteScalarCoreAsync(IDbCommand command, CancellationToken token) =>
            ExecuteAsync(command, "scalar", token, static (native, token) => native.ExecuteScalarAsync(token));
        protected override Task<int> ExecuteNonQueryCoreAsync(IDbCommand command, CancellationToken token) =>
            ExecuteAsync(command, "non_query", token, static (native, token) => native.ExecuteNonQueryAsync(token));
        private async Task<T> ExecuteAsync<T>(IDbCommand command, string kind, CancellationToken token,
            Func<SqliteCommand, CancellationToken, Task<T>> execute)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            var dispatched = false;
            var result = default(T)!;
            Exception? failure = null;
            try
            {
                var native = Prepare(command);
                result = await owner.ExecuteCommandWithTelemetryAsync(command, kind, true, owner.Type, token, () =>
                {
                    dispatched = true;
                    return execute(native, token);
                }).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            Finish(command, dispatched, token, failure, detach: true);
            return result;
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
                    return new SQLiteAsyncDataLinqDataReader(await native.ExecuteReaderAsync(token).ConfigureAwait(false),
                        null, native, owner.DiagnosticProviderInstanceId, detachBorrowedCommand: true);
                }).ConfigureAwait(false);
            }
            catch (Exception failure) { Finish(command, dispatched, token, failure, detach: true); throw; }
        }

        object? ISyncCommandAccess.ExecuteScalar(IDbCommand command) => Execute(command, "scalar", static native => native.ExecuteScalar());
        int ISyncCommandAccess.ExecuteNonQuery(IDbCommand command) => Execute(command, "non_query", static native => native.ExecuteNonQuery());
        IDataLinqDataReader ISyncCommandAccess.ExecuteReader(IDbCommand command) => Execute(command, "reader",
            native => new SQLiteAsyncDataLinqDataReader(native.ExecuteReader(), null, native, owner.DiagnosticProviderInstanceId, detachBorrowedCommand: true));

        private T Execute<T>(IDbCommand command, string kind, Func<SqliteCommand, T> execute)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            var dispatched = false;
            var result = default(T)!;
            Exception? failure = null;
            try
            {
                var native = Prepare(command);
                result = owner.ExecuteCommandWithTelemetry(command, kind, true, owner.Type, () =>
                {
                    dispatched = true;
                    return execute(native);
                });
            }
            catch (Exception error) { failure = error; }
            Finish(command, dispatched, default, failure, detach: kind != "reader" || failure is not null);
            return result;
        }
        private void Finish(IDbCommand command, bool dispatched, CancellationToken token, Exception? failure, bool detach)
        {
            ExecutionFailures? failures = null;
            if (failure is not null)
                (failures = new()).AddReported(failure, dispatched ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Validation,
                    ClassifyNativeFailure(failure, token));
            if (detach && owner.nativeResource?.Connection is { } connection && ReferenceEquals(command.Connection, connection))
            {
                // SQLite closes registered commands when its connection is closed.
                // The command's enclosing owner, not transaction disposal, owns it.
                using var cleanup = ExecutionFailureScope.Begin();
                var occurrence = ExecutionFailureContexts.CaptureOccurrence();
                try { command.Transaction = null; command.Connection = null; }
                catch (Exception error)
                {
                    ExecutionFailureContexts.DiscardEarlierReport(error, occurrence);
                    (failures ??= new()).AddCleanup(error);
                }
            }
            if (failures?.Primary is not { } primary) return;
            CommandDispatchEvidence.Attach(primary, failures.Snapshot(new(), ExecutionCompletion.NotAttempted,
                ExecutionRecoveryActions.Dispose, owner.ManagedTransaction?.TransactionID, ExecutionOperationKind.Unknown,
                owner.DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true), command, dispatched);
            failures.ThrowIfAny();
        }
    }
}
