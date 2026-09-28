using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq;

public abstract partial class DatabaseTransaction
{
    private readonly RecoveryRollbackSettings standaloneRecoverySettings;
    private TransactionOperationGate? standaloneGate;
    private ExecutionFailureContext? standaloneFailure;
    internal TransactionOperationGate StandaloneExecutionGate =>
        LazyInitializer.EnsureInitialized(ref standaloneGate, () => new(null, DiagnosticProviderInstanceId));

    /// <summary>Commits a standalone provider transaction asynchronously.</summary>
    /// <remarks>Legacy implementations reject this operation. A managed transaction's provider handle must be completed through its managed wrapper.</remarks>
    public virtual Task CommitAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This provider transaction does not support asynchronous completion.");

    /// <summary>Rolls back a standalone provider transaction asynchronously.</summary>
    /// <remarks>This does not provide managed cache or mutable-model finalization.</remarks>
    public virtual Task RollbackAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This provider transaction does not support asynchronous completion.");

    /// <summary>Performs permitted recovery and releases a standalone provider transaction asynchronously.</summary>
    public virtual ValueTask DisposeAsync() =>
        throw new NotSupportedException("This provider transaction does not support asynchronous disposal.");

    internal void EnsureStandaloneCompletion()
    {
        if (ManagedTransaction is not null)
            throw new InvalidOperationException("Complete or dispose this provider transaction through its owning managed transaction.");
    }

    internal StandaloneTransactionOperation BeginStandaloneCommand()
    {
        EnsureStandaloneCompletion();
        var operation = new StandaloneTransactionOperation(StandaloneExecutionGate, ExecutionOperationKind.RawCommand);
        try
        {
            EnsureStandaloneCommandAllowed(operation.Step);
            return operation;
        }
        catch { operation.Dispose(); throw; }
    }

    internal void EnsureStandaloneCommandAllowed(TransactionOperationGate.Step? owner = null)
    {
        EnsureStandaloneCompletion();
        EnsureSynchronousResourceUsable();
        if (Status is DatabaseTransactionStatus.Committed or DatabaseTransactionStatus.RolledBack ||
            SynchronousCompletion != ExecutionCompletion.NotAttempted || synchronousRollbackAttempted)
            throw new InvalidOperationException("This provider transaction no longer accepts commands.");
        if (owner is null) StandaloneExecutionGate.ThrowIfBusy("execute a standalone command", ExecutionOperationKind.RawCommand);
        else StandaloneExecutionGate.ValidateStep(owner);
        EnsureStandaloneRecoveryAllowed(ExecutionRecoveryActions.Continue);
    }

    internal void RecordStandaloneFailure(TransactionOperationGate.Step owner, ExecutionFailureContext context)
    {
        StandaloneExecutionGate.ValidateStep(owner);
        Volatile.Write(ref standaloneFailure, context);
    }

    private bool StandaloneRecoveryAllows(ExecutionRecoveryActions action) =>
        Volatile.Read(ref standaloneFailure) is not { } failure || (failure.Recovery & action) != 0;

    private void EnsureStandaloneRecoveryAllowed(ExecutionRecoveryActions action)
    {
        if (!StandaloneRecoveryAllows(action))
            throw new InvalidOperationException("The standalone transaction's failed execution permits only its recorded recovery actions.");
    }

