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
    private readonly Func<AsyncReaderInvocation<T>> capture;
    private readonly CancellationToken methodToken;

    internal AsyncReaderEnumerable(
        Func<IAsyncReaderSource> capture, Func<IAsyncDataReader, T> materialize,
        Transaction? transaction = null, CancellationToken cancellationToken = default,
        ReadExecutionIdentity identity = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(materialize);
        this.capture = () => new(capture() ?? throw new InvalidOperationException("Reader capture returned no source."), materialize, transaction,
            Identity: identity.Bind(transaction));
        methodToken = cancellationToken;
    }

    internal AsyncReaderEnumerable(Func<AsyncReaderInvocation<T>> capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        this.capture = capture;
        methodToken = cancellationToken;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        var invocation = capture() ?? throw new InvalidOperationException("Reader capture returned no invocation.");
        ArgumentNullException.ThrowIfNull(invocation.Source);
        if ((invocation.Materialize is null ? 0 : 1) + (invocation.Buffer is null ? 0 : 1) + (invocation.Continuation is null ? 0 : 1) != 1)
            throw new InvalidOperationException("A reader invocation requires exactly one row materializer, buffer or continuation.");
        return new AsyncReaderEnumerator<T>(invocation.Source, invocation.Materialize, invocation.Transaction, methodToken, cancellationToken,
            invocation.Buffer, invocation.Continuation, invocation.Identity, invocation.Telemetry);
    }
}

internal sealed record AsyncReaderInvocation<T>(IAsyncReaderSource Source, Func<IAsyncDataReader, T>? Materialize,
    Transaction? Transaction = null, IAsyncReaderBuffer<T>? Buffer = null, IAsyncReaderContinuation<T>? Continuation = null,
    ReadExecutionIdentity Identity = default, QueryTelemetryContext Telemetry = default);

/// <summary>Invocation-local aggregation. No result is visible until all rows and cleanup succeed.</summary>
internal interface IAsyncReaderBuffer<T>
{
    void AddRow(IAsyncDataReader reader);
    IReadOnlyList<T> Complete(CancellationToken cancellationToken);
}

/// <summary>Private composition after the initial reader and command have closed, under the same owner.</summary>
internal interface IAsyncReaderContinuation<T> : IAsyncReadFailureEvidence
{
    bool RequiresInitialReader { get; }
    void AddRow(IAsyncDataReader reader);
    Task<IReadOnlyList<T>> CompleteAsync(TransactionOperationGate.Step? owner, CancellationToken token);
}

internal sealed record AsyncEnumerationFailure(Exception Cause, Exception? CleanupFailure,
    ExecutionFailureContext Context);

