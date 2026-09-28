using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Instances;

public partial interface IImmutableRelation<T> where T : IModelInstance
{
    /// <summary>Creates a deferred asynchronous row view. A synchronous-only implementation fails explicitly.</summary>
    /// <remarks>The view may buffer. Method and enumerator cancellation tokens both apply.</remarks>
    IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This relation does not provide asynchronous execution.");

    /// <summary>Materializes a complete relation snapshot asynchronously.</summary>
    ValueTask<ImmutableArray<T>> ValuesAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ValuesAsync(this, cancellationToken);

    /// <summary>Returns the keys belonging to this relation.</summary>
    ValueTask<ImmutableArray<DataLinqKey>> KeysAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.KeysAsync(this, cancellationToken);

    /// <summary>Materializes relation rows keyed by primary key; duplicate keys fail.</summary>
    ValueTask<FrozenDictionary<DataLinqKey, T>> ToFrozenDictionaryAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToFrozenDictionaryAsync(this, cancellationToken);

    /// <summary>Returns the row only if its key belongs to this relation.</summary>
    ValueTask<T?> GetAsync(DataLinqKey key, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.GetAsync(this, key, cancellationToken);

    /// <summary>Tests key membership in this relation.</summary>
    ValueTask<bool> ContainsKeyAsync(DataLinqKey key, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ContainsKeyAsync(this, key, cancellationToken);

    /// <summary>Materializes the relation rows as a list.</summary>
    ValueTask<List<T>> ToListAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToListAsync(this, cancellationToken);

    /// <summary>Materializes the relation rows as an array.</summary>
    ValueTask<T[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToArrayAsync(this, cancellationToken);

    /// <summary>Executes the local First terminal over asynchronous relation rows.</summary>
    ValueTask<T> FirstAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstAsync(this, cancellationToken);

    /// <summary>Executes the local First terminal with a synchronous predicate.</summary>
    ValueTask<T> FirstAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstAsync(this, predicate, cancellationToken);

    /// <summary>Executes the local FirstOrDefault terminal over asynchronous relation rows.</summary>
    ValueTask<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstOrDefaultAsync(this, cancellationToken);

    /// <summary>Executes the local FirstOrDefault terminal with a synchronous predicate.</summary>
    ValueTask<T?> FirstOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstOrDefaultAsync(this, predicate, cancellationToken);

    /// <summary>Executes the local Single terminal over asynchronous relation rows.</summary>
    ValueTask<T> SingleAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleAsync(this, cancellationToken);

    /// <summary>Executes the local Single terminal with a synchronous predicate.</summary>
    ValueTask<T> SingleAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleAsync(this, predicate, cancellationToken);

    /// <summary>Executes the local SingleOrDefault terminal over asynchronous relation rows.</summary>
    ValueTask<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleOrDefaultAsync(this, cancellationToken);

    /// <summary>Executes the local SingleOrDefault terminal with a synchronous predicate.</summary>
    ValueTask<T?> SingleOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleOrDefaultAsync(this, predicate, cancellationToken);

    /// <summary>Executes the local Last terminal over asynchronous relation rows.</summary>
    ValueTask<T> LastAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastAsync(this, cancellationToken);

    /// <summary>Executes the local Last terminal with a synchronous predicate.</summary>
    ValueTask<T> LastAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastAsync(this, predicate, cancellationToken);

    /// <summary>Executes the local LastOrDefault terminal over asynchronous relation rows.</summary>
    ValueTask<T?> LastOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastOrDefaultAsync(this, cancellationToken);

    /// <summary>Executes the local LastOrDefault terminal with a synchronous predicate.</summary>
    ValueTask<T?> LastOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastOrDefaultAsync(this, predicate, cancellationToken);

    /// <summary>Executes the local Any terminal over asynchronous relation rows.</summary>
    ValueTask<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AnyAsync(this, cancellationToken);

    /// <summary>Executes the local Any terminal with a synchronous predicate.</summary>
    ValueTask<bool> AnyAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AnyAsync(this, predicate, cancellationToken);

    /// <summary>Executes the local Count terminal over asynchronous relation rows.</summary>
    ValueTask<int> CountAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.CountAsync(this, cancellationToken);

    /// <summary>Executes the local Count terminal with a synchronous predicate.</summary>
    ValueTask<int> CountAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.CountAsync(this, predicate, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<int> SumAsync(Func<T, int> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<int?> SumAsync(Func<T, int?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<long> SumAsync(Func<T, long> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<long?> SumAsync(Func<T, long?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<float> SumAsync(Func<T, float> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<float?> SumAsync(Func<T, float?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<double> SumAsync(Func<T, double> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<double?> SumAsync(Func<T, double?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<decimal> SumAsync(Func<T, decimal> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local sum of the selected model values.</summary>
    ValueTask<decimal?> SumAsync(Func<T, decimal?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<double> AverageAsync(Func<T, int> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<double?> AverageAsync(Func<T, int?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<double> AverageAsync(Func<T, long> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<double?> AverageAsync(Func<T, long?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<float> AverageAsync(Func<T, float> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<float?> AverageAsync(Func<T, float?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<double> AverageAsync(Func<T, double> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<double?> AverageAsync(Func<T, double?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<decimal> AverageAsync(Func<T, decimal> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local average of the selected model values.</summary>
    ValueTask<decimal?> AverageAsync(Func<T, decimal?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <summary>Computes the local min of selected model values.</summary>
    ValueTask<TResult?> MinAsync<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.MinAsync(this, selector, cancellationToken);

    /// <summary>Computes the local max of selected model values.</summary>
    ValueTask<TResult?> MaxAsync<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.MaxAsync(this, selector, cancellationToken);

}
