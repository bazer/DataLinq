using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class NativeAsyncCachePublicationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task InvalidationDuringNativeAsyncConstructionCannotRepublishAnOlderGeneration(TestProviderDescriptor descriptor)
    {
        foreach (var route in new[] { "key", "query" })
        foreach (var invalidation in new[] { "clear", "precise", "commit" })
        foreach (var warmBeforeRelease in new[] { false, true })
        {
            using var scope = new NativeAsyncTestDatabase<CachePublicationDb>(descriptor, $"w2_async_publication_{route}_{invalidation}_{warmBeforeRelease}");
            var database = scope.Database;
            var original = await database.InsertAsyncCore(new MutableCachePublicationRow { Id = 1, Name = "old" });
            database.Cache.Clear();
            using var gate = CachePublicationGate.Install(database.Provider.TelemetryInstanceId);
            // A test-only dedicated thread reaches the constructor barrier even
            // when SQLite's native awaitable call completes synchronously.
            var read = Task.Factory.StartNew(async () => route == "key"
                ? (await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([1], database.Provider.ReadOnlyAccess))!
                : (await Rows(database.Query().Rows.Where(row => row.Name == "old"))).Single(),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            try
            {
                gate.WaitUntilBlocked();
                if (invalidation == "commit")
                {
                    var mutable = original.Mutate();
                    mutable.Name = "new";
                    await database.UpdateAsyncCore(mutable);
                }
                else
                {
                    await database.Provider.DatabaseAccess.ExecuteNonQueryAsyncCore("UPDATE publication_rows SET name='new' WHERE id=1");
                    if (invalidation == "clear") database.Cache.Clear();
                    else database.Cache.Invalidate<CachePublicationRow, int>(1);
                }
                CachePublicationRow? fresh = null;
                if (warmBeforeRelease)
                {
                    fresh = await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([1], database.Provider.ReadOnlyAccess)
                        .WaitAsync(TimeSpan.FromSeconds(15));
                    await Assert.That(fresh!.Name).IsEqualTo("new");
                }
                gate.Release();
                _ = await read.WaitAsync(TimeSpan.FromSeconds(20));
                var subsequent = (await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([1], database.Provider.ReadOnlyAccess))!;
                await Assert.That(subsequent.Name).IsEqualTo("new");
                if (fresh is not null) await Assert.That(subsequent).IsSameReferenceAs(fresh);
            }
            finally
            {
                gate.Release();
                await read.WaitAsync(TimeSpan.FromSeconds(20));
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CancellationDuringNativeMaterializationReleasesOwnership(TestProviderDescriptor descriptor)
    {
        using var scope = new NativeAsyncTestDatabase<CachePublicationDb>(descriptor, nameof(CancellationDuringNativeMaterializationReleasesOwnership));
        var database = scope.Database;
        await database.InsertAsyncCore(new MutableCachePublicationRow { Id = 1, Name = "one" });
        database.Cache.Clear();
        using var cancellation = new CancellationTokenSource();
        using var gate = CachePublicationGate.Install(database.Provider.TelemetryInstanceId);
        var read = Task.Factory.StartNew(() => AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([1], database.Provider.ReadOnlyAccess, cancellation.Token),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        try
        {
            gate.WaitUntilBlocked();
            cancellation.Cancel();
            gate.Release();
            await Assert.That(async () => { await read.WaitAsync(TimeSpan.FromSeconds(20)); }).Throws<OperationCanceledException>();
            // A canceled observer cannot invalidate a complete reusable row, but
            // the subsequent operation must not inherit its cancellation/owner.
            var fresh = await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([1], database.Provider.ReadOnlyAccess);
            await Assert.That(fresh!.Name).IsEqualTo("one");
            await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([1], database.Provider.ReadOnlyAccess)).IsSameReferenceAs(fresh);
        }
        finally
        {
            gate.Release();
            try { await read.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (OperationCanceledException) when (read.IsCanceled) { }
        }
    }

    private static async Task<List<T>> Rows<T>(IQueryable<T> query)
    {
        var source = ((ExpressionQueryPlanProvider)query.Provider).ExecuteEnumerableAsyncCore<T>(query.Expression, default);
        var rows = new List<T>();
        await foreach (var row in source) rows.Add(row);
        return rows;
    }
}