internal sealed class AsyncReaderEnumerator<T> : IAsyncEnumerator<T>, IHelperTrackedReader
{
    private const string Operation = "enumerate asynchronous reader rows";
    private readonly IAsyncReaderSource source;
    private readonly Func<IAsyncDataReader, T>? materialize;
    private IAsyncReaderBuffer<T>? buffer;
    private IAsyncReaderContinuation<T>? continuation;
    private IAsyncReadFailureEvidence? failureEvidence;
    private bool continuationStarted;
    private IReadOnlyList<T>? bufferedResults;
    private int bufferedPosition;
    private readonly Transaction? transaction;
    private readonly ReadExecutionIdentity identity;
    private QueryExecutionTelemetry telemetry;
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
        IAsyncReaderSource source, Func<IAsyncDataReader, T>? materialize, Transaction? transaction,
        CancellationToken methodToken, CancellationToken enumeratorToken, IAsyncReaderBuffer<T>? buffer = null,
        IAsyncReaderContinuation<T>? continuation = null, ReadExecutionIdentity identity = default,
        QueryTelemetryContext telemetryContext = default)
    {
        this.source = source;
        this.materialize = materialize;
        this.buffer = buffer;
        this.continuation = continuation;
        failureEvidence = source as IAsyncReadFailureEvidence;
        this.transaction = transaction;
        this.identity = identity.Bind(transaction);
        telemetry = new(telemetryContext);
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
        using var diagnostics = ExecutionFailureScope.Begin();
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
                stage = ExecutionFailureStage.Finalization;
                cause = ExecutionFailureCause.LocalFinalizationError;
                telemetry.MakeCurrent();
                stage = ExecutionFailureStage.Validation;
                cause = ExecutionFailureCause.Unknown;
                transaction?.EnsureCanRead(Operation, ownership?.Step, identity.Operation);
                if (!started)
                {
                    source.Validate();
                    if (source is IAsyncTransactionReaderSource && transaction is null)
                        throw new InvalidOperationException("This reader source requires a managed transaction owner.");
                    CheckCancellation();
                    if (transaction is not null)
                        ownership = DataSourceAccess.BeginRead(transaction, Operation, cancellationToken: token, operationKind: identity.Operation);
                    ownership?.RegisterReader(this);
                    started = true;
                    stage = ExecutionFailureStage.Finalization;
                    cause = ExecutionFailureCause.LocalFinalizationError;
                    telemetry.Start();
                    stage = ExecutionFailureStage.CommandExecution;
                    cause = ExecutionFailureCause.Unknown;
                    // Assignment precedes cancellation: a successfully acquired reader must
                    // be cleaned up even if its provider completed despite a cancellation request.
                    if (continuation?.RequiresInitialReader != false)
                        reader = await (source is IAsyncTransactionReaderSource ownedSource
                            ? ownedSource.OpenReaderAsync(ownership!.Step, token)
                            : source.OpenReaderAsync(token)).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Reader acquisition returned no reader.");
                }

                if (buffer is not null || continuation is not null)
                {
                    while (reader is not null)
                    {
                        stage = ExecutionFailureStage.RowLoading;
                        cause = ExecutionFailureCause.Unknown;
                        CheckCancellation();
                        var hasRow = await reader!.ReadNextRowAsync(token).ConfigureAwait(false);
                        CheckCancellation();
                        if (!hasRow) break;
                        stage = ExecutionFailureStage.Materialization;
                        cause = ExecutionFailureCause.MaterializationError;
                        if (buffer is not null) buffer.AddRow(reader);
                        else continuation!.AddRow(reader);
                    }
                    stage = ExecutionFailureStage.Materialization;
                    cause = ExecutionFailureCause.MaterializationError;
                    if (buffer is not null)
                    {
                        bufferedResults = buffer.Complete(token);
                        buffer = null;
                    }
                    CheckCancellation();
                    failures = new();
                    await DisposeReaderAsync(failures).ConfigureAwait(false);
                    if (failures.Primary is null && continuation is not null)
                    {
                        CheckCancellation();
                        continuationStarted = true;
                        failureEvidence = continuation;
                        bufferedResults = await continuation.CompleteAsync(ownership?.Step, token).ConfigureAwait(false);
                        continuation = null;
                        CheckCancellation();
                    }
                    // Keep transaction admission through buffered enumeration, matching
                    // the synchronous grouped path; native resources are already closed.
                }
                if (failures?.Primary is null)
                {
                    stage = ExecutionFailureStage.RowLoading;
                    cause = ExecutionFailureCause.Unknown;
                    CheckCancellation();
                    if (bufferedResults is not null)
                    {
                        if (bufferedPosition < bufferedResults.Count)
                        {
                            current = bufferedResults[bufferedPosition++];
                            hasCurrent = true;
                            return true;
                        }
                    }
                    else
                    {
                        var hasRow = await reader!.ReadNextRowAsync(token).ConfigureAwait(false);
                        CheckCancellation();
                        if (hasRow)
                        {
                            stage = ExecutionFailureStage.Materialization;
                            cause = ExecutionFailureCause.MaterializationError;
                            var value = materialize!(reader);
                            CheckCancellation();
                            current = value;
                            hasCurrent = true;
                            return true;
                        }
                    }
                }
            }
            catch (Exception primary)
            {
                failures ??= new ExecutionFailures();
                failures.AddReported(primary, stage, primary is OperationCanceledException canceled &&
                    canceled.CancellationToken == token && token.IsCancellationRequested ? ExecutionFailureCause.Cancellation : cause);
            }
            failures ??= new ExecutionFailures();
            await FinishAsync(failures, succeeded: failures.Primary is null).ConfigureAwait(false);
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
        using var diagnostics = ExecutionFailureScope.Begin();
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
        using var diagnostics = ExecutionFailureScope.Begin();
        using (call)
        {
            var failures = new ExecutionFailures();
            await FinishAsync(failures).ConfigureAwait(false);
            failures.ThrowIfAny();
        }
    }

    private async ValueTask FinishAsync(ExecutionFailures failures, bool succeeded = false)
    {
        if (finished)
            return;
        finished = true;
        hasCurrent = false;
        current = default!;
        buffer = null;
        continuation = null;
        bufferedResults = null;
        try
        {
            try { telemetry.MakeCurrent(); }
            catch (Exception failure) { QueryExecutionTelemetry.AddFailure(failures, failure); }
            await DisposeReaderAsync(failures).ConfigureAwait(false);
            ExecutionFailures? reportingFailures = failures;
            telemetry.Complete(ref reportingFailures, succeeded);

            if (failures.Primary is { } primary)
            {
                var evidence = new ReadFailureEvidence();
                var assessmentSucceeded = true;
                // A child operation already classified its own settled resources. The
                // initial reader is closed before continuation, so do not replace that
                // assessment with evidence from the earlier, successful key read.
                // Before admission, a gate rejection already describes the active
                // operation. This enumerator owns no work and must not replace its
                // FinishActiveOperation policy with a read-cleanup assessment.
                var reported = continuationStarted || !started ? ExecutionFailureContexts.GetCurrent(primary) : null;
                if (reported is null && started && failureEvidence is { } classifier)
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
                    reported?.Completion ?? (transaction is null ? ExecutionCompletion.NotApplicable : ExecutionCompletion.NotAttempted),
                    reported?.Recovery ?? recovery, transaction?.TransactionID, identity.Operation, identity.ProviderInstanceId);
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
                failureEvidence = null;
                linkedTokens?.Dispose();
                linkedTokens = null;
            }
        }
    }

    private async ValueTask DisposeReaderAsync(ExecutionFailures failures)
    {
        var ownedReader = reader;
        reader = null;
        try
        {
            // Cleanup never inherits a canceled enumeration token or uses sync fallback.
            if (ownedReader is not null) await ownedReader.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanup) { failures.AddCleanup(cleanup); }
    }
}
