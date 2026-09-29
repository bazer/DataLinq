using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Linq.Planning.Expressions;

namespace DataLinq.Linq;

/// <summary>Asynchronous execution of supported DataLinq queries.</summary>
/// <remarks>
/// Import DataLinq.Linq deliberately; use this class's static methods or an alias when other
/// query libraries expose the same names. Predicates and selectors are provider expressions,
/// not local delegates. Async execution preserves each backend's existing translation limits
/// and does not fall back to synchronous enumeration. SQLite may perform synchronous driver
/// work. Await each ValueTask once.
/// </remarks>
public static class DataLinqAsyncQueryableExtensions
{
    /// <summary>Creates a deferred asynchronous row view of a DataLinq query.</summary>
    /// <remarks>
    /// Values are captured for each GetAsyncEnumerator call. Sequence and enumerator construction
    /// perform no database I/O; execution starts on the first move and may buffer. Method and
    /// enumerator tokens both apply, including during buffered iteration. Dispose each enumerator
    /// to release its resources; it does not complete a caller-owned transaction.
    /// </remarks>
    public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        RequireProvider(source).ExecuteEnumerableAsyncCore<T>(source.Expression, cancellationToken);

    /// <summary>Materializes a query as a list and closes its owned reader before returning.</summary>
    /// <remarks>Captures parameters before the first suspension; no partial list is returned on failure.</remarks>
    public static ValueTask<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        new(RequireProvider(source).ExecuteListAsyncCore<T>(source.Expression, cancellationToken));

    /// <summary>Materializes a query as an array and closes its owned reader before returning.</summary>
    /// <remarks>Captures parameters before the first suspension; no partial array is returned on failure.</remarks>
    public static ValueTask<T[]> ToArrayAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        ToArrayCore(source.ToListAsync(cancellationToken));

    private static async ValueTask<T[]> ToArrayCore<T>(ValueTask<List<T>> pending) =>
        (await pending.ConfigureAwait(false)).ToArray();

