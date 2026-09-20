using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class Transaction
{
    // Internal entry points until W2 provider evidence and W3 public declarations.
    internal Task CommitAsyncCore(CancellationToken cancellationToken = default) =>
        CompleteAsyncCore(commit: true, cancellationToken);

    internal Task RollbackAsyncCore(CancellationToken cancellationToken = default) =>
        CompleteAsyncCore(commit: false, cancellationToken);

    private async Task CompleteAsyncCore(bool commit, CancellationToken cancellationToken)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        using var operation = BeginExclusiveOperation(commit ? "commit asynchronously" : "roll back asynchronously", completion: true,
            operationKind: commit ? ExecutionOperationKind.Commit : ExecutionOperationKind.Rollback);
        var resource = new ManagedAsyncCompletion(this, RequireAsyncCompletion());
        if (commit) resource.ValidateCommit();
        else resource.ValidateRollback();
        cancellationToken.ThrowIfCancellationRequested();
        using var step = ExecutionGate.EnterStep(operation);
        if (commit)
        {
            await resource.CommitAsync(step, cancellationToken).ConfigureAwait(false);
            resource.FinalizeCommit(step);
        }
        else
            await resource.RollbackAsync(step, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask DisposeAsyncCore(RecoveryRollbackSettings? settings = null, TimeProvider? timeProvider = null)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        if (IsDisposed)
        {
            ExecutionGate.ThrowIfActive("dispose asynchronously", ExecutionOperationKind.Dispose);
            return;
        }
        using var operation = BeginExclusiveOperation("dispose asynchronously", completion: true, operationKind: ExecutionOperationKind.Dispose);
        var resource = new ManagedAsyncCompletion(this, RequireAsyncCompletion());
        resource.ValidateDisposal();
        var failures = new ExecutionFailures();
        var actions = ExecutionRecoveryActions.Dispose;
        try { actions = resource.Recovery; }
        catch (Exception failure) { failures.AddReported(failure, ExecutionFailureStage.Recovery); }
        var recovery = new AutomaticTransactionRecovery(ExecutionGate, operation, resource,
            settings ?? new(), failures, resource.Completion, actions, TransactionID, timeProvider);
        try { await recovery.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            if (recovery.FailureContext is { } context)
                Volatile.Write(ref asyncFailureContext, context);
            if (IsDisposed)
                UpdateAsyncRecovery(resource.Completion, ExecutionRecoveryActions.None);
        }
    }

    internal async Task<TResult> RunCallbackAsyncCore<TResult>(Func<CancellationToken, Task<TResult>> callback,
        RecoveryRollbackSettings settings, CancellationToken cancellationToken = default, TimeProvider? timeProvider = null,
        ExecutionOperationKind callbackOperation = ExecutionOperationKind.TransactionCallback)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(settings);
        var resource = new ManagedAsyncCompletion(this, RequireAsyncCompletion());
        try
        {
            return await TransactionCallbackRunner.RunAsync(ExecutionGate, resource, settings, TransactionID,
                callback, cancellationToken, timeProvider, callbackOperation).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            if (ExecutionFailureContexts.GetCurrent(failure) is { } context)
                Volatile.Write(ref asyncFailureContext, context);
            throw;
        }
        finally
        {
            if (IsDisposed) UpdateAsyncRecovery(resource.Completion, ExecutionRecoveryActions.None);
        }
    }

    private IAsyncTransactionCompletion RequireAsyncCompletion() =>
        DatabaseAccess as IAsyncTransactionCompletion ?? throw new NotSupportedException(
            $"Provider transaction '{DatabaseAccess.GetType().Name}' does not implement explicit asynchronous completion.");

    private sealed class ManagedAsyncCompletion(Transaction transaction, IAsyncTransactionCompletion provider)
        : IAsyncHelperTransaction
    {
        internal ExecutionCompletion Completion => transaction.AsyncFailureContext?.Completion ??
            transaction.MutableOwnership.Outcome switch
            {
                MutableTransactionOutcome.Committed or MutableTransactionOutcome.CommittedStateFinalizationFailed => ExecutionCompletion.Committed,
                MutableTransactionOutcome.RolledBack => ExecutionCompletion.RolledBack,
                MutableTransactionOutcome.CommitOutcomeUnknown or MutableTransactionOutcome.RollbackOutcomeUnknown or
                    MutableTransactionOutcome.ExternalCompletionUnknown => ExecutionCompletion.Unknown,
                _ => ExecutionCompletion.NotAttempted
            };

        public ExecutionRecoveryActions Recovery
        {
            get
            {
                if (transaction.IsDisposed) return ExecutionRecoveryActions.None;
                if (Completion is ExecutionCompletion.Committed or ExecutionCompletion.RolledBack ||
                    Volatile.Read(ref transaction.managedRollbackAttempted) != 0 ||
                    provider.InitializationState != TransactionInitializationState.Ready)
                    return ExecutionRecoveryActions.Dispose;
                var permitted = transaction.AsyncFailureContext?.Recovery ??
                    (ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
                return ExecutionRecoveryActions.Dispose | (provider.Recovery & permitted & ExecutionRecoveryActions.Rollback);
            }
        }

        public void ValidateCallback() => ValidateCommit();
        public void ValidateCommit() => Validate(AsyncCompletionOperation.Commit);
        internal void ValidateRollback() => Validate(AsyncCompletionOperation.Rollback);
        internal void ValidateDisposal() => provider.ValidateCompletion(AsyncCompletionOperation.Dispose);

        private void Validate(AsyncCompletionOperation operation)
        {
            transaction.EnsureAttachedTransactionNotCompletedExternally("complete asynchronously");
            transaction.EnsureTransactionCanComplete("complete asynchronously", rejectPoisoned: operation == AsyncCompletionOperation.Commit);
            if (provider.InitializationState is not (TransactionInitializationState.Unused or TransactionInitializationState.Ready))
                throw new InvalidOperationException("Transaction initialization is unusable; only disposal is permitted.");
            provider.ValidateCompletion(operation);
        }

        public async Task CommitAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            transaction.ExecutionGate.ValidateStep(owner);
            Volatile.Write(ref transaction.managedCommitFinalizationState, 1);
            try
            {
                // Completing an unused wrapper never initializes it merely to commit nothing.
                if (provider.InitializationState != TransactionInitializationState.Unused)
                    await provider.CommitAsync(owner, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                var failures = new ExecutionFailures();
                failures.AddReported(failure, ExecutionFailureStage.Commit, CancellationCause(failure, cancellationToken));
                foreach (var cleanup in transaction.FinalizeUncertainCompletionState(
                    MutableTransactionOutcome.CommitOutcomeUnknown, MutableInvalidationReason.CommitOutcomeUnknown))
                    failures.AddReported(cleanup, ExecutionFailureStage.Finalization);
                ResetCommitNotification();
                var recovery = ExecutionRecoveryActions.Dispose;
                try { recovery = Recovery; }
                catch (Exception inspection) { failures.AddReported(inspection, ExecutionFailureStage.Recovery); }
                transaction.DatabaseAccess.CompleteAsyncTransactionTelemetry(ExecutionCompletion.Unknown, failures, ExecutionOperationKind.Commit);
                Report(failures, ExecutionCompletion.Unknown, recovery, ExecutionOperationKind.Commit);
                throw;
            }
            transaction.DatabaseAccess.RecordConfirmedAsyncCompletion(ExecutionCompletion.Committed);
            transaction.UpdateAsyncRecovery(ExecutionCompletion.Committed, ExecutionRecoveryActions.Dispose);
        }

        public void FinalizeCommit(TransactionOperationGate.Step owner)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            transaction.ExecutionGate.ValidateStep(owner);
            var failures = new ExecutionFailures();
            try
            {
                try { transaction.FinalizeCommittedState(); }
                catch (Exception failure)
                {
                    failures.AddReported(failure, ExecutionFailureStage.Finalization);
                    if (failure is TransactionCommitFinalizationException committed)
                        foreach (var cleanup in committed.CleanupFailures)
                            failures.AddCleanup(cleanup);
                }
                if (failures.Primary is null)
                {
                    Volatile.Write(ref transaction.managedCommitFinalizationState, 2);
                    transaction.DatabaseAccess.NotifyConfirmedAsyncCompletion(failures, completeTelemetry: false);
                    using var notification = ExecutionFailureScope.Begin();
                    try { transaction.PublishDeferredCommittedStatus(); }
                    catch (Exception failure) { ExecutionActivity.AddFailure(failures, failure, ExecutionOperationKind.Commit); }
                }
                transaction.DatabaseAccess.CompleteAsyncTransactionTelemetry(ExecutionCompletion.Committed, failures, ExecutionOperationKind.Commit);
                Report(failures, ExecutionCompletion.Committed, ExecutionRecoveryActions.Dispose, ExecutionOperationKind.Commit);
            }
            finally { ResetCommitNotification(); }
        }

        public async Task RollbackAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            transaction.ExecutionGate.ValidateStep(owner);
            var previous = Completion;
            var failures = new ExecutionFailures();
            var confirmed = false;
            Volatile.Write(ref transaction.managedRollbackAttempted, 1);
            Volatile.Write(ref transaction.managedRollbackFinalizationState, 1);
            try
            {
                try
                {
                    if (provider.InitializationState != TransactionInitializationState.Unused)
                        await provider.RollbackAsync(owner, cancellationToken).ConfigureAwait(false);
                    confirmed = true;
                    transaction.DatabaseAccess.RecordConfirmedAsyncCompletion(ExecutionCompletion.RolledBack);
                }
                catch (Exception failure)
                {
                    failures.AddReported(failure, ExecutionFailureStage.Recovery, CancellationCause(failure, cancellationToken));
                }
                var completion = ExecutionRecoveryPolicy.PreserveCompletion(previous,
                    confirmed ? ExecutionCompletion.RolledBack : ExecutionCompletion.Unknown);
                var uncertainCommit = transaction.MutableOwnership.Outcome == MutableTransactionOutcome.CommitOutcomeUnknown;
                foreach (var failure in transaction.FinalizeUncommittedState(
                    uncertainCommit ? MutableTransactionOutcome.CommitOutcomeUnknown : confirmed
                        ? MutableTransactionOutcome.RolledBack : MutableTransactionOutcome.RollbackOutcomeUnknown,
                    uncertainCommit ? MutableInvalidationReason.CommitOutcomeUnknown : confirmed
                        ? MutableInvalidationReason.RolledBack : MutableInvalidationReason.RollbackOutcomeUnknown))
                    failures.AddReported(failure, ExecutionFailureStage.Finalization);
                transaction.UpdateAsyncRecovery(completion, ExecutionRecoveryActions.Dispose);
                Volatile.Write(ref transaction.managedRollbackFinalizationState, 2);
                if (confirmed)
                {
                    transaction.DatabaseAccess.NotifyConfirmedAsyncCompletion(failures, completeTelemetry: false);
                    using var notification = ExecutionFailureScope.Begin();
                    try { transaction.PublishDeferredRolledBackStatus(); }
                    catch (Exception failure) { ExecutionActivity.AddFailure(failures, failure, ExecutionOperationKind.Rollback); }
                }
                transaction.DatabaseAccess.CompleteAsyncTransactionTelemetry(completion, failures, ExecutionOperationKind.Rollback);
                Report(failures, completion, ExecutionRecoveryActions.Dispose, ExecutionOperationKind.Rollback);
            }
            finally
            {
                Volatile.Write(ref transaction.deferredRolledBackStatus, 0);
                Volatile.Write(ref transaction.managedRollbackFinalizationState, 0);
            }
        }

        public async ValueTask DisposeTransactionAsync(TransactionOperationGate.Step owner)
        {
            using var diagnostics = ExecutionFailureScope.Begin();
            transaction.ExecutionGate.ValidateStep(owner);
            if (Interlocked.Exchange(ref transaction.disposeState, 1) != 0) return;
            var completion = Completion;
            var failures = new ExecutionFailures();
            try { await provider.DisposeTransactionAsync(owner).ConfigureAwait(false); }
            catch (Exception failure) { failures.AddCleanup(failure); }

            try
            {
                var outcome = transaction.MutableOwnership.Outcome;
                var cleanup = outcome == MutableTransactionOutcome.Unresolved
                    ? transaction.FinalizeUncommittedState(MutableTransactionOutcome.OpenTransactionDisposed, MutableInvalidationReason.OpenTransactionDisposed)
                    : transaction.Provider.State.Cache.RemoveTransactionBestEffort(transaction);
                foreach (var failure in cleanup) failures.AddCleanup(failure);
            }
            catch (Exception failure) { failures.AddCleanup(failure); }
            transaction.DatabaseAccess.CompleteAsyncTransactionTelemetry(completion, failures, ExecutionOperationKind.Dispose);
            Report(failures, completion, ExecutionRecoveryActions.None, ExecutionOperationKind.Dispose);
        }

        public async ValueTask DisposeConnectionAsync(TransactionOperationGate.Step owner)
        {
            transaction.ExecutionGate.ValidateStep(owner);
            // Independent of transaction disposal success; no request token or implicit retry.
            await provider.DisposeConnectionAsync(owner).ConfigureAwait(false);
        }

        private void Report(ExecutionFailures failures, ExecutionCompletion completion, ExecutionRecoveryActions recovery,
            ExecutionOperationKind operation)
        {
            if (failures.Primary is not { } primary) return;
            var context = failures.Snapshot(new(), completion, recovery, transaction.TransactionID,
                operation, transaction.ExecutionGate.ProviderInstanceId);
            Volatile.Write(ref transaction.asyncFailureContext, context);
            ExecutionFailureContexts.Attach(primary, context);
            failures.ThrowIfAny();
        }

        private void ResetCommitNotification()
        {
            Volatile.Write(ref transaction.deferredCommittedStatus, 0);
            Volatile.Write(ref transaction.managedCommitFinalizationState, 0);
        }

        private static ExecutionFailureCause CancellationCause(Exception failure, CancellationToken token) =>
            failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown;
    }
}
