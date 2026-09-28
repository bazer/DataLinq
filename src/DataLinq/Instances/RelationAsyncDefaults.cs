using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Instances;

// The dependency direction is row view -> values -> keyed snapshot -> keyed access.
// Concrete methods call these helpers, never cast-and-forward to their own interface slot.
internal static class RelationAsyncDefaults
{
    internal static async ValueTask<ImmutableArray<T>> ValuesAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance =>
        ImmutableArray.CreateRange(await relation.AsAsyncEnumerable(cancellationToken).ToArrayAsync(cancellationToken).ConfigureAwait(false));

    internal static async ValueTask<FrozenDictionary<DataLinqKey, T>> ToFrozenDictionaryAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        var values = await relation.ValuesAsync(cancellationToken).ConfigureAwait(false);
        var keyed = new Dictionary<DataLinqKey, T>();
        foreach (var row in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            keyed.Add(row.PrimaryKeys(), row);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return keyed.ToFrozenDictionary();
    }

    internal static async ValueTask<ImmutableArray<DataLinqKey>> KeysAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance =>
        (await relation.ToFrozenDictionaryAsync(cancellationToken).ConfigureAwait(false)).Keys;

    internal static async ValueTask<T?> GetAsync<T>(IImmutableRelation<T> relation, DataLinqKey key, CancellationToken cancellationToken)
        where T : IModelInstance =>
        (await relation.ToFrozenDictionaryAsync(cancellationToken).ConfigureAwait(false)).TryGetValue(key, out var value) ? value : default;

    internal static async ValueTask<bool> ContainsKeyAsync<T>(IImmutableRelation<T> relation, DataLinqKey key, CancellationToken cancellationToken)
        where T : IModelInstance =>
        (await relation.ToFrozenDictionaryAsync(cancellationToken).ConfigureAwait(false)).ContainsKey(key);

    internal static ValueTask<List<T>> ToListAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).ToListAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T[]> ToArrayAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T> FirstAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).FirstAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T> FirstAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).FirstAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<T?> FirstOrDefaultAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).FirstOrDefaultAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T?> FirstOrDefaultAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).FirstOrDefaultAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<T> SingleAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).SingleAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T> SingleAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).SingleAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<T?> SingleOrDefaultAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).SingleOrDefaultAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T?> SingleOrDefaultAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).SingleOrDefaultAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<T> LastAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).LastAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T> LastAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).LastAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<T?> LastOrDefaultAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).LastOrDefaultAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<T?> LastOrDefaultAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).LastOrDefaultAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<bool> AnyAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).AnyAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<bool> AnyAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).AnyAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<int> CountAsync<T>(IImmutableRelation<T> relation, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        return relation.AsAsyncEnumerable(cancellationToken).CountAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<int> CountAsync<T>(IImmutableRelation<T> relation, Func<T, bool> predicate, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return relation.AsAsyncEnumerable(cancellationToken).CountAsync(predicate, cancellationToken: cancellationToken);
    }

    internal static ValueTask<int> SumAsync<T>(IImmutableRelation<T> relation, Func<T, int> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<int?> SumAsync<T>(IImmutableRelation<T> relation, Func<T, int?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<long> SumAsync<T>(IImmutableRelation<T> relation, Func<T, long> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<long?> SumAsync<T>(IImmutableRelation<T> relation, Func<T, long?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<float> SumAsync<T>(IImmutableRelation<T> relation, Func<T, float> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<float?> SumAsync<T>(IImmutableRelation<T> relation, Func<T, float?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double> SumAsync<T>(IImmutableRelation<T> relation, Func<T, double> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double?> SumAsync<T>(IImmutableRelation<T> relation, Func<T, double?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<decimal> SumAsync<T>(IImmutableRelation<T> relation, Func<T, decimal> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<decimal?> SumAsync<T>(IImmutableRelation<T> relation, Func<T, decimal?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).SumAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, int> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double?> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, int?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, long> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double?> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, long?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<float> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, float> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<float?> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, float?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, double> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<double?> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, double?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<decimal> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, decimal> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<decimal?> AverageAsync<T>(IImmutableRelation<T> relation, Func<T, decimal?> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).AverageAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<TResult?> MinAsync<T, TResult>(IImmutableRelation<T> relation, Func<T, TResult> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).MinAsync(cancellationToken: cancellationToken);
    }

    internal static ValueTask<TResult?> MaxAsync<T, TResult>(IImmutableRelation<T> relation, Func<T, TResult> selector, CancellationToken cancellationToken)
        where T : IModelInstance
    {
        ArgumentNullException.ThrowIfNull(selector);
        return relation.AsAsyncEnumerable(cancellationToken).Select(selector).MaxAsync(cancellationToken: cancellationToken);
    }

}

// Capture is I/O-free; the compiler combines method/enumerator tokens and disposes
// any linked token source. Loading begins only on the first MoveNextAsync.
internal sealed class DeferredRelationSnapshot<T>(
    Func<Func<CancellationToken, ValueTask<ImmutableArray<T>>>> capture,
    CancellationToken methodToken) : IAsyncEnumerable<T>
{
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        Enumerate(capture(), methodToken).GetAsyncEnumerator(cancellationToken);

    private static async IAsyncEnumerable<T> Enumerate(
        Func<CancellationToken, ValueTask<ImmutableArray<T>>> load,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var values = await load(cancellationToken).ConfigureAwait(false);
        foreach (var row in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
