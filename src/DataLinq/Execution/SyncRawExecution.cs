using System;
using DataLinq.Mutation;

namespace DataLinq.Execution;

internal static class SyncRawExecution
{
    internal static TResult Execute<TValue, TResult>(SyncRawCommand command, SyncCommandKind kind,
        Transaction? transaction, Func<SyncRawCommand, TransactionOperationGate.Step?, TValue> execute,
        Func<TValue, TResult> convert)
    {
        const string operation = "execute a synchronous raw command";
        transaction?.EnsureCanRead(operation, operationKind: ExecutionOperationKind.RawCommand);
        command.Reserve(kind, transaction is not null);
        using var ownership = transaction is null ? null : DataSourceAccess.BeginRead(transaction, operation, operationKind: ExecutionOperationKind.RawCommand);
        ExecutionFailures? failures = null;
        var value = default(TValue)!;
        try { value = execute(command, ownership?.Step); }
        catch (Exception failure) { (failures = new()).AddReported(failure, command.Stage); }
        failures = command.DisposeOwnedCommand(failures);
        var result = default(TResult)!;
        if (failures is null)
        {
            try { result = convert(value); }
            catch (Exception failure) { (failures = new()).AddReported(failure, ExecutionFailureStage.Materialization, ExecutionFailureCause.MaterializationError); }
        }
        PublishFailure(command, failures, transaction, ownership);
        failures?.ThrowIfAny();
        return result;
    }

    internal static void PublishFailure(SyncRawCommand command, ExecutionFailures? failures,
        Transaction? transaction, TransactionReadScope? ownership)
    {
        if (failures?.Primary is not null)
            PublishFailure(failures, transaction, ownership, command.GetFailureEvidence);
    }

    internal static void PublishFailure(ExecutionFailures? failures, Transaction? transaction,
        TransactionReadScope? ownership, Func<Exception, ReadFailureEvidence> classify)
    {
        if (failures?.Primary is not { } failure) return;
        var evidence = new ReadFailureEvidence();
        var assessed = true;
        try { evidence = classify(failure); }
        catch (Exception assessment)
        {
            assessed = false;
            failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
        }
        var recovery = transaction is null ? ExecutionRecoveryActions.None
            : ExecutionRecoveryPolicy.ForReadFailure(evidence, !failures.HasCleanupFailure && assessed);
        var context = failures.Snapshot(evidence,
            transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
            recovery, transaction?.TransactionID, ExecutionOperationKind.RawCommand, transaction?.ExecutionGate.ProviderInstanceId);
        if (ownership is not null) transaction!.RecordAsyncReadFailure(ownership.Step, context);
        ExecutionFailureContexts.Attach(failure, context);
        ownership?.ReportFailure(failure);
    }
}
