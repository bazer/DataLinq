using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class SqlLocalProjectionExecutor
{
    private readonly DataSourceAccess dataSource;
    private readonly CancellationToken cancellationToken;

    public SqlLocalProjectionExecutor(
        DataSourceAccess dataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        this.dataSource = dataSource;
        this.cancellationToken = cancellationToken;
    }

    public IEnumerable<TResult> Execute<TResult>(QueryPlanInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var projection = invocation.Template.Projection;
        var recipe = GetProjectionRecipe(projection);
        var evaluationOptions = projection.Disposition == QueryPlanProjectionDisposition.AotSafe
            ? ProjectionEvaluationOptions.AotStrict
            : ProjectionEvaluationOptions.Default;
        var planSqlBuilder = new QueryPlanSqlBuilder(invocation, dataSource);
        var joinedSources = planSqlBuilder.GetJoinedSources().ToArray();

        return joinedSources.Length > 1
            ? ExecuteJoinedProjection<TResult>(
                invocation,
                recipe,
                evaluationOptions,
                planSqlBuilder,
                joinedSources)
            : ExecuteSingleSourceProjection<TResult>(
                invocation,
                recipe,
                evaluationOptions);
    }

    private IEnumerable<TResult> ExecuteSingleSourceProjection<TResult>(
        QueryPlanInvocation invocation,
        QueryPlanProjectionRecipe recipe,
        ProjectionEvaluationOptions evaluationOptions)
    {
        var rootSource = invocation.Template.Sources
            .First(static source => source.Kind == QueryPlanSourceKind.RootTable);
        // Reuse entity materialization so row-local recipes observe the same cache identity,
        // converter semantics, and transaction-local rows as normal entity queries.
        var entityInvocation = ReprojectAsEntity(invocation, rootSource);

        foreach (var row in ExecuteEntityRows(entityInvocation))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceValues = new Dictionary<QueryPlanSourceSlot, object?>
            {
                [rootSource] = row
            };
            var result = QueryPlanProjectionRecipeEvaluator.Evaluate(
                recipe,
                sourceValues,
                invocation.Values,
                evaluationOptions);
            cancellationToken.ThrowIfCancellationRequested();
            yield return QueryProjectionResultMaterializer.ConvertResult<TResult>(result);
        }
    }

    private IEnumerable<TResult> ExecuteJoinedProjection<TResult>(
        QueryPlanInvocation invocation,
        QueryPlanProjectionRecipe recipe,
        ProjectionEvaluationOptions evaluationOptions,
        QueryPlanSqlBuilder planSqlBuilder,
        QueryPlanSourceSlot[] joinedSources)
    {
        var select = planSqlBuilder.BuildSelect<TResult>();
        select.What(planSqlBuilder.GetJoinedPrimaryKeySelectors().ToArray());

        // Buffer every key tuple before cache hydration. Reloading a cache miss while the join
        // reader is open would require nested readers on the same provider/transaction connection.
        int[][]? primaryKeyOrdinalsBySource = null;
        var joinedPrimaryKeyRows = new List<object[]>();
        foreach (var reader in select.ReadReader(cancellationToken))
        {
            primaryKeyOrdinalsBySource ??= GetJoinedPrimaryKeyOrdinals(reader, joinedSources);
            var primaryKeysBySource = new object[joinedSources.Length];
            for (var sourceIndex = 0; sourceIndex < joinedSources.Length; sourceIndex++)
            {
                primaryKeysBySource[sourceIndex] = ReadPrimaryKey(
                    reader,
                    joinedSources[sourceIndex],
                    primaryKeyOrdinalsBySource[sourceIndex]);
            }

            joinedPrimaryKeyRows.Add(primaryKeysBySource);
        }

        foreach (var primaryKeysBySource in joinedPrimaryKeyRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceValues = new Dictionary<QueryPlanSourceSlot, object?>(joinedSources.Length);
            for (var sourceIndex = 0; sourceIndex < joinedSources.Length; sourceIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = joinedSources[sourceIndex];
                sourceValues[source] = GetJoinedRow(source, primaryKeysBySource[sourceIndex])
                    ?? throw new InvalidOperationException(
                        $"Joined row for table '{source.Table.DbName}' could not be materialized from its provider primary key.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = QueryPlanProjectionRecipeEvaluator.Evaluate(
                recipe,
                sourceValues,
                invocation.Values,
                evaluationOptions);
            cancellationToken.ThrowIfCancellationRequested();
            yield return QueryProjectionResultMaterializer.ConvertResult<TResult>(result);
        }
    }

    private IImmutableInstance? GetJoinedRow(
        QueryPlanSourceSlot source,
        object primaryKey)
    {
        var tableCache = dataSource.Provider.GetTableCache(source.Table);
        if (tableCache.TryGetRowFromProviderKeyValue(primaryKey, dataSource, out var row))
            return row;

        return primaryKey is DataLinqKey dataLinqKey
            ? tableCache.GetRow(dataLinqKey, dataSource)
            : null;
    }

    private static object ReadPrimaryKey(
        IDataLinqDataReader reader,
        QueryPlanSourceSlot source,
        IReadOnlyList<int> primaryKeyOrdinals)
    {
        var primaryKeyColumns = source.Table.PrimaryKeyColumns;
        if (primaryKeyColumns.Count == 1)
        {
            var primaryKeyColumn = primaryKeyColumns[0];
            var providerType = primaryKeyColumn.ProviderClrType;

            // Joined local projections buffer reader keys before cache hydration. Converted
            // primary keys cannot use the generated/model-valued scalar fast path, so preserve
            // the already-proven SC-3 dynamic-key boundary for the exact canonical Int32 slice.
            // Wider converted, UUID, and composite reader keys remain on their existing paths
            // until their provider and codec contracts are proven independently.
            if (primaryKeyColumn.HasScalarConverter &&
                providerType is not null &&
                (Nullable.GetUnderlyingType(providerType) ?? providerType) == typeof(int))
            {
                return DataLinqKey.FromValue(
                    ProviderRowDecoder.DecodeCanonicalValue(
                        reader,
                        primaryKeyColumn,
                        primaryKeyOrdinals[0],
                        "reader.joined-key-selection"));
            }

            return reader.GetValue<object>(primaryKeyColumn, primaryKeyOrdinals[0])!;
        }

        var values = new object?[primaryKeyColumns.Count];
        for (var index = 0; index < values.Length; index++)
            values[index] = reader.GetValue<object>(primaryKeyColumns[index], primaryKeyOrdinals[index]);

        return DataLinqKey.FromValues(values);
    }

    private static int[][] GetJoinedPrimaryKeyOrdinals(
        IDataLinqDataReader reader,
        IReadOnlyList<QueryPlanSourceSlot> sources)
    {
        var ordinals = new int[sources.Count][];
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            ordinals[sourceIndex] = new int[source.Table.PrimaryKeyColumns.Length];
            for (var columnIndex = 0; columnIndex < ordinals[sourceIndex].Length; columnIndex++)
            {
                ordinals[sourceIndex][columnIndex] = reader.GetOrdinal(
                    QueryPlanSqlBuilder.GetJoinedPrimaryKeyAlias(sourceIndex, columnIndex));
            }
        }

        return ordinals;
    }

    private static QueryPlanInvocation ReprojectAsEntity(
        QueryPlanInvocation invocation,
        QueryPlanSourceSlot source)
    {
        var template = invocation.Template;
        var entityTemplate = new QueryPlanTemplate(
            template.Sources,
            template.Operations,
            new QueryPlanProjection.Entity(source),
            ReprojectResultAsEntity(template.Result, source.ElementType),
            template.BindingDeclarations,
            template.Specialization);

        return QueryPlanInvocation.Bind(entityTemplate, invocation.Values.Items);
    }

    private static QueryPlanResult ReprojectResultAsEntity(
        QueryPlanResult result,
        Type entityType)
        => result.Kind switch
        {
            QueryPlanResultKind.Sequence or
            QueryPlanResultKind.Single or
            QueryPlanResultKind.SingleOrDefault or
            QueryPlanResultKind.First or
            QueryPlanResultKind.FirstOrDefault or
            QueryPlanResultKind.Last or
            QueryPlanResultKind.LastOrDefault => new QueryPlanResult(result.Kind, entityType),
            _ => result
        };

    private IEnumerable<object?> ExecuteEntityRows(QueryPlanInvocation invocation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = new QueryPlanSqlBuilder(invocation, dataSource)
            .BuildSelect<object>()
            .Execute();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    private static QueryPlanProjectionRecipe GetProjectionRecipe(
        QueryPlanProjection projection)
        => projection switch
        {
            QueryPlanProjection.Anonymous anonymous => anonymous.Recipe,
            QueryPlanProjection.ComputedRowLocal computed => computed.Recipe,
            QueryPlanProjection.JoinedRowLocal joined => joined.Recipe,
            _ => throw new QueryTranslationException(
                $"Projection '{projection.Kind}' does not define a normalized row-local execution recipe.")
        };
}
