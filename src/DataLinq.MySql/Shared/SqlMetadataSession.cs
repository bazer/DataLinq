using System;
using System.Data;
using DataLinq.Execution;
using DataLinq.Logging;
using MySqlConnector;

namespace DataLinq.MySql;

// The provider retains its data source. This session owns only the connection.
internal sealed class SqlMetadataSession(MySqlConnection connection, SqlDbAccess access) : ISyncMetadataSession, IAsyncReadFailureEvidence
{
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
            failures.AddReported(failure, ExecutionFailureStage.Initialization, failure is MySqlException
                ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown, ExecutionOperationKind.MetadataRead);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, ExecutionOperationKind.MetadataRead, access.DiagnosticProviderInstanceId,
                providerIdentityIsAuthoritative: true));
            throw;
        }
    }
    public IDbCommand CreateCommand(CapturedSql sql)
    {
        var factory = new SqlDbAccess.NativeCommandFactory(sql);
        factory.Validate(AsyncCommandKind.Reader);
        return factory.CreateCommand();
    }
    public IDataLinqDataReader ExecuteReader(IDbCommand command) => access.ReadMetadata(connection, command);
    public object? ExecuteScalar(IDbCommand command) => access.ReadMetadataScalar(connection, command);
    public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => SqlDbAccess.MetadataFailureEvidence(failure);
    public void Dispose() => connection.Dispose();
}

public partial class SqlDbAccess
{
    internal static ReadFailureEvidence MetadataFailureEvidence(Exception failure) => new(Cause: failure switch
    {
        MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired } => ExecutionFailureCause.Timeout,
        MySqlException => ExecutionFailureCause.ProviderError,
        _ => ExecutionFailureCause.Unknown
    });

    internal IDataLinqDataReader ReadMetadata(MySqlConnection connection, IDbCommand command) =>
        ExecuteMetadata(connection, command, "reader", native => new SqlDataLinqDataReader(native.ExecuteReader(), databaseType));

    internal object? ReadMetadataScalar(MySqlConnection connection, IDbCommand command) =>
        ExecuteMetadata(connection, command, "scalar", native => native.ExecuteScalar());

    private TResult ExecuteMetadata<TResult>(MySqlConnection connection, IDbCommand command, string kind,
        Func<MySqlCommand, TResult> execute)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var stage = ExecutionFailureStage.Validation;
        try
        {
            validateLifecycle?.Invoke();
            var native = (MySqlCommand)command;
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
                ? ExecutionFailureCause.ApplicationError : failure switch
                {
                    MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired } => ExecutionFailureCause.Timeout,
                    MySqlException => ExecutionFailureCause.ProviderError,
                    _ => ExecutionFailureCause.Unknown
                }, ExecutionOperationKind.MetadataRead);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(MetadataFailureEvidence(failure), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, ExecutionOperationKind.MetadataRead, DiagnosticProviderInstanceId,
                providerIdentityIsAuthoritative: true));
            throw;
        }
    }
}
