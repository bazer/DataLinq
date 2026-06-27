using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Metadata;
using DataLinq.Mutation;

namespace DataLinq;

public class Queryable<T> : IOrderedQueryable<T>
{
    private readonly IQueryProvider provider;

    public Queryable(IQueryProvider provider, Expression expression)
    {
        this.provider = provider;
        Expression = expression;
    }

    public Queryable(DataSourceAccess dataSource, TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(table);

        provider = ExpressionQueryPlanProvider.ForExecution(dataSource);
        Expression = Expression.Constant(this);
    }

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider => provider;

    public IEnumerator<T> GetEnumerator()
    {
        if (provider is ExpressionQueryPlanProvider expressionProvider)
            return expressionProvider.ExecuteEnumerable<T>(Expression).GetEnumerator();

        return provider.Execute<IEnumerable<T>>(Expression).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
