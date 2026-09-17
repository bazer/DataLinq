using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace DataLinq.Execution;

// Internal evidence vocabulary. Public diagnostic declarations/mappings remain W3 work.
internal enum ExecutionFailureCause { Unknown, Cancellation, Timeout, ProviderError, MaterializationError }
internal enum ExecutionFailureStage { Validation, Initialization, CommandExecution, RowLoading, Materialization, Cleanup, Recovery, Callback, Commit, Finalization }
internal enum ExecutionCompletion { NotApplicable, NotAttempted, Committed, RolledBack, Unknown }
internal enum ExecutionEffects { Unknown, NoStatement, OrdinaryRead, Mutation, Initialization }
internal enum TransactionIntegrity { Unknown, Confirmed, Lost }
[Flags]
internal enum ExecutionRecoveryActions { None = 0, Continue = 1, Rollback = 2, Dispose = 4 }

/// <summary>Provider-recorded evidence, never inferred from an open connection or a canceled token.</summary>
internal sealed record ReadFailureEvidence(
    ExecutionFailureCause Cause = ExecutionFailureCause.Unknown,
    ExecutionEffects Effects = ExecutionEffects.Unknown,
    TransactionIntegrity Integrity = TransactionIntegrity.Unknown,
    bool RollbackAvailable = false);

/// <summary>Optional, I/O-free classification of settled provider work after reader cleanup.</summary>
internal interface IAsyncReadFailureEvidence
{
    ReadFailureEvidence GetReadFailureEvidence(Exception failure);
}

internal sealed record ExecutionSecondaryFailure(
    ExecutionFailureCause Cause, ExecutionFailureStage Stage, Exception Exception);

internal sealed class ExecutionFailureContext
{
    internal ExecutionFailureCause Cause { get; }
    internal ExecutionFailureStage Stage { get; }
    internal ExecutionCompletion Completion { get; }
    internal ExecutionRecoveryActions Recovery { get; }
    internal uint? TransactionId { get; }
    internal IReadOnlyList<ExecutionSecondaryFailure> SecondaryFailures { get; }

    internal ExecutionFailureContext(ExecutionFailureCause cause, ExecutionFailureStage stage,
        ExecutionCompletion completion, ExecutionRecoveryActions recovery, uint? transactionId,
        IEnumerable<ExecutionSecondaryFailure> secondaryFailures)
    {
        Cause = cause;
        Stage = stage;
        Completion = completion;
        Recovery = recovery;
        TransactionId = transactionId;
        SecondaryFailures = Array.AsReadOnly(secondaryFailures.ToArray());
    }

    internal ExecutionFailureContext AfterRecovery(ExecutionCompletion completion, ExecutionRecoveryActions recovery) =>
        new(Cause, Stage, ExecutionRecoveryPolicy.PreserveCompletion(Completion, completion), recovery,
            TransactionId, SecondaryFailures);
}

internal static class ExecutionRecoveryPolicy
{
    internal static ExecutionRecoveryActions ForReadFailure(ReadFailureEvidence evidence, bool cleanupSucceeded)
    {
        var actions = ExecutionRecoveryActions.Dispose;
        if (!cleanupSucceeded || evidence.Effects == ExecutionEffects.Initialization || evidence.Integrity == TransactionIntegrity.Lost)
            return actions;
        if (evidence.RollbackAvailable)
            actions |= ExecutionRecoveryActions.Rollback;
        if (evidence.Integrity == TransactionIntegrity.Confirmed &&
            evidence.Effects is ExecutionEffects.NoStatement or ExecutionEffects.OrdinaryRead)
            actions |= ExecutionRecoveryActions.Continue;
        return actions;
    }

    // Later rollback/cleanup cannot prove that an uncertain earlier commit never happened.
    // Conversely, later cleanup failure cannot erase an already confirmed completion.
    internal static ExecutionCompletion PreserveCompletion(ExecutionCompletion previous, ExecutionCompletion observed) =>
        previous == ExecutionCompletion.NotAttempted ? observed : previous;
}

/// <summary>Collects failures in encounter order without replacing or flattening original exceptions.</summary>
internal sealed class ExecutionFailures
{
    private ExceptionDispatchInfo? primary;
    private ExecutionFailureCause cause;
    private ExecutionFailureStage stage;
    private readonly List<ExecutionSecondaryFailure> secondary = [];
    internal Exception? Primary => primary?.SourceException;
    internal bool HasCleanupFailure { get; private set; }
    internal Exception? FirstCleanupFailure { get; private set; }

    internal void Add(Exception exception, ExecutionFailureCause failureCause, ExecutionFailureStage failureStage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (failureStage == ExecutionFailureStage.Cleanup)
        {
            HasCleanupFailure = true;
            FirstCleanupFailure ??= exception;
        }
        if (primary is null)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
            cause = failureCause;
            stage = failureStage;
        }
        else if (!ReferenceEquals(exception, Primary) && !secondary.Any(x => ReferenceEquals(x.Exception, exception)))
            secondary.Add(new(failureCause, failureStage, exception));
    }

    internal void AddReported(Exception exception, ExecutionFailureStage fallbackStage)
    {
        var context = ExecutionFailureContexts.Get(exception);
        Add(exception, context?.Cause ?? ExecutionFailureCause.Unknown, context?.Stage ?? fallbackStage);
        if (context is not null)
            foreach (var failure in context.SecondaryFailures)
                Add(failure.Exception, failure.Cause, failure.Stage);
    }

    internal ExecutionFailureContext Snapshot(ReadFailureEvidence evidence, ExecutionCompletion completion,
        ExecutionRecoveryActions recovery, uint? transactionId) =>
        new(cause == ExecutionFailureCause.Unknown && stage is ExecutionFailureStage.CommandExecution or ExecutionFailureStage.RowLoading or ExecutionFailureStage.Cleanup
                ? evidence.Cause : cause,
            stage == ExecutionFailureStage.CommandExecution && evidence.Effects == ExecutionEffects.Initialization
                ? ExecutionFailureStage.Initialization : stage,
            completion, recovery, transactionId, secondary);

    internal void ThrowIfAny() => primary?.Throw();
}

/// <summary>Direct exception lookup; snapshots contain no live transaction, command or connection.</summary>
internal static class ExecutionFailureContexts
{
    private sealed class Holder { internal ExecutionFailureContext? Value; }
    private static readonly ConditionalWeakTable<Exception, Holder> contexts = new();

    internal static ExecutionFailureContext? Get(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return contexts.TryGetValue(exception, out var holder) ? Volatile.Read(ref holder.Value) : null;
    }

    internal static void Attach(Exception exception, ExecutionFailureContext context) =>
        Volatile.Write(ref contexts.GetValue(exception, static _ => new Holder()).Value, context);
}
