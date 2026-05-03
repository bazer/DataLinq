using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DataLinq.Exceptions;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;

namespace DataLinq.Linq;

internal static class LocalSequenceExtractor
{
    internal static object?[] Evaluate(Expression expression)
    {
        if (TryEvaluate(expression, out var values))
            return values;

        throw new InvalidQueryException($"Expression '{expression}' cannot be evaluated as a local sequence.");
    }

    internal static object?[] Evaluate(QueryModel queryModel)
    {
        if (TryEvaluate(queryModel, out var values))
            return values;

        throw new InvalidQueryException($"Query model '{queryModel}' cannot be evaluated as a local sequence.");
    }

    internal static bool TryEvaluate(Expression expression, out object?[] values)
    {
        values = [];
        expression = NormalizeSequenceExpression(expression);

        if (ContainsForbiddenQueryReference(expression, allowedQuerySource: null))
            return false;

        object? value;
        try
        {
            value = EvaluateLocalExpression(expression);
        }
        catch
        {
            return false;
        }

        return TryConvertToArray(value, out values);
    }

    internal static bool TryEvaluate(QueryModel queryModel, out object?[] values)
    {
        values = [];

        if (queryModel.BodyClauses.Count != 0 ||
            queryModel.ResultOperators.Any(resultOperator => resultOperator is not ContainsResultOperator and not AnyResultOperator))
        {
            return false;
        }

        if (!TryEvaluate(queryModel.MainFromClause.FromExpression, out var sourceValues))
            return false;

        var selector = queryModel.SelectClause.Selector;
        if (selector is QuerySourceReferenceExpression querySourceReference &&
            querySourceReference.ReferencedQuerySource == queryModel.MainFromClause)
        {
            values = sourceValues;
            return true;
        }

        if (ContainsForbiddenQueryReference(selector, queryModel.MainFromClause))
            return false;

        try
        {
            var projector = CompileProjection(queryModel.MainFromClause, selector);
            values = sourceValues.Select(projector).ToArray();
            return true;
        }
        catch
        {
            values = [];
            return false;
        }
    }

    internal static bool TryProject(
        MainFromClause mainFromClause,
        Expression selector,
        object?[] sourceValues,
        out object?[] values)
    {
        values = [];
        selector = UnwrapQueryColumnAccess(selector);

        if (selector is QuerySourceReferenceExpression querySourceReference &&
            querySourceReference.ReferencedQuerySource == mainFromClause)
        {
            values = sourceValues;
            return true;
        }

        if (ContainsForbiddenQueryReference(selector, mainFromClause))
            return false;

        try
        {
            var projector = CompileProjection(mainFromClause, selector);
            values = sourceValues.Select(projector).ToArray();
            return true;
        }
        catch
        {
            values = [];
            return false;
        }
    }

    internal static Expression UnwrapQueryColumnAccess(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            expression = unary.Operand;
        }

        if (expression is MemberExpression { Member.Name: "Value", Expression: not null } memberExpression &&
            Nullable.GetUnderlyingType(memberExpression.Expression.Type) != null)
        {
            expression = memberExpression.Expression;
        }

        while (expression is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private static object? EvaluateLocalExpression(Expression expression)
    {
        if (expression is ConstantExpression constantExpression)
            return constantExpression.Value;

        var evaluatedExpression = Evaluator.PartialEval(
            expression,
            candidate => candidate is not QuerySourceReferenceExpression and not SubQueryExpression);

        if (evaluatedExpression is ConstantExpression constantAfterEval)
            return constantAfterEval.Value;

        return Expression.Lambda(evaluatedExpression!).Compile().DynamicInvoke();
    }

    private static Func<object?, object?> CompileProjection(MainFromClause mainFromClause, Expression selector)
    {
        var parameter = Expression.Parameter(mainFromClause.ItemType, mainFromClause.ItemName);
        var body = new QuerySourceReplacementVisitor(mainFromClause, parameter).Visit(selector)!;
        var lambda = Expression.Lambda(body, parameter).Compile();

        return value => lambda.DynamicInvoke(value);
    }

    private static Expression NormalizeSequenceExpression(Expression expression)
    {
        // ReadOnlySpan<T> cannot be boxed or returned from DynamicInvoke. The current translator only needs
        // the backing local array for the tested implicit array-to-span shape.
        if (expression.Type.IsByRefLike &&
            expression is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } implicitCall)
        {
            return implicitCall.Arguments[0];
        }

        return expression;
    }

    private static bool TryConvertToArray(object? value, out object?[] values)
    {
        values = value switch
        {
            null => [],
            object?[] array => array,
            IEnumerable<object?> enumerable => enumerable.ToArray(),
            IEnumerable enumerable => enumerable.Cast<object?>().ToArray(),
            _ => []
        };

        return value == null || values.Length != 0 || value is IEnumerable;
    }

    private static bool ContainsForbiddenQueryReference(Expression expression, IQuerySource? allowedQuerySource)
    {
        var visitor = new QueryReferenceVisitor(allowedQuerySource);
        visitor.Visit(expression);
        return visitor.ContainsForbiddenReference;
    }

    private sealed class QueryReferenceVisitor(IQuerySource? allowedQuerySource) : ExpressionVisitor
    {
        internal bool ContainsForbiddenReference { get; private set; }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is QuerySourceReferenceExpression querySourceReference)
            {
                if (querySourceReference.ReferencedQuerySource != allowedQuerySource)
                    ContainsForbiddenReference = true;

                return node;
            }

            if (node is SubQueryExpression)
            {
                ContainsForbiddenReference = true;
                return node;
            }

            return node.CanReduce ? Visit(node.Reduce()) : node;
        }
    }

    private sealed class QuerySourceReplacementVisitor(IQuerySource querySource, ParameterExpression parameter) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node.Name == querySource.ItemName && node.Type == querySource.ItemType)
                return parameter;

            return base.VisitParameter(node);
        }

        protected override Expression VisitExtension(Expression node)
        {
            if (node is QuerySourceReferenceExpression querySourceReference &&
                querySourceReference.ReferencedQuerySource == querySource)
            {
                return parameter;
            }

            return node.CanReduce ? Visit(node.Reduce()) : node;
        }
    }
}
