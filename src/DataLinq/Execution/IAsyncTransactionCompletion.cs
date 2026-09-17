using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

internal enum AsyncCompletionOperation { Commit, Rollback, Dispose }

/// <summary>
/// Explicit internal provider capability. Validation and state inspection do no I/O.
/// Commit/rollback returns confirm database completion and must not include notifications,
/// telemetry, managed finalization or disposal. Failed initialization permits disposal only.
/// Separate disposal steps await all work, are safe independently and never retry rollback.
/// Production provider binding remains W2; no sync fallback is permitted.
/// </summary>
internal interface IAsyncTransactionCompletion
{
    TransactionInitializationState InitializationState { get; }
    ExecutionRecoveryActions Recovery { get; }
    void ValidateCompletion(AsyncCompletionOperation operation);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
    ValueTask DisposeTransactionAsync();
    ValueTask DisposeConnectionAsync();
}
