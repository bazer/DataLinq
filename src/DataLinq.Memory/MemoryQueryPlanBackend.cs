using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;

namespace DataLinq.Memory;

internal sealed class MemoryQueryPlanBackend : IQueryPlanBackend
{
    private static readonly IReadOnlySet<QueryPlanFeature> supportedFeatures =
        new HashSet<QueryPlanFeature>
        {
            QueryPlanFeature.SourceCount(QueryPlanSourceCountKind.Single),
            QueryPlanFeature.SourceTopology(QueryPlanSourceTopology.ExactlyOneRoot),
            QueryPlanFeature.SourceKind(QueryPlanSourceKind.RootTable),
            QueryPlanFeature.SourceCardinality(QueryPlanSourceCardinality.Many),
            QueryPlanFeature.SourceNullability(QueryPlanSourceNullability.NonNullable),
            QueryPlanFeature.Projection(QueryPlanProjectionKind.Entity),
            QueryPlanFeature.ProjectionDisposition(QueryPlanProjectionDisposition.Direct),
            QueryPlanFeature.Result(QueryPlanResultKind.Sequence)
        };

    internal static IReadOnlyList<string> SupportedCapabilityTokens { get; } =
        supportedFeatures
            .Select(static feature => feature.Token)
            .OrderBy(static token => token, StringComparer.Ordinal)
            .ToArray();

    private static readonly QueryBackendCapabilities capabilities = new(
        "memory",
        QueryPlanFeatureCatalog.All.Select(static feature =>
            new KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>(
                feature,
                supportedFeatures.Contains(feature)
                    ? QueryBackendCapabilityDisposition.Supported
                    : QueryBackendCapabilityDisposition.Unsupported)));

    private readonly MemoryReadSource source;

    internal MemoryQueryPlanBackend(MemoryReadSource source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public IDataLinqReadSource Source => source;

    public QueryBackendCapabilities Capabilities => capabilities;

    public IQueryEntityCursor OpenEntityCursor(ValidatedQueryExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureBackend(this);
        request.Context.CancellationToken.ThrowIfCancellationRequested();

        var template = request.Invocation.Template;
        if (template.Sources.Count != 1 ||
            template.Operations.Count != 0 ||
            template.Projection is not QueryPlanProjection.Entity
            {
                Source.Kind: QueryPlanSourceKind.RootTable
            } entity ||
            !ReferenceEquals(entity.Source, template.Sources[0]) ||
            template.Result.Kind != QueryPlanResultKind.Sequence)
        {
            throw CreateCapabilityInvariantException(request);
        }

        return new MemoryEntityCursor(
            source,
            source.GetRows(entity.Source.Table),
            request.Context.CancellationToken);
    }

    public IQueryProjectionCursor<TResult> OpenProjectionCursor<TResult>(
        ValidatedQueryExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureBackend(this);
        throw CreateCapabilityInvariantException(request);
    }

    public TResult ExecuteScalar<TResult>(ValidatedQueryExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureBackend(this);
        throw CreateCapabilityInvariantException(request);
    }

    public bool TryExecuteTerminalEntity(
        ValidatedQueryExecutionRequest request,
        out IImmutableInstance? result)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureBackend(this);
        result = null;
        throw CreateCapabilityInvariantException(request);
    }

    private static InvalidOperationException CreateCapabilityInvariantException(
        ValidatedQueryExecutionRequest request) =>
        new(
            "The memory capability profile validated a request outside its implemented " +
            $"pass-through entity-sequence shape. Projection: '{request.Invocation.Template.Projection.Kind}'; " +
            $"result: '{request.Invocation.Template.Result.Kind}'; operations: {request.Invocation.Template.Operations.Count}.");
}

internal sealed class MemoryEntityCursor : IQueryEntityCursor
{
    private readonly MemoryReadSource source;
    private readonly CancellationToken cancellationToken;
    private IReadOnlyList<CanonicalProviderValueRow>? rows;
    private IImmutableInstance? current;
    private int currentIndex = -1;

    internal MemoryEntityCursor(
        MemoryReadSource source,
        IReadOnlyList<CanonicalProviderValueRow> rows,
        CancellationToken cancellationToken)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.rows = rows ?? throw new ArgumentNullException(nameof(rows));
        this.cancellationToken = cancellationToken;
    }

    public IImmutableInstance Current => current ?? throw new InvalidOperationException(
        "The memory query cursor is not positioned on a row.");

    public bool MoveNext()
    {
        var currentRows = rows;
        if (currentRows is null)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextIndex = checked(currentIndex + 1);
            if (nextIndex >= currentRows.Count)
            {
                Dispose();
                return false;
            }

            source.RecordScanRowVisited();
            cancellationToken.ThrowIfCancellationRequested();
            var next = source.Materialize(currentRows[nextIndex]);
            cancellationToken.ThrowIfCancellationRequested();

            currentIndex = nextIndex;
            current = next;
            return true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        rows = null;
        current = null;
    }
}
