using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public class QueryExecutionContractTests
{
    [Test]
    public async Task Prepare_RejectsUnsupportedPlanBeforeOpeningBackendEntityPaths()
    {
        var (metadata, invocation) = CreateEntityInvocation(QueryPlanResultKind.Single);
        var unsupportedFeature = QueryPlanFeature.Projection(QueryPlanProjectionKind.Entity);
        var backend = new TrackingBackend(CreateCapabilities(unsupportedFeature));
        var source = new TrackingReadSource(metadata, backend);
        var request = new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(source, CancellationToken.None));

        var exception = Capture<QueryBackendCapabilityException>(() =>
            ValidatedQueryExecutionRequest.Prepare(request));

        await Assert.That(exception.Feature).IsEqualTo(unsupportedFeature.Token);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_RejectsUnsupportedScalarPlanBeforeExecutingBackend()
    {
        var (metadata, invocation) = CreateScalarInvocation(QueryPlanResultKind.Count, typeof(int));
        var unsupportedFeature = QueryPlanFeature.Result(QueryPlanResultKind.Count);
        var backend = new TrackingBackend(CreateCapabilities(unsupportedFeature));
        var source = new TrackingReadSource(metadata, backend);
        var request = new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(source, CancellationToken.None));

        var exception = Capture<QueryBackendCapabilityException>(() =>
            ValidatedQueryExecutionRequest.Prepare(request));

        await Assert.That(exception.Feature).IsEqualTo(unsupportedFeature.Token);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_RejectsUnsupportedProjectionPlanBeforeOpeningBackendCursor()
    {
        var (metadata, invocation) = CreateProjectionInvocation();
        var unsupportedFeature = QueryPlanFeature.Projection(QueryPlanProjectionKind.ScalarMember);
        var backend = new TrackingBackend(CreateCapabilities(unsupportedFeature));
        var source = new TrackingReadSource(metadata, backend);
        var request = new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(source, CancellationToken.None));

        var exception = Capture<QueryBackendCapabilityException>(() =>
            ValidatedQueryExecutionRequest.Prepare(request));

        await Assert.That(exception.Feature).IsEqualTo(unsupportedFeature.Token);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_RejectsPreCancellationBeforeAccessingBackend()
    {
        var (metadata, invocation) = CreateEntityInvocation();
        var backend = new TrackingBackend(CreateCapabilities());
        var source = new TrackingReadSource(metadata, backend);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(source, cancellation.Token));

        _ = Capture<OperationCanceledException>(() =>
            ValidatedQueryExecutionRequest.Prepare(request));

        await Assert.That(source.BackendAccesses).IsEqualTo(0);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_RejectsForeignMetadataBeforeAccessingBackend()
    {
        var (_, invocation) = CreateEntityInvocation();
        var foreignMetadata = GetEmployeesMetadata();
        var backend = new TrackingBackend(CreateCapabilities());
        var source = new TrackingReadSource(foreignMetadata, backend);
        var request = new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(source, CancellationToken.None));

        var exception = Capture<ArgumentException>(() =>
            ValidatedQueryExecutionRequest.Prepare(request));

        await Assert.That(exception.Message).Contains("does not own query-plan source 's0'");
        await Assert.That(source.BackendAccesses).IsEqualTo(0);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_RejectsBackendBoundToAnotherReadSource()
    {
        var (metadata, invocation) = CreateEntityInvocation();
        var backend = new TrackingBackend(CreateCapabilities());
        _ = new TrackingReadSource(metadata, backend);
        var source = new TrackingReadSource(metadata, backend, bindBackend: false);
        var request = new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(source, CancellationToken.None));

        var exception = Capture<InvalidOperationException>(() =>
            ValidatedQueryExecutionRequest.Prepare(request));

        await Assert.That(exception.Message).Contains("backend bound to another source");
        await Assert.That(source.BackendAccesses).IsEqualTo(1);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Prepare_StoresRequirementsAndTheExactSelectedBackend()
    {
        var (metadata, invocation) = CreateEntityInvocation();
        var backend = new TrackingBackend(CreateCapabilities());
        var source = new TrackingReadSource(metadata, backend);
        var context = new QueryExecutionContext(source, CancellationToken.None);
        var request = new QueryExecutionRequest(invocation, context);

        var validated = ValidatedQueryExecutionRequest.Prepare(request);

        await Assert.That(ReferenceEquals(validated.Request, request)).IsTrue();
        await Assert.That(ReferenceEquals(validated.Invocation, invocation)).IsTrue();
        await Assert.That(ReferenceEquals(validated.Context, context)).IsTrue();
        await Assert.That(ReferenceEquals(validated.Backend, backend)).IsTrue();
        await Assert.That(validated.Requirements.Structural.Count).IsGreaterThan(0);
        await Assert.That(validated.Requirements.Structural.Any(requirement =>
            requirement.Feature == QueryPlanFeature.Projection(QueryPlanProjectionKind.Entity))).IsTrue();
        await Assert.That(source.BackendAccesses).IsEqualTo(1);
    }

    [Test]
    public async Task BackendEntryPointsRequireValidatedRequestsAndValidatedConstructionIsPrivate()
    {
        var entryMethods = typeof(IQueryPlanBackend)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => !method.IsSpecialName)
            .OrderBy(static method => method.Name, StringComparer.Ordinal)
            .ToArray();
        var constructors = typeof(ValidatedQueryExecutionRequest)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        await Assert.That(entryMethods.Length).IsGreaterThanOrEqualTo(2);
        foreach (var method in entryMethods)
        {
            var parameters = method.GetParameters();
            await Assert.That(parameters.Length).IsGreaterThan(0);
            await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(ValidatedQueryExecutionRequest));
            await Assert.That(parameters.Any(parameter =>
                parameter.ParameterType == typeof(QueryExecutionRequest) ||
                parameter.ParameterType == typeof(QueryPlanInvocation))).IsFalse();
        }

        await Assert.That(constructors.Length).IsGreaterThan(0);
        await Assert.That(constructors.All(static constructor => constructor.IsPrivate)).IsTrue();
    }

    [Test]
    public async Task ScalarCount_UsesExactValidatedBackendOnceAndReturnsSemanticResult()
    {
        var (metadata, invocation) = CreateScalarInvocation(QueryPlanResultKind.Count, typeof(int));
        var backend = new TrackingBackend(
            CreateCapabilities(),
            7);
        var source = new TrackingReadSource(metadata, backend);
        var request = ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(source, CancellationToken.None)));

        var result = ExpressionQueryPlanExecutor.Execute<int>(request);

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(1);
        await Assert.That(ReferenceEquals(backend.LastScalarRequest, request)).IsTrue();
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ScalarAny_UsesBackendValueWithoutOpeningEntityPaths()
    {
        var (metadata, invocation) = CreateScalarInvocation(QueryPlanResultKind.Any, typeof(bool));
        var backend = new TrackingBackend(
            CreateCapabilities(),
            false);
        var source = new TrackingReadSource(metadata, backend);
        var request = ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(source, CancellationToken.None)));

        var result = ExpressionQueryPlanExecutor.Execute<bool>(request);

        await Assert.That(result).IsFalse();
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(1);
        await Assert.That(ReferenceEquals(backend.LastScalarRequest, request)).IsTrue();
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ProjectionSequence_UsesExactValidatedBackendOnceAndReturnsSemanticResults()
    {
        var (metadata, invocation) = CreateProjectionInvocation();
        var backend = new TrackingBackend(
            CreateCapabilities(),
            projectionResults: ["Ada", "Grace"]);
        var source = new TrackingReadSource(metadata, backend);
        var request = ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(source, CancellationToken.None)));

        var result = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(request).ToArray();

        await Assert.That(result).IsEquivalentTo(["Ada", "Grace"]);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(1);
        await Assert.That(ReferenceEquals(backend.LastProjectionRequest, request)).IsTrue();
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ProjectionTerminal_UsesExactValidatedBackendOnceAndReturnsSemanticResult()
    {
        var (metadata, invocation) = CreateProjectionInvocation(QueryPlanResultKind.First);
        var backend = new TrackingBackend(
            CreateCapabilities(),
            projectionResults: ["Ada"]);
        var source = new TrackingReadSource(metadata, backend);
        var request = ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(source, CancellationToken.None)));

        var result = ExpressionQueryPlanExecutor.Execute<string>(request);

        await Assert.That(result).IsEqualTo("Ada");
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(1);
        await Assert.That(ReferenceEquals(backend.LastProjectionRequest, request)).IsTrue();
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
        await Assert.That(backend.TryExecuteTerminalEntityCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AotStrictScalarProjection_StillUsesBackendCursor()
    {
        var (metadata, invocation) = CreateProjectionInvocation();
        var backend = new TrackingBackend(
            CreateCapabilities(),
            projectionResults: ["Ada"]);
        var source = new TrackingReadSource(metadata, backend);

        var result = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(
                source,
                invocation,
                ProjectionEvaluationOptions.AotStrict)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(["Ada"]);
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(1);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AotStrictSqlRowProjection_RejectsBeforeOpeningBackendCursor()
    {
        var (metadata, invocation) = CreateSqlRowProjectionInvocation();
        var backend = new TrackingBackend(CreateCapabilities());
        var source = new TrackingReadSource(metadata, backend);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanExecutor.ExecuteEnumerable<ProjectionBox>(
                    source,
                    invocation,
                    ProjectionEvaluationOptions.AotStrict)
                .ToArray());

        await Assert.That(exception.Message).Contains("AOT-strict mode");
        await Assert.That(backend.OpenProjectionCursorCalls).IsEqualTo(0);
        await Assert.That(backend.OpenEntityCursorCalls).IsEqualTo(0);
        await Assert.That(backend.ExecuteScalarCalls).IsEqualTo(0);
    }

    [Test]
    public async Task EntityCursor_DisposesEnumeratorAfterCompleteEnumeration()
    {
        var rows = new TrackingEntityEnumerator(rowCount: 1);
        var cursor = new EnumeratorQueryEntityCursor(rows, CancellationToken.None);

        await Assert.That(cursor.MoveNext()).IsTrue();
        await Assert.That(cursor.MoveNext()).IsFalse();
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task EntityCursor_DisposesEnumeratorWhenConsumerStopsEarly()
    {
        var rows = new TrackingEntityEnumerator(rowCount: 2);
        var cursor = new EnumeratorQueryEntityCursor(rows, CancellationToken.None);

        await Assert.That(cursor.MoveNext()).IsTrue();
        cursor.Dispose();
        cursor.Dispose();

        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
    }

    [Test]
    public async Task EntityCursor_DisposesEnumeratorWhenEnumerationFails()
    {
        var rows = new TrackingEntityEnumerator(rowCount: 2, throwOnMoveNextCall: 2);
        var cursor = new EnumeratorQueryEntityCursor(rows, CancellationToken.None);

        await Assert.That(cursor.MoveNext()).IsTrue();
        var exception = Capture<InvalidOperationException>(() => cursor.MoveNext());

        await Assert.That(exception.Message).IsEqualTo("Synthetic entity enumeration failure.");
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
    }

    [Test]
    public async Task EntityCursor_DisposesEnumeratorWhenCancellationIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        var rows = new TrackingEntityEnumerator(rowCount: 2);
        var cursor = new EnumeratorQueryEntityCursor(rows, cancellation.Token);

        await Assert.That(cursor.MoveNext()).IsTrue();
        cancellation.Cancel();
        _ = Capture<OperationCanceledException>(() => cursor.MoveNext());

        await Assert.That(rows.MoveNextCalls).IsEqualTo(1);
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
    }

    [Test]
    public async Task ProjectionCursor_DisposesEnumeratorAfterCompleteEnumeration()
    {
        var rows = new TrackingProjectionEnumerator<int>([1]);
        var cursor = new EnumeratorQueryProjectionCursor<int>(rows, CancellationToken.None);

        await Assert.That(cursor.MoveNext()).IsTrue();
        await Assert.That(cursor.Current).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ProjectionCursor_DisposesEnumeratorWhenConsumerStopsEarly()
    {
        var rows = new TrackingProjectionEnumerator<int>([1, 2]);
        var cursor = new EnumeratorQueryProjectionCursor<int>(rows, CancellationToken.None);

        await Assert.That(cursor.MoveNext()).IsTrue();
        cursor.Dispose();
        cursor.Dispose();

        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
    }

    [Test]
    public async Task ProjectionCursor_DisposesEnumeratorWhenEnumerationFails()
    {
        var rows = new TrackingProjectionEnumerator<int>([1, 2], throwOnMoveNextCall: 2);
        var cursor = new EnumeratorQueryProjectionCursor<int>(rows, CancellationToken.None);

        await Assert.That(cursor.MoveNext()).IsTrue();
        var exception = Capture<InvalidOperationException>(() => cursor.MoveNext());

        await Assert.That(exception.Message).IsEqualTo("Synthetic projection enumeration failure.");
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
    }

    [Test]
    public async Task ProjectionCursor_DisposesEnumeratorWhenCancellationIsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        var rows = new TrackingProjectionEnumerator<int>([1, 2]);
        var cursor = new EnumeratorQueryProjectionCursor<int>(rows, cancellation.Token);

        await Assert.That(cursor.MoveNext()).IsTrue();
        cancellation.Cancel();
        var exception = Capture<OperationCanceledException>(() => cursor.MoveNext());

        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(rows.MoveNextCalls).IsEqualTo(1);
        await Assert.That(rows.DisposeCalls).IsEqualTo(1);
        await Assert.That(cursor.MoveNext()).IsFalse();
    }

    private static (DatabaseDefinition Metadata, QueryPlanInvocation Invocation) CreateEntityInvocation(
        QueryPlanResultKind resultKind = QueryPlanResultKind.Sequence)
    {
        var metadata = GetEmployeesMetadata();
        var table = metadata.TableModels
            .Single(model => model.Model.CsType.Type == typeof(Employee))
            .Table;
        var source = new QueryPlanSourceSlot(
            "s0",
            "t0",
            table,
            typeof(Employee),
            QueryPlanSourceKind.RootTable,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);
        var template = new QueryPlanTemplate(
            [source],
            [],
            new QueryPlanProjection.Entity(source),
            resultKind == QueryPlanResultKind.Sequence
                ? QueryPlanResult.Sequence(typeof(Employee))
                : new QueryPlanResult(resultKind, typeof(Employee)),
            QueryPlanBindingDeclarations.Empty,
            QueryPlanSpecialization.Empty);

        return (
            metadata,
            QueryPlanInvocation.Bind(template, Array.Empty<QueryPlanInvocationValue>()));
    }

    private static (DatabaseDefinition Metadata, QueryPlanInvocation Invocation) CreateScalarInvocation(
        QueryPlanResultKind resultKind,
        Type resultType)
    {
        var (metadata, entityInvocation) = CreateEntityInvocation();
        var entityTemplate = entityInvocation.Template;
        var template = new QueryPlanTemplate(
            entityTemplate.Sources,
            entityTemplate.Operations,
            entityTemplate.Projection,
            new QueryPlanResult(resultKind, resultType),
            entityTemplate.BindingDeclarations,
            entityTemplate.Specialization);

        return (
            metadata,
            QueryPlanInvocation.Bind(template, Array.Empty<QueryPlanInvocationValue>()));
    }

    private static (DatabaseDefinition Metadata, QueryPlanInvocation Invocation) CreateProjectionInvocation(
        QueryPlanResultKind resultKind = QueryPlanResultKind.Sequence)
    {
        var (metadata, entityInvocation) = CreateEntityInvocation();
        var entityTemplate = entityInvocation.Template;
        var source = entityTemplate.Sources.Single();
        var column = source.Table.GetColumnByDbName("first_name");
        var projection = new QueryPlanProjection.ScalarMember(source, column, typeof(string));
        var template = new QueryPlanTemplate(
            entityTemplate.Sources,
            entityTemplate.Operations,
            projection,
            resultKind == QueryPlanResultKind.Sequence
                ? QueryPlanResult.Sequence(typeof(string))
                : new QueryPlanResult(resultKind, typeof(string)),
            entityTemplate.BindingDeclarations,
            entityTemplate.Specialization);

        return (
            metadata,
            QueryPlanInvocation.Bind(template, Array.Empty<QueryPlanInvocationValue>()));
    }

    private static (DatabaseDefinition Metadata, QueryPlanInvocation Invocation) CreateSqlRowProjectionInvocation()
    {
        var (metadata, entityInvocation) = CreateEntityInvocation();
        var entityTemplate = entityInvocation.Template;
        var source = entityTemplate.Sources.Single();
        var column = source.Table.GetColumnByDbName("first_name");
        var constructor = typeof(ProjectionBox).GetConstructors().Single();
        var projection = new QueryPlanProjection.SqlRow(
            typeof(ProjectionBox),
            [
                new QueryPlanProjectionMember(
                    nameof(ProjectionBox.Value),
                    new QueryPlanColumnValue(source, column, typeof(string)))
            ],
            constructor);
        var template = new QueryPlanTemplate(
            entityTemplate.Sources,
            entityTemplate.Operations,
            projection,
            QueryPlanResult.Sequence(typeof(ProjectionBox)),
            entityTemplate.BindingDeclarations,
            entityTemplate.Specialization);

        return (
            metadata,
            QueryPlanInvocation.Bind(template, Array.Empty<QueryPlanInvocationValue>()));
    }

    private static DatabaseDefinition GetEmployeesMetadata()
        => MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();

    private static QueryBackendCapabilities CreateCapabilities(QueryPlanFeature? unsupportedFeature = null)
        => new(
            "test",
            QueryPlanFeatureCatalog.All.Select(feature =>
                new KeyValuePair<QueryPlanFeature, QueryBackendCapabilityDisposition>(
                    feature,
                    unsupportedFeature.HasValue && feature == unsupportedFeature.Value
                        ? QueryBackendCapabilityDisposition.Unsupported
                        : QueryBackendCapabilityDisposition.Supported)));

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
    }

    private sealed class TrackingReadSource : IDataLinqQueryPlanServices
    {
        private readonly IQueryPlanBackend backend;

        public TrackingReadSource(
            DatabaseDefinition metadata,
            IQueryPlanBackend backend,
            bool bindBackend = true)
        {
            Metadata = metadata;
            this.backend = backend;
            if (bindBackend && backend is TrackingBackend trackingBackend)
                trackingBackend.Bind(this);
        }

        public DatabaseDefinition Metadata { get; }

        public int BackendAccesses { get; private set; }

        public IModelMaterializationServices MaterializationServices =>
            throw new InvalidOperationException("Materialization services must not be accessed by request preparation.");

        public IQueryPlanBackend QueryPlanBackend
        {
            get
            {
                BackendAccesses++;
                return backend;
            }
        }
    }

    private sealed class TrackingBackend(
        QueryBackendCapabilities capabilities,
        object? scalarResult = null,
        IReadOnlyList<object?>? projectionResults = null) : IQueryPlanBackend
    {
        public IDataLinqReadSource Source { get; private set; } = null!;

        public QueryBackendCapabilities Capabilities { get; } = capabilities;

        public int OpenEntityCursorCalls { get; private set; }

        public int OpenProjectionCursorCalls { get; private set; }

        public int ExecuteScalarCalls { get; private set; }

        public int TryExecuteTerminalEntityCalls { get; private set; }

        public ValidatedQueryExecutionRequest? LastScalarRequest { get; private set; }

        public ValidatedQueryExecutionRequest? LastProjectionRequest { get; private set; }

        public void Bind(IDataLinqReadSource source) => Source = source;

        public IQueryEntityCursor OpenEntityCursor(ValidatedQueryExecutionRequest request)
        {
            OpenEntityCursorCalls++;
            return new EnumeratorQueryEntityCursor(
                new TrackingEntityEnumerator(rowCount: 0),
                CancellationToken.None);
        }

        public IQueryProjectionCursor<TResult> OpenProjectionCursor<TResult>(
            ValidatedQueryExecutionRequest request)
        {
            request.EnsureBackend(this);
            OpenProjectionCursorCalls++;
            LastProjectionRequest = request;
            return new EnumeratorQueryProjectionCursor<TResult>(
                (projectionResults ?? [])
                    .Select(static value => value is null ? default! : (TResult)value)
                    .GetEnumerator(),
                request.Context.CancellationToken);
        }

        public TResult ExecuteScalar<TResult>(ValidatedQueryExecutionRequest request)
        {
            request.EnsureBackend(this);
            ExecuteScalarCalls++;
            LastScalarRequest = request;
            return scalarResult is null
                ? default!
                : (TResult)scalarResult;
        }

        public bool TryExecuteTerminalEntity(
            ValidatedQueryExecutionRequest request,
            out IImmutableInstance? result)
        {
            TryExecuteTerminalEntityCalls++;
            result = null;
            return false;
        }
    }

    private sealed class TrackingEntityEnumerator(
        int rowCount,
        int? throwOnMoveNextCall = null) : IEnumerator<IImmutableInstance>
    {
        private int remainingRows = rowCount;

        public int DisposeCalls { get; private set; }

        public int MoveNextCalls { get; private set; }

        public IImmutableInstance Current => null!;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            MoveNextCalls++;
            if (MoveNextCalls == throwOnMoveNextCall)
                throw new InvalidOperationException("Synthetic entity enumeration failure.");

            if (remainingRows == 0)
                return false;

            remainingRows--;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrackingProjectionEnumerator<TResult>(
        IReadOnlyList<TResult> values,
        int? throwOnMoveNextCall = null) : IEnumerator<TResult>
    {
        private int index = -1;

        public int DisposeCalls { get; private set; }

        public int MoveNextCalls { get; private set; }

        public TResult Current => values[index];

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            MoveNextCalls++;
            if (MoveNextCalls == throwOnMoveNextCall)
                throw new InvalidOperationException("Synthetic projection enumeration failure.");

            if (index + 1 >= values.Count)
                return false;

            index++;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        public void Dispose() => DisposeCalls++;
    }

    private sealed record ProjectionBox(string Value);
}
