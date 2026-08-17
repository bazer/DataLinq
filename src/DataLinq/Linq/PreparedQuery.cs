using System;
using System.Collections.Generic;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;

namespace DataLinq.Linq;

/// <summary>
/// An immutable structural query plan that binds fresh argument values on every execution.
/// </summary>
/// <typeparam name="TDatabase">The database model type.</typeparam>
/// <typeparam name="TArgument">The invocation argument type.</typeparam>
/// <typeparam name="TResult">The query result type.</typeparam>
/// <remarks>
/// A prepared query is thread-safe. Each execution snapshots mutable scalar arrays and local
/// sequences before validating the plan against the selected execution source and backend.
/// </remarks>
public sealed class PreparedQuery<TDatabase, TArgument, TResult>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private readonly QueryPlanTemplate template;
    private readonly IReadOnlyList<PreparedQueryBindingExpression> bindings;

    internal PreparedQuery(ExpressionQueryPlanParseResult preparation)
    {
        template = preparation.Template;
        bindings = preparation.PreparedBindings;

        if (template.Result.Kind == QueryPlanResultKind.Sequence)
        {
            throw new QueryTranslationException(
                "PrepareQuery requires a terminal or scalar result. Use PrepareSequenceQuery for an IQueryable<T> result shape.");
        }

        PreparedQueryExecution.ValidateBindings(template, bindings);
    }

    /// <summary>
    /// Executes the prepared query against a database, read-only access, or transaction.
    /// </summary>
    /// <param name="source">The execution source whose metadata and backend are validated.</param>
    /// <param name="argument">The current invocation argument.</param>
    /// <param name="cancellationToken">A token checked before backend execution.</param>
    /// <returns>The query result.</returns>
    public TResult Execute(
        IDataSourceAccess<TDatabase> source,
        TArgument argument,
        CancellationToken cancellationToken = default)
    {
        var request = PreparedQueryExecution.CreateRequest(
            template,
            bindings,
            source,
            argument,
            cancellationToken);
        return ExpressionQueryPlanExecutor.Execute<TResult>(request);
    }
}

/// <summary>
/// An immutable prepared structural query plan that streams a sequence of entities or projections.
/// </summary>
/// <typeparam name="TDatabase">The database model type.</typeparam>
/// <typeparam name="TArgument">The invocation argument type.</typeparam>
/// <typeparam name="TElement">The sequence element type.</typeparam>
public sealed class PreparedSequenceQuery<TDatabase, TArgument, TElement>
    where TDatabase : class, IDatabaseModel<TDatabase>
{
    private readonly QueryPlanTemplate template;
    private readonly IReadOnlyList<PreparedQueryBindingExpression> bindings;

    internal PreparedSequenceQuery(ExpressionQueryPlanParseResult preparation)
    {
        template = preparation.Template;
        bindings = preparation.PreparedBindings;

        if (template.Result.Kind != QueryPlanResultKind.Sequence)
        {
            throw new QueryTranslationException(
                "PrepareSequenceQuery requires an IQueryable<T> sequence result shape.");
        }

        PreparedQueryExecution.ValidateBindings(template, bindings);
    }

    /// <summary>
    /// Executes and streams the prepared query against a compatible execution source.
    /// </summary>
    /// <param name="source">The database, read-only access, or transaction to query.</param>
    /// <param name="argument">The current invocation argument.</param>
    /// <param name="cancellationToken">A token checked before and during backend execution.</param>
    /// <returns>A lazy sequence backed by an invocation-local immutable value snapshot.</returns>
    public IEnumerable<TElement> Execute(
        IDataSourceAccess<TDatabase> source,
        TArgument argument,
        CancellationToken cancellationToken = default)
    {
        var request = PreparedQueryExecution.CreateRequest(
            template,
            bindings,
            source,
            argument,
            cancellationToken);
        return ExpressionQueryPlanExecutor.ExecuteEnumerable<TElement>(request);
    }
}

internal static class PreparedQueryExecution
{
    public static void ValidateBindings(
        QueryPlanTemplate template,
        IReadOnlyList<PreparedQueryBindingExpression> bindings)
    {
        if (bindings.Count != template.BindingDeclarations.Count)
        {
            throw new InvalidOperationException(
                $"Prepared query produced {bindings.Count} argument binders for " +
                $"{template.BindingDeclarations.Count} declarations.");
        }
    }

    public static ValidatedQueryExecutionRequest CreateRequest<TDatabase, TArgument>(
        QueryPlanTemplate template,
        IReadOnlyList<PreparedQueryBindingExpression> bindings,
        IDataSourceAccess<TDatabase> source,
        TArgument argument,
        CancellationToken cancellationToken)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        ArgumentNullException.ThrowIfNull(source);

        var values = new QueryPlanInvocationValue[bindings.Count];
        for (var index = 0; index < bindings.Count; index++)
            values[index] = bindings[index].Evaluate(argument);

        var invocation = QueryPlanInvocation.BindParserOwned(template, values);
        var readSource = source is Database<TDatabase> database
            ? database.Provider.ReadOnlyAccess
            : source;
        return ValidatedQueryExecutionRequest.Prepare(
            new QueryExecutionRequest(
                invocation,
                new QueryExecutionContext(readSource, cancellationToken)));
    }
}
