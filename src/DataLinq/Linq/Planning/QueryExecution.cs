using System;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Interfaces;

namespace DataLinq.Linq.Planning;

internal readonly struct QueryExecutionContext
{
    public QueryExecutionContext(
        IDataLinqReadSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
        CancellationToken = cancellationToken;
    }

    public IDataLinqReadSource Source { get; }

    public CancellationToken CancellationToken { get; }
}

internal readonly struct QueryExecutionRequest
{
    public QueryExecutionRequest(
        QueryPlanInvocation invocation,
        QueryExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        Invocation = invocation;
        Context = context;
    }

    public QueryPlanInvocation Invocation { get; }

    public QueryExecutionContext Context { get; }
}

internal readonly struct ValidatedQueryExecutionRequest
{
    private ValidatedQueryExecutionRequest(
        QueryExecutionRequest request,
        IQueryPlanBackend backend)
    {
        Request = request;
        Backend = backend;
    }

    public QueryExecutionRequest Request { get; }

    public QueryPlanInvocation Invocation => Request.Invocation;

    public QueryExecutionContext Context => Request.Context;

    /// <summary>
    /// Reconstructs the inspectable requirement model on demand. Runtime execution validates the
    /// compact features directly and never retains this diagnostic-only object graph.
    /// </summary>
    public QueryPlanRequirements Requirements => QueryPlanRequirements.Extract(Invocation);

    public IQueryPlanBackend Backend { get; }

    public static ValidatedQueryExecutionRequest Prepare(in QueryExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Invocation);

        var context = request.Context;
        ArgumentNullException.ThrowIfNull(context.Source);
        context.CancellationToken.ThrowIfCancellationRequested();
        ValidateSourceOwnership(request.Invocation, context.Source);

        if (context.Source is not IDataLinqQueryPlanServices services)
        {
            throw new NotSupportedException(
                $"Read source type '{context.Source.GetType().FullName}' does not yet provide query-plan execution services.");
        }

        var backend = services.QueryPlanBackend
            ?? throw new InvalidOperationException("The read source returned no query-plan backend.");
        if (!ReferenceEquals(backend.Source, context.Source))
        {
            throw new InvalidOperationException(
                "The read source returned a query-plan backend bound to another source.");
        }

        QueryPlanCapabilityValidator.ValidateForExecution(
            request.Invocation,
            backend.Capabilities);

        return new ValidatedQueryExecutionRequest(request, backend);
    }

    public void EnsureBackend(IQueryPlanBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (!ReferenceEquals(Backend, backend))
        {
            throw new InvalidOperationException(
                "The query execution request was validated for a different backend instance.");
        }

        if (!ReferenceEquals(backend.Source, Context.Source))
        {
            throw new InvalidOperationException(
                "The validated query backend is no longer bound to the request read source.");
        }
    }

    private static void ValidateSourceOwnership(
        QueryPlanInvocation invocation,
        IDataLinqReadSource source)
    {
        var sourceSlots = invocation.Template.Sources;
        for (var index = 0; index < sourceSlots.Count; index++)
        {
            var sourceSlot = sourceSlots[index];
            if (ReferenceEquals(sourceSlot.Table.Database, source.Metadata))
                continue;

            throw new ArgumentException(
                $"Read source metadata does not own query-plan source '{sourceSlot.Id}' " +
                $"for table '{sourceSlot.Table.DbName}'.",
                nameof(source));
        }
    }
}

internal interface IQueryPlanBackend
{
    IDataLinqReadSource Source { get; }

    QueryBackendCapabilities Capabilities { get; }

    IQueryEntityCursor OpenEntityCursor(ValidatedQueryExecutionRequest request);

    IQueryProjectionCursor<TResult> OpenProjectionCursor<TResult>(
        ValidatedQueryExecutionRequest request);

    TResult ExecuteScalar<TResult>(ValidatedQueryExecutionRequest request);

    bool TryExecuteTerminalEntity(
        ValidatedQueryExecutionRequest request,
        out IImmutableInstance? result);
}

internal interface IQueryEntityCursor : IDisposable
{
    IImmutableInstance Current { get; }

    bool MoveNext();
}

internal interface IQueryProjectionCursor<out TResult> : IDisposable
{
    TResult Current { get; }

    bool MoveNext();
}
