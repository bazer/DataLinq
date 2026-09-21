using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;

namespace DataLinq;

public abstract partial class DatabaseAccess
{
    private TResult ExecuteCommandTelemetry<TResult>(IDbCommand command, string kind, bool transactional,
        TransactionType? transactionType, Func<TResult> execute)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var reportingScope = ExecutionFailureScope.Current;
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(execute);
        var telemetry = new CommandExecutionTelemetry(TelemetryContext, kind, transactional, transactionType);
        ExecutionFailures? failures = null;
        using (ExecutionFailureScope.Begin())
        {
            try { telemetry.Start(command); }
            catch (Exception failure) { ExecutionActivity.AddFailure(failures ??= new(reportingScope), failure, ExecutionOperationKind.Unknown); }
        }
        var result = default(TResult)!;
        var succeeded = false;
        if (failures is null)
        {
            using var dispatch = ExecutionFailureScope.Begin();
            try
            {
                telemetry.BeginDispatch();
                result = execute();
                succeeded = true;
            }
            catch (Exception failure) { (failures ??= new(reportingScope)).AddReported(failure, ExecutionFailureStage.CommandExecution); }
        }
        telemetry.Complete(ref failures, succeeded);
        // A reader cannot transfer to its caller when reporting fails after acquisition.
        // Scalar results and the borrowed command remain untouched.
        if (failures is not null && succeeded && kind == "reader" && result is IDisposable reader)
        {
            using var cleanup = ExecutionFailureScope.Begin();
            try { reader.Dispose(); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        ThrowCommandTelemetryFailures(failures, transactional, command, telemetry.Dispatched);
        return result;
    }

    // Native adapters call these only around actual provider dispatch, after validation,
    // connection opening and transaction initialization. No admission or fallback is added.
    internal Task<TResult> ExecuteCommandWithTelemetryAsync<TResult>(IDbCommand command, string kind, bool transactional,
        TransactionType? transactionType, CancellationToken token, Func<Task<TResult>> execute)
    {
        if (kind is not ("scalar" or "non_query")) throw new ArgumentOutOfRangeException(nameof(kind));
        return ExecuteCommandTelemetryAsync(command, kind, transactional, transactionType, token, execute, null);
    }

    internal Task<IAsyncDataReader> ExecuteReaderWithTelemetryAsync(IDbCommand command, bool transactional,
        TransactionType? transactionType, CancellationToken token, Func<Task<IAsyncDataReader>> execute) =>
        ExecuteCommandTelemetryAsync(command, "reader", transactional, transactionType, token, execute,
            static reader => reader.DisposeAsync());

    private async Task<TResult> ExecuteCommandTelemetryAsync<TResult>(IDbCommand command, string kind, bool transactional,
        TransactionType? transactionType, CancellationToken token, Func<Task<TResult>> execute,
        Func<TResult, ValueTask>? disposeUnreturnedReader)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var reportingScope = ExecutionFailureScope.Current;
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(execute);
        try { token.ThrowIfCancellationRequested(); }
        catch (OperationCanceledException failure)
        {
            var canceled = new ExecutionFailures(reportingScope);
            canceled.Add(failure, ExecutionFailureCause.Cancellation, ExecutionFailureStage.CommandExecution);
            ThrowCommandTelemetryFailures(canceled, transactional, command, dispatched: false);
            throw;
        }
        var telemetry = new CommandExecutionTelemetry(TelemetryContext, kind, transactional, transactionType);
        ExecutionFailures? failures = null;
        using (ExecutionFailureScope.Begin())
        {
            try { telemetry.Start(command); }
            catch (Exception failure) { ExecutionActivity.AddFailure(failures ??= new(reportingScope), failure, ExecutionOperationKind.Unknown); }
        }
        var result = default(TResult)!;
        var succeeded = false;
        if (failures is null)
        {
            using var dispatch = ExecutionFailureScope.Begin();
            try
            {
                token.ThrowIfCancellationRequested();
                telemetry.BeginDispatch();
                result = await execute().ConfigureAwait(false);
                succeeded = true;
            }
            catch (Exception failure)
            {
                (failures ??= new(reportingScope)).AddReported(failure, ExecutionFailureStage.CommandExecution,
                    failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                        ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
            }
        }
        telemetry.Complete(ref failures, succeeded);
        if (failures is not null && succeeded && result is not null && disposeUnreturnedReader is not null)
        {
            using var cleanup = ExecutionFailureScope.Begin();
            try { await disposeUnreturnedReader(result).ConfigureAwait(false); }
            catch (Exception failure) { failures.AddCleanup(failure); }
        }
        ThrowCommandTelemetryFailures(failures, transactional, command, telemetry.Dispatched);
        return result;
    }

    private void ThrowCommandTelemetryFailures(ExecutionFailures? failures, bool transactional, IDbCommand command, bool dispatched)
    {
        if (failures?.Primary is not { } primary) return;
        var context = failures.Snapshot(new(),
            transactional ? ExecutionCompletion.NotAttempted : ExecutionCompletion.NotApplicable,
            transactional ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.None,
            managedTransaction?.TransactionID, fallbackProviderInstanceId: DiagnosticProviderInstanceId,
            providerIdentityIsAuthoritative: true);
        // Override any nested command's evidence with facts about this actual command.
        CommandDispatchEvidence.Attach(primary, context, command, dispatched);
        failures.ThrowIfAny();
    }
}
