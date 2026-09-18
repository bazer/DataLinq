using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;

namespace DataLinq.Linq.Planning.Expressions;

internal sealed partial class ExpressionQueryPlanProvider
{
    internal IAsyncEnumerable<T> ExecuteEnumerableAsyncCore<T>(Expression expression, CancellationToken token = default) =>
        new CapturedQueryEnumerable<T>(() => ExpressionQueryPlanExecutor.ExecuteEnumerableAsyncCore<T>(CaptureAsyncRequest<T>(expression, token)));

    internal Task<T> ExecuteAsyncCore<T>(Expression expression, CancellationToken token = default) =>
        ExpressionQueryPlanExecutor.ExecuteAsyncCore<T>(CaptureAsyncRequest<T>(expression, token));

    internal Task<List<T>> ExecuteListAsyncCore<T>(Expression expression, CancellationToken token = default)
    {
        var request = CaptureAsyncRequest<T>(expression, token);
        return BufferAsync(ExpressionQueryPlanExecutor.ExecuteEnumerableAsyncCore<T>(request));
    }

    private ValidatedQueryExecutionRequest CaptureAsyncRequest<T>(Expression expression, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (readSource is null) throw new NotSupportedException("A parsing-only query provider cannot execute queries.");
        if (readSource is Interfaces.IDataSourceAccess sqlSource)
            Mutation.DataSourceAccess.EnsureReadAllowed(sqlSource, "capture an asynchronous expression query");
        var invocation = Parse(expression, typeof(T));
        return ValidatedQueryExecutionRequest.PrepareForAsync(new(invocation, new(readSource, token)));
    }

    private static async Task<List<T>> BufferAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var row in source.ConfigureAwait(false)) result.Add(row);
        return result;
    }

    private sealed class CapturedQueryEnumerable<T>(Func<IAsyncEnumerable<T>> capture) : IAsyncEnumerable<T>
    {
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            capture().GetAsyncEnumerator(cancellationToken);
    }
}

internal static partial class ExpressionQueryPlanExecutor
{
    internal static IAsyncEnumerable<T> ExecuteEnumerableAsyncCore<T>(ValidatedQueryExecutionRequest request)
    {
        if (request.Invocation.Template.Result.Kind != QueryPlanResultKind.Sequence)
            throw new QueryTranslationException("Async sequence execution requires a sequence result.");
        ValidateProjectionDisposition(request.Invocation.Template.Projection, ProjectionEvaluationOptions.Default);
        return RequireAsync(request).ExecuteSequenceAsync<T>(request);
    }

    internal static Task<T> ExecuteAsyncCore<T>(ValidatedQueryExecutionRequest request)
    {
        if (request.Invocation.Template.Result.Kind == QueryPlanResultKind.Sequence)
            throw new QueryTranslationException("Async terminal execution requires a terminal result.");
        ValidateProjectionDisposition(request.Invocation.Template.Projection, ProjectionEvaluationOptions.Default);
        return RequireAsync(request).ExecuteAsync<T>(request);
    }

    private static IAsyncQueryPlanBackend RequireAsync(ValidatedQueryExecutionRequest request) =>
        request.Backend as IAsyncQueryPlanBackend ?? throw new NotSupportedException("This query-plan backend does not provide asynchronous execution.");
}
