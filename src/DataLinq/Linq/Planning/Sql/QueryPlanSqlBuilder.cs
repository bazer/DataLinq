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
    public const string ScalarProjectionAlias = "value";

    private readonly QueryPlanInvocation invocation;
    private readonly QueryPlanTemplate template;
    private readonly DataSourceAccess dataSource;
    private readonly QueryPlanSqlSourceMap sourceMap;
    private QueryPlanSqlValueRenderer valueRenderer;
    private QueryPlanDerivedColumnMap? derivedColumns;

    public QueryPlanSqlBuilder(QueryPlanInvocation invocation, DataSourceAccess dataSource)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(dataSource);

        this.invocation = invocation;
        template = invocation.Template;
        this.dataSource = dataSource;
        sourceMap = new QueryPlanSqlSourceMap(template);
        valueRenderer = new QueryPlanSqlValueRenderer(dataSource, sourceMap, invocation.Values);
    }

    public SqlQuery<T> BuildSqlQuery<T>()
    {
        var root = sourceMap.RootSource;
        var query = new SqlQuery<T>(root.Table, dataSource, root.Alias);
        var predicateBuilder = new QueryPlanSqlPredicateBuilder<T>(query, sourceMap, valueRenderer);
        var pushdownIndex = 0;

        foreach (var operation in template.Operations)
        {
            switch (operation)
            {
                case QueryPlanOperation.Pushdown pushdown:
                    query = PushDown(query, pushdown, pushdownIndex++, out derivedColumns);
                    valueRenderer = new QueryPlanSqlValueRenderer(dataSource, sourceMap, invocation.Values, derivedColumns);
                    predicateBuilder = new QueryPlanSqlPredicateBuilder<T>(query, sourceMap, valueRenderer);
                    break;

                case QueryPlanOperation.Join join:
                    ApplyJoin(query, join.JoinShape);
                    break;

                case QueryPlanOperation.Where where:
                    predicateBuilder.Apply(where.Predicate);
                    break;

                case QueryPlanOperation.Having having:
                    predicateBuilder.ApplyHaving(having.Predicate);
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

        ApplyResultShape(query);
        return query;
    }

    private SqlQuery<T> PushDown<T>(
        SqlQuery<T> currentQuery,
        QueryPlanOperation.Pushdown pushdown,
        int pushdownIndex,
        out QueryPlanDerivedColumnMap? pushedDownColumns)
    {
        pushedDownColumns = null;

        if (currentQuery.HasDerivedSource)
            throw new QueryTranslationException("Nested query pushdown is not supported after a derived source has already been applied.");

        if (pushdown.Operations.Any(static operation => operation is QueryPlanOperation.Join))
            return PushDownJoined<T>(pushdown, pushdownIndex, out pushedDownColumns);

        var root = sourceMap.RootSource;
        var innerTemplate = new QueryPlanTemplate(
            template.Sources,
            pushdown.Operations,
            new QueryPlanProjection.Entity(root),
            QueryPlanResult.Sequence(root.ElementType),
            template.BindingDeclarations,
            template.Specialization);
        var innerInvocation = QueryPlanInvocation.Bind(innerTemplate, invocation.Values.Items);
        var innerSql = new QueryPlanSqlBuilder(innerInvocation, dataSource)
            .BuildSelect<object>()
            .ToSql($"dlp{pushdownIndex}_");

        return new SqlQuery<T>(root.Table, dataSource, root.Alias)
            .UseDerivedSource(innerSql);
    }

    private SqlQuery<T> PushDownJoined<T>(
        QueryPlanOperation.Pushdown pushdown,
        int pushdownIndex,
        out QueryPlanDerivedColumnMap pushedDownColumns)
    {
        if (template.Projection is not QueryPlanProjection.SqlRow sqlRow)
        {
            throw new QueryTranslationException(
                "Joined pushdown is supported only for SQL-backed joined projection rows. " +
                "Materialize before composing further over row-local joined projections.");
        }

        var root = sourceMap.RootSource;
        var innerTemplate = new QueryPlanTemplate(
            template.Sources,
            pushdown.Operations,
            sqlRow,
            QueryPlanResult.Sequence(sqlRow.ResultType),
            template.BindingDeclarations,
            template.Specialization);
        var innerInvocation = QueryPlanInvocation.Bind(innerTemplate, invocation.Values.Items);
        var innerBuilder = new QueryPlanSqlBuilder(innerInvocation, dataSource);
        var innerSelect = innerBuilder.BuildSqlQuery<object>().SelectQuery();
        innerSelect.What(GetProjectionRowSelectors(sqlRow.Members)
            .Concat(GetJoinedPrimaryKeySelectors())
            .ToArray());

        var innerSql = innerSelect.ToSql($"dlp{pushdownIndex}_");
        pushedDownColumns = QueryPlanDerivedColumnMap.FromSqlRowProjection(root.Alias, sqlRow);

        return new SqlQuery<T>(root.Table, dataSource, root.Alias)
            .UseDerivedSource(innerSql);
    }

    public Select<T> BuildSelect<T>()
    {
        if (template.Projection is QueryPlanProjection.GroupedAggregate &&
            template.Result.Kind is QueryPlanResultKind.Count or QueryPlanResultKind.Any)
        {
            return BuildGroupedAggregateScalarSelect<T>();
        }

        var query = BuildSqlQuery<T>();
        var select = query.SelectQuery();

        if (template.Projection is QueryPlanProjection.GroupedAggregate groupedAggregate)
        {
            select.What(GetGroupedAggregateSelectors(groupedAggregate).ToArray());
            return select;
        }

        if (IsProjectionRowResult(template.Result.Kind))
        {
            switch (template.Projection)
            {
                case QueryPlanProjection.ScalarMember scalar:
                    select.What(GetScalarProjectionSelector(scalar));
                    return select;

                case QueryPlanProjection.SqlRow sqlRow:
                    select.What(GetProjectionRowSelectors(sqlRow.Members).ToArray());
                    return select;
            }
        }

        switch (template.Result.Kind)
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
                var alias = GetJoinedPrimaryKeyAlias(sourceIndex, columnIndex);
                var sourceSql = derivedColumns is null
                    ? $"{source.Alias}.{escape}{column.DbName}{escape}"
                    : $"{derivedColumns.SourceAlias}.{escape}{alias}{escape}";
                selectors.Add($"{sourceSql} AS {escape}{alias}{escape}");
            }
        }

        return selectors;
    }

    public IReadOnlyList<QueryPlanSourceSlot> GetJoinedSources()
    {
        if (!template.Operations.Any(static operation => ContainsJoinOperation(operation)))
            return [sourceMap.RootSource];

        return template.Sources
            .Where(static source => source.Kind is
                QueryPlanSourceKind.RootTable or
                QueryPlanSourceKind.ExplicitJoin or
                QueryPlanSourceKind.ImplicitJoin)
            .OrderBy(static source => source.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ContainsJoinOperation(QueryPlanOperation operation)
        => operation switch
        {
            QueryPlanOperation.Join => true,
            QueryPlanOperation.Pushdown pushdown => pushdown.Operations.Any(static inner => ContainsJoinOperation(inner)),
            _ => false
        };

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
            if (ordering.Value is QueryPlanColumnValue column)
            {
                query.OrderByRaw(
                    valueRenderer.RenderColumnSql(column),
                    ordering.Direction == QueryPlanOrderingDirection.Ascending);
                continue;
            }

            if (ordering.Value is QueryPlanGroupKeyValue or QueryPlanGroupedAggregateValue)
            {
                query.OrderByRaw(
                    valueRenderer.RenderSqlExpression(ordering.Value),
                    ordering.Direction == QueryPlanOrderingDirection.Ascending);
                continue;
            }

            throw new QueryTranslationException(
                $"Query plan order-by value '{ordering.Value.Kind}' is not supported by SQL rendering. " +
                "Only direct source-slot columns and grouped aggregate row members are supported for ordering.");
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

    private void ApplyResultShape<T>(SqlQuery<T> query)
    {
        switch (template.Result.Kind)
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

            case QueryPlanResultKind.Last:
            case QueryPlanResultKind.LastOrDefault:
                if (query.OrderByList.Count != 0)
                {
                    query.OrderByList = query.OrderByList
                        .Select(static orderBy => orderBy.ReverseDirection())
                        .ToList();
                    query.Limit(1);
                }

                break;
        }
    }

    private string GetAggregateSelectorSql()
    {
        var selector = template.Result.AggregateSelector
            ?? throw new QueryTranslationException($"Query plan result '{template.Result.Kind}' requires an aggregate selector.");
        var selectorSql = GetAggregateColumnExpression(selector, template.Result.Kind.ToString());

        return template.Result.Kind switch
        {
            QueryPlanResultKind.Sum => $"COALESCE(SUM({selectorSql}), 0)",
            QueryPlanResultKind.Min => $"MIN({selectorSql})",
            QueryPlanResultKind.Max => $"MAX({selectorSql})",
            QueryPlanResultKind.Average => $"AVG({selectorSql})",
            _ => throw new QueryTranslationException($"Query plan result '{template.Result.Kind}' is not an aggregate result.")
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
                QueryPlanGroupedAggregateValue aggregate => valueRenderer.RenderGroupedAggregateSql(aggregate),
                _ => throw new QueryTranslationException(
                    $"Grouped aggregate projection value '{member.Value.Kind}' is not supported by SQL rendering.")
            };

            selectors.Add($"{expression} AS {escape}{member.Name}{escape}");
        }

        return selectors;
    }

    private string GetScalarProjectionSelector(QueryPlanProjection.ScalarMember projection)
    {
        var value = new QueryPlanColumnValue(projection.Source, projection.Column, projection.ResultType);
        var escape = dataSource.Provider.Constants.EscapeCharacter;
        return $"{valueRenderer.RenderSqlExpression(value)} AS {escape}{ScalarProjectionAlias}{escape}";
    }

    private IReadOnlyList<string> GetProjectionRowSelectors(IReadOnlyList<QueryPlanProjectionMember> members)
    {
        var selectors = new List<string>(members.Count);
        var escape = dataSource.Provider.Constants.EscapeCharacter;

        foreach (var member in members)
            selectors.Add($"{valueRenderer.RenderSqlExpression(member.Value)} AS {escape}{member.Name}{escape}");

        return selectors;
    }

    private static bool IsProjectionRowResult(QueryPlanResultKind kind)
        => kind is QueryPlanResultKind.Sequence or
            QueryPlanResultKind.First or
            QueryPlanResultKind.FirstOrDefault or
            QueryPlanResultKind.Single or
            QueryPlanResultKind.SingleOrDefault or
            QueryPlanResultKind.Last or
            QueryPlanResultKind.LastOrDefault;

    private Select<T> BuildGroupedAggregateScalarSelect<T>()
    {
        var root = sourceMap.RootSource;
        var innerTemplate = new QueryPlanTemplate(
            template.Sources,
            template.Operations,
            template.Projection,
            QueryPlanResult.Sequence(template.Projection.ResultType),
            template.BindingDeclarations,
            template.Specialization);
        var innerInvocation = QueryPlanInvocation.Bind(innerTemplate, invocation.Values.Items);
        var innerSql = new QueryPlanSqlBuilder(innerInvocation, dataSource)
            .BuildSelect<object>()
            .ToSql("dlg0_");

        return new SqlQuery<T>(root.Table, dataSource, root.Alias)
            .UseDerivedSource(innerSql)
            .SelectQuery()
            .What("COUNT(*)");
    }

    private string GetAggregateColumnExpression(QueryPlanValue selector, string operatorName)
    {
        var column = QueryPlanAggregateSelectorValidator.RequireDirectNumericColumn(selector, operatorName);
        return valueRenderer.RenderColumnSql(column);
    }
}
