using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Exceptions;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Linq.Planning.Sql;

internal sealed class QueryPlanSqlBuilder
{
    private readonly DataLinqQueryPlan plan;
    private readonly DataSourceAccess dataSource;
    private readonly QueryPlanSqlSourceMap sourceMap;
    private readonly QueryPlanSqlValueRenderer valueRenderer;

    public QueryPlanSqlBuilder(DataLinqQueryPlan plan, DataSourceAccess dataSource)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(dataSource);

        this.plan = plan;
        this.dataSource = dataSource;
        sourceMap = new QueryPlanSqlSourceMap(plan);
        valueRenderer = new QueryPlanSqlValueRenderer(dataSource, sourceMap, plan.Bindings);
    }

    public SqlQuery<T> BuildSqlQuery<T>()
    {
        var root = sourceMap.RootSource;
        var query = new SqlQuery<T>(root.Table, dataSource, root.Alias);
        var predicateBuilder = new QueryPlanSqlPredicateBuilder<T>(query, sourceMap, valueRenderer);
        var pushdownIndex = 0;

        foreach (var operation in plan.Operations)
        {
            switch (operation)
            {
                case QueryPlanOperation.Pushdown pushdown:
                    query = PushDown(query, pushdown, pushdownIndex++);
                    predicateBuilder = new QueryPlanSqlPredicateBuilder<T>(query, sourceMap, valueRenderer);
                    break;

                case QueryPlanOperation.Join join:
                    ApplyJoin(query, join.JoinShape);
                    break;

                case QueryPlanOperation.Where where:
                    predicateBuilder.Apply(where.Predicate);
                    break;

                case QueryPlanOperation.OrderBy orderBy:
                    ApplyOrderBy(query, orderBy);
                    break;

                case QueryPlanOperation.GroupBy groupBy:
                    ApplyGroupBy(query, groupBy);
                    break;

                case QueryPlanOperation.Skip skip:
                    query.Offset(GetPagingCount(skip.Count, QueryPlanOperationKind.Skip));
                    break;

                case QueryPlanOperation.Take take:
                    query.Limit(GetPagingCount(take.Count, QueryPlanOperationKind.Take));
                    break;

                default:
                    throw new QueryTranslationException($"Query plan operation '{operation.Kind}' is not supported by SQL rendering.");
            }
        }

        ApplyResultLimit(query);
        return query;
    }

    private SqlQuery<T> PushDown<T>(SqlQuery<T> currentQuery, QueryPlanOperation.Pushdown pushdown, int pushdownIndex)
    {
        if (currentQuery.HasDerivedSource)
            throw new QueryTranslationException("Nested query pushdown is not supported after a derived source has already been applied.");

        if (pushdown.Operations.Any(static operation => operation is QueryPlanOperation.Join))
            throw new QueryTranslationException("Query pushdown over joins is not supported until joined source-slot composition is implemented.");

        var root = sourceMap.RootSource;
        var innerPlan = new DataLinqQueryPlan(
            plan.Sources,
            pushdown.Operations,
            new QueryPlanProjection.Entity(root),
            QueryPlanResult.Sequence(root.ElementType),
            plan.Bindings);
        var innerSql = new QueryPlanSqlBuilder(innerPlan, dataSource)
            .BuildSelect<object>()
            .ToSql($"dlp{pushdownIndex}_");

        return new SqlQuery<T>(root.Table, dataSource, root.Alias)
            .UseDerivedSource(innerSql);
    }

    public Select<T> BuildSelect<T>()
    {
        var query = BuildSqlQuery<T>();
        var select = query.SelectQuery();

        if (plan.Projection is QueryPlanProjection.GroupedAggregate groupedAggregate)
        {
            select.What(GetGroupedAggregateSelectors(groupedAggregate).ToArray());
            return select;
        }

        switch (plan.Result.Kind)
        {
            case QueryPlanResultKind.Count:
            case QueryPlanResultKind.Any:
                select.What("COUNT(*)");
                break;

            case QueryPlanResultKind.Sum:
            case QueryPlanResultKind.Min:
            case QueryPlanResultKind.Max:
            case QueryPlanResultKind.Average:
                select.What(GetAggregateSelectorSql());
                break;
        }

        return select;
    }

    public IReadOnlyList<string> GetJoinedPrimaryKeySelectors()
    {
        var joinSources = GetJoinedSources();
        var selectors = new List<string>();
        var escape = dataSource.Provider.Constants.EscapeCharacter;

        for (var sourceIndex = 0; sourceIndex < joinSources.Count; sourceIndex++)
        {
            var source = joinSources[sourceIndex];
            for (var columnIndex = 0; columnIndex < source.Table.PrimaryKeyColumns.Length; columnIndex++)
            {
                var column = source.Table.PrimaryKeyColumns[columnIndex];
                selectors.Add($"{source.Alias}.{escape}{column.DbName}{escape} AS {escape}{GetJoinedPrimaryKeyAlias(sourceIndex, columnIndex)}{escape}");
            }
        }

        return selectors;
    }

    public IReadOnlyList<QueryPlanSourceSlot> GetJoinedSources()
    {
        if (!plan.Operations.Any(static operation => operation is QueryPlanOperation.Join))
            return [sourceMap.RootSource];

        return plan.Sources
            .Where(static source => source.Kind is QueryPlanSourceKind.RootTable or QueryPlanSourceKind.ExplicitJoin)
            .OrderBy(static source => source.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static string GetJoinedPrimaryKeyAlias(int sourceIndex, int columnIndex)
        => $"dl_{sourceIndex}_pk_{columnIndex}";

    private void ApplyJoin<T>(SqlQuery<T> query, QueryPlanJoin join)
    {
        if (join.Kind != QueryPlanJoinKind.Inner)
            throw new QueryTranslationException($"Query plan join kind '{join.Kind}' is not supported by SQL rendering.");

        var leftSource = sourceMap.Get(join.LeftSource);
        var rightSource = sourceMap.Get(join.RightSource);
        if (rightSource.Kind is not QueryPlanSourceKind.ExplicitJoin and not QueryPlanSourceKind.ImplicitJoin)
            throw new QueryTranslationException($"Query plan join source slot '{rightSource.Id}' is not a join source.");

        query.Join(rightSource.Table.DbName, rightSource.Alias)
            .On(on => on
                .AddWhere(join.LeftColumn.DbName, leftSource.Alias, BooleanType.And)
                .EqualToColumn(join.RightColumn.DbName, rightSource.Alias));
    }

    private void ApplyOrderBy<T>(SqlQuery<T> query, QueryPlanOperation.OrderBy orderBy)
    {
        foreach (var ordering in orderBy.Orderings)
        {
            if (ordering.Value is not QueryPlanColumnValue column)
            {
                throw new QueryTranslationException(
                    $"Query plan order-by value '{ordering.Value.Kind}' is not supported by SQL rendering. " +
                    "Only direct source-slot columns are supported for ordering.");
            }

            var source = sourceMap.Get(column.Source);
            query.OrderBy(column.Column, source.Alias, ordering.Direction == QueryPlanOrderingDirection.Ascending);
        }
    }

    private void ApplyGroupBy<T>(SqlQuery<T> query, QueryPlanOperation.GroupBy groupBy)
    {
        foreach (var key in groupBy.Keys)
            query.GroupByRaw(valueRenderer.RenderSqlExpression(key));
    }

    private int GetPagingCount(QueryPlanValue count, QueryPlanOperationKind operationKind)
    {
        var value = valueRenderer.GetScalarValue(count);
        if (value is null)
            throw new QueryTranslationException($"Query plan {operationKind} count cannot be null.");

        var result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        if (result < 0)
            throw new QueryTranslationException($"Query plan {operationKind} count cannot be negative.");

        return result;
    }

    private void ApplyResultLimit<T>(SqlQuery<T> query)
    {
        switch (plan.Result.Kind)
        {
            case QueryPlanResultKind.Single:
            case QueryPlanResultKind.SingleOrDefault:
                query.Limit(2);
                break;

            case QueryPlanResultKind.First:
            case QueryPlanResultKind.FirstOrDefault:
            case QueryPlanResultKind.Any:
                query.Limit(1);
                break;
        }
    }

    private string GetAggregateSelectorSql()
    {
        var selector = plan.Result.AggregateSelector
            ?? throw new QueryTranslationException($"Query plan result '{plan.Result.Kind}' requires an aggregate selector.");
        var selectorSql = GetAggregateColumnExpression(selector);

        return plan.Result.Kind switch
        {
            QueryPlanResultKind.Sum => $"COALESCE(SUM({selectorSql}), 0)",
            QueryPlanResultKind.Min => $"MIN({selectorSql})",
            QueryPlanResultKind.Max => $"MAX({selectorSql})",
            QueryPlanResultKind.Average => $"AVG({selectorSql})",
            _ => throw new QueryTranslationException($"Query plan result '{plan.Result.Kind}' is not an aggregate result.")
        };
    }

    private IReadOnlyList<string> GetGroupedAggregateSelectors(QueryPlanProjection.GroupedAggregate projection)
    {
        var selectors = new List<string>(projection.Members.Count);
        var escape = dataSource.Provider.Constants.EscapeCharacter;

        foreach (var member in projection.Members)
        {
            var expression = member.Value switch
            {
                QueryPlanGroupKeyValue groupKey => valueRenderer.RenderSqlExpression(groupKey.Key),
                QueryPlanGroupedAggregateValue { Aggregate: QueryPlanGroupedAggregateKind.Count } => "COUNT(*)",
                QueryPlanGroupedAggregateValue aggregate => throw new QueryTranslationException(
                    $"Grouped aggregate '{aggregate.Aggregate}' is not supported by SQL rendering."),
                _ => throw new QueryTranslationException(
                    $"Grouped aggregate projection value '{member.Value.Kind}' is not supported by SQL rendering.")
            };

            selectors.Add($"{expression} AS {escape}{member.Name}{escape}");
        }

        return selectors;
    }

    private string GetAggregateColumnExpression(QueryPlanValue selector)
    {
        var unwrapped = selector is QueryPlanConvertedValue converted
            ? converted.Value
            : selector;

        if (unwrapped is not QueryPlanColumnValue column)
        {
            throw new QueryTranslationException(
                $"Query plan aggregate selector '{unwrapped.Kind}' is not supported. " +
                "Only direct numeric source-slot columns are supported.");
        }

        if (!IsNumericType(unwrapped.ClrType))
        {
            throw new QueryTranslationException(
                $"Query plan aggregate selector column '{column.Column.DbName}' must be numeric. " +
                $"Selector type: {unwrapped.ClrType}");
        }

        return valueRenderer.RenderColumnSql(column);
    }

    private static bool IsNumericType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type.IsEnum)
            return false;

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or
            TypeCode.SByte or
            TypeCode.Int16 or
            TypeCode.UInt16 or
            TypeCode.Int32 or
            TypeCode.UInt32 or
            TypeCode.Int64 or
            TypeCode.UInt64 or
            TypeCode.Single or
            TypeCode.Double or
            TypeCode.Decimal => true,
            _ => false
        };
    }
}
