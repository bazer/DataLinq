using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;

namespace DataLinq.Tests.Unit;

public class CacheNotificationManagerTests
{
    private readonly TableCache.CacheNotificationManager manager;

    public CacheNotificationManagerTests()
    {
        var managerType = typeof(TableCache).GetNestedType("CacheNotificationManager", BindingFlags.NonPublic | BindingFlags.Public);
        manager = (TableCache.CacheNotificationManager)Activator.CreateInstance(managerType!, true)!;
    }

    [Test]
    public async Task Clean_RemovesDeadSubscribers()
    {
        var liveSubscriber = new MockSubscriber();
        manager.Subscribe(liveSubscriber);
        var weakReference = SubscribeAndForget();

        await Assert.That(GetSubscriberCount()).IsEqualTo(2);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        await Assert.That(weakReference.TryGetTarget(out _)).IsFalse();
        manager.Clean();

        await Assert.That(GetSubscriberCount()).IsEqualTo(1);
        await Assert.That(liveSubscriber.ClearCacheCallCount).IsEqualTo(0);

        manager.Notify();

        await Assert.That(liveSubscriber.ClearCacheCallCount).IsEqualTo(1);
        await Assert.That(GetSubscriberCount()).IsEqualTo(0);
    }

    [Test]
    public async Task SubscribeDuringNotify_DoesNotLoseSubscriber()
    {
        using var waitHandle = new ManualResetEventSlim(false);
        var subscriberA = new MockSubscriber(waitHandle, delayMs: 100);
        var subscriberB = new MockSubscriber();

        manager.Subscribe(subscriberA);

        var notifyTask = Task.Run(manager.Notify);
        await Assert.That(waitHandle.Wait(TimeSpan.FromSeconds(2))).IsTrue();

        manager.Subscribe(subscriberB);
        await notifyTask;

        manager.Notify();

        await Assert.That(subscriberA.ClearCacheCallCount).IsEqualTo(1);
        await Assert.That(subscriberB.ClearCacheCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task SubscribeAndNotify_NotifiesLiveSubscriber()
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber);

        manager.Notify();

        await Assert.That(subscriber.ClearCacheCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Notify_DoesNotNotifyGarbageCollectedSubscriber()
    {
        var liveSubscriber = new MockSubscriber();
        manager.Subscribe(liveSubscriber);
        var weakReference = SubscribeAndForget();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        manager.Notify();

        await Assert.That(weakReference.TryGetTarget(out _)).IsFalse();
        await Assert.That(liveSubscriber.ClearCacheCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Clean_DoesNotDropLiveSubscribers()
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber);

        manager.Clean();

        await Assert.That(GetSubscriberCount()).IsEqualTo(1);
        await Assert.That(subscriber.ClearCacheCallCount).IsEqualTo(0);

        manager.Notify();

        await Assert.That(subscriber.ClearCacheCallCount).IsEqualTo(1);
    }

    private int GetSubscriberCount()
    {
        var subscribersField = typeof(TableCache.CacheNotificationManager).GetField("_subscribers", BindingFlags.NonPublic | BindingFlags.Instance);
        var queue = (ConcurrentQueue<WeakReference<ICacheNotification>>)subscribersField!.GetValue(manager)!;
        return queue.Count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<MockSubscriber> SubscribeAndForget()
    {
        var subscriber = new MockSubscriber();
        manager.Subscribe(subscriber);
        return new WeakReference<MockSubscriber>(subscriber);
    }

    private sealed class MockSubscriber(ManualResetEventSlim? waitHandle = null, int delayMs = 0) : ICacheNotification
    {
        private int clearCacheCallCount;

        public int ClearCacheCallCount => clearCacheCallCount;

        public void Clear()
        {
            if (delayMs > 0)
                Thread.Sleep(delayMs);

            Interlocked.Increment(ref clearCacheCallCount);
            waitHandle?.Set();
        }
    }
}
