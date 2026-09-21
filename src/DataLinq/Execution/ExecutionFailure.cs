using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace DataLinq.Execution;

// Internal evidence vocabulary. Public diagnostic declarations/mappings remain W3 work.
internal enum ExecutionFailureCause { Unknown, Cancellation, Timeout, ProviderError, MaterializationError, ApplicationError, LocalFinalizationError, InvalidOperation }
internal enum ExecutionOperationKind
{
    Unknown, Query, KeyLookup, RelationLoad, Insert, Update, Save, Delete, Commit, Rollback,
    Dispose, TransactionCallback, RawCommand, MetadataRead, SchemaValidation, ExistenceCheck,
    Provisioning, ProviderConfiguration
}
internal enum ExecutionFailureStage { Validation, Initialization, CommandExecution, RowLoading, Materialization, Cleanup, Recovery, Callback, Commit, Finalization, Unknown, Notification, CacheRecovery }
internal enum ExecutionCompletion { NotApplicable, NotAttempted, Committed, RolledBack, Unknown }
internal enum ExecutionEffects { Unknown, NoStatement, OrdinaryRead, Mutation, Initialization }
internal enum TransactionIntegrity { Unknown, Confirmed, Lost }
[Flags]
internal enum ExecutionRecoveryActions { None = 0, Continue = 1, Rollback = 2, Dispose = 4, FinishActiveOperation = 8 }

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
    ExecutionFailureCause Cause, ExecutionFailureStage Stage, Exception Exception,
    ExecutionOperationKind Operation = ExecutionOperationKind.Unknown);

// Captured only at an owned reporting boundary, before another throw can replace
// the direct exception lookup. Carries an immutable snapshot, never live authority.
internal readonly record struct ObservedExecutionFailure(Exception Exception, ExecutionFailureContext? Context)
{
    internal static ObservedExecutionFailure Capture(Exception exception) => new(exception, ExecutionFailureContexts.GetCurrent(exception));
}

internal sealed class ExecutionFailureContext
{
    internal ExecutionFailureCause Cause { get; }
    internal ExecutionFailureStage Stage { get; }
    internal ExecutionCompletion Completion { get; }
    internal ExecutionRecoveryActions Recovery { get; }
    internal uint? TransactionId { get; }
    internal ExecutionOperationKind Operation { get; }
    internal string? ProviderInstanceId { get; }
    internal ExecutionOperationKind? ActiveOperation { get; }
    internal IReadOnlyList<ExecutionSecondaryFailure> SecondaryFailures { get; }
    internal bool HasCleanupFailure { get; }
    internal ExecutionFailureScope? Scope { get; init; } = ExecutionFailureScope.Current;
    internal CommandDispatchEvidence? CommandDispatch { get; init; }

    internal ExecutionFailureContext(ExecutionFailureCause cause, ExecutionFailureStage stage,
        ExecutionCompletion completion, ExecutionRecoveryActions recovery, uint? transactionId,
        IEnumerable<ExecutionSecondaryFailure> secondaryFailures, bool hasCleanupFailure = false,
        ExecutionOperationKind operation = ExecutionOperationKind.Unknown, string? providerInstanceId = null,
        ExecutionOperationKind? activeOperation = null)
    {
        Cause = cause;
        Stage = stage;
        Completion = completion;
        Recovery = recovery;
        TransactionId = transactionId;
        Operation = operation;
        ProviderInstanceId = providerInstanceId;
        ActiveOperation = activeOperation;
        SecondaryFailures = Array.AsReadOnly(secondaryFailures.ToArray());
        HasCleanupFailure = hasCleanupFailure || stage is ExecutionFailureStage.Cleanup or ExecutionFailureStage.CacheRecovery ||
            SecondaryFailures.Any(x => x.Stage is ExecutionFailureStage.Cleanup or ExecutionFailureStage.CacheRecovery);
    }

    internal ExecutionFailureContext AfterRecovery(ExecutionCompletion completion, ExecutionRecoveryActions recovery) =>
        new(Cause, Stage, ExecutionRecoveryPolicy.PreserveCompletion(Completion, completion), recovery,
            TransactionId, SecondaryFailures, HasCleanupFailure, Operation, ProviderInstanceId, ActiveOperation)
            { Scope = Scope, CommandDispatch = CommandDispatch };
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
    private readonly ExecutionFailureScope? scope;
    internal ExecutionFailures() : this(ExecutionFailureScope.Current) { }
    internal ExecutionFailures(ExecutionFailureScope? scope) => this.scope = scope;
    private ExceptionDispatchInfo? primary;
    private ExecutionFailureCause cause;
    private ExecutionFailureStage stage;
    private ExecutionOperationKind operation;
    private ExecutionOperationKind unreportedOperation;
    private string? providerInstanceId;
    private uint? primaryTransactionId;
    private ExecutionOperationKind? activeOperation;
    private CommandDispatchEvidence? commandDispatch;
    private readonly List<ExecutionSecondaryFailure> secondary = [];
    internal Exception? Primary => primary?.SourceException;
    internal bool HasCleanupFailure { get; private set; }
    internal Exception? FirstCleanupFailure { get; private set; }

    internal void RecordCleanupFailure(Exception exception)
    {
        HasCleanupFailure = true;
        FirstCleanupFailure ??= exception;
    }

