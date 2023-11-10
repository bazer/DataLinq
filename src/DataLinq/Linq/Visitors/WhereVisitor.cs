using DataLinq.Query;
using DataLinq.Utils;
using Remotion.Linq.Clauses;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace DataLinq.Linq.Visitors
{
    /// <summary>
    /// The WhereVisitor class is responsible for traversing an expression tree 
    /// that represents a LINQ Where clause and converting it into the corresponding 
    /// SQL query predicates.
    /// </summary>
    internal class WhereVisitor : ExpressionVisitor
    {
        protected SqlQuery query;
        // Tracks the number of negations (NOT operations) encountered.
        NonNegativeInt negations = new NonNegativeInt(0);
        // Tracks the number of OR operations encountered.
        NonNegativeInt ors = new NonNegativeInt(0);
        // A stack to manage groups of WHERE clauses.
        Stack<WhereGroup<object>> whereGroups = new Stack<WhereGroup<object>>();

        /// <summary>
        /// Initializes a new instance of the WhereVisitor with a given SQL query.
        /// </summary>
        /// <param name="query">The SQL query to be built upon.</param>
        internal WhereVisitor(SqlQuery query)
        {
            this.query = query;
        }

        /// <summary>
        /// Parses a given WhereClause into SQL query predicates.
        /// </summary>
        /// <param name="whereClause">The LINQ WhereClause to parse.</param>
        internal void Parse(WhereClause whereClause)
        {
            Visit(whereClause.Predicate);
        }

        // TODO: Consider handling other unary operators if needed.
        protected override Expression VisitUnary(UnaryExpression node)
        {
            if (node.NodeType == ExpressionType.Not)
                negations.Increment();

            // Unwraps nested Convert operations, if any, to get to the actual expression.
            if (node.NodeType == ExpressionType.Convert)
            {
                while (node.NodeType == ExpressionType.Convert && node.Operand is UnaryExpression expr)
                    node = expr;

                return Visit(node.Operand);
            }

            return base.VisitUnary(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node.CanReduce)
            {
                return Visit(node.Reduce());
            }

            if (node is Remotion.Linq.Clauses.Expressions.QuerySourceReferenceExpression querySourceRef)
            {
                return querySourceRef;
            }

            return base.VisitExtension(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == "Value" && Nullable.GetUnderlyingType(node.Expression.Type) != null)
            {
                var nonNullableType = Nullable.GetUnderlyingType(node.Expression.Type);
                var memberExp = Expression.Property(node.Expression, nonNullableType.GetProperty("Value"));

                return Expression.Convert(memberExp, nonNullableType);
            }

            return base.VisitMember(node);
        }

        // TODO: Extend to support additional method calls as necessary.
        // Throws exceptions for unsupported scenarios, indicating areas that require implementation.
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

        // Handles binary operations and translates them into SQL predicates.
        // TODO: Handle additional binary expressions as needed.
        protected override Expression VisitBinary(BinaryExpression node)
        {
            var left = node.Left;
            var right = node.Right;

            if (node.NodeType == ExpressionType.AndAlso || node.NodeType == ExpressionType.OrElse)
            {
                whereGroups.Push(negations.Decrement() > 0
                    ? query.AddWhereNotGroup(ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And)
                    : query.AddWhereGroup(ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And));

                Visit(left);

                if (node.NodeType == ExpressionType.OrElse)
                    ors.Increment();

                Visit(right);

                whereGroups.Pop();

                return node;
            }

            left = Visit(left);
            right = Visit(right);

            var fields = query.GetFields(left, right);

            var group = whereGroups.Count > 0
                ? whereGroups.Peek()
                : query.GetBaseWhereGroup();

            var where = negations.Decrement() > 0
                ? group.AddWhereNot(fields.Key, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And)
                : group.AddWhere(fields.Key, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And);

            // TODO: Implement additional comparison operations as necessary.
            // Translates the binary expression into the corresponding SQL predicate.
            switch (node.NodeType)
            {
                case ExpressionType.Equal:
                    where.EqualTo(fields.Value);
                    break;
                case ExpressionType.NotEqual:
                    where.NotEqualTo(fields.Value);
                    break;
                case ExpressionType.GreaterThan:
                    where.GreaterThan(fields.Value);
                    break;
                case ExpressionType.GreaterThanOrEqual:
                    where.GreaterThanOrEqual(fields.Value);
                    break;
                case ExpressionType.LessThan:
                    where.LessThan(fields.Value);
                    break;
                case ExpressionType.LessThanOrEqual:
                    where.LessThanOrEqual(fields.Value);
                    break;
                default:
                    throw new NotImplementedException("Operation not implemented");
            }

            return node;
        }
    }
}
