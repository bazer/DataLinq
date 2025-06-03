using System;
using System.Runtime.CompilerServices;
using System.Threading;
using DataLinq.Utils;
using Xunit;

namespace DataLinq.Tests.WeakEventManagerTests;

public class WeakEventManagerMemoryTests
{
    private const string TestEventName = "MemoryTestEvent";

    // Helper method to perform the subscription.
    // The key is that 'subscriber' is local to this method.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<object> SubscribeAndReturnWeakReference(WeakEventManager wem, TestSubscriber subscriber)
    {
        // The handler delegate is created here. It captures 'subscriber'.
        EventHandler<TestEventArgs> handler = subscriber.InstanceHandler;
        wem.AddEventHandler(handler, TestEventName);

        // We are returning a WeakReference to the subscriber.
        // The 'handler' delegate itself (which holds a strong ref to 'subscriber' via its Target)
        // is also now held *weakly* by the WeakEventManager (via the target of the delegate).
        // If 'subscriber' has no other strong refs outside this call stack, it becomes eligible for GC.
        return new WeakReference<object>(subscriber);
    }

    [Fact]
    public void SubscriberIsCollected_WhenNoStrongReferencesExist()
    {
        var wem = new WeakEventManager();

        // Create the subscriber instance. This is the strong reference we will null out.
        TestSubscriber? subscriberInstance = new TestSubscriber();

        // Call the helper to subscribe. The helper returns a WeakReference.
        // The 'subscriberInstance' is passed by value (its reference is copied).
        WeakReference<object> weakSubscriber = SubscribeAndReturnWeakReference(wem, subscriberInstance);

        // Crucial step: Remove the only strong reference to the subscriber object
        // that this test method directly controls. After this, the subscriber object
        // should only be "kept alive" if the WeakEventManager (or something else unexpectedly)
        // is holding a strong reference to it. The WEM should only hold a weak ref to its target.
        // The delegate passed to AddEventHandler *does* hold a strong ref to subscriberInstance.Target,
        // but the WEM should be storing a WeakReference to that target.
        subscriberInstance = null;

        // Attempt to force garbage collection.
        // Running it multiple times and waiting for finalizers increases the chance of collection.
        for (int i = 0; i < 3; i++) // Try a few GCs
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }
        // Optionally, add a small sleep if tests are still flaky, but try to avoid.
        Thread.Sleep(50);

        Assert.False(weakSubscriber.TryGetTarget(out _), "Subscriber object should have been collected.");

        // Further verification: After collection, raising the event should not reach the (now gone) handler,
        // and the WeakEventManager should clean up the dead subscription.
        var liveSubscriberForCleanupCheck = new TestSubscriber();
        wem.AddEventHandler(liveSubscriberForCleanupCheck.InstanceHandler, TestEventName);

        int initialLiveHandlerCount = liveSubscriberForCleanupCheck.HandlerCallCount;

        wem.HandleEvent(this, new TestEventArgs("Post-GC Event"), TestEventName);

        // Check that only the still-live handler was called.
        Assert.Equal(initialLiveHandlerCount + 1, liveSubscriberForCleanupCheck.HandlerCallCount);
        // If you could inspect the internal state of 'wem', you'd expect the subscription
        // count for TestEventName to be 1 (only the liveSubscriberForCleanupCheck).
    }
}