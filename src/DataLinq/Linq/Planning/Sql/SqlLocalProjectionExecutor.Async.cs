using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Sql;

internal sealed partial class SqlLocalProjectionExecutor
{
    internal AsyncReaderInvocation<T> CaptureAsync<T>(QueryPlanInvocation invocation)
    {
        var projection = invocation.Template.Projection;
        var recipe = GetProjectionRecipe(projection);
        var options = projection.Disposition == QueryPlanProjectionDisposition.AotSafe
            ? ProjectionEvaluationOptions.AotStrict : ProjectionEvaluationOptions.Default;
        var builder = new QueryPlanSqlBuilder(invocation, dataSource);
        var joined = builder.GetJoinedSources().ToArray();
        if (joined.Length <= 1)
        {
            var root = invocation.Template.Sources.First(x => x.Kind == QueryPlanSourceKind.RootTable);
            var input = new QueryPlanSqlBuilder(ReprojectAsEntity(invocation, root), dataSource).BuildSelect<object>().CaptureModels();
            return AsyncReaderTransform.Capture(input, (rows, token) =>
            {
                var result = new List<T>(rows.Count);
                foreach (var row in rows)
                {
                    token.ThrowIfCancellationRequested();
                    var values = new Dictionary<QueryPlanSourceSlot, object?> { [root] = row };
                    result.Add(EvaluateAsyncProjection<T>(recipe, values, invocation, options));
                }
                return (IReadOnlyList<T>)result;
            });
        }
        var select = builder.BuildSelect<T>();
        select.What(builder.GetJoinedPrimaryKeySelectors().ToArray());
        var factory = IAsyncSqlReaderFactory.Require(dataSource.DatabaseAccess).CaptureInvocation()
            ?? throw new InvalidOperationException("The async reader factory returned no invocation snapshot.");
        var source = factory.BindReader(CapturedSql.Capture(select.ToSql()));
        return new(source, null, dataSource as Transaction,
            Continuation: new JoinedProjection<T>(this, invocation, recipe, options, joined, factory, source),
            Identity: ReadExecutionIdentity.Capture(dataSource, ExecutionOperationKind.Query));
    }

    private static T EvaluateAsyncProjection<T>(QueryPlanProjectionRecipe recipe, Dictionary<QueryPlanSourceSlot, object?> sources,
        QueryPlanInvocation invocation, ProjectionEvaluationOptions options) =>
        QueryProjectionResultMaterializer.ConvertResult<T>(QueryPlanProjectionRecipeEvaluator.Evaluate(recipe, sources, invocation.Values, options));

    private sealed class JoinedProjection<T>(SqlLocalProjectionExecutor executor, QueryPlanInvocation invocation,
        QueryPlanProjectionRecipe recipe, ProjectionEvaluationOptions options, QueryPlanSourceSlot[] sources,
        IAsyncSqlReaderFactory factory, IAsyncReaderSource initialSource) : IAsyncReaderContinuation<T>
    {
        private readonly List<DataLinqKey[]> keyRows = [];
        private int[][]? ordinals;
        private IAsyncReadFailureEvidence? evidence = initialSource as IAsyncReadFailureEvidence;
        public bool RequiresInitialReader => true;

        public void AddRow(IAsyncDataReader reader)
        {
            ordinals ??= GetJoinedPrimaryKeyOrdinals(reader, sources);
            var keys = new DataLinqKey[sources.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var columns = sources[i].Table.PrimaryKeyColumns;
                var values = new object?[columns.Count];
                for (var c = 0; c < values.Length; c++)
                    values[c] = ProviderRowDecoder.DecodeCanonicalValue(reader, columns[c], ordinals[i][c], "reader:async-joined-key", columns[c].IsGuidColumn);
                keys[i] = DataLinqKey.FromOwnedValues(values);
            }
            keyRows.Add(keys);
        }

        public async Task<IReadOnlyList<T>> CompleteAsync(TransactionOperationGate.Step? owner, CancellationToken token)
        {
            // The join reader is closed before any lookup. The captured factory and
            // owned key tuples remain fixed through hydration and local evaluation.
            var hydrated = new Dictionary<(int Source, DataLinqKey Key), IImmutableInstance>();
            var results = new List<T>(keyRows.Count);
            foreach (var keys in keyRows)
            {
                var values = new Dictionary<QueryPlanSourceSlot, object?>(sources.Length);
                for (var i = 0; i < sources.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    if (!hydrated.TryGetValue((i, keys[i]), out var row))
                    {
                        row = await executor.dataSource.Provider.GetTableCache(sources[i].Table)
                            .GetProviderRowAsyncCore(keys[i], executor.dataSource, token, owner, factory, current => evidence = current,
                                operationKind: ExecutionOperationKind.Query).ConfigureAwait(false)
                            ?? throw new InvalidOperationException($"Joined row for table '{sources[i].Table.DbName}' could not be materialized from its provider primary key.");
                        hydrated.Add((i, keys[i]), row);
                    }
                    values[sources[i]] = row;
                }
                token.ThrowIfCancellationRequested();
                results.Add(EvaluateAsyncProjection<T>(recipe, values, invocation, options));
            }
            return results;
        }

        public ReadFailureEvidence GetReadFailureEvidence(Exception failure) => evidence?.GetReadFailureEvidence(failure) ?? new();
    }
}
