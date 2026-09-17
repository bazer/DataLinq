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

// An internal evidence record, not the eventual public failure-context API or a trust verdict.
internal sealed record AsyncEnumerationFailure(Exception Cause, Exception? CleanupFailure);

internal sealed class AsyncReaderEnumerator<T> : IAsyncEnumerator<T>
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
            try
            {
                transaction?.EnsureCanRead(Operation, ownership?.Step);
                if (!started)
                {
                    source.Validate();
                    token.ThrowIfCancellationRequested();
                    if (transaction is not null)
                        ownership = DataSourceAccess.BeginRead(transaction, Operation, cancellationToken: token);
                    started = true;
                    // Assignment precedes cancellation: a successfully acquired reader must
                    // be cleaned up even if its provider completed despite a cancellation request.
                    reader = await source.OpenReaderAsync(token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Reader acquisition returned no reader.");
                }

                token.ThrowIfCancellationRequested();
                var hasRow = await reader!.ReadNextRowAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!hasRow)
                {
                    await FinishAsync().ConfigureAwait(false);
                    return false;
                }
                var value = materialize(reader);
                token.ThrowIfCancellationRequested();
                current = value;
                hasCurrent = true;
                return true;
            }
            catch (Exception primary)
            {
                Exception? cleanupFailure = null;
                try { await FinishAsync().ConfigureAwait(false); }
                catch (Exception cleanup) { cleanupFailure = cleanup; }
                Failure = new(primary, ReferenceEquals(primary, cleanupFailure) ? null : cleanupFailure);
                throw;
            }
        }
    }

    public ValueTask DisposeAsync() => DisposeCoreAsync(calls.Enter());

    private async ValueTask DisposeCoreAsync(EnumeratorCallGate.Call call)
    {
        using (call)
        {
            try { await FinishAsync().ConfigureAwait(false); }
            catch (Exception cleanup)
            {
                Failure = new(cleanup, null);
                throw;
            }
        }
    }

    private async ValueTask FinishAsync()
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
            // Cleanup never inherits a canceled enumeration token or uses sync fallback.
            if (ownedReader is not null)
                await ownedReader.DisposeAsync().ConfigureAwait(false);
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
