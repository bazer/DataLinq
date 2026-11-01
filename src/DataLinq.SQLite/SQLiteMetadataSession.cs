using System;
using System.Data;
using DataLinq.Execution;
using DataLinq.Logging;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

// Runtime observation owns one read connection, never a memory keeper or root.
internal sealed class SQLiteMetadataSession(string connectionString, SQLiteDbAccess access) : ISyncMetadataSession, IAsyncReadFailureEvidence
{
    private readonly SqliteConnection connection = new(connectionString);
    public void Validate(int? seconds)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
    }
    public void Open()
    {
        try { connection.Open(); }
        catch (Exception failure)
        {
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.Initialization, failure is SqliteException
                ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown, ExecutionOperationKind.MetadataRead);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, ExecutionOperationKind.MetadataRead, access.DiagnosticProviderInstanceId,
                providerIdentityIsAuthoritative: true));
            throw;
        }
    }
    public IDbCommand CreateCommand(CapturedSql sql)
    {
        var factory = new SQLiteDbAccess.NativeCommandFactory(sql);
        factory.Validate(AsyncCommandKind.Reader);
        return factory.CreateCommand();
    }
    public IDataLinqDataReader ExecuteReader(IDbCommand command) => access.ReadMetadata(connection, command);
    public object? ExecuteScalar(IDbCommand command) => access.ReadMetadataScalar(connection, command);
    public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => SQLiteDbAccess.MetadataFailureEvidence(failure);
    public void Dispose() => connection.Dispose();
}

public partial class SQLiteDbAccess
{
    internal static ReadFailureEvidence MetadataFailureEvidence(Exception failure) => new(Cause: failure is SqliteException
        ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);

    internal IDataLinqDataReader ReadMetadata(SqliteConnection connection, IDbCommand command) =>
        ExecuteMetadata(connection, command, "reader", native => new SQLiteDataLinqDataReader(native.ExecuteReader()));

    internal object? ReadMetadataScalar(SqliteConnection connection, IDbCommand command) =>
        ExecuteMetadata(connection, command, "scalar", native => native.ExecuteScalar());

    private TResult ExecuteMetadata<TResult>(SqliteConnection connection, IDbCommand command, string kind,
        Func<SqliteCommand, TResult> execute)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var stage = ExecutionFailureStage.Validation;
        try
        {
            validateLifecycle?.Invoke();
            var native = (SqliteCommand)command;
            native.Connection = connection;
            stage = ExecutionFailureStage.Notification;
            using (ExecutionFailureScope.Begin()) Log.SqlCommand(loggingConfiguration, command);
            stage = ExecutionFailureStage.CommandExecution;
            return ExecuteCommandWithTelemetry(command, kind, false, null, () => execute(native));
        }
        catch (Exception failure)
        {
            var failures = new ExecutionFailures();
            failures.AddReported(failure, stage, stage == ExecutionFailureStage.Notification
                ? ExecutionFailureCause.ApplicationError : failure is SqliteException
                    ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown, ExecutionOperationKind.MetadataRead);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(MetadataFailureEvidence(failure), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, ExecutionOperationKind.MetadataRead, DiagnosticProviderInstanceId,
                providerIdentityIsAuthoritative: true));
            throw;
        }
    }
}
