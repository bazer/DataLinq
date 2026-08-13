using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Expressions;

internal sealed class ExpressionQueryPlanProvider : IQueryProvider
{
    private readonly DatabaseDefinition metadata;
    private readonly IDataLinqReadSource? readSource;

    public ExpressionQueryPlanProvider(DatabaseDefinition metadata)
    {
        this.metadata = metadata;
    }

    private ExpressionQueryPlanProvider(IDataLinqReadSource readSource)
    {
        this.readSource = readSource;
        metadata = readSource.Metadata;
    }

    public static ExpressionQueryPlanProvider ForExecution(DataSourceAccess dataSource)
        => ForExecution((IDataLinqReadSource)dataSource);

    public static ExpressionQueryPlanProvider ForExecution(IDataLinqReadSource readSource)
    {
        ArgumentNullException.ThrowIfNull(readSource);
        return new ExpressionQueryPlanProvider(readSource);
    }

    public IQueryable<TElement> CreateRoot<TElement>()
        => new ExpressionPlanQueryable<TElement>(this);

    public IQueryable CreateQuery(Expression expression)
        => throw new NotSupportedException("Non-generic query creation is not supported by the DataLinq expression plan provider.");

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new ExpressionPlanQueryable<TElement>(this, expression);

    public object? Execute(Expression expression)
        => Execute<object?>(expression);

    public TResult Execute<TResult>(Expression expression)
    {
        var plan = Parse(expression, typeof(TResult));
        var request = ValidatedQueryExecutionRequest.Prepare(CreateExecutionRequest(plan));
        return ExpressionQueryPlanExecutor.Execute<TResult>(request);
    }

    public IEnumerable<TElement> ExecuteEnumerable<TElement>(Expression expression)
    {
        var plan = Parse(expression, typeof(TElement));
        var request = ValidatedQueryExecutionRequest.Prepare(CreateExecutionRequest(plan));
        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TElement>(request);
    }

    public QueryPlanInvocation Parse(Expression expression, Type resultType)
        => ExpressionQueryPlanParser.Convert(metadata, expression, resultType);

    private QueryExecutionRequest CreateExecutionRequest(QueryPlanInvocation invocation)
    {
        if (readSource is null)
        {
            throw new NotSupportedException(
                "The DataLinq expression plan provider was created for parsing only and cannot execute queries.");
        }

        return new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(readSource, CancellationToken.None));
    }
}

internal sealed class ExpressionPlanQueryable<T> : IOrderedQueryable<T>
{
    private readonly ExpressionQueryPlanProvider provider;

    public ExpressionPlanQueryable(ExpressionQueryPlanProvider provider)
        : this(provider, null)
    {
    }

    public ExpressionPlanQueryable(ExpressionQueryPlanProvider provider, Expression? expression)
    {
        this.provider = provider;
        Expression = expression ?? Expression.Constant(this);
    }

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider => provider;

    public IEnumerator<T> GetEnumerator()
        => provider.ExecuteEnumerable<T>(Expression).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}

