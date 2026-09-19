using System;
using System.Threading;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Execution;

// Diagnostic values only: this grants no admission and retains no live source.
internal readonly record struct ReadExecutionIdentity(ExecutionOperationKind Operation, string? ProviderInstanceId)
{
    internal static ReadExecutionIdentity Capture(IDataSourceAccess source, ExecutionOperationKind operation,
        TransactionOperationGate.Step? owner = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return owner is not null ? new(owner.Kind, owner.ProviderInstanceId)
            : new(operation, source is Transaction transaction
                ? transaction.ExecutionGate.ProviderInstanceId : source.Provider.TelemetryInstanceId);
    }

    internal ReadExecutionIdentity Bind(Transaction? transaction) =>
        new(Operation, transaction?.ExecutionGate.ProviderInstanceId ?? ProviderInstanceId);

    // Local conversion/cache/relation work can fail without opening a reader. Do
    // not invent provider dispatch or overwrite an inner boundary's trust policy.
    internal void ReportLocalFailure(Exception failure, IDataSourceAccess source,
        TransactionOperationGate.Step? owner, CancellationToken token,
        ExecutionFailureStage stage = ExecutionFailureStage.Materialization)
    {
        var existing = ExecutionFailureContexts.Get(failure);
        var failures = new ExecutionFailures();
        failures.AddReported(failure, stage,
            failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : stage == ExecutionFailureStage.Materialization
                    ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown, Operation);
        var transaction = source as Transaction;
        var recovery = existing?.Recovery ?? (owner is null ? ExecutionRecoveryActions.None
            : ExecutionRecoveryPolicy.ForReadFailure(new(Effects: ExecutionEffects.NoStatement,
                Integrity: TransactionIntegrity.Confirmed, RollbackAvailable: true), true));
        var context = failures.Snapshot(new(), existing?.Completion ?? (transaction is null
                ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted),
            recovery, transaction?.TransactionID, Operation, ProviderInstanceId);
        if (existing is null && owner is not null) transaction!.RecordAsyncReadFailure(owner, context);
        ExecutionFailureContexts.Attach(failure, context);
    }
}
