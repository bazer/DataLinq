using System;
using System.Threading;
using DataLinq.Execution;
using DataLinq.Exceptions;
using DataLinq.Instances;

namespace DataLinq.Mutation;

public partial class Transaction
{
    internal TResult RunOwnedCallbackCore<TResult>(Func<TResult> callback)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ArgumentNullException.ThrowIfNull(callback);
        var failures = new ExecutionFailures();
        var result = default(TResult)!;
        TransactionOperationGate.HelperOwner? lifetime = null;
        var stage = ExecutionFailureStage.Validation;
        try
        {
            EnsureTransactionCanComplete("begin callback helper", rejectPoisoned: true);
            lifetime = ExecutionGate.BeginHelperLifetime();
            stage = ExecutionFailureStage.Callback;
            result = callback();
        }
        catch (Exception failure)
        {
            if (lifetime?.TryGetObserved(failure, out var observed) == true)
                failures.AddObserved(observed, stage, ExecutionFailureCause.ApplicationError, ExecutionOperationKind.TransactionCallback);
            else
                failures.AddReported(failure, stage, stage == ExecutionFailureStage.Callback
                    ? ExecutionFailureCause.ApplicationError : ExecutionFailureCause.Unknown, ExecutionOperationKind.TransactionCallback);
        }

        // The successful synchronous path uses synchronous provider completion and
        // cleanup. Only unfinished work escaping a callback can require waiting for
        // previously admitted async work; never detach it or race provider cleanup.
        using var owner = lifetime is null
            ? BeginExclusiveOperation("clean up failed callback helper", completion: true, operationKind: ExecutionOperationKind.TransactionCallback)
            : lifetime.CloseAndDrainAsync(failures).GetAwaiter().GetResult();
        DataLinqRollbackRequestedException? commitVeto = null;
        var noStartedProviderWork = false;
        if (failures.Primary is null)
        {
            try { CommitCore(owner); }
            catch (Exception failure)
            {
                failures.AddReported(failure, ExecutionFailureStage.Commit, fallbackOperation: ExecutionOperationKind.Commit);
                if (failure is DataLinqRollbackRequestedException veto && !IsPoisoned && AsyncFailureContext?.HasCleanupFailure != true)
                {
                    commitVeto = veto;
                    try { noStartedProviderWork = DatabaseAccess is IAsyncTransactionCompletion { InitializationState: TransactionInitializationState.Unused }; }
                    catch (Exception inspection) { failures.AddReported(inspection, ExecutionFailureStage.Validation); }
                }
            }
        }
        using (ExecutionFailureScope.Begin())
        {
            try { DisposeCore(owner); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        if (failures.Primary is { } primary)
        {
            var completed = DatabaseAccess.SynchronousCompletion != ExecutionCompletion.NotAttempted
                ? DatabaseAccess.SynchronousCompletion : MutableOwnership.Outcome switch
                {
                    MutableTransactionOutcome.Committed or MutableTransactionOutcome.CommittedStateFinalizationFailed => ExecutionCompletion.Committed,
                    MutableTransactionOutcome.RolledBack => ExecutionCompletion.RolledBack,
                    MutableTransactionOutcome.CommitOutcomeUnknown or MutableTransactionOutcome.RollbackOutcomeUnknown or
                        MutableTransactionOutcome.ExternalCompletionUnknown => ExecutionCompletion.Unknown,
                    _ => Status switch
                    {
                        DatabaseTransactionStatus.Committed => ExecutionCompletion.Committed,
                        DatabaseTransactionStatus.RolledBack => ExecutionCompletion.RolledBack,
                        DatabaseTransactionStatus.Open => ExecutionCompletion.Unknown,
                        _ => ExecutionCompletion.NotAttempted
                    }
                };
            var completion = ExecutionRecoveryPolicy.PreserveCompletion(AsyncFailureContext?.Completion ?? ExecutionCompletion.NotAttempted, completed);
            var context = failures.Snapshot(new(), completion, ExecutionRecoveryActions.None, TransactionID,
                ExecutionOperationKind.TransactionCallback, ExecutionGate.ProviderInstanceId);
            Volatile.Write(ref asyncFailureContext, context);
            ExecutionFailureContexts.Attach(primary, context);
            if (ReferenceEquals(primary, commitVeto))
                commitVeto!.ConfirmHelperRollback(TransactionID, context, noStartedProviderWork);
        }
        failures.ThrowIfAny();
        return result;
    }
}
