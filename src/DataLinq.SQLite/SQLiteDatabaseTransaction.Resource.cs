using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using Microsoft.Data.Sqlite;

namespace DataLinq.SQLite;

public partial class SQLiteDatabaseTransaction : IAsyncTransactionCompletion
{
    private readonly object resourceLock = new();
    private LazyTransactionResource<NativeTransactionResource>? lazyResource;
    private NativeTransactionResource? nativeResource;
    private TransactionOperationGate? unmanagedGate;

    private LazyTransactionResource<NativeTransactionResource> Resource
    {
        get
        {
            lock (resourceLock)
            {
                if (lazyResource is not null) return lazyResource;
                var gate = ManagedTransaction?.ExecutionGate ?? (unmanagedGate ??= new(0, DiagnosticProviderInstanceId));
                if (DbTransaction is not null)
                {
                    nativeResource = new(this, (SqliteConnection)dbConnection!, DbTransaction);
                    return lazyResource = new(gate, nativeResource);
                }
                return lazyResource = new(gate, () => nativeResource = new(this));
            }
        }
    }

    private void InitializeUnmanaged()
    {
        var resource = Resource;
        if (resource.State == TransactionInitializationState.Ready) return;
        if (ManagedTransaction is not null)
            throw new InvalidOperationException("Native initialization requires the managed operation owner.");
        using var operation = unmanagedGate!.Enter("initialize native transaction");
        resource.GetOrInitialize(operation);
    }

    private LazyTransactionResource<NativeTransactionResource> AsyncResource
    {
        get { ValidateNativeTransactionCapability(); return Resource; }
    }

    private TransactionInitializationState InitializationState => SynchronousResourceUnavailable
        ? TransactionInitializationState.Disposed : Resource.State;

    TransactionInitializationState IAsyncTransactionCompletion.InitializationState => InitializationState;

    ExecutionRecoveryActions IAsyncTransactionCompletion.Recovery => ExecutionRecoveryActions.Dispose |
        (InitializationState == TransactionInitializationState.Ready && NativeRollbackAvailable()
            ? ExecutionRecoveryActions.Rollback : ExecutionRecoveryActions.None);

    private bool NativeRollbackAvailable() => nativeResource?.Transaction?.Connection?.State == ConnectionState.Open;

    void IAsyncTransactionCompletion.ValidateCompletion(AsyncCompletionOperation operation)
    {
        if (operation == AsyncCompletionOperation.Dispose) return;
        EnsureSynchronousResourceUsable();
        Resource.Validate();
        ValidateNativeTransactionCapability();
        if (Status is DatabaseTransactionStatus.Committed or DatabaseTransactionStatus.RolledBack)
            throw new InvalidOperationException("The transaction has already completed.");
    }

    private void ValidateNativeTransactionCapability()
    {
        if (DbTransaction is not null && (DbTransaction.GetType() != typeof(SqliteTransaction) ||
            dbConnection?.GetType() != typeof(SqliteConnection)))
            throw new NotSupportedException("Asynchronous transaction execution requires unmodified Microsoft.Data.Sqlite resources.");
    }

    private SqliteTransaction RequireNativeTransaction(TransactionOperationGate.Step owner)
    {
        (ManagedTransaction ?? throw new InvalidOperationException("Native async completion requires a managed owner."))
            .ExecutionGate.ValidateStep(owner);
        return (SqliteTransaction)GetActiveProviderTransaction("complete asynchronously");
    }

