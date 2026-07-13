using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Metadata;

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
            QueryPlanFeature.Operation(QueryPlanOperationKind.Where),
            QueryPlanFeature.Operation(QueryPlanOperationKind.OrderBy),
            QueryPlanFeature.Operation(QueryPlanOperationKind.Take),
            QueryPlanFeature.OrderingDirection(QueryPlanOrderingDirection.Ascending),
            QueryPlanFeature.OrderingDirection(QueryPlanOrderingDirection.Descending),
            QueryPlanFeature.OrderingShape(QueryPlanOrderingShape.SingleDirectNonNullableInt32PrimaryKeyColumn),
            QueryPlanFeature.PagingCompositionShape(QueryPlanPagingCompositionShape.SingleTakeAfterSingleOrdering),
            QueryPlanFeature.Predicate(QueryPlanPredicateKind.Compare),
            QueryPlanFeature.ComparisonOperator(QueryPlanComparisonOperator.Equal),
            QueryPlanFeature.NullSemantics(QueryPlanNullSemantics.Default),
            QueryPlanFeature.ComparisonShape(QueryPlanComparisonShape.DirectNonNullableInt32ColumnAndScalar),
            QueryPlanFeature.ValueKind(QueryPlanValueKind.Column, QueryPlanValueUse.PredicateOperand),
            QueryPlanFeature.ValueKind(QueryPlanValueKind.ScalarBinding, QueryPlanValueUse.PredicateOperand),
            QueryPlanFeature.ValueKind(QueryPlanValueKind.Column, QueryPlanValueUse.Ordering),
            QueryPlanFeature.ValueKind(QueryPlanValueKind.ScalarBinding, QueryPlanValueUse.PagingCount),
            QueryPlanFeature.PagingCountShape(QueryPlanPagingCountShape.NonNegativeInt32ScalarBinding),
            QueryPlanFeature.Projection(QueryPlanProjectionKind.Entity),
            QueryPlanFeature.Projection(QueryPlanProjectionKind.ScalarMember),
            QueryPlanFeature.ProjectionDisposition(QueryPlanProjectionDisposition.Direct),
            QueryPlanFeature.ScalarProjectionShape(
                QueryPlanScalarProjectionShape.DirectNonNullableInt32RootColumn),
            QueryPlanFeature.Result(QueryPlanResultKind.Sequence),
            QueryPlanFeature.BindingKind(QueryPlanBindingKind.Scalar),
            QueryPlanFeature.ScalarNullness(QueryPlanBindingNullness.NonNull),
            QueryPlanFeature.ValueKind(
                QueryPlanValueKind.Column,
                QueryPlanValueUse.ProjectionMember)
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
            template.Projection is not QueryPlanProjection.Entity
            {
                Source.Kind: QueryPlanSourceKind.RootTable
            } entity ||
            !ReferenceEquals(entity.Source, template.Sources[0]) ||
            template.Result.Kind != QueryPlanResultKind.Sequence)
        {
            throw CreateCapabilityInvariantException(request);
        }

        var executionPlan = MemoryRowExecutionPlan.Compile(request, entity.Source);
        return new MemoryEntityCursor(
            source,
            OpenCanonicalRowCursor(entity.Source, executionPlan, request.Context.CancellationToken),
            request.Context.CancellationToken);
    }

    public IQueryProjectionCursor<TResult> OpenProjectionCursor<TResult>(
        ValidatedQueryExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureBackend(this);
        request.Context.CancellationToken.ThrowIfCancellationRequested();

        var template = request.Invocation.Template;
        if (template.Sources.Count != 1 ||
            template.Projection is not QueryPlanProjection.ScalarMember
            {
                Source.Kind: QueryPlanSourceKind.RootTable
            } projection ||
            !ReferenceEquals(projection.Source, template.Sources[0]) ||
            template.Result.Kind != QueryPlanResultKind.Sequence ||
            template.Result.ResultType != typeof(TResult) ||
            projection.ResultType != typeof(TResult) ||
            QueryPlanScalarProjectionShapeFacts.Classify(projection, template.Sources) !=
                QueryPlanScalarProjectionShape.DirectNonNullableInt32RootColumn)
        {
            throw CreateCapabilityInvariantException(request);
        }

        var executionPlan = MemoryRowExecutionPlan.Compile(request, projection.Source);
        return new MemoryScalarProjectionCursor<TResult>(
            OpenCanonicalRowCursor(
                projection.Source,
                executionPlan,
                request.Context.CancellationToken),
            projection.Column,
            request.Context.CancellationToken);
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

    private MemoryCanonicalRowCursor OpenCanonicalRowCursor(
        QueryPlanSourceSlot rootSource,
        MemoryRowExecutionPlan executionPlan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = source.GetRows(rootSource.Table);
        cancellationToken.ThrowIfCancellationRequested();
        return new MemoryCanonicalRowCursor(source, rows, executionPlan, cancellationToken);
    }

    private static InvalidOperationException CreateCapabilityInvariantException(
        ValidatedQueryExecutionRequest request) =>
        new(
            "The memory capability profile validated a request outside its implemented " +
            $"row-sequence shape. Projection: '{request.Invocation.Template.Projection.Kind}'; " +
            $"result: '{request.Invocation.Template.Result.Kind}'; operations: {request.Invocation.Template.Operations.Count}.");
}

