using System;
using System.Threading.Tasks;
using DataLinq.Utils;

namespace DataLinq.Tests.Unit.WeakEventManagerTests;

public class WeakEventManagerTests
{
    private const string TestEventName = "TestEvent";
    private const string AnotherTestEventName = "AnotherEvent";

    [Test]
    public async Task AddAndHandle_InstanceHandler_IsCalled()
    {
        var weakEventManager = new WeakEventManager();
        var subscriber = new TestSubscriber();
        EventHandler<TestEventArgs> handler = subscriber.InstanceHandler;

        weakEventManager.AddEventHandler(handler, TestEventName);
        weakEventManager.HandleEvent(this, new TestEventArgs("Hello"), TestEventName);

        await Assert.That(subscriber.HandlerCallCount).IsEqualTo(1);
        await Assert.That(subscriber.LastMessage).IsEqualTo("Hello");
        await Assert.That(ReferenceEquals(this, subscriber.LastSender)).IsTrue();
    }

    [Test]
    public async Task AddAndHandle_StaticHandler_IsCalled()
    {
        int callCount;
        string lastMessage;
        bool senderMatched;

        lock (TestSubscriber.StaticSyncRoot)
        {
            TestSubscriber.ResetStatic();
            var weakEventManager = new WeakEventManager();
            EventHandler<TestEventArgs> handler = TestSubscriber.StaticHandler;

            weakEventManager.AddEventHandler(handler, TestEventName);
            weakEventManager.HandleEvent(this, new TestEventArgs("Static Hello"), TestEventName);

            callCount = TestSubscriber.StaticHandlerCallCount;
            lastMessage = TestSubscriber.StaticLastMessage;
            senderMatched = ReferenceEquals(this, TestSubscriber.StaticLastSender);
        }

        await Assert.That(callCount).IsEqualTo(1);
        await Assert.That(lastMessage).IsEqualTo("Static Hello");
        await Assert.That(senderMatched).IsTrue();
    }

    [Test]
    public async Task Remove_InstanceHandler_IsNotCalled()
    {
        var weakEventManager = new WeakEventManager();
        var subscriber = new TestSubscriber();
        EventHandler<TestEventArgs> handler = subscriber.InstanceHandler;

        weakEventManager.AddEventHandler(handler, TestEventName);
        weakEventManager.RemoveEventHandler(handler, TestEventName);
        weakEventManager.HandleEvent(this, new TestEventArgs("Should not see"), TestEventName);

        await Assert.That(subscriber.HandlerCallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Remove_StaticHandler_IsNotCalled()
    {
        int callCount;

        lock (TestSubscriber.StaticSyncRoot)
        {
            TestSubscriber.ResetStatic();
            var weakEventManager = new WeakEventManager();
            EventHandler<TestEventArgs> handler = TestSubscriber.StaticHandler;

            weakEventManager.AddEventHandler(handler, TestEventName);
            weakEventManager.RemoveEventHandler(handler, TestEventName);
            weakEventManager.HandleEvent(this, new TestEventArgs("Should not see static"), TestEventName);

            callCount = TestSubscriber.StaticHandlerCallCount;
        }

        await Assert.That(callCount).IsEqualTo(0);
    }

    [Test]
    public async Task Add_SameInstanceHandlerMultipleTimes_IsCalledOnce()
    {
        var weakEventManager = new WeakEventManager();
        var subscriber = new TestSubscriber();
        EventHandler<TestEventArgs> handler = subscriber.InstanceHandler;

        weakEventManager.AddEventHandler(handler, TestEventName);
        weakEventManager.AddEventHandler(handler, TestEventName);
        weakEventManager.HandleEvent(this, new TestEventArgs("Instance Multiple Adds"), TestEventName);

        await Assert.That(subscriber.HandlerCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Add_SameStaticHandlerMultipleTimes_IsCalledOnce()
    {
        int callCount;

        lock (TestSubscriber.StaticSyncRoot)
        {
            TestSubscriber.ResetStatic();
            var weakEventManager = new WeakEventManager();
            EventHandler<TestEventArgs> handler = TestSubscriber.StaticHandler;

            weakEventManager.AddEventHandler(handler, TestEventName);
            weakEventManager.AddEventHandler(handler, TestEventName);
            weakEventManager.HandleEvent(this, new TestEventArgs("Static Multiple Adds"), TestEventName);

            callCount = TestSubscriber.StaticHandlerCallCount;
        }

        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task HandleEvent_NoSubscribers_DoesNotThrow()
    {
        var weakEventManager = new WeakEventManager();
        weakEventManager.HandleEvent(this, new TestEventArgs("No one listening"), TestEventName);
        await Task.CompletedTask;
    }

    [Test]
    public async Task HandleEvent_HandlerThrows_OtherHandlersStillCalled()
    {
        var weakEventManager = new WeakEventManager();
        var subscriber1 = new TestSubscriber();
        var subscriber2 = new TestSubscriber();

        EventHandler<TestEventArgs> handler1 = subscriber1.InstanceHandler;
        EventHandler<TestEventArgs> throwingHandler = (_, _) => throw new InvalidOperationException("Test Exception");
        EventHandler<TestEventArgs> handler2 = subscriber2.InstanceHandler;

        weakEventManager.AddEventHandler(handler1, TestEventName);
        weakEventManager.AddEventHandler(throwingHandler, TestEventName);
        weakEventManager.AddEventHandler(handler2, TestEventName);
        weakEventManager.HandleEvent(this, new TestEventArgs("Testing exceptions"), TestEventName);

        await Assert.That(subscriber1.HandlerCallCount).IsEqualTo(1);
        await Assert.That(subscriber2.HandlerCallCount).IsEqualTo(1);
    }

    [Test]
    public async Task DifferentEventNames_AreIsolated()
    {
        var weakEventManager = new WeakEventManager();
        var subscriber1 = new TestSubscriber();
        var subscriber2 = new TestSubscriber();
        EventHandler<TestEventArgs> handler1 = subscriber1.InstanceHandler;
        EventHandler<TestEventArgs> handler2 = subscriber2.InstanceHandler;

        weakEventManager.AddEventHandler(handler1, TestEventName);
        weakEventManager.AddEventHandler(handler2, AnotherTestEventName);

        weakEventManager.HandleEvent(this, new TestEventArgs("For Event 1"), TestEventName);
        await Assert.That(subscriber1.HandlerCallCount).IsEqualTo(1);
        await Assert.That(subscriber2.HandlerCallCount).IsEqualTo(0);

        weakEventManager.HandleEvent(this, new TestEventArgs("For Event 2"), AnotherTestEventName);
        await Assert.That(subscriber1.HandlerCallCount).IsEqualTo(1);
        await Assert.That(subscriber2.HandlerCallCount).IsEqualTo(1);
    }
}
