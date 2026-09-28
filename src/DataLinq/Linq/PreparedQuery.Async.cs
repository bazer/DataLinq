using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning.Expressions;

namespace DataLinq.Linq;

public sealed partial class PreparedQuery<TDatabase, TArgument, TResult>
{
    /// <summary>Executes a prepared scalar or row query asynchronously against a compatible SQL source.</summary>
    /// <param name="source">The database, read-only access, or transaction to query.</param>
    /// <param name="argument">The invocation argument, captured before the first suspension.</param>
    /// <param name="cancellationToken">Optional cancellation for initialization and execution.</param>
    /// <returns>The completed result after owned execution resources have been cleaned up.</returns>
    public Task<TResult> ExecuteAsync(IDataSourceAccess<TDatabase> source, TArgument argument, CancellationToken cancellationToken = default) =>
        ExecuteAsyncCore(source, argument, cancellationToken);

    internal Task<TResult> ExecuteAsyncCore(IDataSourceAccess<TDatabase> source, TArgument argument, CancellationToken token = default) =>
        ExpressionQueryPlanExecutor.ExecuteAsyncCore<TResult>(PreparedQueryExecution.CreateRequest(template, bindings, source, argument, token, asynchronous: true));
}

public sealed partial class PreparedSequenceQuery<TDatabase, TArgument, TElement>
{
    /// <summary>Captures a prepared invocation and returns its deferred asynchronous sequence.</summary>
    /// <param name="source">The compatible SQL database, read-only access, or transaction.</param>
    /// <param name="argument">The invocation argument, captured by this call.</param>
    /// <param name="cancellationToken">Optional cancellation, combined with each enumerator's token.</param>
    /// <returns>A repeatable sequence that executes afresh using the captured invocation.</returns>
    /// <remarks>
    /// Neither this call nor enumerator construction performs database I/O. Execution starts on
    /// the first move and may buffer. Each enumerator owns its resources, but does not complete
    /// a caller-owned transaction. Dispose it on every exit. Memory sources are not supported.
    /// </remarks>
    public IAsyncEnumerable<TElement> ExecuteAsync(IDataSourceAccess<TDatabase> source, TArgument argument, CancellationToken cancellationToken = default) =>
        ExecuteAsyncCore(source, argument, cancellationToken);

    internal IAsyncEnumerable<TElement> ExecuteAsyncCore(IDataSourceAccess<TDatabase> source, TArgument argument, CancellationToken token = default)
    {
        // Deliberately outside an async iterator: arguments belong to this invocation,
        // while each enumeration creates fresh resources and observes current rows.
        var request = PreparedQueryExecution.CreateRequest(template, bindings, source, argument, token, asynchronous: true);
        return ExpressionQueryPlanExecutor.ExecuteEnumerableAsyncCore<TElement>(request);
    }
}
