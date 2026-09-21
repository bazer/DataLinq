using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledCompletionProvider : IAsyncTransactionCompletion
{
    private TransactionInitializationState initializationState = TransactionInitializationState.Ready;
    public TransactionInitializationState InitializationState
    {
        get => InspectInitialization?.Invoke() ?? initializationState;
        set => initializationState = value;
    }
    internal Func<TransactionInitializationState>? InspectInitialization { get; set; }
    internal Func<TransactionOperationGate.Step, ValueTask>? DisposeResource { get; set; }
    public ExecutionRecoveryActions Recovery
    {
        get
        {
            RecoveryReads++;
            InspectRecovery?.Invoke();
            return RecoveryFailure is { } failure ? throw failure : RecoveryActions;
        }
    }
    internal int RecoveryReads { get; private set; }
    internal Action? InspectRecovery { get; set; }
    internal ExecutionRecoveryActions RecoveryActions { get; set; } = ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose;
    internal Exception? RecoveryFailure { get; set; }
    internal AsyncCheckpoint Commit { get; set; } = new();
    internal AsyncCheckpoint Rollback { get; set; } = new();
    internal AsyncCheckpoint TransactionCleanup { get; set; } = new();
    internal AsyncCheckpoint ConnectionCleanup { get; set; } = new();
    internal List<string> Calls { get; } = [];
    internal Exception? ValidationFailure { get; set; }
    internal Action<AsyncCompletionOperation>? Validating { get; set; }
    internal Action? Committed { get; set; }
    internal CancellationToken RollbackToken { get; private set; }
    public void ValidateCompletion(AsyncCompletionOperation operation)
    {
        Validating?.Invoke(operation);
        if (ValidationFailure is not null) throw ValidationFailure;
    }
    public async Task CommitAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
    {
        Calls.Add("commit");
        await Commit.ReachAsync(cancellationToken);
        Committed?.Invoke();
    }
    public Task RollbackAsync(TransactionOperationGate.Step owner, CancellationToken cancellationToken)
    {
        Calls.Add("rollback");
        RollbackToken = cancellationToken;
        return Rollback.ReachAsync(cancellationToken);
    }
    public async ValueTask DisposeTransactionAsync(TransactionOperationGate.Step owner)
    {
        Calls.Add("dispose-transaction");
        await TransactionCleanup.ReachAsync(CancellationToken.None);
        if (DisposeResource is not null) await DisposeResource(owner);
    }
    public ValueTask DisposeConnectionAsync(TransactionOperationGate.Step owner)
    {
        Calls.Add("dispose-connection");
        return new(ConnectionCleanup.ReachAsync(CancellationToken.None));
    }
}
