using System;
using System.Diagnostics;

namespace DataLinq.Execution;

/// <summary>
/// Synchronous logical-query reporting inside an existing read admission. This owns
/// diagnostic state only; it neither grants admission nor decides provider recovery.
/// </summary>
internal struct SyncQueryExecution(QueryTelemetryContext context, ReadExecutionIdentity identity, uint? transactionId)
{
    private QueryExecutionTelemetry telemetry = new(context, identity.Operation);
    private ExecutionFailures? failures;
    private ExecutionFailureContext? executionContext;

    internal bool Start()
    {
        var reportingScope = ExecutionFailureScope.Current;
        using var reporting = ExecutionFailureScope.Begin();
        try { telemetry.Start(); return true; }
        catch (Exception failure)
        {
            ExecutionActivity.AddFailure(failures ??= new(reportingScope), failure, identity.Operation);
            return false;
        }
    }

    internal bool MakeCurrent()
    {
        if (!telemetry.NeedsCurrent) return true;
        var reportingScope = ExecutionFailureScope.Current;
        using var reporting = ExecutionFailureScope.Begin();
        try { telemetry.MakeCurrent(); return true; }
        catch (Exception failure)
        {
            ExecutionActivity.AddFailure(failures ??= new(reportingScope), failure, identity.Operation);
            return false;
        }
    }

    internal bool RestoreCurrent(Activity? caller)
    {
        // Completion observers can stop the caller while the query is active.
        // Do not reintroduce that stopped activity into the ambient context.
        if (caller is { IsStopped: true }) caller = null;
        if (ReferenceEquals(Activity.Current, caller)) return true;
        var reportingScope = ExecutionFailureScope.Current;
        using var reporting = ExecutionFailureScope.Begin();
        try { Activity.Current = caller; return true; }
        catch (Exception failure)
        {
            ExecutionActivity.AddFailure(failures ??= new(reportingScope), failure, identity.Operation);
            return false;
        }
    }

    internal void RecordFailure(Exception failure, ExecutionFailureStage stage,
        ExecutionFailureCause cause = ExecutionFailureCause.Unknown)
    {
        var observed = ObservedExecutionFailure.Capture(failure);
        if (failures?.Primary is null) executionContext = observed.Context;
        (failures ??= new()).AddObserved(observed, stage, cause, identity.Operation);
    }

    internal void RecordCleanup(Exception failure, ExecutionFailureScope? reportingScope) =>
        (failures ??= new(reportingScope)).AddCleanup(failure);

    internal void Complete(bool succeeded) => telemetry.Complete(ref failures, succeeded);

    internal void ThrowIfAny()
    {
        if (failures?.Primary is not { } primary) return;
        // A child may already have settled completion/recovery. Only reuse facts
        // from this source, never a foreign transaction's diagnostic identity.
        var previous = executionContext is { } captured && captured.TransactionId == transactionId &&
            (captured.ProviderInstanceId is null || captured.ProviderInstanceId == identity.ProviderInstanceId)
            ? captured : null;
        ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(),
            previous?.Completion ?? (transactionId is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted),
            transactionId is null ? ExecutionRecoveryActions.None : failures.HasCleanupFailure
                ? ExecutionRecoveryActions.Dispose : previous?.Recovery ?? ExecutionRecoveryActions.Dispose,
            transactionId, identity.Operation, identity.ProviderInstanceId));
        failures.ThrowIfAny();
    }
}
