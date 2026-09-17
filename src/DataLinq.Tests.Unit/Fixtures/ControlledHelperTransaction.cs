using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledHelperTransaction : ControlledTransactionRecovery, IAsyncHelperTransaction
{
    internal AsyncCheckpoint Commit { get; set; } = new();
    internal Exception? CallbackValidationFailure { get; set; }
    internal Exception? CommitValidationFailure { get; set; }
    internal Exception? FinalizationFailure { get; set; }
    internal Action? Committed { get; set; }
    internal int Finalizations { get; private set; }
    public ExecutionRecoveryActions Recovery { get; set; } = ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose;

    public void ValidateCallback()
    {
        if (CallbackValidationFailure is not null) throw CallbackValidationFailure;
    }

    public void ValidateCommit()
    {
        if (CommitValidationFailure is not null) throw CommitValidationFailure;
    }

    public async Task CommitAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
    {
        Calls.Enqueue("commit");
        await Commit.ReachAsync(cancellationToken);
        Committed?.Invoke();
    }

    public void FinalizeCommit(TransactionOperationGate.Step owner)
    {
        Finalizations++;
        if (FinalizationFailure is not null) throw FinalizationFailure;
    }
}
