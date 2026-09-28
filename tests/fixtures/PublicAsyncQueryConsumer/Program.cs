using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Linq;
using QueryAsync = DataLinq.Linq.DataLinqAsyncQueryableExtensions;

// Deliberately not a friend assembly. Standard local async LINQ must be available
// transitively on .NET 8/9 and from the framework on .NET 10.
if (await Values().Where(value => value > 1).CountAsync() != 2)
    throw new InvalidOperationException("Standard async LINQ did not compose correctly.");

IQueryable<int> foreign = new[] { 1, 2 }.AsQueryable();
await Reject(() => foreign.CountAsync());
await Reject(() => QueryAsync.CountAsync(foreign));
await Reject(() => DataLinqAsyncQueryableExtensions.CountAsync(foreign, value => value > 0, cancellationToken: default));
await Reject<double>(() => foreign.AverageAsync(value => value));
await Reject<int?>(() => foreign.MinAsync(value => (int?)value));
await Reject<string?>(() => foreign.MaxAsync(value => value.ToString()));
await Reject<int>(() => foreign.SingleOrDefaultAsync());
var relation = new ImmutableRelationMock<IImmutableInstance>([]);
IImmutableRelation<IImmutableInstance> relationInterface = relation;
if (await relation.CountAsync() != 0 || await relationInterface.CountAsync() != 0 ||
    await relation.SumAsync(_ => 1) != 0 || (await relation.KeysAsync()).Length != 0 ||
    await relation.GetAsync(DataLinqKey.Null) is not null)
    throw new InvalidOperationException("Concrete/interface async relation defaults disagree.");
if (await relation.AsAsyncEnumerable().AnyAsync())
    throw new InvalidOperationException("The empty relation produced a row.");
IDatabaseProvider legacy = new LegacyProvider();
if (legacy.ExecutionOptions.RecoveryRollbackTimeout != TimeSpan.FromSeconds(30))
    throw new InvalidOperationException("Legacy options default changed.");
await RejectTask(() => ((IAsyncDisposable)legacy).DisposeAsync().AsTask());
await RejectTask(() => legacy.CommitAsync(_ => Task.CompletedTask));
await RejectTask(() => legacy.CommitAsync((_, _) => Task.CompletedTask));
await RejectTask(() => legacy.CommitAsync(_ => Task.FromResult(42)));
await RejectTask(() => legacy.CommitAsync((_, _) => Task.FromResult(42)));
var legacyTransaction = new LegacyTransaction();
await RejectTask(() => legacyTransaction.CommitAsync(cancellationToken: default));
await RejectTask(() => legacyTransaction.RollbackAsync(cancellationToken: default));
await RejectTask(() => ((IAsyncDisposable)legacyTransaction).DisposeAsync().AsTask());
Console.WriteLine("Public async query consumer passed on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);

static async Task RejectTask(Func<Task> action)
{
    try { await action(); }
    catch (NotSupportedException) { return; }
    throw new InvalidOperationException("A legacy async default was accepted.");
}

static async Task Reject<T>(Func<ValueTask<T>> action)
{
    try { await action(); }
    catch (NotSupportedException) { return; }
    throw new InvalidOperationException("A foreign query provider was accepted.");
}

static async IAsyncEnumerable<int> Values()
{
    await Task.CompletedTask;
    yield return 1;
    yield return 2;
    yield return 3;
}

// Compile exact prepared return types from outside the internals-visible test assemblies.
internal static class PreparedBinding
{
    internal static Task<TResult> Scalar<D, TArgument, TResult>(
        PreparedQuery<D, TArgument, TResult> query, IDataSourceAccess<D> source, TArgument argument)
        where D : class, IDatabaseModel<D> =>
        query.ExecuteAsync(source, argument, cancellationToken: default);

    internal static IAsyncEnumerable<TElement> Sequence<D, TArgument, TElement>(
        PreparedSequenceQuery<D, TArgument, TElement> query, IDataSourceAccess<D> source, TArgument argument)
        where D : class, IDatabaseModel<D> =>
        query.ExecuteAsync(source, argument, cancellationToken: default);
}

internal static class LookupBinding
{
    internal static ValueTask<T?> Database<D, T>(DataLinq.Database<D> database, DataLinqKey key)
        where D : class, IDatabaseModel<D> where T : IImmutableInstance =>
        database.GetAsync<T>(key, cancellationToken: default);

    internal static ValueTask<T?> Transaction<D, T>(DataLinq.Mutation.Transaction<D> transaction, DataLinqKey key)
        where D : class, IDatabaseModel<D> where T : IImmutableInstance =>
        transaction.GetAsync<T>(key, cancellationToken: default);

    internal static ValueTask<T?> Canonical<T>(IDataSourceAccess source, DataLinqKey key)
        where T : IImmutableInstance =>
        IImmutable<T>.GetByProviderKeyAsync(key, source, cancellationToken: default);

    internal static ValueTask<T?> Reference<T>(IAsyncImmutableForeignKey<T> reference)
        where T : IImmutableInstance =>
        reference.GetAsync(cancellationToken: default);
}
