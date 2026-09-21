using System;
using System.Diagnostics;
using DataLinq.Diagnostics;

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
        // Snapshot once: if no listener exists, skip both activity creation and
        // its callback scope. Metrics and timing still start, and a listener
        // enabled during execution can observe completion measurements.
        var createActivity = context.TableName is not null && DataLinqTelemetry.HasActivityListeners;
        var reportingScope = ExecutionFailureScope.Current;
        using ExecutionFailureScope.Call? reporting = createActivity ? ExecutionFailureScope.Begin() : null;
        try { telemetry.Start(createActivity); return true; }
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
        => ExecutionActivity.RestoreCurrent(caller, ref failures, identity.Operation);

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
