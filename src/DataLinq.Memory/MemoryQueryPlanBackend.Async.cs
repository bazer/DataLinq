using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Linq.Planning;

namespace DataLinq.Memory;

internal sealed partial class MemoryQueryPlanBackend
{
    public IAsyncEnumerable<T> ExecuteSequenceAsync<T>(ValidatedQueryExecutionRequest request)
    {
        request.EnsureBackend(this);
        if (request.Invocation.Template.Result.Kind != QueryPlanResultKind.Sequence)
            throw CreateCapabilityInvariantException(request);
        return new AsyncRows<T>(this, request);
    }

    public async Task<T> ExecuteAsync<T>(ValidatedQueryExecutionRequest request)
    {
        request.EnsureBackend(this);
        var result = request.Invocation.Template.Result;
        // These are the existing local CPU kernels, never a database-I/O fallback.
        if (result.IsScalarResult) return ExecuteScalar<T>(request);
        if (!IsElementResult(result.Kind)) throw CreateCapabilityInvariantException(request);

        await using var rows = new AsyncRows<T>(this, request).GetAsyncEnumerator();
        if (!await rows.MoveNextAsync().ConfigureAwait(false))
        {
            if (result.Kind is QueryPlanResultKind.Single or QueryPlanResultKind.First)
                throw new InvalidOperationException("Sequence contains no elements");
            return default!;
        }
        var value = rows.Current;
        // The local cursor enforces Single cardinality before materialization and
        // bounds ordered First to one row, as it does for synchronous execution.
        if (await rows.MoveNextAsync().ConfigureAwait(false)) throw MemorySingleResult.MoreThanOneElement();
        return value;
    }

    private sealed class AsyncRows<T>(MemoryQueryPlanBackend backend, ValidatedQueryExecutionRequest request) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncRowsEnumerator<T>(backend, request, cancellationToken);
    }

    private sealed class AsyncRowsEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly MemoryQueryPlanBackend backend;
        private readonly ValidatedQueryExecutionRequest request;
        private readonly EnumeratorCallGate calls = new();
        private CancellationTokenSource? linkedTokens;
        private IQueryEntityCursor? entities;
        private IQueryProjectionCursor<T>? projections;
        private bool finished;
        private bool hasCurrent;
        private T current = default!;

        internal AsyncRowsEnumerator(MemoryQueryPlanBackend backend, ValidatedQueryExecutionRequest request, CancellationToken enumerationToken)
        {
            this.backend = backend;
            var token = request.Context.CancellationToken;
            if (enumerationToken.CanBeCanceled && enumerationToken != token)
            {
                if (token.CanBeCanceled)
                {
                    linkedTokens = CancellationTokenSource.CreateLinkedTokenSource(token, enumerationToken);
                    token = linkedTokens.Token;
                }
                else token = enumerationToken;
            }
            try
            {
                // Revalidate the bound invocation with the combined token without
                // checking cancellation or visiting the local store yet.
                this.request = ValidatedQueryExecutionRequest.PrepareForAsync(
                    new(request.Invocation, new(request.Context.Source, token)));
                this.request.EnsureBackend(backend);
            }
            catch { linkedTokens?.Dispose(); throw; }
        }

        public T Current
        {
            get
            {
                using var call = calls.Enter();
                return hasCurrent ? current : throw new InvalidOperationException("The async memory enumerator is not positioned on a row.");
            }
        }

        public ValueTask<bool> MoveNextAsync()
        {
            using var call = calls.Enter();
            if (finished) return ValueTask.FromResult(false);
            hasCurrent = false;
            current = default!;
            try
            {
                // Cursor construction/compilation is deferred to the first move.
                // The same token reaches predicates, scans, sorting and conversion.
                request.Context.CancellationToken.ThrowIfCancellationRequested();
                if (request.Invocation.Template.Projection is QueryPlanProjection.Entity)
                {
                    entities ??= backend.OpenEntityCursor(request);
                    if (entities.MoveNext())
                    {
                        current = (T)(object)entities.Current;
                        hasCurrent = true;
                        return ValueTask.FromResult(true);
                    }
                }
                else
                {
                    projections ??= backend.OpenProjectionCursor<T>(request);
                    if (projections.MoveNext())
                    {
                        current = projections.Current;
                        hasCurrent = true;
                        return ValueTask.FromResult(true);
                    }
                }
                Finish();
                return ValueTask.FromResult(false);
            }
            catch (Exception failure)
            {
                Finish();
                return MemoryAsyncResult.FromFailure<bool>(failure);
            }
        }

        public ValueTask DisposeAsync()
        {
            using var call = calls.Enter();
            Finish();
            return ValueTask.CompletedTask;
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            hasCurrent = false;
            current = default!;
            try { entities?.Dispose(); projections?.Dispose(); }
            finally { entities = null; projections = null; linkedTokens?.Dispose(); linkedTokens = null; }
        }
    }
}
