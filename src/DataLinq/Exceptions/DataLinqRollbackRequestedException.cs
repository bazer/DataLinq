using System;
using DataLinq.Execution;

namespace DataLinq.Exceptions;

/// <summary>An explicit business rollback request prevents new execution or commit.</summary>
/// <remarks>The reason is distinct from operational failure diagnostics. Manual owners must still roll back and dispose.</remarks>
public sealed class DataLinqRollbackRequestedException : InvalidOperationException
{
    /// <summary>Gets the original non-null reason, without formatting it into the exception message.</summary>
    public object Reason { get; }

    // Only an owning boundary may certify a fresh commit veto after cleanup. A
    // callback-thrown veto, reused exception or NotAttempted outcome alone is not proof.
    internal uint? ConfirmedHelperTransactionId { get; private set; }

    internal void ConfirmHelperRollback(uint transactionId, ExecutionFailureContext? context, bool noStartedProviderWork)
    {
        if (context is { HasCleanupFailure: false, SecondaryFailures.Count: 0 } &&
            context.TransactionId == transactionId &&
            (context.Completion == ExecutionCompletion.RolledBack ||
                noStartedProviderWork && context.Completion == ExecutionCompletion.NotAttempted))
            ConfirmedHelperTransactionId = transactionId;
    }

    internal DataLinqRollbackRequestedException(object reason)
        : base("Rollback was requested for this transaction; new execution and commit are not permitted.") => Reason = reason;
}
