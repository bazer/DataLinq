using Remotion.Linq.Clauses;
using System;
using System.Linq.Expressions;
using Slim.Query;

namespace Slim.Linq.Visitors
{
    internal class WhereVisitor: ExpressionVisitor
    {
        protected SqlQuery query;

        internal WhereVisitor(SqlQuery query)
        {
            this.query = query;
        }

        internal void Parse(WhereClause whereClause)
        {
            Visit(whereClause.Predicate);
        }

        protected override Expression VisitExtension(Expression node)
        {
            return base.VisitExtension(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            return base.VisitMember(node);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.AndAlso)
            {
                Visit(node.Left);
                Visit(node.Right);

                return node;
            }

            var fields = query.GetFields(node);

            var where = query.Where(fields.Key);

            if (node.NodeType == ExpressionType.Equal)
                where.EqualTo(fields.Value);
            else if (node.NodeType == ExpressionType.NotEqual)
                where.NotEqualTo(fields.Value);
            else if (node.NodeType == ExpressionType.GreaterThan)
                where.GreaterThan(fields.Value);
            else if (node.NodeType == ExpressionType.GreaterThanOrEqual)
                where.GreaterThanOrEqual(fields.Value);
            else if (node.NodeType == ExpressionType.LessThan)
                where.LessThan(fields.Value);
            else if (node.NodeType == ExpressionType.LessThanOrEqual)
                where.LessThanOrEqual(fields.Value);
            else
                throw new NotImplementedException("Operation not implemented");

            return node;
        }
    }
}