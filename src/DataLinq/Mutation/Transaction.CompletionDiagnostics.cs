using System;
using System.Collections.Generic;
using DataLinq.Execution;

namespace DataLinq.Mutation;

public partial class Transaction
{
    // Capture each owned local occurrence before later recovery callbacks can
    // overwrite the exception's direct lookup. These records grant no authority.
    private ObservedExecutionFailure CaptureLocalCompletionFailure(Exception failure, ExecutionFailureStage stage)
    {
        var failures = new ExecutionFailures();
        failures.AddReported(failure, stage, ExecutionFailureCause.LocalFinalizationError);
        // More specific nested diagnostics must not erase the fact that the
        // enclosing cache recovery failed, even when exception identity repeats.
        if (stage == ExecutionFailureStage.CacheRecovery) failures.RecordCleanupFailure(failure);
        return new(failure, failures.Snapshot(new(), ExecutionCompletion.NotAttempted,
            ExecutionRecoveryActions.Dispose, TransactionID, fallbackProviderInstanceId: ExecutionGate.ProviderInstanceId));
    }

    private IReadOnlyList<ObservedExecutionFailure> CollectDisposedCacheRecoveryFailures()
    {
        var failures = new List<ObservedExecutionFailure>();
        CollectCacheRecoveryFailures(failures, observe => Provider.State.Cache.RemoveTransactionBestEffort(this, observe));
        return failures;
    }

    private void CollectCacheRecoveryFailures(List<ObservedExecutionFailure> failures, Func<Action<Exception>, IReadOnlyList<Exception>> action)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        try
        {
            action(failure => failures.Add(CaptureLocalCompletionFailure(failure, ExecutionFailureStage.CacheRecovery)));
        }
        catch (Exception failure) { failures.Add(CaptureLocalCompletionFailure(failure, ExecutionFailureStage.CacheRecovery)); }
    }
}
