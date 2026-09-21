using System;
using System.Diagnostics;
using System.Threading;
using DataLinq.Diagnostics;
using DataLinq.Execution;

namespace DataLinq;

// Built-in direct synchronous resource boundary. No async facade or new public
// provider contract; the legacy protected telemetry signatures remain available.
internal interface ISyncTransactionCompletionResource
{
    void Complete(bool rollback);
    bool RollbackForDisposal();
    void CloseConnection();
    void DisposeConnection();
    void DisposeTransaction();
}

public abstract partial class DatabaseTransaction
{
    private int synchronousDisposed;
    private bool synchronousRollbackAttempted;
    private bool synchronousInitializationFailed;
    private bool synchronousTelemetryDeferred;
    internal bool SynchronousResourceUnavailable => Volatile.Read(ref synchronousDisposed) != 0 || synchronousInitializationFailed;
    internal ExecutionCompletion SynchronousCompletion { get; private set; } = ExecutionCompletion.NotAttempted;

    internal void EnsureSynchronousResourceUsable()
    {
        if (Volatile.Read(ref synchronousDisposed) != 0 || synchronousInitializationFailed)
            throw new ObjectDisposedException(GetType().Name);
    }

    private void BeginSynchronousTransactionTelemetry()
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        if (transactionTelemetryStarted) return;
        transactionTelemetryStarted = true;
        transactionStartedTimestamp = Stopwatch.GetTimestamp();
        var caller = Activity.Current;
        var reportingScope = ExecutionFailureScope.Current;
        ExecutionFailures? failures = null;
        using (ExecutionFailureScope.Begin())
        {
            try
            {
                transactionActivity = DataLinqTelemetry.CreateTransactionActivity(TelemetryContext, Type);
                transactionActivity?.Start();
            }
            catch (Exception failure) { ExecutionActivity.AddFailure(failures ??= new(reportingScope), failure, ExecutionOperationKind.Unknown); }
        }
        DataLinqTelemetry.RecordTransactionStarted(TelemetryContext, ExecutionOperationKind.Unknown, ref failures);
        if (failures is null) return;
        synchronousInitializationFailed = true;
        if (this is ISyncTransactionCompletionResource resource) DisposeSynchronousResources(resource, failures);
        CompleteAsyncTransactionTelemetry(ExecutionCompletion.NotAttempted, failures, ExecutionOperationKind.Unknown);
        ExecutionActivity.RestoreCurrent(caller, ref failures, ExecutionOperationKind.Unknown);
        PublishSynchronousFailure(failures!, ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose,
            ExecutionOperationKind.Unknown, initialization: true);
        failures!.ThrowIfAny();
    }

    private void CompleteSynchronousTransactionTelemetry(DatabaseTransactionStatus outcome)
    {
        if (synchronousTelemetryDeferred || !transactionTelemetryStarted || transactionTelemetryCompleted) return;
        using var diagnostics = ExecutionFailureScope.Begin();
        var operation = CompletionKind(outcome);
        var failures = new ExecutionFailures();
        var completion = CompletionOutcome(outcome);
        CompleteAsyncTransactionTelemetry(completion, failures, operation);
        PublishSynchronousFailure(failures, completion, ExecutionRecoveryActions.Dispose, operation);
        failures.ThrowIfAny();
    }

    private void FailSynchronousTransactionTelemetry(DatabaseTransactionStatus outcome, Exception failure)
    {
        if (transactionTelemetryCompleted) return;
        using var diagnostics = ExecutionFailureScope.Begin();
        var operation = CompletionKind(outcome);
        var failures = new ExecutionFailures();
        failures.AddReported(failure, operation == ExecutionOperationKind.Commit ? ExecutionFailureStage.Commit : ExecutionFailureStage.Recovery,
            fallbackOperation: operation);
        if (!synchronousTelemetryDeferred) CompleteAsyncTransactionTelemetry(ExecutionCompletion.Unknown, failures, operation);
        // Called from a provider catch: preserve its original throw, adding facts
        // without throwing a reporter exception from this legacy void hook.
        PublishSynchronousFailure(failures, ExecutionCompletion.Unknown, ExecutionRecoveryActions.Dispose, operation);
    }

    internal void CompleteSynchronousTransaction(ISyncTransactionCompletionResource resource, bool rollback)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var outcome = rollback ? DatabaseTransactionStatus.RolledBack : DatabaseTransactionStatus.Committed;
        if (Volatile.Read(ref synchronousDisposed) != 0 && Status == outcome) return;
        EnsureSynchronousResourceUsable();
        var operation = CompletionKind(outcome);
        if (Status is DatabaseTransactionStatus.Committed or DatabaseTransactionStatus.RolledBack)
            throw new InvalidOperationException("The transaction has already completed.");
        if (rollback && synchronousRollbackAttempted)
            throw new InvalidOperationException("Rollback has already been attempted; only disposal is permitted.");
        var caller = synchronousTelemetryDeferred ? Activity.Current : SynchronousCompletionCaller();
        ExecutionFailures? failures = new();
        var confirmed = false;
        try
        {
            if (rollback) synchronousRollbackAttempted = true;
            if (Status == DatabaseTransactionStatus.Open) resource.Complete(rollback);
            confirmed = true;
            SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion, CompletionOutcome(outcome));
            RecordConfirmedAsyncCompletion(CompletionOutcome(outcome));
        }
        catch (Exception failure)
        {
            SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion, ExecutionCompletion.Unknown);
            failures.AddReported(failure, rollback ? ExecutionFailureStage.Recovery : ExecutionFailureStage.Commit,
                fallbackOperation: operation);
        }
        if (confirmed) NotifyConfirmedAsyncCompletion(failures, completeTelemetry: false, requestedOperation: operation);
        if (confirmed) DisposeSynchronousResources(resource, failures);
        if (!synchronousTelemetryDeferred)
            CompleteAsyncTransactionTelemetry(confirmed ? CompletionOutcome(outcome) : ExecutionCompletion.Unknown, failures, operation);
        ExecutionActivity.RestoreCurrent(caller, ref failures, operation);
        PublishSynchronousFailure(failures!, SynchronousCompletion,
            !confirmed && !rollback ? ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.Dispose, operation);
        failures!.ThrowIfAny();
    }

    internal void DisposeSynchronousTransaction(ISyncTransactionCompletionResource resource)
    {
        if (Volatile.Read(ref synchronousDisposed) != 0) return;
        using var diagnostics = ExecutionFailureScope.Begin();
        var caller = synchronousTelemetryDeferred ? Activity.Current : SynchronousCompletionCaller();
        ExecutionFailures? failures = new();
        var confirmed = false;
        if (!synchronousInitializationFailed && !synchronousRollbackAttempted && Status == DatabaseTransactionStatus.Open)
        {
            synchronousRollbackAttempted = true;
            try
            {
                confirmed = resource.RollbackForDisposal();
                SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion,
                    confirmed ? ExecutionCompletion.RolledBack : ExecutionCompletion.Unknown);
                if (confirmed) RecordConfirmedAsyncCompletion(ExecutionCompletion.RolledBack);
            }
            catch (Exception failure)
            {
                SynchronousCompletion = ExecutionRecoveryPolicy.PreserveCompletion(SynchronousCompletion, ExecutionCompletion.Unknown);
                failures.AddReported(failure, ExecutionFailureStage.Recovery, fallbackOperation: ExecutionOperationKind.Dispose);
            }
        }
        if (confirmed) NotifyConfirmedAsyncCompletion(failures, completeTelemetry: false, requestedOperation: ExecutionOperationKind.Dispose);
        DisposeSynchronousResources(resource, failures);
        if (!synchronousTelemetryDeferred)
            CompleteAsyncTransactionTelemetry(confirmed ? ExecutionCompletion.RolledBack : SynchronousCompletion, failures, ExecutionOperationKind.Dispose);
        ExecutionActivity.RestoreCurrent(caller, ref failures, ExecutionOperationKind.Dispose);
        PublishSynchronousFailure(failures!, SynchronousCompletion, ExecutionRecoveryActions.None, ExecutionOperationKind.Dispose);
        failures!.ThrowIfAny();
    }

    private void DisposeSynchronousResources(ISyncTransactionCompletionResource resource, ExecutionFailures failures)
    {
        if (Interlocked.Exchange(ref synchronousDisposed, 1) != 0) return;
        using (ExecutionFailureScope.Begin())
        {
            try { resource.CloseConnection(); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        using (ExecutionFailureScope.Begin())
        {
            try { resource.DisposeConnection(); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        using (ExecutionFailureScope.Begin())
        {
            try { resource.DisposeTransaction(); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
    }

    private Activity? SynchronousCompletionCaller() => ReferenceEquals(Activity.Current, transactionActivity)
        ? transactionActivity?.Parent : Activity.Current;

    private void PublishSynchronousFailure(ExecutionFailures failures, ExecutionCompletion completion,
        ExecutionRecoveryActions recovery, ExecutionOperationKind operation, bool initialization = false)
    {
        if (failures.Primary is not { } failure) return;
        ExecutionFailureContexts.Attach(failure, failures.Snapshot(new(Effects: initialization ? ExecutionEffects.Initialization : ExecutionEffects.Unknown),
            completion, recovery, ManagedTransaction?.TransactionID, operation,
            string.IsNullOrEmpty(TelemetryContext.ProviderInstanceId) ? null : TelemetryContext.ProviderInstanceId));
    }

    internal SynchronousTelemetryScope DeferSynchronousTransactionTelemetry(TransactionOperationGate.Step owner)
    {
        (ManagedTransaction ?? throw new InvalidOperationException("Deferred telemetry requires a managed transaction.")).ExecutionGate.ValidateStep(owner);
        if (synchronousTelemetryDeferred) throw new InvalidOperationException("Transaction telemetry already has a completion owner.");
        synchronousTelemetryDeferred = true;
        return new(this, owner, SynchronousCompletionCaller());
    }

    internal readonly struct SynchronousTelemetryScope(DatabaseTransaction transaction, TransactionOperationGate.Step owner, Activity? caller) : IDisposable
    {
        internal void Complete(ExecutionFailures failures, ExecutionCompletion completion, ExecutionOperationKind operation)
        {
            transaction.ManagedTransaction!.ExecutionGate.ValidateStep(owner);
            transaction.CompleteAsyncTransactionTelemetry(completion, failures, operation);
            ExecutionFailures? reported = failures;
            ExecutionActivity.RestoreCurrent(caller, ref reported, operation);
        }
        public void Dispose() => transaction.synchronousTelemetryDeferred = false;
    }

    private static ExecutionOperationKind CompletionKind(DatabaseTransactionStatus outcome) => outcome == DatabaseTransactionStatus.Committed
        ? ExecutionOperationKind.Commit : ExecutionOperationKind.Rollback;
    private static ExecutionCompletion CompletionOutcome(DatabaseTransactionStatus outcome) => outcome == DatabaseTransactionStatus.Committed
        ? ExecutionCompletion.Committed : ExecutionCompletion.RolledBack;
}
