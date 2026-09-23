using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Query;
using MySqlConnector;

namespace DataLinq.MySql;

public partial class SqlDatabaseTransaction : IAsyncTransactionCompletion
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
                    nativeResource = new(this, (MySqlConnection)dbConnection!, DbTransaction);
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
        if (DbTransaction is not null and not MySqlTransaction)
            throw new NotSupportedException("Asynchronous transaction execution requires a MySqlConnector transaction.");
    }

    private MySqlTransaction RequireNativeTransaction(TransactionOperationGate.Step owner)
    {
        (ManagedTransaction ?? throw new InvalidOperationException("Native async completion requires a managed owner."))
            .ExecutionGate.ValidateStep(owner);
        return (MySqlTransaction)GetActiveProviderTransaction("complete asynchronously");
    }

    async Task IAsyncTransactionCompletion.CommitAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
        => await RequireNativeTransaction(owner).CommitAsync(cancellationToken).ConfigureAwait(false);

    async Task IAsyncTransactionCompletion.RollbackAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
        => await RequireNativeTransaction(owner).RollbackAsync(cancellationToken).ConfigureAwait(false);

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
        private readonly SqlDatabaseTransaction owner;
        internal MySqlConnection? Connection { get; private set; }
        internal IDbTransaction? Transaction { get; private set; }
        private int transactionDisposed;
        private int connectionDisposed;

        internal NativeTransactionResource(SqlDatabaseTransaction owner) => this.owner = owner;
        internal NativeTransactionResource(SqlDatabaseTransaction owner, MySqlConnection connection, IDbTransaction transaction)
            : this(owner) { Connection = connection; Transaction = transaction; }

        public void Initialize()
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            try
            {
                Connection = (owner.dataSource ?? throw new InvalidOperationException("The data source is null.")).OpenConnection();
                Transaction = Connection.BeginTransaction(IsolationLevel.ReadCommitted);
                if (owner.databaseName is not null)
                {
                    using var setup = SetupCommand();
                    owner.LogNativeCommand(setup);
                    owner.ExecuteCommandWithTelemetry(setup, "non_query", true, owner.Type, setup.ExecuteNonQuery);
                }
                Publish();
            }
            catch (Exception failure) { ReportInitialization(failure, default); throw; }
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            try
            {
                Connection = await (owner.dataSource ?? throw new InvalidOperationException("The data source is null."))
                    .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                Transaction = await Connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
                if (owner.databaseName is not null)
                {
                    await using var setup = SetupCommand();
                    owner.LogNativeCommand(setup);
                    await owner.ExecuteCommandWithTelemetryAsync(setup, "non_query", true, owner.Type, cancellationToken,
                        () => setup.ExecuteNonQueryAsync(cancellationToken)).ConfigureAwait(false);
                }
                Publish();
            }
            catch (Exception failure) { ReportInitialization(failure, cancellationToken); throw; }
        }

        private MySqlCommand SetupCommand() => new($"USE {SqlIdentifier.Quote(owner.databaseName, "`")};", Connection, (MySqlTransaction)Transaction!);

        private void Publish()
        {
            owner.BeginAsyncTransactionTelemetry(ExecutionOperationKind.Unknown);
            // Opening, begin, USE and telemetry have all settled. No public command is
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
            if (Interlocked.Exchange(ref transactionDisposed, 1) == 0) Transaction?.Dispose();
        }
        internal void DisposeConnection()
        {
            if (Interlocked.Exchange(ref connectionDisposed, 1) == 0) Connection?.Dispose();
        }
        internal async ValueTask DisposeTransactionAsync()
        {
            if (Interlocked.Exchange(ref transactionDisposed, 1) != 0) return;
            if (Transaction is MySqlTransaction native) await native.DisposeAsync().ConfigureAwait(false);
            else Transaction?.Dispose();
        }
        private async ValueTask DisposeConnectionAsync()
        {
            if (Interlocked.Exchange(ref connectionDisposed, 1) == 0 && Connection is not null)
                await Connection.DisposeAsync().ConfigureAwait(false);
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
