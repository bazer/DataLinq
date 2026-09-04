using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Instances;

namespace DataLinq.Tests.Unit.Core;

public sealed class IndexExpirationTests
{
    [Test]
    public async Task ExpirationCannotRemoveAReplacementFromAnOldTimestamp()
    {
        long tick = 10;
        var cache = new TypedIndexCache<int>(() => tick);
        var oldKey = DataLinqKey.FromValue(100);
        var replacementKey = DataLinqKey.FromValue(200);
        cache.TryAdd(1, [oldKey]);
        cache.TryRemove(1, out _);
        tick = 30;
        cache.TryAdd(1, [replacementKey]);
        tick = 15; // Also handle clock rollback without blocking earlier expirations.
        cache.TryAdd(2, [oldKey]);

        await Assert.That(cache.RemoveInsertedBeforeTick(20)).IsEqualTo(1);
        await Assert.That(cache.TryGet(1, out var remaining)).IsTrue();
        await Assert.That(remaining!.Single()).IsEqualTo(replacementKey);
        await Assert.That(cache.RemoveInsertedBeforeTick(30)).IsEqualTo(0);
        await Assert.That(cache.RemoveInsertedBeforeTick(31)).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
        cache.TryRemovePrimaryKey(replacementKey, out var removed);
        await Assert.That(removed).IsEqualTo(0);
    }

    [Test]
    public async Task RemovalChurnDoesNotRetainExpirationRecords()
    {
        var cache = new TypedIndexCache<int>();
        var empty = cache.GetMemoryEstimate();
        var key = DataLinqKey.FromValue(1);
        for (var i = 0; i < 50_000; i++)
        {
            cache.TryAdd(i % 3, [key]);
            if ((i & 1) == 0)
                cache.TryRemove(i % 3, out _);
            else
                cache.TryRemovePrimaryKey(key, out _);
        }
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.GetMemoryEstimate()).IsEqualTo(empty);
        await Assert.That(cache.RemoveInsertedBeforeTick(long.MaxValue)).IsEqualTo(0);
        cache.TryAdd(1, [key]);
        cache.Clear();
        await Assert.That(cache.GetMemoryEstimate()).IsEqualTo(empty);
    }

    [Test]
    public async Task ConcurrentChurnAndExpirationKeepForwardAndReverseEntriesConsistent()
    {
        long tick = 0;
        var cache = new TypedIndexCache<int>(() => Interlocked.Increment(ref tick));
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 5_000; i++)
            {
                var key = DataLinqKey.FromValue(worker);
                cache.TryAdd(worker, [key]);
                if ((i & 1) == 0)
                    cache.TryRemovePrimaryKey(key, out _);
                else
                    cache.TryRemove(worker, out _);
                cache.RemoveInsertedBeforeTick(Interlocked.Read(ref tick) - 2);
            }
        })).ToArray();
        start.Set();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        cache.RemoveInsertedBeforeTick(long.MaxValue);
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.GetMemoryEstimate()).IsEqualTo(new TypedIndexCache<int>().GetMemoryEstimate());
        for (var i = 0; i < 4; i++)
        {
            cache.TryRemovePrimaryKey(DataLinqKey.FromValue(i), out var removed);
            await Assert.That(removed).IsEqualTo(0);
        }
    }
}
