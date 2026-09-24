using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using MySqlConnector;

namespace DataLinq.MySql;

// Owns execution resources only. Disposal never drops a provisioned database.
internal sealed class SqlAdministrativeSession : IAsyncExistenceProbeSession, IAsyncProvisioningSession, IAsyncMetadataSession
{
    private readonly MySqlConnection connection;
    private readonly MySqlDataSource? ownedSource;
    private int disposed;
    private readonly ExecutionOperationKind operationKind;
    private readonly string? providerInstanceId;

    internal SqlAdministrativeSession(MySqlConnection connection, SqlDbAccess access, CapturedSql sql,
        ExecutionOperationKind operationKind, MySqlDataSource? ownedSource = null)
    {
        this.connection = connection;
        this.operationKind = operationKind;
        this.ownedSource = ownedSource;
        providerInstanceId = access.DiagnosticProviderInstanceId;
        Access = access.BindSession(connection);
        CommandFactory = new SqlDbAccess.NativeCommandFactory(sql);
    }

    internal static SqlAdministrativeSession CreateOwned(string connectionString, CapturedSql sql,
        ExecutionOperationKind operationKind = ExecutionOperationKind.Provisioning)
    {
        var source = new MySqlDataSourceBuilder(connectionString).Build();
        MySqlConnection? connection = null;
        try
        {
            connection = source.CreateConnection();
            return new(connection, new SqlDbAccess(source, DataLinqLoggingConfiguration.NullConfiguration),
                sql, operationKind, source);
        }
        catch
        {
            try { connection?.Dispose(); }
            finally { source.Dispose(); }
            throw;
        }
    }

    public IAsyncDatabaseAccess Access { get; }
    public IAsyncOwnedCommandFactory CommandFactory { get; }
    public IAsyncMetadataCommands Commands { get; } = new MetadataCommands();
    public Task InitializeAsync(CancellationToken cancellationToken) => OpenAsync(cancellationToken);

    private sealed class MetadataCommands : IAsyncMetadataCommands
    {
        public void Validate(int? commandTimeoutSeconds)
        {
            if (commandTimeoutSeconds < 0) throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
        }
        public IAsyncOwnedCommandFactory Capture(CapturedSql query) => new SqlDbAccess.NativeCommandFactory(query);
    }

    public async Task OpenAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var diagnostics = ExecutionFailureScope.Begin();
        try { await connection.OpenAsync(token).ConfigureAwait(false); }
        catch (Exception failure)
        {
            var cause = failure switch
            {
                OperationCanceledException canceled when token.IsCancellationRequested && canceled.CancellationToken == token => ExecutionFailureCause.Cancellation,
                MySqlException { ErrorCode: MySqlErrorCode.CommandTimeoutExpired } => ExecutionFailureCause.Timeout,
                MySqlException => ExecutionFailureCause.ProviderError,
                _ => ExecutionFailureCause.Unknown
            };
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.Initialization, cause, operationKind);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, operationKind, providerInstanceId, providerIdentityIsAuthoritative: true));
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        using var diagnostics = ExecutionFailureScope.Begin();
        var failures = new ExecutionFailures();
        using (ExecutionFailureScope.Begin())
        {
            try { await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        if (ownedSource is not null)
        {
            using var cleanup = ExecutionFailureScope.Begin();
            try { await ownedSource.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        if (failures.Primary is not { } primary) return;
        ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
            ExecutionRecoveryActions.None, null, operationKind, providerInstanceId, providerIdentityIsAuthoritative: true));
        failures.ThrowIfAny();
    }
}