    /// <summary>Returns the first row; throws when the query is empty.</summary>
    public static ValueTask<T> FirstAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.First(query), cancellationToken);

    /// <summary>Returns the first row; throws when the query is empty. Applies a provider-translated predicate.</summary>
    public static ValueTask<T> FirstAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.First(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Returns the first row, or the element type's default when empty.</summary>
    public static ValueTask<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.FirstOrDefault(query), cancellationToken);

    /// <summary>Returns the first row, or the element type's default when empty. Applies a provider-translated predicate.</summary>
    public static ValueTask<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.FirstOrDefault(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Returns the only row; throws for empty or multiple results.</summary>
    public static ValueTask<T> SingleAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Single(query), cancellationToken);

    /// <summary>Returns the only row; throws for empty or multiple results. Applies a provider-translated predicate.</summary>
    public static ValueTask<T> SingleAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Single(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Returns the only row or its default when empty; throws for multiple results.</summary>
    public static ValueTask<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.SingleOrDefault(query), cancellationToken);

    /// <summary>Returns the only row or its default when empty; throws for multiple results. Applies a provider-translated predicate.</summary>
    public static ValueTask<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.SingleOrDefault(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Returns the last row; throws when the query is empty.</summary>
    public static ValueTask<T> LastAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Last(query), cancellationToken);

    /// <summary>Returns the last row; throws when the query is empty. Applies a provider-translated predicate.</summary>
    public static ValueTask<T> LastAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Last(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Returns the last row, or the element type's default when empty.</summary>
    public static ValueTask<T?> LastOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.LastOrDefault(query), cancellationToken);

    /// <summary>Returns the last row, or the element type's default when empty. Applies a provider-translated predicate.</summary>
    public static ValueTask<T?> LastOrDefaultAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.LastOrDefault(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Tests whether the query has any rows.</summary>
    public static ValueTask<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Any(query), cancellationToken);

    /// <summary>Tests whether the query has any rows. Applies a provider-translated predicate.</summary>
    public static ValueTask<bool> AnyAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Any(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Counts the rows in the query.</summary>
    public static ValueTask<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Count(query), cancellationToken);

    /// <summary>Counts the rows in the query. Applies a provider-translated predicate.</summary>
    public static ValueTask<int> CountAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Count(query, default(Expression<Func<T, bool>>)!),
            predicate, cancellationToken);

    /// <summary>Computes the sum of a provider-translated int selector.</summary>
    public static ValueTask<int> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, int>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated int? selector.</summary>
    public static ValueTask<int?> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, int?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, int?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated long selector.</summary>
    public static ValueTask<long> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, long>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, long>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated long? selector.</summary>
    public static ValueTask<long?> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, long?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, long?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated float selector.</summary>
    public static ValueTask<float> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, float>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, float>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated float? selector.</summary>
    public static ValueTask<float?> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, float?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, float?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated double selector.</summary>
    public static ValueTask<double> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, double>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, double>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated double? selector.</summary>
    public static ValueTask<double?> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, double?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, double?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated decimal selector.</summary>
    public static ValueTask<decimal> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, decimal>>)!),
            selector, cancellationToken);

    /// <summary>Computes the sum of a provider-translated decimal? selector.</summary>
    public static ValueTask<decimal?> SumAsync<T>(this IQueryable<T> source, Expression<Func<T, decimal?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Sum(query, default(Expression<Func<T, decimal?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated int selector.</summary>
    public static ValueTask<double> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, int>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, int>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated int? selector.</summary>
    public static ValueTask<double?> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, int?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, int?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated long selector.</summary>
    public static ValueTask<double> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, long>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, long>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated long? selector.</summary>
    public static ValueTask<double?> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, long?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, long?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated float selector.</summary>
    public static ValueTask<float> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, float>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, float>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated float? selector.</summary>
    public static ValueTask<float?> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, float?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, float?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated double selector.</summary>
    public static ValueTask<double> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, double>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, double>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated double? selector.</summary>
    public static ValueTask<double?> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, double?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, double?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated decimal selector.</summary>
    public static ValueTask<decimal> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, decimal>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, decimal>>)!),
            selector, cancellationToken);

    /// <summary>Computes the average of a provider-translated decimal? selector.</summary>
    public static ValueTask<decimal?> AverageAsync<T>(this IQueryable<T> source, Expression<Func<T, decimal?>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Average(query, default(Expression<Func<T, decimal?>>)!),
            selector, cancellationToken);

    /// <summary>Computes the minimum of a supported provider-translated selector.</summary>
    /// <remarks>Preserves LINQ result nullability and the backend's empty-result and selector rules.</remarks>
    public static ValueTask<TResult?> MinAsync<TSource, TResult>(this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Min(query, default(Expression<Func<TSource, TResult>>)!),
            selector, cancellationToken);

    /// <summary>Computes the maximum of a supported provider-translated selector.</summary>
    /// <remarks>Preserves LINQ result nullability and the backend's empty-result and selector rules.</remarks>
    public static ValueTask<TResult?> MaxAsync<TSource, TResult>(this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, CancellationToken cancellationToken = default) =>
        Execute(source, query => System.Linq.Queryable.Max(query, default(Expression<Func<TSource, TResult>>)!),
            selector, cancellationToken);

    private static ExpressionQueryPlanProvider RequireProvider<T>(IQueryable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Provider as ExpressionQueryPlanProvider
            ?? throw new NotSupportedException("Asynchronous query execution requires a DataLinq query provider.");
    }

    private static ValueTask<TResult> Execute<T, TResult>(
        IQueryable<T> source, Expression<Func<IQueryable<T>, TResult>> operation, CancellationToken token)
    {
        var provider = RequireProvider(source);
        var method = ((MethodCallExpression)operation.Body).Method;
        return new(provider.ExecuteAsyncCore<TResult>(Expression.Call(method, source.Expression), token));
    }

    private static ValueTask<TResult> Execute<T, TResult>(
        IQueryable<T> source, Expression<Func<IQueryable<T>, TResult>> operation,
        LambdaExpression predicateOrSelector, CancellationToken token,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(predicateOrSelector))] string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicateOrSelector, parameterName);
        var provider = RequireProvider(source);
        var method = ((MethodCallExpression)operation.Body).Method;
        return new(provider.ExecuteAsyncCore<TResult>(
            Expression.Call(method, source.Expression, Expression.Quote(predicateOrSelector)), token));
    }
}