    async Task IAsyncTransactionCompletion.CommitAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        try { await RequireNativeTransaction(owner).CommitAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception failure) { ReportNativeCompletion(failure, cancellationToken, commit: true); throw; }
    }

    async Task IAsyncTransactionCompletion.RollbackAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        try { await RequireNativeTransaction(owner).RollbackAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception failure) { ReportNativeCompletion(failure, cancellationToken, commit: false); throw; }
    }

    private void ReportNativeCompletion(Exception failure, CancellationToken token, bool commit)
    {
        var operation = commit ? ExecutionOperationKind.Commit : ExecutionOperationKind.Rollback;
        var failures = new ExecutionFailures();
        failures.AddReported(failure, commit ? ExecutionFailureStage.Commit : ExecutionFailureStage.Recovery,
            ClassifyNativeFailure(failure, token), operation);
        // The managed owner determines permitted recovery and preserves any prior
        // uncertain commit. Native failure alone cannot establish a database outcome.
        ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.Unknown,
            ExecutionRecoveryActions.Dispose, ManagedTransaction?.TransactionID, operation,
            DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true));
    }

    async ValueTask IAsyncTransactionCompletion.DisposeTransactionAsync(TransactionOperationGate.Step owner)
    {
        ManagedTransaction!.ExecutionGate.ValidateStep(owner);
        // Materialize the adopted bundle, but never initialize an unused wrapper.
        _ = Resource;
        if (nativeResource is not null) await nativeResource.DisposeTransactionAsync().ConfigureAwait(false);
    }

    ValueTask IAsyncTransactionCompletion.DisposeConnectionAsync(TransactionOperationGate.Step owner)
        => Resource.DisposeAsync(owner);

    private sealed class NativeTransactionResource : ITransactionResource
    {
        private readonly SQLiteDatabaseTransaction owner;
        internal SqliteConnection? Connection { get; private set; }
        internal IDbTransaction? Transaction { get; private set; }
        private int transactionDisposed;
        private int connectionDisposed;

        internal NativeTransactionResource(SQLiteDatabaseTransaction owner) => this.owner = owner;
        internal NativeTransactionResource(SQLiteDatabaseTransaction owner, SqliteConnection connection, IDbTransaction transaction)
            : this(owner) { Connection = connection; Transaction = transaction; }

        public void Initialize()
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            try
            {
                Connection = new SqliteConnection(owner.connectionString ?? throw new InvalidOperationException("The connection string is null."));
                Connection.Open();
                using (var setup = SetupCommand())
                    owner.ExecuteCommandWithTelemetry(setup, "non_query", false, null, setup.ExecuteNonQuery);
                Transaction = Connection.BeginTransaction(SQLiteConnectionPolicy.OwnedTransactionIsolationLevel, deferred: true);
                Publish();
            }
            catch (Exception failure) { ReportInitialization(failure, default); throw; }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            try
            {
                Connection = new SqliteConnection(owner.connectionString ?? throw new InvalidOperationException("The connection string is null."));
                await Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (var setup = SetupCommand())
                    await owner.ExecuteCommandWithTelemetryAsync(setup, "non_query", false, null, cancellationToken,
                        () => setup.ExecuteNonQueryAsync(cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                // The driver has no deferred async begin overload. Its inherited
                // overload is also synchronous but chooses BEGIN IMMEDIATE. Use
                // the concrete deferred API to preserve SQLite's existing policy.
                Transaction = Connection.BeginTransaction(SQLiteConnectionPolicy.OwnedTransactionIsolationLevel, deferred: true);
                Publish();
            }
            catch (Exception failure) { ReportInitialization(failure, cancellationToken); throw; }
        }

        private SqliteCommand SetupCommand() => new(SQLiteConnectionPolicy.CommittedVisibilitySql, Connection);

        private void Publish()
        {
            owner.BeginAsyncTransactionTelemetry(ExecutionOperationKind.Unknown);
            // Opening, visibility policy, deferred begin and telemetry have settled. No public command is
            // used during setup: callbacks cannot borrow this initialization authority.
            using (ExecutionFailureScope.Begin())
            {
                try { owner.SetStatus(DatabaseTransactionStatus.Open); }
                catch (Exception failure)
                {
                    var failures = new ExecutionFailures();
                    failures.Add(failure, ExecutionFailureCause.ApplicationError, ExecutionFailureStage.Notification);
                    ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(Effects: ExecutionEffects.Initialization),
                        ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, owner.ManagedTransaction?.TransactionID,
                        fallbackProviderInstanceId: owner.DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true));
                    throw;
                }
            }
            owner.dbConnection = Connection;
            owner.DbTransaction = Transaction;
        }

        private void ReportInitialization(Exception failure, CancellationToken token)
        {
            owner.RecordFailedInitialization();
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.Initialization, ClassifyNativeFailure(failure, token));
            owner.CompleteAsyncTransactionTelemetry(ExecutionCompletion.NotAttempted, failures, ExecutionOperationKind.Unknown);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(Effects: ExecutionEffects.Initialization),
                ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, owner.ManagedTransaction?.TransactionID,
                fallbackProviderInstanceId: owner.DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true));
        }

        internal void DisposeTransaction()
        {
            if (Interlocked.Exchange(ref transactionDisposed, 1) != 0) return;
            var occurrence = ExecutionFailureContexts.CaptureOccurrence();
            try { Transaction?.Dispose(); }
            catch (Exception failure) { ReportCleanup(failure, occurrence); throw; }
        }
        internal void DisposeConnection()
        {
            if (Interlocked.Exchange(ref connectionDisposed, 1) != 0) return;
            var occurrence = ExecutionFailureContexts.CaptureOccurrence();
            try { Connection?.Dispose(); }
            catch (Exception failure) { ReportCleanup(failure, occurrence); throw; }
        }
        internal async ValueTask DisposeTransactionAsync()
        {
            if (Interlocked.Exchange(ref transactionDisposed, 1) != 0) return;
            var occurrence = ExecutionFailureContexts.CaptureOccurrence();
            try
            {
                if (Transaction is SqliteTransaction native) await native.DisposeAsync().ConfigureAwait(false);
                else Transaction?.Dispose();
            }
            catch (Exception failure) { ReportCleanup(failure, occurrence); throw; }
        }
        private async ValueTask DisposeConnectionAsync()
        {
            if (Interlocked.Exchange(ref connectionDisposed, 1) != 0 || Connection is null) return;
            var occurrence = ExecutionFailureContexts.CaptureOccurrence();
            try { await Connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { ReportCleanup(failure, occurrence); throw; }
        }

        private void ReportCleanup(Exception failure, long occurrence)
        {
            // Native cleanup owns classification, not completion certainty. Keep
            // current nested reports and let the outer coordinator retain its
            // primary error and established transaction outcome.
            ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
            var failures = new ExecutionFailures();
            failures.AddCleanup(failure);
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(Cause: ClassifyNativeFailure(failure)), ExecutionCompletion.NotAttempted,
                ExecutionRecoveryActions.Dispose, owner.ManagedTransaction?.TransactionID, ExecutionOperationKind.Dispose,
                owner.DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true));
        }
        public void Dispose()
        {
            var failures = new ExecutionFailures();
            using (ExecutionFailureScope.Begin())
            {
                try { DisposeTransaction(); } catch (Exception failure) { failures.AddCleanup(failure); }
            }
            using (ExecutionFailureScope.Begin())
            {
                try { DisposeConnection(); } catch (Exception failure) { failures.AddCleanup(failure); }
            }
            ThrowCleanup(failures);
        }
        public async ValueTask DisposeAsync()
        {
            var failures = new ExecutionFailures();
            using (ExecutionFailureScope.Begin())
            {
                try { await DisposeTransactionAsync().ConfigureAwait(false); } catch (Exception failure) { failures.AddCleanup(failure); }
            }
            using (ExecutionFailureScope.Begin())
            {
                try { await DisposeConnectionAsync().ConfigureAwait(false); } catch (Exception failure) { failures.AddCleanup(failure); }
            }
            ThrowCleanup(failures);
        }

        private void ThrowCleanup(ExecutionFailures failures)
        {
            if (failures.Primary is not { } failure) return;
            ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(), ExecutionCompletion.NotAttempted,
                ExecutionRecoveryActions.Dispose, owner.ManagedTransaction?.TransactionID,
                fallbackProviderInstanceId: owner.DiagnosticProviderInstanceId, providerIdentityIsAuthoritative: true));
            failures.ThrowIfAny();
        }
    }
}
