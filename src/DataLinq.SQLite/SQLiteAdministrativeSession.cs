using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Logging;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

// Owns one execution connection. Existing keepers and provisioned objects remain
// outside this session's disposal; SQLite's awaitable I/O is still synchronous.
internal sealed class SQLiteAdministrativeSession : IAsyncExistenceProbeSession, IAsyncProvisioningSession,
    IAsyncJournalModeSession, IAsyncMetadataSession
{
    private readonly SqliteConnection connection;
    private readonly ExecutionOperationKind operation;
    private readonly string? providerInstanceId;
    private readonly bool provisioning;
    private readonly bool applyVisibility;
    private int disposed;

    internal SQLiteAdministrativeSession(string connectionString, SQLiteDbAccess access, CapturedSql sql,
        ExecutionOperationKind operation, bool provisioning = false, bool applyVisibility = true)
    {
        connection = new SqliteConnection(connectionString);
        this.operation = operation;
        this.provisioning = provisioning;
        this.applyVisibility = applyVisibility && !provisioning;
        providerInstanceId = access.DiagnosticProviderInstanceId;
        Access = access.BindSession(connection);
        CommandFactory = new SQLiteDbAccess.NativeCommandFactory(sql);
    }

    internal static SQLiteAdministrativeSession CreateOwned(string connectionString, CapturedSql sql,
        ExecutionOperationKind operation, bool provisioning = false, bool applyVisibility = true) =>
        new(connectionString, new SQLiteDbAccess(connectionString, DataLinqLoggingConfiguration.NullConfiguration), sql, operation, provisioning, applyVisibility);

    public IAsyncDatabaseAccess Access { get; }
    public IAsyncOwnedCommandFactory CommandFactory { get; }
    public IAsyncMetadataCommands Commands { get; } = new MetadataCommands();
    public Task InitializeAsync(CancellationToken token) => OpenAsync(token);

    public async Task OpenAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var diagnostics = ExecutionFailureScope.Begin();
        try
        {
            token.ThrowIfCancellationRequested();
            if (provisioning)
            {
                var options = new SqliteConnectionStringBuilder(connection.ConnectionString);
                await SQLiteConnectionStringFactory.EnsureKeepAliveIfInMemoryAsync(connection.ConnectionString, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                // Preserve text-factory creation semantics even for ReadWrite mode.
                // CreateNew prevents a competing creator's file from being truncated.
                if (!SQLiteConnectionStringFactory.IsInMemory(options) && !File.Exists(options.DataSource))
                {
                    try
                    {
                        using var file = new FileStream(options.DataSource, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    }
                    catch (IOException) when (File.Exists(options.DataSource)) { }
                }
            }
            await connection.OpenAsync(token).ConfigureAwait(false);
            if (applyVisibility)
                await new OwnedCommandExecution(Access, new SQLiteDbAccess.NativeCommandFactory(
                    CapturedSql.Capture(new DataLinq.Query.Sql(SQLiteConnectionPolicy.CommittedVisibilitySql))))
                    .ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            var cause = failure switch
            {
                OperationCanceledException canceled when token.IsCancellationRequested && canceled.CancellationToken == token => ExecutionFailureCause.Cancellation,
                SqliteException => ExecutionFailureCause.ProviderError,
                _ => ExecutionFailureCause.Unknown
            };
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.Initialization, cause, operation);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, operation, providerInstanceId, providerIdentityIsAuthoritative: true));
            throw;
        }
    }

    private sealed class MetadataCommands : IAsyncMetadataCommands
    {
        public void Validate(int? seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        }
        public IAsyncOwnedCommandFactory Capture(CapturedSql query) => new SQLiteDbAccess.NativeCommandFactory(query);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        using var diagnostics = ExecutionFailureScope.Begin();
        try { await connection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure)
        {
            var failures = new ExecutionFailures();
            failures.AddCleanup(failure);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
                ExecutionRecoveryActions.None, null, operation, providerInstanceId, providerIdentityIsAuthoritative: true));
            throw;
        }
    }
}
