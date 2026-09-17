using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>
/// Internal, deferred single-reader orchestration. Capture must be I/O-free and return
/// a source bound to this invocation; materialization reads the borrowed current row.
/// Native provider wiring and general query snapshots remain separate integration work.
/// </summary>
internal sealed class AsyncReaderEnumerable<T> : IAsyncEnumerable<T>
{
    private readonly Func<IAsyncReaderSource> capture;
    private readonly Func<IAsyncDataReader, T> materialize;
    private readonly Transaction? transaction;
    private readonly CancellationToken methodToken;

    internal AsyncReaderEnumerable(
        Func<IAsyncReaderSource> capture, Func<IAsyncDataReader, T> materialize,
        Transaction? transaction = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(materialize);
        this.capture = capture;
        this.materialize = materialize;
        this.transaction = transaction;
        methodToken = cancellationToken;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        new AsyncReaderEnumerator<T>(
            capture() ?? throw new InvalidOperationException("Reader capture returned no source."),
            materialize, transaction, methodToken, cancellationToken);
}

internal sealed record AsyncEnumerationFailure(Exception Cause, Exception? CleanupFailure,
    ExecutionFailureContext Context);

internal sealed class AsyncReaderEnumerator<T> : IAsyncEnumerator<T>, IHelperTrackedReader
{
    private const string Operation = "enumerate asynchronous reader rows";
    private readonly IAsyncReaderSource source;
    private readonly Func<IAsyncDataReader, T> materialize;
    private readonly Transaction? transaction;
    private readonly EnumeratorCallGate calls = new();
    private readonly CancellationToken token;
    private CancellationTokenSource? linkedTokens;
    private TransactionReadScope? ownership;
    private IAsyncDataReader? reader;
    private bool started;
    private bool finished;
    private bool helperDrained;
    private bool hasCurrent;
    private T current = default!;

    internal AsyncEnumerationFailure? Failure { get; private set; }

    internal AsyncReaderEnumerator(
        IAsyncReaderSource source, Func<IAsyncDataReader, T> materialize, Transaction? transaction,
        CancellationToken methodToken, CancellationToken enumeratorToken)
    {
        this.source = source;
        this.materialize = materialize;
        this.transaction = transaction;
        if (!methodToken.CanBeCanceled)
            token = enumeratorToken;
        else if (!enumeratorToken.CanBeCanceled || methodToken == enumeratorToken)
            token = methodToken;
        else
        {
            linkedTokens = CancellationTokenSource.CreateLinkedTokenSource(methodToken, enumeratorToken);
            token = linkedTokens.Token;
        }
    }

    public T Current
    {
        get
        {
            using var call = calls.Enter();
            if (!hasCurrent)
                throw new InvalidOperationException("The enumerator is not positioned on a row.");
            return current;
        }
    }

    // Acquire synchronously, before the async state machine touches mutable iterator state.
    public ValueTask<bool> MoveNextAsync() => MoveNextCoreAsync(calls.Enter());

