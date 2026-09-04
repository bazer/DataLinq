using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;

namespace DataLinq.Tests.Unit;

public sealed class CacheHistoryConcurrencyTests
{
    [Test]
    public async Task CapacityChangesTrimImmediatelyAndKeepTheNewestSnapshots()
    {
        var history = new CacheHistory(4);
        var snapshots = Enumerable.Range(0, 4).Select(i => new DatabaseCacheSnapshot(DateTime.UnixEpoch.AddSeconds(i), [])).ToArray();
        foreach (var snapshot in snapshots)
            history.Add(snapshot);

        history.MaxCapacity = 2;
        await Assert.That(history.GetHistory().SequenceEqual(snapshots.Skip(2))).IsTrue();
        await Assert.That(history.Count).IsEqualTo(2u);
        await Assert.That(history.GetLatest()).IsSameReferenceAs(snapshots[3]);

        history.MaxCapacity = 0;
        history.Add(snapshots[0]);
        await Assert.That(history.Count).IsEqualTo(0u);
        await Assert.That(history.GetHistory().Length).IsEqualTo(0);
        await Assert.That(history.GetLatest()).IsNull();
        history.MaxCapacity = 1;
        history.Add(snapshots[1]);
        history.Clear();
        await Assert.That(history.Count).IsEqualTo(0u);
        await Assert.That(history.GetLatest()).IsNull();
    }

    [Test]
    public async Task ConcurrentAddSnapshotClearAndResizePreserveListIntegrity()
    {
        var history = new CacheHistory(64);
        using var start = new ManualResetEventSlim();
        Task Run(Action<int> action) => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 20_000; i++)
                action(i);
        });
        var tasks = new[]
        {
            Run(i => history.Add(new DatabaseCacheSnapshot(DateTime.UnixEpoch.AddTicks(i), []))),
            Run(i => history.Add(new DatabaseCacheSnapshot(DateTime.UnixEpoch.AddTicks(i), []))),
            Run(_ =>
            {
                var snapshots = history.GetHistory();
                if (snapshots.Length > 64 || snapshots.Any(snapshot => snapshot is null))
                    throw new InvalidOperationException("Invalid history snapshot.");
                history.GetLatest();
            }),
            Run(_ => history.Clear()),
            Run(i => history.MaxCapacity = (uint)(i % 65))
        };
        start.Set();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(history.Count).IsEqualTo((uint)history.GetHistory().Length);
        await Assert.That(history.Count <= history.MaxCapacity).IsTrue();
    }

    [Test]
    public async Task AddInvokesSubscribersAfterReleasingTheHistoryLock()
    {
        var history = new CacheHistory();
        var callbackRan = false;
        history.OnAdd += _ =>
        {
            // A different thread must be able to mutate history before the callback returns.
            Task.Run(history.Clear).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            callbackRan = true;
        };
        history.Add(new DatabaseCacheSnapshot(DateTime.UtcNow, []));
        await Assert.That(callbackRan).IsTrue();
        await Assert.That(history.Count).IsEqualTo(0u);
    }
}
