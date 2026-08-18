using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using DataLinq.Exceptions;
using DataLinq.Interfaces;

namespace DataLinq.Linq.Planning.Expressions;

internal sealed class PreparedQueryBindingExpression
{
    private readonly string bindingId;
    private readonly QueryPlanBindingKind kind;
    private readonly Expression expression;
    private readonly ParameterExpression argumentParameter;
    private readonly ParameterExpression? sequenceElementParameter;
    private readonly Expression? sequenceElementSelector;

    private PreparedQueryBindingExpression(
        string bindingId,
        QueryPlanBindingKind kind,
        Expression expression,
        ParameterExpression argumentParameter,
        ParameterExpression? sequenceElementParameter = null,
        Expression? sequenceElementSelector = null)
    {
        this.bindingId = bindingId;
        this.kind = kind;
        this.expression = expression;
        this.argumentParameter = argumentParameter;
        this.sequenceElementParameter = sequenceElementParameter;
        this.sequenceElementSelector = sequenceElementSelector;

        RetainedValueValidator.Validate(expression, argumentParameter, sequenceElementParameter);
        if (sequenceElementSelector is not null)
            RetainedValueValidator.Validate(sequenceElementSelector, argumentParameter, sequenceElementParameter);
    }

    public static PreparedQueryBindingExpression Scalar(
        string bindingId,
        Expression expression,
        ParameterExpression argumentParameter)
        => new(bindingId, QueryPlanBindingKind.Scalar, expression, argumentParameter);

    public static PreparedQueryBindingExpression LocalSequence(
        string bindingId,
        Expression expression,
        ParameterExpression argumentParameter,
        ParameterExpression? sequenceElementParameter = null,
        Expression? sequenceElementSelector = null)
        => new(
            bindingId,
            QueryPlanBindingKind.LocalSequence,
            expression,
            argumentParameter,
            sequenceElementParameter,
            sequenceElementSelector);

    public QueryPlanInvocationValue Evaluate(object? argument)
    {
        return kind switch
        {
            QueryPlanBindingKind.Scalar => new QueryPlanInvocationValue.Scalar(
                bindingId,
                EvaluateScalar(argument)),
            QueryPlanBindingKind.LocalSequence => new QueryPlanInvocationValue.LocalSequence(
                bindingId,
                Array.AsReadOnly(EvaluateSequence(argument))),
            _ => throw new InvalidOperationException($"Prepared-query binding '{bindingId}' has unsupported kind '{kind}'.")
        };
    }

    private object? EvaluateScalar(object? argument)
    {
        var value = ExpressionLocalValueEvaluator.Evaluate(expression, argumentParameter, argument);
        return value is Array array ? array.Clone() : value;
    }

    private object?[] EvaluateSequence(object? argument)
    {
        var values = EvaluateLocalSequence(expression, argumentParameter, argument);
        if (sequenceElementSelector is null)
            return values;

        return values
            .Select(value => ExpressionLocalValueEvaluator.Evaluate(
                sequenceElementSelector,
                sequenceElementParameter,
                value))
            .ToArray();
    }

    private static object?[] EvaluateLocalSequence(
        Expression expression,
        ParameterExpression argumentParameter,
        object? argument)
    {
        expression = Unwrap(expression);
        if (expression.Type.IsByRefLike &&
            expression is MethodCallExpression { Method.Name: "op_Implicit", Arguments.Count: 1 } implicitCall)
        {
            expression = implicitCall.Arguments[0];
        }

        if (expression is MethodCallExpression methodCall &&
            methodCall.Method.IsGenericMethod &&
            methodCall.Method.Name == nameof(Enumerable.Select) &&
            methodCall.Method.GetGenericMethodDefinition().DeclaringType == typeof(Enumerable) &&
            methodCall.Arguments.Count == 2)
        {
            var sourceValues = EvaluateLocalSequence(methodCall.Arguments[0], argumentParameter, argument);
            var selector = Unwrap(methodCall.Arguments[1]) as LambdaExpression
                ?? throw new QueryTranslationException($"Prepared-query local sequence selector '{methodCall.Arguments[1]}' is not a lambda.");
            if (selector.Parameters.Count != 1)
                throw new QueryTranslationException("Prepared-query local sequence selectors require exactly one parameter.");

            return sourceValues
                .Select(value => ExpressionLocalValueEvaluator.Evaluate(
                    selector.Body,
                    selector.Parameters[0],
                    value))
                .ToArray();
        }

        var evaluated = ExpressionLocalValueEvaluator.Evaluate(
            expression,
            argumentParameter,
            argument);
        if (evaluated is IQueryable)
        {
            throw new QueryTranslationException(
                $"IQueryable expression '{expression}' cannot be evaluated as a prepared-query local sequence.");
        }

        return evaluated switch
        {
            null => [],
            object?[] array => array.Select(CopyElement).ToArray(),
            IEnumerable<object?> generic => generic.Select(CopyElement).ToArray(),
            IEnumerable enumerable => enumerable.Cast<object?>().Select(CopyElement).ToArray(),
            _ => throw new QueryTranslationException(
                $"Prepared-query expression '{expression}' did not produce a local sequence.")
        };
    }

    private static object? CopyElement(object? value)
        => value is Array array ? array.Clone() : value;

    private static Expression Unwrap(Expression expression)
    {
        while (expression is UnaryExpression unary &&
               unary.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    private sealed class RetainedValueValidator(
        ParameterExpression argumentParameter,
        ParameterExpression? sequenceElementParameter) : ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> lambdaParameters = [];

        public static void Validate(
            Expression expression,
            ParameterExpression argumentParameter,
            ParameterExpression? sequenceElementParameter)
        {
            new RetainedValueValidator(argumentParameter, sequenceElementParameter).Visit(expression);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is null || node.Type.IsValueType || node.Value is string)
                return node;

            var type = node.Value.GetType();
            if (node.Value is IQueryable or IDataLinqReadSource ||
                type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) ||
                type.Name.Contains("DisplayClass", StringComparison.Ordinal) ||
                type.Name.Contains("AnonStorey", StringComparison.Ordinal))
            {
                throw new QueryTranslationException(
                    "Prepared queries cannot capture closure instances, query roots, databases, or transactions in invocation bindings. " +
                    $"Pass the changing value through the prepared-query argument instead. Binding expression: {node}");
            }

            throw new QueryTranslationException(
                $"Prepared-query binding expression '{node}' retains reference value type '{type.FullName}'. " +
                "Pass that value through the prepared-query argument instead.");
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node != argumentParameter &&
                node != sequenceElementParameter &&
                !lambdaParameters.Contains(node))
            {
                throw new QueryTranslationException(
                    $"Prepared-query binding expression contains unexpected parameter '{node.Name}'.");
            }

            return node;
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            foreach (var parameter in node.Parameters)
                lambdaParameters.Add(parameter);

            Visit(node.Body);

            foreach (var parameter in node.Parameters)
                lambdaParameters.Remove(parameter);

            return node;
        }
    }
}
