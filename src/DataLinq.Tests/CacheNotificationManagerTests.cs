using System;
using System.Collections.Generic;
using System.Collections.Concurrent; // Added for ConcurrentQueue
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
        // Updated to new implementation using ConcurrentQueue _subscribers
        var fieldInfo = typeof(TableCache.CacheNotificationManager).GetField("_subscribers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(fieldInfo); // Fail fast if implementation changes again
        var queue = (ConcurrentQueue<WeakReference<ICacheNotification>>)fieldInfo!.GetValue(_manager)!;
        return queue.Count;
    }
 
    [Fact]
    public void Clean_RemovesDeadSubscribers()
    {
        var weakRef = SubscribeAndForget();
 
        Assert.Equal(1, GetSubscriberCount()); // Contains the dead reference
 
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.False(weakRef.TryGetTarget(out _));
 
        Assert.Equal(1, GetSubscriberCount()); // Still present before notify
 
        _manager.Notify();
 
        Assert.Equal(0, GetSubscriberCount()); // Snapshot consumed; queue empty
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
 
        // With snapshot-consume semantics, A was only in the first snapshot, B only in the second.
        Assert.Equal(1, subscriberA.ClearCacheCallCount);
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