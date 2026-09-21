using System;
using System.Diagnostics;
using DataLinq.Diagnostics;

namespace DataLinq.Execution;

/// <summary>Owned activity finalization; observer failures are diagnostic facts, never admission authority.</summary>
internal static class ExecutionActivity
{
    internal static bool RestoreCurrent(Activity? caller, ref ExecutionFailures? failures, ExecutionOperationKind operation)
    {
        if (caller is { IsStopped: true }) caller = null;
        if (ReferenceEquals(Activity.Current, caller)) return true;
        var reportingScope = ExecutionFailureScope.Current;
        using var restoration = ExecutionFailureScope.Begin();
        try { Activity.Current = caller; return true; }
        catch (Exception failure)
        {
            AddFailure(failures ??= new(reportingScope), failure, operation);
            return false;
        }
    }

    internal static void Complete(ref Activity? activity, ref ExecutionFailures? failures, bool succeeded,
        ExecutionOperationKind operation, string? outcome = null)
    {
        var completedActivity = activity;
        activity = null;
        if (completedActivity is null) return;
        var reportingScope = ExecutionFailureScope.Current;
        using (ExecutionFailureScope.Begin())
        {
            try
            {
                if (failures?.Primary is { } failure) DataLinqTelemetry.RecordException(completedActivity, failure);
                else if (!succeeded) completedActivity.SetStatus(ActivityStatusCode.Error);
                completedActivity.SetTag("datalinq.outcome", outcome ?? (succeeded && failures?.Primary is null ? "success" : "failure"));
            }
            catch (Exception failure) { AddFailure(failures ??= new(reportingScope), failure, operation); }
        }
        using (ExecutionFailureScope.Begin())
        {
            try { completedActivity.Dispose(); }
            catch (Exception failure) { AddFailure(failures ??= new(reportingScope), failure, operation); }
        }
        // Activity.Stop notifies listeners before restoring Current. A listener can
        // throw after the activity became stopped, leaving it ambient in this call.
        if (ReferenceEquals(Activity.Current, completedActivity))
        {
            using var restoration = ExecutionFailureScope.Begin();
            try { Activity.Current = completedActivity.Parent is { IsStopped: false } parent ? parent : null; }
            catch (Exception failure) { AddFailure(failures ??= new(reportingScope), failure, operation); }
        }
    }

    internal static void AddFailure(ExecutionFailures failures, Exception failure, ExecutionOperationKind operation) =>
        failures.Add(failure, ExecutionFailureCause.LocalFinalizationError, ExecutionFailureStage.Finalization, operation);

    internal static void AddFailure(ref ExecutionFailures? failures, Exception failure, ExecutionOperationKind operation) =>
        AddFailure(failures ??= new(), failure, operation);
}