    internal async Task CompleteStandaloneAsync(IAsyncTransactionCompletion resource, bool rollback, CancellationToken token)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        EnsureStandaloneCompletion();
        var kind = rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit;
        using var operation = new StandaloneTransactionOperation(StandaloneExecutionGate, kind);
        EnsureSynchronousResourceUsable();
        resource.ValidateCompletion(rollback ? AsyncCompletionOperation.Rollback : AsyncCompletionOperation.Commit);
        EnsureStandaloneRecoveryAllowed(rollback ? ExecutionRecoveryActions.Rollback : ExecutionRecoveryActions.Continue);
        if (synchronousRollbackAttempted || (!rollback && SynchronousCompletion == ExecutionCompletion.Unknown))
            throw new InvalidOperationException("Completion was already attempted; only permitted recovery may continue.");
        token.ThrowIfCancellationRequested();
        var failures = new ExecutionFailures();
        var confirmed = false;
        try
        {
            if (rollback) synchronousRollbackAttempted = true;
            if (resource.InitializationState != TransactionInitializationState.Unused)
            {
                if (rollback) await resource.RollbackAsync(operation.Step, token).ConfigureAwait(false);
                else await resource.CommitAsync(operation.Step, token).ConfigureAwait(false);
            }
            var observed = rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed;
            SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion, observed);
            RecordConfirmedAsyncCompletion(observed);
            confirmed = true;
        }
        catch (Exception failure)
        {
            SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion, ExecutionCompletion.Unknown);
            failures.AddReported(failure, rollback ? ExecutionFailureStage.Recovery : ExecutionFailureStage.Commit,
                fallbackOperation: kind);
        }
        if (confirmed)
        {
            NotifyConfirmedAsyncCompletion(failures, completeTelemetry: false, requestedOperation: kind);
            await DisposeStandaloneResourcesAsync(resource, operation.Step, failures).ConfigureAwait(false);
        }
        CompleteAsyncTransactionTelemetry(SynchronousCompletion, failures, kind);
        var recovery = ExecutionRecoveryActions.None;
        if (!confirmed)
        {
            recovery = ExecutionRecoveryActions.Dispose;
            if (!rollback)
            {
                using var inspection = ExecutionFailureScope.Begin();
                try { recovery |= resource.Recovery & ExecutionRecoveryActions.Rollback; }
                catch (Exception failure) { failures.AddReported(failure, ExecutionFailureStage.Recovery, fallbackOperation: kind); }
            }
        }
        PublishSynchronousFailure(failures, SynchronousCompletion, recovery, kind);
        failures.ThrowIfAny();
    }

    internal async ValueTask DisposeStandaloneAsync(IAsyncTransactionCompletion resource)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        EnsureStandaloneCompletion();
        using var operation = new StandaloneTransactionOperation(StandaloneExecutionGate, ExecutionOperationKind.Dispose);
        if (Volatile.Read(ref synchronousDisposed) != 0) return;
        resource.ValidateCompletion(AsyncCompletionOperation.Dispose);
        var failures = new ExecutionFailures();
        var rollback = false;
        if (!synchronousRollbackAttempted && Status == DatabaseTransactionStatus.Open && StandaloneRecoveryAllows(ExecutionRecoveryActions.Rollback))
        {
            using var inspection = ExecutionFailureScope.Begin();
            try { rollback = (resource.Recovery & ExecutionRecoveryActions.Rollback) != 0; }
            catch (Exception failure) { failures.AddReported(failure, ExecutionFailureStage.Recovery, fallbackOperation: ExecutionOperationKind.Dispose); }
        }
        if (rollback)
        {
            synchronousRollbackAttempted = true;
            using var rollbackScope = ExecutionFailureScope.Begin();
            using var budget = new CancellationTokenSource(standaloneRecoverySettings.RecoveryRollbackTimeout);
            try
            {
                await resource.RollbackAsync(operation.Step, budget.Token).ConfigureAwait(false);
                SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion, ExecutionCompletion.RolledBack);
                RecordConfirmedAsyncCompletion(ExecutionCompletion.RolledBack);
                NotifyConfirmedAsyncCompletion(failures, completeTelemetry: false, requestedOperation: ExecutionOperationKind.Dispose);
            }
            catch (Exception failure)
            {
                SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion, ExecutionCompletion.Unknown);
                failures.AddReported(failure, ExecutionFailureStage.Recovery,
                    failure is OperationCanceledException canceled && canceled.CancellationToken == budget.Token && budget.IsCancellationRequested
                        ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown, ExecutionOperationKind.Rollback);
            }
        }
        await DisposeStandaloneResourcesAsync(resource, operation.Step, failures).ConfigureAwait(false);
        CompleteAsyncTransactionTelemetry(SynchronousCompletion, failures, ExecutionOperationKind.Dispose);
        PublishSynchronousFailure(failures, SynchronousCompletion, ExecutionRecoveryActions.None, ExecutionOperationKind.Dispose);
        failures.ThrowIfAny();
    }

    private async ValueTask DisposeStandaloneResourcesAsync(IAsyncTransactionCompletion resource,
        TransactionOperationGate.Step step, ExecutionFailures failures)
    {
        if (Interlocked.Exchange(ref synchronousDisposed, 1) != 0) return;
        using (ExecutionFailureScope.Begin())
        {
            try { await resource.DisposeTransactionAsync(step).ConfigureAwait(false); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        using (ExecutionFailureScope.Begin())
        {
            try { await resource.DisposeConnectionAsync(step).ConfigureAwait(false); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
    }
}

/// <summary>Owns one standalone operation without introducing a managed transaction or its model/cache semantics.</summary>
internal sealed class StandaloneTransactionOperation : IDisposable
{
    private readonly TransactionOperationGate.Lease lease;
    private int references = 1;
    private int disposed;
    internal TransactionOperationGate.Step Step { get; }

    internal StandaloneTransactionOperation(TransactionOperationGate gate, ExecutionOperationKind kind)
    {
        lease = gate.Enter("execute standalone " + kind, completion: kind != ExecutionOperationKind.RawCommand, operationKind: kind);
        Step = gate.EnterStep(lease);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) Release();
    }

    internal IDisposable RetainThroughCommandCleanup()
    {
        Interlocked.Increment(ref references);
        return new CleanupHold(this);
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref references) != 0) return;
        Step.Dispose();
        lease.Dispose();
    }

    private sealed class CleanupHold(StandaloneTransactionOperation owner) : IDisposable
    {
        private int released;
        public void Dispose() { if (Interlocked.Exchange(ref released, 1) == 0) owner.Release(); }
    }
}
