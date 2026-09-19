using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Mutation;

namespace DataLinq.Execution;

/// <summary>Local query projection/result semantics inside the original reader's owner and failure boundary.</summary>
internal static class AsyncReaderTransform
{
    internal static AsyncReaderInvocation<TResult> Capture<T, TResult>(AsyncReaderInvocation<T> input,
        Func<IReadOnlyList<T>, CancellationToken, IReadOnlyList<TResult>> transform)
    {
        if (input.Buffer is not null) throw new InvalidOperationException("Query transforms require a materializer or continuation.");
        return new(input.Source, null, input.Transaction, Continuation: new Transform<T, TResult>(input, transform), Identity: input.Identity);
    }

    private sealed class Transform<T, TResult>(AsyncReaderInvocation<T> input,
        Func<IReadOnlyList<T>, CancellationToken, IReadOnlyList<TResult>> transform) : IAsyncReaderContinuation<TResult>
    {
        private readonly List<T> rows = [];
        public bool RequiresInitialReader => input.Continuation?.RequiresInitialReader ?? true;
        public void AddRow(IAsyncDataReader reader)
        {
            if (input.Continuation is { } continuation) continuation.AddRow(reader);
            else rows.Add(input.Materialize!(reader));
        }
        public async Task<IReadOnlyList<TResult>> CompleteAsync(TransactionOperationGate.Step? owner, CancellationToken token)
        {
            var values = input.Continuation is { } continuation
                ? await continuation.CompleteAsync(owner, token).ConfigureAwait(false) : rows;
            token.ThrowIfCancellationRequested();
            return transform(values, token);
        }
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) =>
            (input.Continuation as IAsyncReadFailureEvidence ?? input.Source as IAsyncReadFailureEvidence)?.GetReadFailureEvidence(failure) ?? new();
    }
}
