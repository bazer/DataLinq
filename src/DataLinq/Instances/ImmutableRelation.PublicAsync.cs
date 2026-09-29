using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;

namespace DataLinq.Instances;

public partial class ImmutableRelation<T, TKey> where T : IImmutableInstance where TKey : notnull
{
    /// <summary>Creates an async row view, capturing its source and key at enumerator construction.</summary>
    /// <remarks>Execution may load the complete relation on the first move; every move observes cancellation.</remarks>
    public virtual IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
        new AsyncReaderEnumerable<T>(() =>
        {
            var source = ResolveDataSource(validateRead: false);
            var key = ProviderKeyComponents.ToDataLinqKey(foreignKey);
            var read = new RelationSnapshotRead(this, source, key);
            return new(read, null, source as Transaction, Continuation: read,
                Identity: ReadExecutionIdentity.Capture(source, ExecutionOperationKind.RelationLoad));
        }, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<T>> ValuesAsync(CancellationToken cancellationToken = default) =>
        new(GetValuesAsyncCore(cancellationToken));

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<DataLinqKey>> KeysAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.KeysAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<FrozenDictionary<DataLinqKey, T>> ToFrozenDictionaryAsync(CancellationToken cancellationToken = default) =>
        new(GetInstancesAsyncCore(cancellationToken));

    /// <inheritdoc />
    public virtual ValueTask<T?> GetAsync(DataLinqKey key, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.GetAsync(this, key, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<bool> ContainsKeyAsync(DataLinqKey key, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ContainsKeyAsync(this, key, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<List<T>> ToListAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToListAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToArrayAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> FirstAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> FirstAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstOrDefaultAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> FirstOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstOrDefaultAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> SingleAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> SingleAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleOrDefaultAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> SingleOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleOrDefaultAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> LastAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> LastAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> LastOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastOrDefaultAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> LastOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastOrDefaultAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AnyAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<bool> AnyAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AnyAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int> CountAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.CountAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int> CountAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.CountAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int> SumAsync(Func<T, int> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int?> SumAsync(Func<T, int?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<long> SumAsync(Func<T, long> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<long?> SumAsync(Func<T, long?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float> SumAsync(Func<T, float> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float?> SumAsync(Func<T, float?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> SumAsync(Func<T, double> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> SumAsync(Func<T, double?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal> SumAsync(Func<T, decimal> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal?> SumAsync(Func<T, decimal?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> AverageAsync(Func<T, int> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> AverageAsync(Func<T, int?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> AverageAsync(Func<T, long> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> AverageAsync(Func<T, long?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float> AverageAsync(Func<T, float> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float?> AverageAsync(Func<T, float?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> AverageAsync(Func<T, double> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> AverageAsync(Func<T, double?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal> AverageAsync(Func<T, decimal> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal?> AverageAsync(Func<T, decimal?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<TResult?> MinAsync<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.MinAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<TResult?> MaxAsync<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.MaxAsync(this, selector, cancellationToken);

}

public partial class ImmutableRelationMock<T> where T : IModelInstance
{
    /// <summary>Creates a cooperatively cancelable async view of this local test snapshot.</summary>
    public virtual IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
        new DeferredRelationSnapshot<T>(() =>
        {
            var captured = Volatile.Read(ref snapshot);
            return token =>
            {
                token.ThrowIfCancellationRequested();
                return new ValueTask<ImmutableArray<T>>(captured.Value.Values);
            };
        }, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<T>> ValuesAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ValuesAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<ImmutableArray<DataLinqKey>> KeysAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.KeysAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<FrozenDictionary<DataLinqKey, T>> ToFrozenDictionaryAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToFrozenDictionaryAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> GetAsync(DataLinqKey key, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.GetAsync(this, key, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<bool> ContainsKeyAsync(DataLinqKey key, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ContainsKeyAsync(this, key, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<List<T>> ToListAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToListAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.ToArrayAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> FirstAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> FirstAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> FirstOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstOrDefaultAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> FirstOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.FirstOrDefaultAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> SingleAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> SingleAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> SingleOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleOrDefaultAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> SingleOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SingleOrDefaultAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> LastAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T> LastAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> LastOrDefaultAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastOrDefaultAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<T?> LastOrDefaultAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.LastOrDefaultAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AnyAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<bool> AnyAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AnyAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int> CountAsync(CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.CountAsync(this, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int> CountAsync(Func<T, bool> predicate, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.CountAsync(this, predicate, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int> SumAsync(Func<T, int> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<int?> SumAsync(Func<T, int?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<long> SumAsync(Func<T, long> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<long?> SumAsync(Func<T, long?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float> SumAsync(Func<T, float> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float?> SumAsync(Func<T, float?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> SumAsync(Func<T, double> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> SumAsync(Func<T, double?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal> SumAsync(Func<T, decimal> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal?> SumAsync(Func<T, decimal?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.SumAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> AverageAsync(Func<T, int> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> AverageAsync(Func<T, int?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> AverageAsync(Func<T, long> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> AverageAsync(Func<T, long?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float> AverageAsync(Func<T, float> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<float?> AverageAsync(Func<T, float?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double> AverageAsync(Func<T, double> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<double?> AverageAsync(Func<T, double?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal> AverageAsync(Func<T, decimal> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<decimal?> AverageAsync(Func<T, decimal?> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.AverageAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<TResult?> MinAsync<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.MinAsync(this, selector, cancellationToken);

    /// <inheritdoc />
    public virtual ValueTask<TResult?> MaxAsync<TResult>(Func<T, TResult> selector, CancellationToken cancellationToken = default) =>
        RelationAsyncDefaults.MaxAsync(this, selector, cancellationToken);

}
