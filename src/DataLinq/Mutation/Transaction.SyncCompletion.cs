using System.Threading;
using DataLinq.Execution;

namespace DataLinq.Mutation;

public partial class Transaction
{
    private void PublishSynchronousCompletionFailure(ExecutionFailures failures, TransactionOperationGate.Lease operation,
        ExecutionOperationKind kind, ExecutionCompletion completion, ExecutionRecoveryActions recovery)
    {
        if (failures.Primary is not { } failure) return;
        var context = failures.Snapshot(new(), completion, recovery, TransactionID, kind, ExecutionGate.ProviderInstanceId);
        Volatile.Write(ref asyncFailureContext, context);
        ExecutionFailureContexts.Attach(failure, context);
        operation.ReportFailure(failure);
    }
}