internal static class ExpressionQueryPlanExecutor
{
    public static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan)
        => ExecuteEnumerable<TElement>(
            Prepare(source, plan),
            ProjectionEvaluationOptions.Default);

    internal static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions)
        => ExecuteEnumerable<TElement>(
            Prepare(source, plan),
            projectionOptions);

    internal static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        ValidatedQueryExecutionRequest request)
        => ExecuteEnumerable<TElement>(request, ProjectionEvaluationOptions.Default);

    private static IEnumerable<TElement> ExecuteEnumerable<TElement>(
        ValidatedQueryExecutionRequest request,
        ProjectionEvaluationOptions projectionOptions)
    {
        var plan = request.Invocation;
        var template = plan.Template;
        if (template.Result.Kind != QueryPlanResultKind.Sequence)
            throw new QueryTranslationException($"Expression parser route expected a sequence result, but the plan result is '{template.Result.Kind}'.");

        ValidateProjectionDisposition(template.Projection, projectionOptions);

        if (template.Projection is QueryPlanProjection.Entity)
            return ExecuteEntitySequence<TElement>(request);

        if (IsBackendProjection(template.Projection))
            return ExecuteProjectionSequence<TElement>(request);

        throw new QueryTranslationException(
            $"Expression parser route cannot execute query plan projection '{template.Projection.Kind}'.");
    }

    public static TResult Execute<TResult>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan)
        => Execute<TResult>(Prepare(source, plan), ProjectionEvaluationOptions.Default);

    internal static TResult Execute<TResult>(
        IDataLinqReadSource source,
        QueryPlanInvocation plan,
        ProjectionEvaluationOptions projectionOptions)
        => Execute<TResult>(Prepare(source, plan), projectionOptions);

    internal static TResult Execute<TResult>(ValidatedQueryExecutionRequest request)
        => Execute<TResult>(request, ProjectionEvaluationOptions.Default);

    private static TResult Execute<TResult>(
        ValidatedQueryExecutionRequest request,
        ProjectionEvaluationOptions projectionOptions)
    {
        var plan = request.Invocation;
        ValidateProjectionDisposition(plan.Template.Projection, projectionOptions);

        if (plan.Template.Projection is QueryPlanProjection.Entity &&
            IsEntityTerminalResult(plan.Template.Result.Kind))
        {
            return ExecuteEntityTerminal<TResult>(request);
        }

        if (plan.Template.Result.IsScalarResult)
            return ExecuteScalar<TResult>(request);

        if (IsBackendProjection(plan.Template.Projection))
            return ExecuteProjectionTerminal<TResult>(request);

        throw new QueryTranslationException(
            $"Expression parser route cannot execute query plan projection '{plan.Template.Projection.Kind}'.");
    }

    private static ValidatedQueryExecutionRequest Prepare(
        IDataLinqReadSource source,
        QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(source);
        return ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(source, CancellationToken.None)));
    }

    private static IEnumerable<TElement> ExecuteEntitySequence<TElement>(
        ValidatedQueryExecutionRequest request)
    {
        using var cursor = request.Backend.OpenEntityCursor(request);
        while (cursor.MoveNext())
            yield return (TElement)(object)cursor.Current;
    }

    private static IEnumerable<TElement> ExecuteProjectionSequence<TElement>(
        ValidatedQueryExecutionRequest request)
    {
        using var cursor = request.Backend.OpenProjectionCursor<TElement>(request);
        while (cursor.MoveNext())
            yield return cursor.Current;
    }

    private static TResult ExecuteEntityTerminal<TResult>(
        ValidatedQueryExecutionRequest request)
    {
        if (request.Backend.TryExecuteTerminalEntity(request, out var optimizedResult))
        {
            return optimizedResult is null
                ? default!
                : (TResult)(object)optimizedResult;
        }

        var sequence = ExecuteEntitySequence<TResult>(request);
        return request.Invocation.Template.Result.Kind switch
        {
            // The backend receives the original First result shape and therefore bounds the
            // cursor to one row. Single forces the final MoveNext that records successful
            // completion in the underlying lazy SQL iterator instead of reporting early disposal.
            QueryPlanResultKind.First => sequence.Single(),
            QueryPlanResultKind.FirstOrDefault => sequence.SingleOrDefault()!,
            QueryPlanResultKind.Single => sequence.Single(),
            QueryPlanResultKind.SingleOrDefault => sequence.SingleOrDefault()!,
            QueryPlanResultKind.Last => sequence.Last(),
            QueryPlanResultKind.LastOrDefault => sequence.LastOrDefault()!,
            var kind => throw new QueryTranslationException(
                $"Expression parser route expected an entity terminal result, but the plan result is '{kind}'.")
        };
    }

    private static bool IsEntityTerminalResult(QueryPlanResultKind resultKind)
        => resultKind is QueryPlanResultKind.First or
            QueryPlanResultKind.FirstOrDefault or
            QueryPlanResultKind.Single or
            QueryPlanResultKind.SingleOrDefault or
            QueryPlanResultKind.Last or
            QueryPlanResultKind.LastOrDefault;

    private static bool IsBackendProjection(QueryPlanProjection projection)
        => projection is QueryPlanProjection.ScalarMember or
            QueryPlanProjection.SqlRow or
            QueryPlanProjection.GroupedAggregate or
            QueryPlanProjection.Anonymous or
            QueryPlanProjection.ComputedRowLocal or
            QueryPlanProjection.JoinedRowLocal;

    private static TResult ExecuteProjectionTerminal<TResult>(
        ValidatedQueryExecutionRequest request)
    {
        var sequence = ExecuteProjectionSequence<TResult>(request);
        return request.Invocation.Template.Result.Kind switch
        {
            // The backend receives the original First result shape and therefore bounds the
            // cursor to one row. Single forces the final MoveNext that records successful
            // completion in lazy SQL iterators instead of reporting early disposal.
            QueryPlanResultKind.First => sequence.Single(),
            QueryPlanResultKind.FirstOrDefault => sequence.SingleOrDefault()!,
            QueryPlanResultKind.Single => sequence.Single(),
            QueryPlanResultKind.SingleOrDefault => sequence.SingleOrDefault()!,
            QueryPlanResultKind.Last => sequence.Last(),
            QueryPlanResultKind.LastOrDefault => sequence.LastOrDefault()!,
            var kind => throw new QueryTranslationException(
                $"Expression parser route expected a projection terminal result, but the plan result is '{kind}'.")
        };
    }

    private static TResult ExecuteScalar<TResult>(ValidatedQueryExecutionRequest request)
        => request.Backend.ExecuteScalar<TResult>(request);

    private static void ValidateProjectionDisposition(
        QueryPlanProjection projection,
        ProjectionEvaluationOptions options)
    {
        if (projection.Disposition == QueryPlanProjectionDisposition.Unsupported)
        {
            throw new QueryTranslationException(
                $"Projection '{projection.Kind}' is an internal parser shape and cannot be executed as a final query projection.");
        }

        if (projection.Disposition == QueryPlanProjectionDisposition.SqlOnlyCompatibility &&
            (!options.AllowCompatibilityObjectConstruction ||
             !options.AllowCompatibilityMemberReflection))
        {
            throw new QueryTranslationException(
                $"Projection '{projection.Kind}' requires SQL-only compatibility execution and cannot execute in AOT-strict mode.");
        }
    }

}