internal sealed class MemoryCanonicalRowCursor : IDisposable
{
    private readonly MemoryReadSource source;
    private readonly MemoryRowExecutionPlan executionPlan;
    private readonly CancellationToken cancellationToken;
    private IReadOnlyList<CanonicalProviderValueRow>? rows;
    private CanonicalProviderValueRow? current;
    private int nextRowIndex;
    private bool orderedRowsPrepared;

    internal MemoryCanonicalRowCursor(
        MemoryReadSource source,
        IReadOnlyList<CanonicalProviderValueRow> rows,
        MemoryRowExecutionPlan executionPlan,
        CancellationToken cancellationToken)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.rows = rows ?? throw new ArgumentNullException(nameof(rows));
        this.executionPlan = executionPlan ?? throw new ArgumentNullException(nameof(executionPlan));
        this.cancellationToken = cancellationToken;
    }

    internal CanonicalProviderValueRow Current => current ?? throw new InvalidOperationException(
        "The memory query cursor is not positioned on a row.");

    internal bool MoveNext()
    {
        current = null;
        var currentRows = rows;
        if (currentRows is null)
            return false;

        try
        {
            if (executionPlan.RequiresBufferedOrdering)
                return MoveNextOrdered(currentRows);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (nextRowIndex >= currentRows.Count)
                {
                    Dispose();
                    return false;
                }

                var row = currentRows[nextRowIndex++];
                source.RecordScanRowVisited();
                cancellationToken.ThrowIfCancellationRequested();
                if (!executionPlan.Matches(row, source, cancellationToken))
                    continue;

                current = row;
                return true;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private bool MoveNextOrdered(IReadOnlyList<CanonicalProviderValueRow> currentRows)
    {
        if (!orderedRowsPrepared)
        {
            currentRows = executionPlan.PrepareOrderedRows(currentRows, source, cancellationToken);
            rows = currentRows;
            orderedRowsPrepared = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (nextRowIndex >= currentRows.Count)
        {
            Dispose();
            return false;
        }

        var row = currentRows[nextRowIndex++];
        cancellationToken.ThrowIfCancellationRequested();
        current = row;
        return true;
    }

    public void Dispose()
    {
        rows = null;
        current = null;
    }
}

internal sealed class MemoryEntityCursor : IQueryEntityCursor
{
    private readonly MemoryReadSource source;
    private readonly CancellationToken cancellationToken;
    private MemoryCanonicalRowCursor? rows;
    private IImmutableInstance? current;

    internal MemoryEntityCursor(
        MemoryReadSource source,
        MemoryCanonicalRowCursor rows,
        CancellationToken cancellationToken)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.rows = rows ?? throw new ArgumentNullException(nameof(rows));
        this.cancellationToken = cancellationToken;
    }

    public IImmutableInstance Current => current ?? throw new InvalidOperationException(
        "The memory entity cursor is not positioned on a row.");

    public bool MoveNext()
    {
        current = null;
        var currentRows = rows;
        if (currentRows is null)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!currentRows.MoveNext())
            {
                Dispose();
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var next = source.Materialize(currentRows.Current);
            cancellationToken.ThrowIfCancellationRequested();
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
        current = null;
        var currentRows = rows;
        rows = null;
        currentRows?.Dispose();
    }
}

internal sealed class MemoryScalarProjectionCursor<TResult> : IQueryProjectionCursor<TResult>
{
    private const string ProjectionSourceName = "memory:scalar-projection";
    private readonly ColumnDefinition column;
    private readonly CancellationToken cancellationToken;
    private MemoryCanonicalRowCursor? rows;
    private TResult? current;
    private bool hasCurrent;

    internal MemoryScalarProjectionCursor(
        MemoryCanonicalRowCursor rows,
        ColumnDefinition column,
        CancellationToken cancellationToken)
    {
        this.rows = rows ?? throw new ArgumentNullException(nameof(rows));
        this.column = column ?? throw new ArgumentNullException(nameof(column));
        this.cancellationToken = cancellationToken;
    }

    public TResult Current => hasCurrent && rows is not null
        ? current!
        : throw new InvalidOperationException(
            "The memory scalar-projection cursor is not positioned on a result.");

    public bool MoveNext()
    {
        hasCurrent = false;
        current = default;
        var currentRows = rows;
        if (currentRows is null)
            return false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!currentRows.MoveNext())
            {
                Dispose();
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var canonicalValue = currentRows.Current[column];
            cancellationToken.ThrowIfCancellationRequested();
            var modelValue = ProviderRowMaterializer.MaterializeValue(
                column,
                canonicalValue,
                ProjectionSourceName);
            cancellationToken.ThrowIfCancellationRequested();
            var next = QueryProjectionResultMaterializer.ConvertResult<TResult>(modelValue);
            cancellationToken.ThrowIfCancellationRequested();
            current = next;
            hasCurrent = true;
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
        hasCurrent = false;
        current = default;
        var currentRows = rows;
        rows = null;
        currentRows?.Dispose();
    }
}
