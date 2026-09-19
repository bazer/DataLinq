using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Memory;

internal static class MemoryAsyncResult
{
    internal static ValueTask<T> FromFailure<T>(Exception failure, ExecutionOperationKind operation, CancellationToken token)
    {
        ReportFailure(failure, operation, token);
        return failure is OperationCanceledException ? PreserveCancellation<T>(failure) : ValueTask.FromException<T>(failure);
    }

    internal static void ReportFailure(Exception failure, ExecutionOperationKind operation, CancellationToken token)
    {
        // These local kernels have no provider/transaction lifetime or native
        // recovery. A reused user exception cannot import an earlier SQL context.
        var cause = failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
            ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown;
        ExecutionFailureContexts.Attach(failure, new(cause, ExecutionFailureStage.Unknown,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, transactionId: null, [], operation: operation));
    }

    // The async method builder preserves the original cancellation exception and
    // canceled status, even for user code that throws with an unrequested token.
    // FromCanceled alone would replace the exception; this completed await never yields.
    private static async ValueTask<T> PreserveCancellation<T>(Exception failure) =>
        await ValueTask.FromException<T>(failure).ConfigureAwait(false);
}