    private async ValueTask<bool> MoveNextCoreAsync(EnumeratorCallGate.Call call)
    {
        using (call)
        {
            if (finished)
                return false;
            hasCurrent = false;
            current = default!;
            ExecutionFailures? failures = null;
            var stage = ExecutionFailureStage.Validation;
            var cause = ExecutionFailureCause.Unknown;
            try
            {
                transaction?.EnsureCanRead(Operation, ownership?.Step);
                if (!started)
                {
                    source.Validate();
                    if (source is IAsyncTransactionReaderSource && transaction is null)
                        throw new InvalidOperationException("This reader source requires a managed transaction owner.");
                    CheckCancellation();
                    if (transaction is not null)
                        ownership = DataSourceAccess.BeginRead(transaction, Operation, cancellationToken: token);
                    ownership?.RegisterReader(this);
                    started = true;
                    stage = ExecutionFailureStage.CommandExecution;
                    // Assignment precedes cancellation: a successfully acquired reader must
                    // be cleaned up even if its provider completed despite a cancellation request.
                    reader = await (source is IAsyncTransactionReaderSource ownedSource
                        ? ownedSource.OpenReaderAsync(ownership!.Step, token)
                        : source.OpenReaderAsync(token)).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Reader acquisition returned no reader.");
                }

                stage = ExecutionFailureStage.RowLoading;
                CheckCancellation();
                var hasRow = await reader!.ReadNextRowAsync(token).ConfigureAwait(false);
                CheckCancellation();
                if (hasRow)
                {
                    stage = ExecutionFailureStage.Materialization;
                    cause = ExecutionFailureCause.MaterializationError;
                    var value = materialize(reader);
                    CheckCancellation();
                    current = value;
                    hasCurrent = true;
                    return true;
                }
            }
            catch (Exception primary)
            {
                failures = new ExecutionFailures();
                failures.AddReported(primary, stage, cause);
            }
            failures ??= new ExecutionFailures();
            await FinishAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
            return false;

            void CheckCancellation()
            {
                if (!token.IsCancellationRequested)
                    return;
                cause = ExecutionFailureCause.Cancellation;
                token.ThrowIfCancellationRequested();
            }
        }
    }

    public ValueTask DisposeAsync() => Volatile.Read(ref helperDrained)
        ? ValueTask.CompletedTask : DisposeCoreAsync(calls.Enter());

    public void StopAdmission() => calls.StopAdmission();

    public async ValueTask DrainAsync()
    {
        await calls.WaitForIdleAsync().ConfigureAwait(false);
        try
        {
            var failures = new ExecutionFailures();
            await FinishAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
        }
        finally { Volatile.Write(ref helperDrained, true); }
    }

    private async ValueTask DisposeCoreAsync(EnumeratorCallGate.Call call)
    {
        using (call)
        {
            var failures = new ExecutionFailures();
            await FinishAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
        }
    }

    private async ValueTask FinishAsync(ExecutionFailures failures)
    {
        if (finished)
            return;
        finished = true;
        hasCurrent = false;
        current = default!;
        var ownedReader = reader;
        reader = null;
        try
        {
            try
            {
                // Cleanup never inherits a canceled enumeration token or uses sync fallback.
                if (ownedReader is not null)
                    await ownedReader.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                failures.AddReported(cleanup, ExecutionFailureStage.Cleanup);
            }

            if (failures.Primary is { } primary)
            {
                var evidence = new ReadFailureEvidence();
                var assessmentSucceeded = true;
                if (started && source is IAsyncReadFailureEvidence classifier)
                {
                    try
                    {
                        evidence = classifier.GetReadFailureEvidence(primary)
                            ?? throw new InvalidOperationException("The provider returned no failure evidence.");
                    }
                    catch (Exception assessment)
                    {
                        assessmentSucceeded = false;
                        failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
                    }
                }
                var recovery = ownership is null ? ExecutionRecoveryActions.None
                    : ExecutionRecoveryPolicy.ForReadFailure(evidence, !failures.HasCleanupFailure && assessmentSucceeded);
                var context = failures.Snapshot(evidence,
                    transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted,
                    recovery, transaction?.TransactionID);
                // Publish restrictions before releasing admission: another operation must
                // never observe a free gate with the old, apparently reusable state.
                if (ownership is not null)
                    transaction!.RecordAsyncReadFailure(ownership.Step, context);
                ExecutionFailureContexts.Attach(primary, context);
                ownership?.ReportFailure(primary);
                Failure = new(primary, ReferenceEquals(primary, failures.FirstCleanupFailure) ? null : failures.FirstCleanupFailure, context);
            }
        }
        finally
        {
            try { ownership?.Dispose(); }
            finally
            {
                ownership = null;
                linkedTokens?.Dispose();
                linkedTokens = null;
            }
        }
    }
}
