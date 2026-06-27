using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Metadata;

namespace DataLinq.Linq.Planning.Expressions;

internal sealed class ExpressionQueryPlanProvider(DatabaseDefinition metadata) : IQueryProvider
{
    public IQueryable<TElement> CreateRoot<TElement>()
        => new ExpressionPlanQueryable<TElement>(this);

    public IQueryable CreateQuery(Expression expression)
        => throw new NotSupportedException("Non-generic query creation is not supported by the DataLinq expression plan provider.");

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        => new ExpressionPlanQueryable<TElement>(this, expression);

    public object? Execute(Expression expression)
        => throw new NotSupportedException("The DataLinq expression plan provider parses query plans but does not execute queries.");

    public TResult Execute<TResult>(Expression expression)
        => throw new NotSupportedException("The DataLinq expression plan provider parses query plans but does not execute queries.");

    public DataLinqQueryPlan Parse(Expression expression, Type resultType)
        => ExpressionQueryPlanParser.Convert(metadata, expression, resultType);
}

internal sealed class ExpressionPlanQueryable<T> : IOrderedQueryable<T>
{
    public ExpressionPlanQueryable(ExpressionQueryPlanProvider provider)
        : this(provider, null)
    {
    }

    public ExpressionPlanQueryable(ExpressionQueryPlanProvider provider, Expression? expression)
    {
        Provider = provider;
        Expression = expression ?? Expression.Constant(this);
    }

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider { get; }

    public IEnumerator<T> GetEnumerator()
        => throw new NotSupportedException("The DataLinq expression plan queryable parses expression trees but does not enumerate results.");

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
