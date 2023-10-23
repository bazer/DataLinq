using DataLinq.Query;
using DataLinq.Utils;
using Remotion.Linq.Clauses;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace DataLinq.Linq.Visitors
{
    internal class WhereVisitor : ExpressionVisitor
    {
        protected SqlQuery query;
        NonNegativeInt negations = new NonNegativeInt(0);
        NonNegativeInt ors = new NonNegativeInt(0);
        Stack<WhereGroup<object>> whereGroups = new Stack<WhereGroup<object>>();

        internal WhereVisitor(SqlQuery query)
        {
            this.query = query;
        }

        internal void Parse(WhereClause whereClause)
        {
            Visit(whereClause.Predicate);
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
                negations.Increment();

            return base.VisitUnary(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            // If the node can be reduced, reduce it and visit the reduced form.
            if (node.CanReduce)
            {
                return Visit(node.Reduce());
            }

            // Otherwise, fall back to the base behavior.
            return base.VisitExtension(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Object == null)
                throw new ArgumentNullException(nameof(node.Object), "Parsing of node without object is not supported");

            if (node.Arguments.Count != 1)
                throw new NotImplementedException($"Operation '{node.Method.Name}' with {node.Arguments.Count} arguments not implemented");

            var field = (string)query.GetValue(node.Object);
            var value = query.GetValue(node.Arguments[0]);

            var group = whereGroups.Count > 0
                ? whereGroups.Peek()
                : query.GetBaseWhereGroup();

            var where = negations.Decrement() > 0
                ? group.AddWhereNot(field, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And)
                : group.AddWhere(field, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And);

            switch (node.Method.Name)
            {
                case "StartsWith":
                        where.Like(value + "%");
                    break;
                case "EndsWith":
                        where.Like("%" + value);
                    break;
                default: throw new NotImplementedException($"Operation '{node.Method.Name}' with {node.Arguments.Count} arguments not implemented");
            }

            return node;
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse)
            {
                whereGroups.Push(negations.Decrement() > 0
                    ? query.AddWhereNotGroup(ors.Decrement() > 0? BooleanType.Or : BooleanType.And)
                    : query.AddWhereGroup(ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And));

                Visit(node.Left);

                if (node.NodeType == ExpressionType.OrElse)
                    ors.Increment();

                Visit(node.Right);

                whereGroups.Pop();

                return node;
            }

            var fields = query.GetFields(node);
            
            var group = whereGroups.Count > 0
                ? whereGroups.Peek()
                : query.GetBaseWhereGroup();

            var where = negations.Decrement() > 0
                ? group.AddWhereNot(fields.Key, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And)
                : group.AddWhere(fields.Key, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And);

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