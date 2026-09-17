using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Execution;

/// <summary>
/// One owned recovery boundary, not caller-directed rollback. Consumes an idle operation
/// lease: the caller must first drain admitted work and close its step. No recovery timer
/// runs during that drain. The transferred lease stays private until all cleanup settles.
/// </summary>
internal sealed class AutomaticTransactionRecovery : IAsyncDisposable
{
    private readonly TransactionOperationGate.Lease owner;
    private readonly TransactionOperationGate.Step step;
    private readonly RecoveryRollbackSettings settings;
    private readonly TimeProvider timeProvider;
    private readonly uint transactionId;
    private readonly ExecutionFailures failures;
    private readonly bool rollbackAllowed;
    private IAsyncTransactionRecovery? resource;
    private ExecutionCompletion completion;
    private ExecutionFailureContext? failureContext;
    private int active;

    internal ExecutionFailureContext? FailureContext => Volatile.Read(ref failureContext);

    internal AutomaticTransactionRecovery(TransactionOperationGate gate, TransactionOperationGate.Lease operation,
        IAsyncTransactionRecovery resource, RecoveryRollbackSettings settings, ExecutionFailures failures,
        ExecutionCompletion completion, ExecutionRecoveryActions recovery, uint transactionId,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(failures);
        // Validate both the gate identity and idleness before transferring resource ownership.
        using (gate.EnterStep(operation)) { }
        owner = operation.Transfer();
        step = gate.EnterStep(owner);
        this.resource = resource;
        this.settings = settings;
        this.failures = failures;
        this.completion = completion;
        this.transactionId = transactionId;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        rollbackAllowed = recovery.HasFlag(ExecutionRecoveryActions.Rollback) &&
            completion is ExecutionCompletion.NotAttempted or ExecutionCompletion.Unknown;
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            throw new InvalidOperationException("Automatic transaction recovery is already in progress.");
        return FinishAsync();
    }

    private async ValueTask FinishAsync()
    {
        try
        {
            if (resource is not { } owned)
                return; // Already reported failures are never replayed by later disposal.
            resource = null;
            try
            {
                if (rollbackAllowed)
                {
                    // No request token is accepted or linked. Expiry requests cancellation;
                    // it never races the await, abandons work or starts a second attempt.
                    try
                    {
                        using var budget = new CancellationTokenSource(settings.RecoveryRollbackTimeout, timeProvider);
                        try
                        {
                            await owned.RollbackAsync(step, budget.Token).ConfigureAwait(false);
                            completion = ExecutionRecoveryPolicy.PreserveCompletion(completion, ExecutionCompletion.RolledBack);
                        }
                        catch (Exception rollback)
                        {
                            var reported = ExecutionFailureContexts.Get(rollback);
                            completion = ExecutionRecoveryPolicy.PreserveCompletion(completion,
                                reported?.TransactionId == transactionId && reported.Completion == ExecutionCompletion.RolledBack
                                    ? ExecutionCompletion.RolledBack : ExecutionCompletion.Unknown);
                            var cause = rollback is OperationCanceledException canceled &&
                                canceled.CancellationToken == budget.Token && budget.IsCancellationRequested
                                    ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown;
                            failures.AddReported(rollback, ExecutionFailureStage.Recovery, cause);
                        }
                    }
                    catch (Exception setup)
                    {
                        // Timer setup/disposal cannot skip cleanup or overwrite established
                        // completion. A setup failure alone does not imply database dispatch.
                        failures.Add(setup, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
                    }
                }

                try { await owned.DisposeTransactionAsync(step).ConfigureAwait(false); }
                catch (Exception cleanup) { failures.AddReported(cleanup, ExecutionFailureStage.Cleanup); }
                try { await owned.DisposeConnectionAsync(step).ConfigureAwait(false); }
                catch (Exception cleanup) { failures.AddReported(cleanup, ExecutionFailureStage.Cleanup); }

                if (failures.Primary is { } primary)
                {
                    // The helper owns this terminal boundary; callers cannot recover a resource
                    // it has already disposed/retired, even when a disposal attempt failed.
                    var context = failures.Snapshot(new(), completion, ExecutionRecoveryActions.None, transactionId);
                    Volatile.Write(ref failureContext, context);
                    ExecutionFailureContexts.Attach(primary, context);
                }
            }
            finally
            {
                step.Dispose();
                owner.Dispose();
            }
            failures.ThrowIfAny();
        }
        finally { Volatile.Write(ref active, 0); }
    }
}