    internal void Add(Exception exception, ExecutionFailureCause failureCause, ExecutionFailureStage failureStage,
        ExecutionOperationKind failureOperation = ExecutionOperationKind.Unknown)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var context = Observe(exception);
        var observedOperation = context is { Operation: not ExecutionOperationKind.Unknown }
            ? context.Operation : failureOperation;
        if (primary is null) unreportedOperation = failureOperation;
        AddCore(exception, failureCause, failureStage, observedOperation, context);
    }

    private void AddCore(Exception exception, ExecutionFailureCause failureCause, ExecutionFailureStage failureStage,
        ExecutionOperationKind observedOperation, ExecutionFailureContext? context)
    {
        if (failureStage is ExecutionFailureStage.Cleanup or ExecutionFailureStage.CacheRecovery)
            RecordCleanupFailure(exception);
        if (primary is null)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
            cause = failureCause;
            stage = failureStage;
            operation = observedOperation;
            providerInstanceId = context?.ProviderInstanceId;
            primaryTransactionId = context?.TransactionId;
            activeOperation = context?.ActiveOperation;
            commandDispatch = context?.CommandDispatch;
        }
        else if (!ReferenceEquals(exception, Primary) && !secondary.Any(x => ReferenceEquals(x.Exception, exception)))
            secondary.Add(new(failureCause, failureStage, exception, observedOperation));
    }

    internal void AddReported(Exception exception, ExecutionFailureStage fallbackStage,
        ExecutionFailureCause fallbackCause = ExecutionFailureCause.Unknown,
        ExecutionOperationKind fallbackOperation = ExecutionOperationKind.Unknown)
        => AddObserved(new(exception, Observe(exception)), fallbackStage, fallbackCause, fallbackOperation);

    // An owned coordinator can hand off facts it already captured, including when
    // application code suppressed ExecutionContext flow. Never reread the exception.
    internal void AddObserved(ObservedExecutionFailure observed, ExecutionFailureStage fallbackStage,
        ExecutionFailureCause fallbackCause = ExecutionFailureCause.Unknown,
        ExecutionOperationKind fallbackOperation = ExecutionOperationKind.Unknown)
    {
        var (exception, context) = observed;
        // Identity deduplication can omit a cleanup occurrence when a provider throws
        // the same exception for execution and disposal. Preserve that safety fact.
        if (context?.HasCleanupFailure == true) HasCleanupFailure = true;
        if (primary is null) unreportedOperation = fallbackOperation;
        AddCore(exception, context?.Cause ?? fallbackCause, context?.Stage ?? fallbackStage,
            context is { Operation: not ExecutionOperationKind.Unknown } ? context.Operation : fallbackOperation, context);
        if (context is not null)
            foreach (var failure in context.SecondaryFailures)
                AddCore(failure.Exception, failure.Cause, failure.Stage, failure.Operation, null);
    }

    internal void AddCleanup(Exception exception)
    {
        // A direct disposal failure establishes a cleanup failure even if the same
        // exception already carries context from earlier execution (or is the primary).
        Add(exception, Observe(exception)?.Cause ?? ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup,
            ExecutionOperationKind.Dispose);
        AddReported(exception, ExecutionFailureStage.Cleanup);
    }

    internal ExecutionFailureContext Snapshot(ReadFailureEvidence evidence, ExecutionCompletion completion,
        ExecutionRecoveryActions recovery, uint? transactionId,
        ExecutionOperationKind fallbackOperation = ExecutionOperationKind.Unknown, string? fallbackProviderInstanceId = null,
        bool providerIdentityIsAuthoritative = false)
    {
        // The current boundary supplies the actual transaction. A provider may
        // reuse an exception from an unrelated invocation; its old identity must
        // not migrate into this operation or imply another transaction's outcome.
        // Standalone factories can explicitly establish that no provider identity
        // exists; a null fallback otherwise means this layer has not supplied one.
        var foreign = primaryTransactionId is not null && primaryTransactionId != transactionId ||
            providerInstanceId is not null && (fallbackProviderInstanceId is not null || providerIdentityIsAuthoritative) &&
                providerInstanceId != fallbackProviderInstanceId;
        var knownOperation = foreign ? unreportedOperation : operation;
        return new(cause == ExecutionFailureCause.Unknown && stage is ExecutionFailureStage.CommandExecution or ExecutionFailureStage.RowLoading or ExecutionFailureStage.Cleanup
                ? evidence.Cause : cause,
            stage == ExecutionFailureStage.CommandExecution && evidence.Effects == ExecutionEffects.Initialization
                ? ExecutionFailureStage.Initialization : stage,
            completion, recovery, transactionId, secondary, HasCleanupFailure,
            knownOperation == ExecutionOperationKind.Unknown ? fallbackOperation : knownOperation,
            foreign ? fallbackProviderInstanceId : providerInstanceId ?? fallbackProviderInstanceId,
            foreign ? null : activeOperation) { Scope = scope, CommandDispatch = commandDispatch };
    }

    private ExecutionFailureContext? Observe(Exception exception)
    {
        var current = ExecutionFailureScope.Current;
        // Recovery/cleanup can create a narrower call beneath this collector.
        // Its newly thrown occurrence must not borrow an earlier sibling's facts.
        return ExecutionFailureContexts.GetObserved(exception,
            current is not null && (scope is null || scope.Contains(current)) ? current : scope);
    }

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

    internal static ExecutionFailureContext? GetCurrent(Exception exception) =>
        GetObserved(exception, ExecutionFailureScope.Current);

    internal static ExecutionFailureContext? GetObserved(Exception exception, ExecutionFailureScope? scope)
    {
        var context = Get(exception);
        return scope is null || scope.Contains(context?.Scope) ? context : null;
    }
}
