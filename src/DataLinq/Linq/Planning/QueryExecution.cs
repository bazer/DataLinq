using System;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Interfaces;

namespace DataLinq.Linq.Planning;

internal sealed record QueryExecutionContext
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

internal sealed record QueryExecutionRequest
{
    public QueryExecutionRequest(
        QueryPlanInvocation invocation,
        QueryExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(context);
        Invocation = invocation;
        Context = context;
    }

    public QueryPlanInvocation Invocation { get; }

    public QueryExecutionContext Context { get; }
}

internal sealed class ValidatedQueryExecutionRequest
{
    private ValidatedQueryExecutionRequest(
        QueryExecutionRequest request,
        QueryPlanRequirements requirements,
        IQueryPlanBackend backend)
    {
        Request = request;
        Requirements = requirements;
        Backend = backend;
    }

    public QueryExecutionRequest Request { get; }

    public QueryPlanInvocation Invocation => Request.Invocation;

    public QueryExecutionContext Context => Request.Context;

    public QueryPlanRequirements Requirements { get; }

    public IQueryPlanBackend Backend { get; }

    public static ValidatedQueryExecutionRequest Prepare(QueryExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = request.Context;
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

        var requirements = QueryPlanCapabilityValidator.Validate(
            request.Invocation,
            backend.Capabilities);

        return new ValidatedQueryExecutionRequest(request, requirements, backend);
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
        foreach (var sourceSlot in invocation.Template.Sources)
        {
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

    bool TryExecuteTerminalEntity(
        ValidatedQueryExecutionRequest request,
        out IImmutableInstance? result);
}

internal interface IQueryEntityCursor : IDisposable
{
    IImmutableInstance Current { get; }

    bool MoveNext();
}
