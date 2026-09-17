using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Fixtures;

internal sealed class ControlledCompletionProvider : IAsyncTransactionCompletion
{
    public TransactionInitializationState InitializationState { get; set; } = TransactionInitializationState.Ready;
    public ExecutionRecoveryActions Recovery => RecoveryFailure is { } failure ? throw failure : RecoveryActions;
    internal ExecutionRecoveryActions RecoveryActions { get; set; } = ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose;
    internal Exception? RecoveryFailure { get; set; }
    internal AsyncCheckpoint Commit { get; set; } = new();
    internal AsyncCheckpoint Rollback { get; set; } = new();
    internal AsyncCheckpoint TransactionCleanup { get; set; } = new();
    internal AsyncCheckpoint ConnectionCleanup { get; set; } = new();
    internal List<string> Calls { get; } = [];
    internal Exception? ValidationFailure { get; set; }
    internal Action? Committed { get; set; }
    internal CancellationToken RollbackToken { get; private set; }
    public void ValidateCompletion(AsyncCompletionOperation operation)
    {
        if (ValidationFailure is not null) throw ValidationFailure;
    }
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        Calls.Add("commit");
        await Commit.ReachAsync(cancellationToken);
        Committed?.Invoke();
    }
    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        Calls.Add("rollback");
        RollbackToken = cancellationToken;
        return Rollback.ReachAsync(cancellationToken);
    }
    public ValueTask DisposeTransactionAsync()
    {
        Calls.Add("dispose-transaction");
        return new(TransactionCleanup.ReachAsync(CancellationToken.None));
    }
    public ValueTask DisposeConnectionAsync()
    {
        Calls.Add("dispose-connection");
        return new(ConnectionCleanup.ReachAsync(CancellationToken.None));
    }
}
