using System;
using System.Linq.Expressions;
using Remotion.Linq.Clauses;

namespace DataLinq.Linq.Visitors;

internal class OrderByVisitor<T> : ExpressionVisitor
{
    protected QueryBuilder<T> builder;
    protected OrderingDirection direction;

    internal OrderByVisitor(QueryBuilder<T> queryBuilder)
    {
        this.builder = queryBuilder;
    }

    internal void Parse(Ordering ordering)
    {
        direction = ordering.OrderingDirection;
        Visit(ordering.Expression);
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        if (node.NodeType == ExpressionType.MemberAccess)
            builder.AddOrderBy(node, direction);
        else
            throw new NotImplementedException($"Operation '{node}' not implemented");

        return node;
    }
}