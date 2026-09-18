using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning.Expressions;

namespace DataLinq.Linq;

public sealed partial class PreparedQuery<TDatabase, TArgument, TResult>
{
    internal Task<TResult> ExecuteAsyncCore(IDataSourceAccess<TDatabase> source, TArgument argument, CancellationToken token = default) =>
        ExpressionQueryPlanExecutor.ExecuteAsyncCore<TResult>(PreparedQueryExecution.CreateRequest(template, bindings, source, argument, token, asynchronous: true));
}

public sealed partial class PreparedSequenceQuery<TDatabase, TArgument, TElement>
{
    internal IAsyncEnumerable<TElement> ExecuteAsyncCore(IDataSourceAccess<TDatabase> source, TArgument argument, CancellationToken token = default)
    {
        // Deliberately outside an async iterator: arguments belong to this invocation,
        // while each enumeration creates fresh resources and observes current rows.
        var request = PreparedQueryExecution.CreateRequest(template, bindings, source, argument, token, asynchronous: true);
        return ExpressionQueryPlanExecutor.ExecuteEnumerableAsyncCore<TElement>(request);
    }
}
