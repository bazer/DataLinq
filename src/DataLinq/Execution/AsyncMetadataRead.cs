using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.ErrorHandling;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using ThrowAway;
using ThrowAway.Extensions;

namespace DataLinq.Execution;

internal static class AsyncMetadataRead
{
    internal static Task<Option<DatabaseDefinition, IDLOptionFailure>> ParseDatabaseAsyncCore(
        this IMetadataFromSqlFactory factory, string name, string csTypeName, string csNamespace,
        string databaseName, string connectionString, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(csTypeName);
        ArgumentNullException.ThrowIfNull(csNamespace);
        ArgumentNullException.ThrowIfNull(databaseName);
        ArgumentNullException.ThrowIfNull(connectionString);
        var capability = factory as IAsyncMetadataFactory
            ?? throw new NotSupportedException("This metadata factory does not support asynchronous reading.");
        var settings = MetadataReadSettings.Import(capability.Options);
        var request = new MetadataImportRequest(name, csTypeName, csNamespace, databaseName, connectionString, settings);
        return Begin(capability.CaptureImport(request), settings, token);
    }

    internal static Task<Option<DatabaseDefinition, IDLOptionFailure>> ReadValidationMetadataAsyncCore(
        this IDatabaseProvider provider, TimeSpan? commandTimeout = null, Action<string>? log = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var settings = MetadataReadSettings.Runtime(commandTimeout, log);
        var capability = provider as IAsyncProviderMetadataSource
            ?? throw new NotSupportedException("This provider does not support asynchronous validation metadata reads.");
        return Begin(capability.CaptureValidationMetadata(settings), settings, token, provider.TelemetryInstanceId);
    }

    private static Task<Option<DatabaseDefinition, IDLOptionFailure>> Begin(IAsyncMetadataReadPlan plan, MetadataReadSettings settings,
        CancellationToken token, string? providerInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        plan.Validate();
        token.ThrowIfCancellationRequested();
        return ExecuteAsync(plan, settings, token, providerInstanceId);
    }

    private static async Task<Option<DatabaseDefinition, IDLOptionFailure>> ExecuteAsync(IAsyncMetadataReadPlan plan, MetadataReadSettings settings,
        CancellationToken token, string? providerInstanceId)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        IAsyncMetadataSession? session = null;
        MetadataReadContext? context = null;
        var failures = new ExecutionFailures();
        Option<DatabaseDefinition, IDLOptionFailure> result = default;
        var resultAvailable = false;
        var stage = ExecutionFailureStage.Validation;
        var localCause = ExecutionFailureCause.Unknown;
        var occurrence = ExecutionFailureContexts.CaptureOccurrence();
        try
        {
            session = plan.CreateSession();
            if (session is null)
            {
                localCause = ExecutionFailureCause.InvalidOperation;
                throw new InvalidOperationException("Metadata capture created no session.");
            }
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            context = new(session.Access, session.Commands, settings.CommandTimeoutSeconds, token,
                new(ExecutionOperationKind.MetadataRead, providerInstanceId));
            token.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.Initialization;
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            await session.OpenAsync(token).ConfigureAwait(false);
            occurrence = ExecutionFailureContexts.CaptureOccurrence();
            token.ThrowIfCancellationRequested();
            stage = ExecutionFailureStage.Materialization;
            result = await plan.ReadAsync(context, token).ConfigureAwait(false);
            resultAvailable = true;
        }
        catch (Exception failure)
        {
            // Opening and successful context commands have already settled;
            // only a new report can describe the parser's later local failure.
            ExecutionFailureContexts.DiscardEarlierReport(failure, occurrence);
            context?.DiscardCompletedCommandReport(failure);
            context?.CopyFailuresTo(failures);
            failures.AddReported(failure, stage,
                failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                    ? ExecutionFailureCause.Cancellation : stage == ExecutionFailureStage.Materialization
                        ? ExecutionFailureCause.MaterializationError : localCause, ExecutionOperationKind.MetadataRead);
        }
        finally
        {
            if (context is not null) await context.CloseAsync(failures).ConfigureAwait(false);
            if (session is not null)
            {
                using var cleanupScope = ExecutionFailureScope.Begin();
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { failures.AddCleanup(cleanup); }
            }
        }
        if (failures.Primary is null)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (result.TryUnwrap(out var metadata, out var readFailure) ? metadata is null : readFailure is null)
                    throw new InvalidOperationException("Metadata reading returned no complete definition.");
            }
            catch (Exception failure)
            {
                failures.Add(failure, failure is OperationCanceledException ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.InvalidOperation,
                    ExecutionFailureStage.Materialization);
            }
        }
        if (failures.Primary is not { } primary) return result;
        ExecutionFailureContexts.Attach(primary, failures.Snapshot(new(), ExecutionCompletion.NotApplicable,
            ExecutionRecoveryActions.None, transactionId: null, ExecutionOperationKind.MetadataRead, providerInstanceId,
            providerIdentityIsAuthoritative: true));
        if (primary is OperationCanceledException) failures.ThrowIfAny();
        IDLOptionFailure operational = DLOptionFailure.Fail(DLFailureType.Exception, primary);
        if (resultAvailable && result.TryUnwrap(out _, out var original) == false && original is not null)
            return DLOptionFailure.AggregateFail([original, operational]);
        return operational;
    }
}
