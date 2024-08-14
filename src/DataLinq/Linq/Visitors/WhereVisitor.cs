using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Xml.Linq;
using DataLinq.Query;
using DataLinq.Utils;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq.Visitors;

/// <summary>
/// The WhereVisitor class is responsible for traversing an expression tree 
/// that represents a LINQ Where clause and converting it into the corresponding 
/// SQL query predicates.
/// </summary>
internal class WhereVisitor<T> : ExpressionVisitor
{
    protected SqlQuery<T> query;
    // Tracks the number of negations (NOT operations) encountered.
    NonNegativeInt negations = new NonNegativeInt(0);
    // Tracks the number of OR operations encountered.
    NonNegativeInt ors = new NonNegativeInt(0);
    // A stack to manage groups of WHERE clauses.
    Stack<WhereGroup<T>> whereGroups = new Stack<WhereGroup<T>>();

    /// <summary>
    /// Initializes a new instance of the WhereVisitor with a given SQL query.
    /// </summary>
    /// <param name="query">The SQL query to be built upon.</param>
    internal WhereVisitor(SqlQuery<T> query)
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

    protected override Expression VisitConstant(ConstantExpression node)
    {
        return base.VisitConstant(node);
    }

    protected override Expression VisitConditional(ConditionalExpression node)
    {
        return base.VisitConditional(node);
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

        if (node is Remotion.Linq.Clauses.Expressions.SubQueryExpression subQuery)
        {
            foreach (var resultOperator in subQuery.QueryModel.ResultOperators)
            {
                if (resultOperator is ContainsResultOperator containsResultOperator)
                {
                    var field = (string)query.GetValue(Visit(containsResultOperator.Item));
                    var array = ConvertToList(query.GetValue(Visit(subQuery.QueryModel.MainFromClause.FromExpression)));

                    AddWhere(Operation.Contains, field, array);
                }
                else
                    throw new NotImplementedException($"Operation '{resultOperator}' not implemented");
            }

            return node;
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

        AddWhere(GetOperation(node.Method.Name), field, [value]);

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

        if (node.NodeType == ExpressionType.NotEqual && fields.Value is bool bValue && bValue == true)
        {
            whereGroups.Push(query.AddWhereGroup(BooleanType.And));
            AddWhere(Operation.EqualNull, fields.Key);
            ors.Increment();
            AddWhere(GetOperation(node.NodeType), fields.Key, fields.Value);
            whereGroups.Pop();
        }
        else       
            AddWhere(GetOperation(node.NodeType), fields.Key, fields.Value);

        //var group = whereGroups.Count > 0
        //    ? whereGroups.Peek()
        //    : query.GetBaseWhereGroup();

        //var where = negations.Decrement() > 0
        //    ? group.AddWhereNot(fields.Key, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And)
        //    : group.AddWhere(fields.Key, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And);

        //List<(ExpressionType type, object value)> nodeTypes = [(node.NodeType, fields.Value)];

        //if (node.NodeType == ExpressionType.NotEqual && fields.Value is bool bValue && bValue == true)
        //    nodeTypes.Add((ExpressionType.Equal, );

        //foreach (var (nodeType, value) in nodeTypes)
        //{
        //    switch (nodeType)
        //    {
        //        case ExpressionType.Equal:
        //            where.EqualTo(fields.Value);
        //            break;
        //        case ExpressionType.NotEqual:
        //            where.NotEqualTo(fields.Value);
        //            break;
        //        case ExpressionType.GreaterThan:
        //            where.GreaterThan(fields.Value);
        //            break;
        //        case ExpressionType.GreaterThanOrEqual:
        //            where.GreaterThanOrEqual(fields.Value);
        //            break;
        //        case ExpressionType.LessThan:
        //            where.LessThan(fields.Value);
        //            break;
        //        case ExpressionType.LessThanOrEqual:
        //            where.LessThanOrEqual(fields.Value);
        //            break;
        //        default:
        //            throw new NotImplementedException("Operation not implemented");
        //    }
        //}

        return node;
    }

    private void AddWhere(Operation operation, string field, params object[] values)
    {
        var group = whereGroups.Count > 0
                    ? whereGroups.Peek()
                    : query.GetBaseWhereGroup();

        var where = negations.Decrement() > 0
            ? group.AddWhereNot(field, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And)
            : group.AddWhere(field, null, ors.Decrement() > 0 ? BooleanType.Or : BooleanType.And);

        switch (operation)
        {
            case Operation.Equal:
                where.EqualTo(values[0]);
                break;
            case Operation.EqualNull:
                where.EqualToNull();
                break;
            case Operation.NotEqual:
                where.NotEqualTo(values[0]);
                break;
            case Operation.NotEqualNull:
                where.NotEqualToNull();
                break;
            case Operation.GreaterThan:
                where.GreaterThan(values[0]);
                break;
            case Operation.GreaterThanOrEqual:
                where.GreaterThanOrEqual(values[0]);
                break;
            case Operation.LessThan:
                where.LessThan(values[0]);
                break;
            case Operation.LessThanOrEqual:
                where.LessThanOrEqual(values[0]);
                break;
            case Operation.StartsWith:
                where.Like(values[0] + "%");
                break;
            case Operation.EndsWith:
                where.Like("%" + values[0]);
                break;
            case Operation.Contains:
                where.In(values);
                break;
            default: throw new NotImplementedException($"Operation '{operation}' not implemented");
        }
    }

    private static Operation GetOperation(ExpressionType expressionType) => expressionType switch
    {
        ExpressionType.Equal => Operation.Equal,
        ExpressionType.NotEqual => Operation.NotEqual,
        ExpressionType.GreaterThan => Operation.GreaterThan,
        ExpressionType.GreaterThanOrEqual => Operation.GreaterThanOrEqual,
        ExpressionType.LessThan => Operation.LessThan,
        ExpressionType.LessThanOrEqual => Operation.LessThanOrEqual,
        _ => throw new NotImplementedException($"Operation '{expressionType}' not implemented"),
    };

    private static Operation GetOperation(string operationName) => operationName switch
    {
        "Contains" => Operation.Contains,
        "StartsWith" => Operation.StartsWith,
        "EndsWith" => Operation.EndsWith,
        _ => throw new NotImplementedException($"Operation '{operationName}' not implemented"),
    };

    private enum Operation
    {
        Equal,
        EqualNull,
        NotEqual,
        NotEqualNull,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
        StartsWith,
        EndsWith,
        Contains
    }

    private static object[] ConvertToList(object obj)
    {
        return obj switch
        {
            object[] arr => arr,
            IEnumerable<object> enumerable => enumerable.ToArray(),
            IEnumerable enumerable => enumerable.Cast<object>().ToArray(),
            _ => throw new ArgumentException("Object is not a list"),
        };
    }
}
