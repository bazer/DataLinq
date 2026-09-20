using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Sql;

internal sealed partial class SqlQueryPlanBackend
{
    public IAsyncEnumerable<T> ExecuteSequenceAsync<T>(ValidatedQueryExecutionRequest request)
    {
        EnsureRequest(request);
        if (request.Invocation.Template.Result.Kind != QueryPlanResultKind.Sequence)
            throw new InvalidOperationException("A sequence backend request must have a sequence result.");
        return new AsyncReaderEnumerable<T>(() => CaptureAsyncRows<T>(request), request.Context.CancellationToken);
    }

    public Task<T> ExecuteAsync<T>(ValidatedQueryExecutionRequest request)
    {
        EnsureRequest(request);
        DataSourceAccess.EnsureReadAllowed(dataSource, "capture an asynchronous terminal query plan", operationKind: ExecutionOperationKind.Query);
        var result = request.Invocation.Template.Result;
        if (result.IsScalarResult)
        {
            EnsureScalarRequest(request);
            if (result.ResultType != typeof(T)) throw new InvalidOperationException("The scalar result type does not match the validated plan.");
            var factory = IAsyncSqlScalarFactory.Require(dataSource.DatabaseAccess);
            var select = new QueryPlanSqlBuilder(request.Invocation, dataSource).BuildSelect<object>();
            var source = factory.BindScalar(CapturedSql.Capture(select.ToSql()));
            return AsyncScalarRead.ExecuteAsync(source, dataSource as Transaction, value => ConvertScalarResult<T>(value, result), request.Context.CancellationToken,
                ReadExecutionIdentity.Capture(dataSource, ExecutionOperationKind.Query),
                QueryTelemetryContext.Capture(dataSource, select.Query.Table.DbName, scalar: true));
        }
        if (result.Kind == QueryPlanResultKind.Sequence)
            throw new InvalidOperationException("A terminal backend request cannot have a sequence result.");
        var captured = AsyncReaderTransform.Capture(CaptureAsyncRows<T>(request),
            (IReadOnlyList<T> rows, CancellationToken _) => (IReadOnlyList<T>)[ApplyTerminal(rows, result.Kind)]);
        return ReadTerminalAsync(captured, request.Context.CancellationToken);
    }

    private AsyncReaderInvocation<T> CaptureAsyncRows<T>(ValidatedQueryExecutionRequest request)
    {
        EnsureRequest(request);
        DataSourceAccess.EnsureReadAllowed(dataSource, "capture an asynchronous query plan", operationKind: ExecutionOperationKind.Query);
        var invocation = request.Invocation;
        if (invocation.Template.Projection is QueryPlanProjection.Entity)
        {
            EnsureEntityRequest(request);
            if (!typeof(T).IsAssignableFrom(invocation.Template.Result.ResultType))
                throw new InvalidOperationException("The entity result type does not match the validated plan.");
            var models = new QueryPlanSqlBuilder(invocation, dataSource).BuildSelect<object>().CaptureModels();
            return AsyncReaderTransform.Capture(models, (rows, token) =>
            {
                var result = new List<T>(rows.Count);
                foreach (var row in rows) { token.ThrowIfCancellationRequested(); result.Add((T)(object)row); }
                return (IReadOnlyList<T>)result;
            });
        }
        EnsureProjectionRequest<T>(request);
        return invocation.Template.Projection switch
        {
            QueryPlanProjection.ScalarMember or QueryPlanProjection.SqlRow or QueryPlanProjection.GroupedAggregate =>
                new SqlDirectProjectionExecutor(dataSource, default).CaptureAsync<T>(invocation),
            _ => new SqlLocalProjectionExecutor(dataSource, default).CaptureAsync<T>(invocation)
        };
    }

    private static T ApplyTerminal<T>(IReadOnlyList<T> rows, QueryPlanResultKind kind) => kind switch
    {
        // SQL rendering applies the original First limit; Single also verifies the
        // bounded result, matching synchronous execution. Last retains source order.
        QueryPlanResultKind.First or QueryPlanResultKind.Single => rows.Single(),
        QueryPlanResultKind.FirstOrDefault or QueryPlanResultKind.SingleOrDefault => rows.SingleOrDefault()!,
        QueryPlanResultKind.Last => rows.Last(),
        QueryPlanResultKind.LastOrDefault => rows.LastOrDefault()!,
        _ => throw new InvalidOperationException($"Unsupported terminal result '{kind}'.")
    };

    private static async Task<T> ReadTerminalAsync<T>(AsyncReaderInvocation<T> captured, CancellationToken token)
    {
        await using var iterator = new AsyncReaderEnumerable<T>(() => captured, token).GetAsyncEnumerator();
        if (!await iterator.MoveNextAsync().ConfigureAwait(false)) throw new InvalidOperationException("A terminal invocation produced no result slot.");
        var result = iterator.Current;
        _ = await iterator.MoveNextAsync().ConfigureAwait(false);
        return result;
    }
}
