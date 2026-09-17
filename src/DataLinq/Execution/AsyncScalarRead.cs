using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>One managed scalar read: validation, admission, execution/cleanup, conversion and failure publication.</summary>
internal static class AsyncScalarRead
{
    internal static async Task<T> ExecuteAsync<T>(IAsyncScalarSource source, Transaction? transaction,
        Func<object?, T> convert, CancellationToken token)
    {
        const string operation = "execute an asynchronous scalar query";
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);
        transaction?.EnsureCanRead(operation);
        source.Validate();
        if (source is IAsyncTransactionScalarSource && transaction is null)
            throw new InvalidOperationException("This scalar source requires a managed transaction owner.");
        token.ThrowIfCancellationRequested();
        using var ownership = transaction is null ? null : DataSourceAccess.BeginRead(transaction, operation, cancellationToken: token);
        var stage = ExecutionFailureStage.CommandExecution;
        var cause = ExecutionFailureCause.Unknown;
        try
        {
            var value = await (source is IAsyncTransactionScalarSource ownedSource
                ? ownedSource.ExecuteScalarAsync(ownership!.Step, token)
                : source.ExecuteScalarAsync(token)).ConfigureAwait(false);
            stage = ExecutionFailureStage.Materialization;
            cause = ExecutionFailureCause.MaterializationError;
            // A request arriving after completed scalar execution/cleanup cannot undo
            // success. Conversion is local and remains inside transaction admission.
            return convert(value);
        }
        catch (Exception failure)
        {
            var failures = new ExecutionFailures();
            failures.AddReported(failure, stage, failure is OperationCanceledException canceled &&
                canceled.CancellationToken == token && token.IsCancellationRequested ? ExecutionFailureCause.Cancellation : cause);
            var evidence = new ReadFailureEvidence();
            var assessmentSucceeded = true;
            if (source is IAsyncReadFailureEvidence classifier)
            {
                try { evidence = classifier.GetReadFailureEvidence(failure) ?? throw new InvalidOperationException("The provider returned no failure evidence."); }
                catch (Exception assessment)
                {
                    assessmentSucceeded = false;
                    failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
                }
            }
            var recovery = ownership is null ? ExecutionRecoveryActions.None
                : ExecutionRecoveryPolicy.ForReadFailure(evidence, !failures.HasCleanupFailure && assessmentSucceeded);
            var context = failures.Snapshot(evidence, transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                recovery, transaction?.TransactionID);
            if (ownership is not null) transaction!.RecordAsyncReadFailure(ownership.Step, context);
            ExecutionFailureContexts.Attach(failure, context);
            ownership?.ReportFailure(failure);
            throw;
        }
    }
}
