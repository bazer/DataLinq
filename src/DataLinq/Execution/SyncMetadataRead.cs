using System;
using DataLinq.ErrorHandling;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Execution;

internal static class SyncMetadataRead
{
    internal static Option<DatabaseDefinition, IDLOptionFailure> ReadValidationMetadataCore(
        this IDatabaseProvider provider, TimeSpan? commandTimeout = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var settings = MetadataReadSettings.Runtime(commandTimeout, log);
        var capability = provider as ISyncProviderMetadataSource
            ?? throw new NotSupportedException("This provider does not support synchronous validation metadata reads.");
        var plan = capability.CaptureValidationMetadata(settings);
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        return Execute(plan, settings, provider.TelemetryInstanceId);
    }

    private static Option<DatabaseDefinition, IDLOptionFailure> Execute(ISyncMetadataReadPlan plan,
        MetadataReadSettings settings, string? providerInstanceId)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ISyncMetadataSession? session = null;
        SyncMetadataReadContext? context = null;
        var failures = new ExecutionFailures();
        Option<DatabaseDefinition, IDLOptionFailure> result = default;
        var resultAvailable = false;
        var stage = ExecutionFailureStage.Validation;
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try
        {
            session = plan.CreateSession() ?? throw new InvalidOperationException("Metadata capture created no session.");
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            context = new(session, settings.CommandTimeoutSeconds, providerInstanceId);
            stage = ExecutionFailureStage.Initialization;
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            session.Open();
            stage = ExecutionFailureStage.Materialization;
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            result = plan.Read(context);
            resultAvailable = true;
        }
        catch (Exception failure)
        {
            ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
            context?.DiscardCompletedCommandReport(failure);
            context?.CopyFailuresTo(failures);
            failures.AddReported(failure, stage, stage == ExecutionFailureStage.Materialization
                ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.Unknown, ExecutionOperationKind.MetadataRead);
        }
        finally
        {
            context?.Close(failures);
            if (session is not null)
            {
                using var cleanup = ExecutionFailureScope.Begin();
                try { session.Dispose(); }
                catch (Exception failure) { failures.AddCleanup(failure); }
            }
        }

        if (failures.Primary is null &&
            (result.TryUnwrap(out var metadata, out var readFailure) ? metadata is null : readFailure is null))
            failures.Add(new InvalidOperationException("Metadata reading returned no complete definition."),
                ExecutionFailureCause.InvalidOperation, ExecutionFailureStage.Materialization, ExecutionOperationKind.MetadataRead);
        if (failures.Primary is not { } primary) return result;
        ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
            ExecutionRecoveryActions.None, null, ExecutionOperationKind.MetadataRead, providerInstanceId,
            providerIdentityIsAuthoritative: true));
        if (primary is OperationCanceledException) failures.ThrowIfAny();
        IDLOptionFailure operational = DLOptionFailure.Fail(DLFailureType.Exception, primary);
        if (resultAvailable && !result.TryUnwrap(out _, out var original) && original is not null)
            return DLOptionFailure.AggregateFail([original, operational]);
        return operational;
    }
}
