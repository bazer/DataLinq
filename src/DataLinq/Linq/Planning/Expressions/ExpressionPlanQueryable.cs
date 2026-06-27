using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Exceptions;
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
        return ExpressionQueryPlanExecutor.Execute<TResult>(GetDataSource(), plan);
    }

    public IEnumerable<TElement> ExecuteEnumerable<TElement>(Expression expression)
    {
        var plan = Parse(expression, typeof(TElement));
        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TElement>(GetDataSource(), plan);
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
        DataLinqQueryPlan plan)
    {
        if (plan.Result.Kind != QueryPlanResultKind.Sequence)
            throw new QueryTranslationException($"Expression parser route expected a sequence result, but the plan result is '{plan.Result.Kind}'.");

        EnsureEntityProjection(plan.Projection);

        return new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<TElement>()
            .ExecuteAs<TElement>();
    }

    public static TResult Execute<TResult>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan)
    {
        return plan.Result.Kind switch
        {
            QueryPlanResultKind.Count or
            QueryPlanResultKind.Any or
            QueryPlanResultKind.Sum or
            QueryPlanResultKind.Min or
            QueryPlanResultKind.Max or
            QueryPlanResultKind.Average => ExecuteScalar<TResult>(dataSource, plan),
            QueryPlanResultKind.First => ExecuteSingle<TResult>(dataSource, plan, static sequence => sequence.First()),
            QueryPlanResultKind.FirstOrDefault => ExecuteSingle<TResult>(dataSource, plan, static sequence => sequence.FirstOrDefault()),
            QueryPlanResultKind.Single => ExecuteSingle<TResult>(dataSource, plan, static sequence => sequence.Single()),
            QueryPlanResultKind.SingleOrDefault => ExecuteSingle<TResult>(dataSource, plan, static sequence => sequence.SingleOrDefault()),
            QueryPlanResultKind.Last => ExecuteSingle<TResult>(dataSource, plan, static sequence => sequence.Last()),
            QueryPlanResultKind.LastOrDefault => ExecuteSingle<TResult>(dataSource, plan, static sequence => sequence.LastOrDefault()),
            var kind => throw new QueryTranslationException($"Expression parser route cannot execute query plan result '{kind}'.")
        };
    }

    private static TResult ExecuteSingle<TResult>(
        DataSourceAccess dataSource,
        DataLinqQueryPlan plan,
        Func<IEnumerable<TResult>, TResult?> selector)
    {
        EnsureEntityProjection(plan.Projection);

        var sequence = new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<TResult>()
            .ExecuteAs<TResult>();
        return selector(sequence)!;
    }

    private static TResult ExecuteScalar<TResult>(DataSourceAccess dataSource, DataLinqQueryPlan plan)
    {
        var select = new QueryPlanSqlBuilder(plan, dataSource)
            .BuildSelect<TResult>();
        var result = select.ExecuteScalar();

        return ConvertScalarResult<TResult>(result, plan.Result);
    }

    private static void EnsureEntityProjection(QueryPlanProjection projection)
    {
        if (projection is QueryPlanProjection.Entity)
            return;

        throw new QueryTranslationException(
            $"Expression parser executable route currently supports entity projection only. Projection kind: '{projection.Kind}'.");
    }

    private static TResult ConvertScalarResult<TResult>(object? result, QueryPlanResult planResult)
    {
        if (result is DBNull)
            result = null;

        if (planResult.Kind == QueryPlanResultKind.Any)
            return (TResult)(object)(Convert.ToInt64(result ?? 0, System.Globalization.CultureInfo.InvariantCulture) > 0);

        if (result is null)
        {
            if (planResult.Kind == QueryPlanResultKind.Sum || Nullable.GetUnderlyingType(typeof(TResult)) is not null)
                return default!;

            throw new InvalidOperationException($"Scalar query plan result '{planResult.Kind}' returned no value.");
        }

        var targetType = Nullable.GetUnderlyingType(typeof(TResult)) ?? typeof(TResult);
        if (targetType.IsInstanceOfType(result))
            return (TResult)result;

        return (TResult)Convert.ChangeType(result, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }
}
