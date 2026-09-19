using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>
/// One captured, finite read. Native resources settle before the result is constructed;
/// private transaction admission also covers local materialization and publication.
/// </summary>
internal sealed class AsyncBufferedRead<TResult>(
    IDataSourceAccess dataSource, IAsyncReaderSource source,
    Action<IAsyncDataReader> addRow, Func<TResult> complete,
    TransactionOperationGate.Step? owner = null, bool firstRowOnly = false,
    ExecutionOperationKind operationKind = ExecutionOperationKind.Unknown) : IAsyncReadFailureEvidence
{
    private const string Operation = "load asynchronous source rows";
    private readonly ReadExecutionIdentity identity = ReadExecutionIdentity.Capture(dataSource, operationKind, owner);
    private int executed;

    internal void Validate()
    {
        DataSourceAccess.EnsureReadAllowed(dataSource, Operation, owner, identity.Operation);
        source.Validate();
        if (source is IAsyncTransactionReaderSource && dataSource is not Transaction)
            throw new InvalidOperationException("This reader source requires a managed transaction owner.");
        if (Volatile.Read(ref executed) != 0)
            throw new InvalidOperationException("A captured buffered read can execute only once.");
    }

    internal Task<TResult> ExecuteAsync(CancellationToken token) =>
        ExecuteAsync(static (result, _, _) => result, token);

    public ReadFailureEvidence GetReadFailureEvidence(Exception failure) =>
        source is IAsyncReadFailureEvidence classifier ? classifier.GetReadFailureEvidence(failure) : new();

    internal async Task<T> ExecuteAsync<T>(
        Func<TResult, TransactionOperationGate.Step?, CancellationToken, T> materialize,
        CancellationToken token,
        Func<TransactionOperationGate.Step?, (bool Found, T Result)>? tryCached = null)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        ArgumentNullException.ThrowIfNull(materialize);
        Validate();
        token.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref executed, 1) != 0)
            throw new InvalidOperationException("A captured buffered read can execute only once.");
        using var ownership = DataSourceAccess.BeginRead(dataSource, Operation, owner, token, identity.Operation);
        var step = owner ?? ownership?.Step;
        var transaction = dataSource as Transaction;
        var failures = new ExecutionFailures();
        var stage = ExecutionFailureStage.Validation;
        var cause = ExecutionFailureCause.Unknown;
        IAsyncDataReader? reader = null;
        T result = default!;
        try
        {
            if (tryCached is not null)
            {
                var cached = tryCached(step);
                token.ThrowIfCancellationRequested();
                if (cached.Found) return cached.Result;
            }
            stage = ExecutionFailureStage.CommandExecution;
            reader = await (source is IAsyncTransactionReaderSource ownedSource
                ? ownedSource.OpenReaderAsync(step!, token)
                : source.OpenReaderAsync(token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Reader acquisition returned no reader.");
            while (true)
            {
                stage = ExecutionFailureStage.RowLoading;
                cause = ExecutionFailureCause.Unknown;
                token.ThrowIfCancellationRequested();
                var hasRow = await reader.ReadNextRowAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!hasRow) break;
                stage = ExecutionFailureStage.Materialization;
                cause = ExecutionFailureCause.MaterializationError;
                addRow(reader);
                if (firstRowOnly) break;
            }
        }
        catch (Exception failure) { Record(failure); }
        finally
        {
            // Cleanup never inherits operation cancellation and never uses synchronous fallback.
            try { if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { failures.AddCleanup(cleanup); }
        }

        if (failures.Primary is null)
        {
            stage = ExecutionFailureStage.Materialization;
            cause = ExecutionFailureCause.MaterializationError;
            try
            {
                token.ThrowIfCancellationRequested();
                result = materialize(complete(), step, token);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception failure) { Record(failure); }
        }
        if (failures.Primary is { } primary)
        {
            var evidence = new ReadFailureEvidence();
            var assessmentSucceeded = true;
            if (source is IAsyncReadFailureEvidence classifier)
            {
                try { evidence = classifier.GetReadFailureEvidence(primary) ?? throw new InvalidOperationException("The provider returned no failure evidence."); }
                catch (Exception assessment)
                {
                    assessmentSucceeded = false;
                    failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
                }
            }
            var recovery = step is null ? ExecutionRecoveryActions.None
                : ExecutionRecoveryPolicy.ForReadFailure(evidence, !failures.HasCleanupFailure && assessmentSucceeded);
            var context = failures.Snapshot(evidence, transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                recovery, transaction?.TransactionID, identity.Operation, identity.ProviderInstanceId);
            if (step is not null) transaction!.RecordAsyncReadFailure(step, context);
            ExecutionFailureContexts.Attach(primary, context);
            ownership?.ReportFailure(primary);
            failures.ThrowIfAny();
        }
        return result;

        void Record(Exception failure) => failures.AddReported(failure, stage,
            failure is OperationCanceledException canceled && canceled.CancellationToken == token && token.IsCancellationRequested
                ? ExecutionFailureCause.Cancellation : cause);
    }
}
