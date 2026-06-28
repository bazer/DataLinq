using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Linq.Planning.Sql;

namespace DataLinq.Linq.Planning.Expressions;

internal sealed class ExpressionQueryPlanProvider : IQueryProvider
{
    private readonly DatabaseDefinition metadata;
    private readonly DataSourceAccess? dataSource;

    public ExpressionQueryPlanProvider(DatabaseDefinition metadata)
    {
        this.metadata = metadata;
    }

    private ExpressionQueryPlanProvider(DataSourceAccess dataSource)
    {
        this.dataSource = dataSource;
        metadata = dataSource.Provider.Metadata;
    }

    public static ExpressionQueryPlanProvider ForExecution(DataSourceAccess dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        return new ExpressionQueryPlanProvider(dataSource);
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
        return ExpressionQueryPlanExecutor.Execute<TResult>(GetDataSource(), plan, expression);
    }

    public IEnumerable<TElement> ExecuteEnumerable<TElement>(Expression expression)
    {
        var plan = Parse(expression, typeof(TElement));
        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TElement>(GetDataSource(), plan, expression);
    }

    public DataLinqQueryPlan Parse(Expression expression, Type resultType)
        => ExpressionQueryPlanParser.Convert(metadata, expression, resultType);

    private DataSourceAccess GetDataSource()
        => dataSource ?? throw new NotSupportedException("The DataLinq expression plan provider was created for parsing only and cannot execute queries.");
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
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        Expression expression)
    {
        if (plan.Result.Kind != QueryPlanResultKind.Sequence)
            throw new QueryTranslationException($"Expression parser route expected a sequence result, but the plan result is '{plan.Result.Kind}'.");

        if (plan.Projection is QueryPlanProjection.Entity)
        {
            return new QueryPlanSqlBuilder(plan, dataSource)
                .BuildSelect<object>()
                .Execute()
                .Cast<TElement>();
        }

        if (plan.Projection is QueryPlanProjection.GroupedAggregate groupedAggregate)
            return ExecuteGroupedAggregateProjection<TElement>(dataSource, plan, groupedAggregate);

        return ExecuteProjectedSequence<TElement>(dataSource, plan, expression);
    }

    public static TResult Execute<TResult>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        Expression expression)
    {
        return plan.Result.Kind switch
        {
            QueryPlanResultKind.Count or
            QueryPlanResultKind.Any or
            QueryPlanResultKind.Sum or
            QueryPlanResultKind.Min or
            QueryPlanResultKind.Max or
            QueryPlanResultKind.Average => ExecuteScalar<TResult>(dataSource, plan),
            QueryPlanResultKind.First => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.First()),
            QueryPlanResultKind.FirstOrDefault => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.FirstOrDefault()),
            QueryPlanResultKind.Single => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.Single()),
            QueryPlanResultKind.SingleOrDefault => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.SingleOrDefault()),
            QueryPlanResultKind.Last => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.Last()),
            QueryPlanResultKind.LastOrDefault => ExecuteSingle<TResult>(dataSource, plan, expression, static sequence => sequence.LastOrDefault()),
            var kind => throw new QueryTranslationException($"Expression parser route cannot execute query plan result '{kind}'.")
        };
    }

    private static TResult ExecuteSingle<TResult>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        Expression expression,
        Func<IEnumerable<TResult>, TResult?> selector)
    {
        if (plan.Projection is not QueryPlanProjection.Entity)
            return selector(ExecuteProjectedSequence<TResult>(dataSource, plan, expression))!;

        var sequence = new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<TResult>()
            .ExecuteAs<TResult>();
        return selector(sequence)!;
    }

    private static TResult ExecuteScalar<TResult>(DataSourceAccess dataSource, DataLinqQueryPlan plan)
    {
        if (RequiresPagedSequenceReduction(plan))
        {
            var pagedResult = ExecutePagedSequenceReduction(dataSource, plan);
            return ConvertScalarResult<TResult>(pagedResult, plan.Result);
        }

        var select = new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<TResult>();
        var result = select.ExecuteScalar();

        return ConvertScalarResult<TResult>(result, plan.Result);
    }

    private static bool RequiresPagedSequenceReduction(DataLinqQueryPlan plan)
        => plan.Result.Kind is QueryPlanResultKind.Count or QueryPlanResultKind.Any &&
           plan.Operations.Any(static operation => operation is QueryPlanOperation.Skip or QueryPlanOperation.Take);

    private static object ExecutePagedSequenceReduction(DataSourceAccess dataSource, DataLinqQueryPlan plan)
    {
        var rootSource = plan.Sources.First(static source => source.Kind == QueryPlanSourceKind.RootTable);
        var sequencePlan = new DataLinqQueryPlan(
            plan.Sources,
            plan.Operations,
            new QueryPlanProjection.Entity(rootSource),
            QueryPlanResult.Sequence(rootSource.ElementType),
            plan.Bindings);
        var rows = ExecuteEntityRows(dataSource, sequencePlan);

        return plan.Result.Kind == QueryPlanResultKind.Any
            ? rows.Any() ? 1 : 0
            : rows.Count();
    }

    private static IEnumerable<TElement> ExecuteProjectedSequence<TElement>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        Expression expression)
    {
        var selector = GetProjectionLambda(expression);
        return plan.Operations.Any(static operation => operation is QueryPlanOperation.Join)
            ? ExecuteJoinedProjection<TElement>(dataSource, plan, selector)
            : ExecuteSingleSourceProjection<TElement>(dataSource, plan, selector);
    }

    private static IEnumerable<TElement> ExecuteSingleSourceProjection<TElement>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        LambdaExpression selector)
    {
        if (selector.Parameters.Count != 1)
            throw new QueryTranslationException($"Projection selector '{selector}' is not supported for a single-source query.");

        var rootSource = plan.Sources.First(static source => source.Kind == QueryPlanSourceKind.RootTable);
        var entityPlan = ReprojectAsEntity(plan, rootSource);
        foreach (var row in ExecuteEntityRows(dataSource, entityPlan))
            yield return ConvertProjectionResult<TElement>(
                ProjectionExpressionEvaluator.Evaluate(selector.Body, selector.Parameters[0], row));
    }

    private static IEnumerable<TElement> ExecuteJoinedProjection<TElement>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        LambdaExpression selector)
    {
        var joinedSources = plan.Sources
            .Where(static source => source.Kind is QueryPlanSourceKind.RootTable or QueryPlanSourceKind.ExplicitJoin)
            .OrderBy(static source => source.Id, StringComparer.Ordinal)
            .ToArray();

        if (selector.Parameters.Count != joinedSources.Length)
            throw new QueryTranslationException($"Join projection selector '{selector}' does not match the query plan source count.");

        var planSqlBuilder = new QueryPlanSqlBuilder(plan, dataSource);
        var select = planSqlBuilder.BuildSelect<TElement>();
        select.What(planSqlBuilder.GetJoinedPrimaryKeySelectors().ToArray());

        int[][]? primaryKeyOrdinalsBySource = null;
        foreach (var reader in select.ReadReader())
        {
            primaryKeyOrdinalsBySource ??= GetJoinedPrimaryKeyOrdinals(reader, joinedSources);
            var parameterValues = new Dictionary<ParameterExpression, object?>(selector.Parameters.Count);
            for (var sourceIndex = 0; sourceIndex < joinedSources.Length; sourceIndex++)
            {
                var source = joinedSources[sourceIndex];
                parameterValues[selector.Parameters[sourceIndex]] = dataSource.Provider.GetTableCache(source.Table)
                    .GetRow(reader, primaryKeyOrdinalsBySource[sourceIndex], dataSource)
                    ?? throw new InvalidOperationException($"Joined row for table '{source.Table.DbName}' could not be materialized from its provider primary key.");
            }

            yield return ConvertProjectionResult<TElement>(
                ProjectionExpressionEvaluator.Evaluate(selector.Body, parameterValues));
        }
    }

    private static IEnumerable<TElement> ExecuteGroupedAggregateProjection<TElement>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        QueryPlanProjection.GroupedAggregate projection)
    {
        var select = new QueryPlanSqlBuilder(plan, dataSource).BuildSelect<TElement>();

        foreach (var reader in select.ReadReader())
        {
            var values = new object?[projection.Members.Count];
            for (var index = 0; index < projection.Members.Count; index++)
            {
                var member = projection.Members[index];
                var ordinal = reader.GetOrdinal(member.Name);
                var rawValue = reader.IsDbNull(ordinal) ? null : reader.GetValue(ordinal);
                values[index] = ConvertReaderValue(rawValue, member.Value.ClrType);
            }

            yield return CreateProjectionRow<TElement>(projection.Constructor, values);
        }
    }

    private static TElement CreateProjectionRow<TElement>(ConstructorInfo constructor, IReadOnlyList<object?> values)
    {
        var parameters = constructor.GetParameters();
        if (parameters.Length != values.Count)
        {
            throw new QueryTranslationException(
                $"Grouped aggregate projection constructor expects {parameters.Length} values, but the query plan supplied {values.Count}.");
        }

        var arguments = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
            arguments[index] = ConvertReaderValue(values[index], parameters[index].ParameterType);

        return ConvertProjectionResult<TElement>(constructor.Invoke(arguments));
    }

    private static int[][] GetJoinedPrimaryKeyOrdinals(IDataLinqDataReader reader, IReadOnlyList<QueryPlanSourceSlot> sources)
    {
        var ordinals = new int[sources.Count][];
        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            ordinals[sourceIndex] = new int[source.Table.PrimaryKeyColumns.Length];
            for (var columnIndex = 0; columnIndex < ordinals[sourceIndex].Length; columnIndex++)
                ordinals[sourceIndex][columnIndex] = reader.GetOrdinal(QueryPlanSqlBuilder.GetJoinedPrimaryKeyAlias(sourceIndex, columnIndex));
        }

        return ordinals;
    }

    private static DataLinqQueryPlan ReprojectAsEntity(DataLinqQueryPlan plan, QueryPlanSourceSlot source)
        => new(
            plan.Sources,
            plan.Operations,
            new QueryPlanProjection.Entity(source),
            plan.Result,
            plan.Bindings);

    private static IEnumerable<object?> ExecuteEntityRows(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan)
        => new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<object>()
            .Execute()
            .Cast<object?>();

    private static LambdaExpression GetProjectionLambda(Expression expression)
    {
        if (TryGetProjectionLambda(expression, out var selector))
            return selector;

        throw new QueryTranslationException(
            $"Projection expression '{expression}' is not supported by the DataLinq expression parser execution route.");
    }

    private static bool TryGetProjectionLambda(Expression expression, out LambdaExpression selector)
    {
        expression = UnwrapConvert(expression);
        if (expression is MethodCallExpression methodCall && IsQueryableMethod(methodCall))
        {
            if (methodCall.Method.Name == nameof(Queryable.Select) && methodCall.Arguments.Count == 2)
            {
                selector = UnwrapLambda(methodCall.Arguments[1], methodCall.ToString());
                return true;
            }

            if (methodCall.Method.Name == nameof(Queryable.Join) && methodCall.Arguments.Count == 5)
            {
                selector = UnwrapLambda(methodCall.Arguments[4], methodCall.ToString());
                return true;
            }

            if (IsTerminalOperator(methodCall.Method.Name) && methodCall.Arguments.Count > 0)
                return TryGetProjectionLambda(methodCall.Arguments[0], out selector);

            if (IsProjectionPassthroughOperator(methodCall.Method.Name) && methodCall.Arguments.Count > 0)
                return TryGetProjectionLambda(methodCall.Arguments[0], out selector);
        }

        selector = null!;
        return false;
    }

    private static bool IsQueryableMethod(MethodCallExpression methodCall)
        => methodCall.Method.DeclaringType == typeof(Queryable);

    private static bool IsTerminalOperator(string methodName)
        => methodName is nameof(Queryable.Single) or
            nameof(Queryable.SingleOrDefault) or
            nameof(Queryable.First) or
            nameof(Queryable.FirstOrDefault) or
            nameof(Queryable.Last) or
            nameof(Queryable.LastOrDefault);

    private static bool IsProjectionPassthroughOperator(string methodName)
        => methodName is nameof(Queryable.OrderBy) or
            nameof(Queryable.OrderByDescending) or
            nameof(Queryable.ThenBy) or
            nameof(Queryable.ThenByDescending) or
            nameof(Queryable.Skip) or
            nameof(Queryable.Take);

    private static LambdaExpression UnwrapLambda(Expression expression, string context)
    {
        expression = UnwrapConvert(expression);
        return expression switch
        {
            LambdaExpression lambda => lambda,
            UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression lambda } => lambda,
            _ => throw new QueryTranslationException($"Lambda expression '{expression}' is not supported in {context}.")
        };
    }

    private static Expression UnwrapConvert(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert ||
                unary.NodeType == ExpressionType.ConvertChecked ||
                unary.NodeType == ExpressionType.Quote))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static T ConvertProjectionResult<T>(object? value)
    {
        if (value is null)
            return default!;

        if (value is T typed)
            return typed;

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    private static object? ConvertReaderValue(object? value, Type targetType)
    {
        if (value is DBNull)
            value = null;

        if (value is null)
            return null;

        var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullableTarget.IsInstanceOfType(value))
            return value;

        if (nonNullableTarget.IsEnum)
        {
            return value is string stringValue
                ? Enum.Parse(nonNullableTarget, stringValue, ignoreCase: false)
                : Enum.ToObject(nonNullableTarget, value);
        }

        return Convert.ChangeType(value, nonNullableTarget, CultureInfo.InvariantCulture);
    }

    private static TResult ConvertScalarResult<TResult>(object? result, QueryPlanResult planResult)
    {
        if (result is DBNull)
            result = null;

        if (planResult.Kind == QueryPlanResultKind.Any)
            return (TResult)(object)(Convert.ToInt64(result ?? 0, CultureInfo.InvariantCulture) > 0);

        if (result is null)
        {
            if (planResult.Kind == QueryPlanResultKind.Sum || Nullable.GetUnderlyingType(typeof(TResult)) is not null)
                return default!;

            throw new InvalidOperationException($"Scalar query plan result '{planResult.Kind}' returned no value.");
        }

        var targetType = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);
        if (targetType.IsInstanceOfType(result))
            return (TResult)result;

        return (TResult)Convert.ChangeType(result, targetType, CultureInfo.InvariantCulture);
    }
}
