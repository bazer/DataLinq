using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using Xunit;

namespace DataLinq.Tests;

// A mock subscriber that correctly implements our notification interface.
public class MockSubscriber : ICacheNotification
{
    private int _clearCacheCallCount;

    public int ClearCacheCallCount => _clearCacheCallCount;

    public ManualResetEventSlim? Mres { get; }
    private readonly int _delayMs;

    public MockSubscriber(ManualResetEventSlim? mres = null, int delayMs = 0)
    {
        Mres = mres;
        _delayMs = delayMs;
    }

    public void Clear()
    {
        if (_delayMs > 0) Thread.Sleep(_delayMs);

        Interlocked.Increment(ref _clearCacheCallCount);

        Mres?.Set();
    }
}

public class CacheNotificationManagerTests
{
    // We can now cast directly to the internal type because of InternalsVisibleTo
    private readonly TableCache.CacheNotificationManager _manager;

    public CacheNotificationManagerTests()
    {
        var managerType = typeof(TableCache).GetNestedType("CacheNotificationManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.NotNull(managerType);
        _manager = (TableCache.CacheNotificationManager)Activator.CreateInstance(managerType, true)!;
        Assert.NotNull(_manager);
    }

    // A helper to get the internal subscriber count for assertions
    private int GetSubscriberCount()
    {
        var fieldInfo = typeof(TableCache.CacheNotificationManager).GetField("subscribers", BindingFlags.NonPublic | BindingFlags.Instance);
        var array = (Array)fieldInfo!.GetValue(_manager)!;
        return array.Length;
    }

    [Fact]
    public void Clean_RemovesDeadSubscribers()
    {
        var weakRef = SubscribeAndForget();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.False(weakRef.TryGetTarget(out _));

        Assert.Equal(1, GetSubscriberCount()); // It contains the dead reference

        _manager.Clean();

        Assert.Equal(0, GetSubscriberCount()); // The dead reference should be gone
    }

    [Fact]
    public async Task SubscribeDuringNotify_DoesNotLoseSubscriber()
    {
        using var mres = new ManualResetEventSlim(false);
        var subscriberA = new MockSubscriber(mres, delayMs: 100);
        var subscriberB = new MockSubscriber();

        _manager.Subscribe(subscriberA);

        var notifyTask = Task.Run(() => _manager.Notify());
        Assert.True(mres.Wait(TimeSpan.FromSeconds(2)), "Timed out waiting for first notification.");

        _manager.Subscribe(subscriberB);
        await notifyTask;

        _manager.Notify();

        Assert.Equal(2, subscriberA.ClearCacheCallCount);
        Assert.Equal(1, subscriberB.ClearCacheCallCount);
    }

    [Fact]
    public void SubscribeAndNotify_NotifiesLiveSubscriber()
    {
        var subscriber = new MockSubscriber();
        _manager.Subscribe(subscriber);

        _manager.Notify();

        Assert.Equal(1, subscriber.ClearCacheCallCount);
    }

    [Fact]
    public void Notify_DoesNotNotifyGarbageCollectedSubscriber()
    {
        var liveSubscriber = new MockSubscriber();
        _manager.Subscribe(liveSubscriber);
        var weakRef = SubscribeAndForget();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        _manager.Notify();

        Assert.False(weakRef.TryGetTarget(out _));
        Assert.Equal(1, liveSubscriber.ClearCacheCallCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<MockSubscriber> SubscribeAndForget()
    {
        var localSubscriber = new MockSubscriber();
        _manager.Subscribe(localSubscriber);
        return new WeakReference<MockSubscriber>(localSubscriber);
    }
}