using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>Internal lifecycle adapter; native implementations and public callbacks remain separate work.</summary>
internal interface IAsyncHelperTransaction : IAsyncTransactionRecovery
{
    void ValidateCallback();
    void ValidateCommit();
    Task CommitAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken);
    void FinalizeCommit(TransactionOperationGate.Step owner);
    ExecutionRecoveryActions Recovery { get; }
}

internal static class TransactionCallbackRunner
{
    internal static async Task<TResult> RunAsync<TResult>(TransactionOperationGate gate,
        IAsyncHelperTransaction resource, RecoveryRollbackSettings settings, uint transactionId,
        Func<CancellationToken, Task<TResult>> callback, CancellationToken cancellationToken = default,
        TimeProvider? timeProvider = null, ExecutionOperationKind callbackOperation = ExecutionOperationKind.TransactionCallback)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(callback);
        resource.ValidateCallback();
        var lifetime = gate.BeginHelperLifetime();
        var failures = new ExecutionFailures();
        var completion = ExecutionCompletion.NotAttempted;
        var stage = ExecutionFailureStage.Validation;
        var result = default(TResult)!;
        try
        {
            CheckCancellation(callbackOperation);
            stage = ExecutionFailureStage.Callback;
            result = await (callback(cancellationToken)
                ?? throw new InvalidOperationException("The transaction callback returned a null task.")).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            var cause = failure is OperationCanceledException canceled &&
                canceled.CancellationToken == cancellationToken && cancellationToken.IsCancellationRequested
                    ? ExecutionFailureCause.Cancellation
                    : stage == ExecutionFailureStage.Callback && callbackOperation == ExecutionOperationKind.TransactionCallback
                        ? ExecutionFailureCause.ApplicationError : ExecutionFailureCause.Unknown;
            if (lifetime.TryGetObserved(failure, out var observed))
                failures.AddObserved(observed, stage, cause, callbackOperation);
            else failures.AddReported(failure, stage, cause, callbackOperation);
        }

        // Closing admission and taking the active-work snapshot are one gate transition.
        // A successful callback does not authorize committing work found unfinished here.
        using var owner = await lifetime.CloseAndDrainAsync(failures).ConfigureAwait(false);
        if (failures.Primary is null)
        {
            var dispatched = false;
            // A successful callback does not lend its reports to commit validation.
            var occurrence = ExecutionFailureContexts.CaptureOccurrence();
            try
            {
                stage = ExecutionFailureStage.Validation;
                resource.ValidateCommit();
                occurrence = ExecutionFailureContexts.CaptureOccurrence();
                CheckCancellation(ExecutionOperationKind.Commit);
                stage = ExecutionFailureStage.Commit;
                using var step = gate.EnterStep(owner);
                dispatched = true;
                await resource.CommitAsync(step, cancellationToken).ConfigureAwait(false);
                completion = ExecutionCompletion.Committed;
                stage = ExecutionFailureStage.Finalization;
                occurrence = ExecutionFailureContexts.CaptureOccurrence();
                resource.FinalizeCommit(step); // Short consistency work ignores late cancellation.
            }
            catch (Exception failure)
            {
                ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
                if (dispatched && completion != ExecutionCompletion.Committed)
                    completion = ExecutionCompletion.Unknown;
                failures.AddReported(failure, stage, stage == ExecutionFailureStage.Finalization
                    ? ExecutionFailureCause.LocalFinalizationError : ExecutionFailureCause.Unknown, ExecutionOperationKind.Commit);
            }
        }

        var recovery = ExecutionRecoveryActions.Dispose;
        using (ExecutionFailureScope.Begin())
        {
            try { recovery = resource.Recovery; }
            catch (Exception failure) { failures.AddReported(failure, ExecutionFailureStage.Recovery, fallbackOperation: callbackOperation); }
        }
        var cleanup = new AutomaticTransactionRecovery(gate, owner, resource, settings, failures,
            completion, recovery, transactionId, timeProvider);
        await cleanup.DisposeAsync().ConfigureAwait(false);
        return result;

        void CheckCancellation(ExecutionOperationKind operationKind)
        {
            try { cancellationToken.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException failure)
            {
                failures.Add(failure, ExecutionFailureCause.Cancellation, stage, operationKind);
                throw;
            }
        }
    }
}
