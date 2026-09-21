using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>One managed scalar read: validation, admission, execution/cleanup, conversion and failure publication.</summary>
internal static class AsyncScalarRead
{
    internal static async Task<T> ExecuteAsync<T>(IAsyncScalarSource source, Transaction? transaction,
        Func<object?, T> convert, CancellationToken token, ReadExecutionIdentity identity = default,
        QueryTelemetryContext telemetryContext = default)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var reportingScope = ExecutionFailureScope.Current;
        const string operation = "execute an asynchronous scalar query";
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);
        identity = identity.Bind(transaction);
        transaction?.EnsureCanRead(operation, operationKind: identity.Operation);
        source.Validate();
        if (source is IAsyncTransactionScalarSource && transaction is null)
            throw new InvalidOperationException("This scalar source requires a managed transaction owner.");
        token.ThrowIfCancellationRequested();
        using var ownership = transaction is null ? null : DataSourceAccess.BeginRead(transaction, operation, cancellationToken: token,
            operationKind: identity.Operation);
        var telemetry = new QueryExecutionTelemetry(telemetryContext);
        var stage = ExecutionFailureStage.Notification;
        var cause = ExecutionFailureCause.LocalFinalizationError;
        ExecutionFailures? failures = null;
        T result = default!;
        var succeeded = false;
        try
        {
            telemetry.Start();
            stage = ExecutionFailureStage.CommandExecution;
            cause = ExecutionFailureCause.Unknown;
            var value = await (source is IAsyncTransactionScalarSource ownedSource
                ? ownedSource.ExecuteScalarAsync(ownership!.Step, token)
                : source.ExecuteScalarAsync(token)).ConfigureAwait(false);
            stage = ExecutionFailureStage.Materialization;
            cause = ExecutionFailureCause.MaterializationError;
            // A request arriving after completed scalar execution/cleanup cannot undo
            // success. Conversion is local and remains inside transaction admission.
            using (ExecutionFailureScope.Begin())
            {
                try { result = convert(value); succeeded = true; }
                catch (Exception failure) { Record(failure); }
            }
        }
        catch (Exception failure) { Record(failure); }
        telemetry.Complete(ref failures, succeeded);
        if (failures?.Primary is { } primary)
        {
            var evidence = new ReadFailureEvidence();
            var assessmentSucceeded = true;
            if (source is IAsyncReadFailureEvidence classifier)
            {
                using var assessmentDiagnostics = ExecutionFailureScope.Begin();
                try { evidence = classifier.GetReadFailureEvidence(primary) ?? throw new InvalidOperationException("The provider returned no failure evidence."); }
                catch (Exception assessment)
                {
                    assessmentSucceeded = false;
                    failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery, identity.Operation);
                }
            }
            var recovery = ownership is null ? ExecutionRecoveryActions.None
                : ExecutionRecoveryPolicy.ForReadFailure(evidence, !failures.HasCleanupFailure && assessmentSucceeded);
            var context = failures.Snapshot(evidence, transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                recovery, transaction?.TransactionID, identity.Operation, identity.ProviderInstanceId);
            if (ownership is not null) transaction!.RecordAsyncReadFailure(ownership.Step, context);
            ExecutionFailureContexts.Attach(primary, context);
            ownership?.ReportFailure(primary);
            failures.ThrowIfAny();
        }
        return result;

        void Record(Exception failure) => (failures ??= new(reportingScope)).AddReported(failure, stage,
            failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : cause);
    }
}
