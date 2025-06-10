using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DataLinq.Cache;
using Xunit;

// Private class inside TableCache in the main library
// We need to use reflection to create an instance of it for testing.
// NOTE: This assumes the nested private class is named "CacheNotificationManager"

namespace DataLinq.Tests;

// A mock subscriber that implements our notification interface.
public class MockSubscriber : ICacheNotification
{
    public int ClearCacheCallCount { get; private set; }
    public ManualResetEventSlim? Mres { get; }
    private readonly int _delayMs;

    // Optional constructor for concurrency tests to introduce delays
    public MockSubscriber(ManualResetEventSlim? mres = null, int delayMs = 0)
    {
        Mres = mres;
        _delayMs = delayMs;
    }

    public void Clear()
    {
        // For concurrency test: delay and then signal
        if (_delayMs > 0) Thread.Sleep(_delayMs);

        // Use a local variable to increment the count and then assign it back to the property
        int currentCount = ClearCacheCallCount;
        Interlocked.Increment(ref currentCount);
        ClearCacheCallCount = currentCount;

        Mres?.Set(); // Signal that the method was called
    }
}



public class CacheNotificationManagerTests
{
    private readonly dynamic _manager;
    private readonly Type _managerType;

    public CacheNotificationManagerTests()
    {
        // Use reflection to create an instance of the private nested class for testing
        _managerType = typeof(TableCache).GetNestedType("CacheNotificationManager", System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(_managerType);
        _manager = Activator.CreateInstance(_managerType!)!;
        Assert.NotNull(_manager);
    }

    [Fact]
    public void SubscribeAndNotify_NotifiesLiveSubscriber()
    {
        // Arrange
        var subscriber = new MockSubscriber();
        _manager.Subscribe(subscriber);

        // Act
        _manager.Notify();

        // Assert
        Assert.Equal(1, subscriber.ClearCacheCallCount);
    }

    [Fact]
    public void Notify_DoesNotNotifyGarbageCollectedSubscriber()
    {
        // Arrange
        var liveSubscriber = new MockSubscriber();
        _manager.Subscribe(liveSubscriber);

        // This helper creates a subscriber that will go out of scope and be collected.
        var weakRef = SubscribeAndForget();

        // Act
        // Force garbage collection to run and collect the subscriber from the helper method.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        _manager.Notify();

        // Assert
        Assert.False(weakRef.TryGetTarget(out _), "The weak reference should be dead.");
        Assert.Equal(1, liveSubscriber.ClearCacheCallCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<MockSubscriber> SubscribeAndForget()
    {
        // This subscriber only has a strong reference within this method's scope.
        var localSubscriber = new MockSubscriber();
        _manager.Subscribe(localSubscriber);
        return new WeakReference<MockSubscriber>(localSubscriber);
    }

    [Fact]
    public void Clean_RemovesDeadSubscribers()
    {
        // Arrange
        var weakRef = SubscribeAndForget();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Assert.False(weakRef.TryGetTarget(out _)); // Confirm it's dead

        // Get the internal bag count via reflection before cleaning
        var bagBefore = (ConcurrentBag<WeakReference<ICacheNotification>>)_manager.subscribers;
        Assert.NotEmpty(bagBefore); // Should contain the dead reference

        // Act
        _manager.Clean();

        // Assert
        var bagAfter = (ConcurrentBag<WeakReference<ICacheNotification>>)_manager.subscribers;
        Assert.Empty(bagAfter); // The dead reference should have been purged
    }

    [Fact]
    public void SubscribeDuringNotify_DoesNotLoseSubscriber()
    {
        // This test manually simulates the race condition without true multithreading
        // to create a fast, reliable, and deadlock-free test.

        // Arrange
        var manager = _manager; // Get a local, typed reference
        var subscribersField = _managerType.GetField("subscribers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(subscribersField);

        var subscriberA = new MockSubscriber();
        var subscriberB = new MockSubscriber();

        manager.Subscribe(subscriberA);

        // --- Manually Simulate the Notify() method's steps ---

        // 1. Atomically swap the bag, getting a snapshot of the subscribers to process.
        // The main subscribers bag is now new and empty.
        var bagToProcess = (ConcurrentBag<WeakReference<ICacheNotification>>)Interlocked.Exchange(ref manager.subscribers, new ConcurrentBag<WeakReference<ICacheNotification>>());

        // 2. *** SIMULATE THE RACE CONDITION ***
        // A new subscription for subscriberB happens NOW, while we are processing the old bag.
        // This new subscription correctly goes into the new, empty _subscribers bag.
        manager.Subscribe(subscriberB);

        // 3. Continue processing the old bag ("bagToProcess")
        var liveSubscribersFromSnapshot = new List<ICacheNotification>();
        foreach (var weakRef in bagToProcess)
        {
            if (weakRef.TryGetTarget(out var subscriber))
            {
                liveSubscribersFromSnapshot.Add(subscriber);
                // Add the live subscriber from the old bag back to the *current* main bag.
                manager.subscribers.Add(weakRef);
            }
        }

        // 4. Notify only the subscribers that were found in the original snapshot.
        foreach (var sub in liveSubscribersFromSnapshot)
        {
            sub.Clear();
        }

        // --- Assertions after the first, simulated notification ---
        Assert.Equal(1, subscriberA.ClearCacheCallCount);
        Assert.Equal(0, subscriberB.ClearCacheCallCount); // B was not in the first snapshot, so it wasn't notified.

        // --- Now, do a second, normal notification ---
        manager.Notify();

        // Assert the final state
        Assert.Equal(2, subscriberA.ClearCacheCallCount); // A was preserved and notified again.
        Assert.Equal(1, subscriberB.ClearCacheCallCount); // B was now in the bag and was notified.
    }
}