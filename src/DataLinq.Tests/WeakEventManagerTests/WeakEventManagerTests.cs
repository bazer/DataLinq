using System;
using DataLinq.Utils;
using Xunit;

namespace DataLinq.Tests.WeakEventManagerTests;

public class WeakEventManagerTests
{
    private const string TestEventName = "TestEvent";
    private const string AnotherTestEventName = "AnotherEvent";

    [Fact]
    public void AddAndHandle_InstanceHandler_IsCalled()
    {
        var wem = new WeakEventManager();
        var subscriber = new TestSubscriber();
        EventHandler<TestEventArgs> handler = subscriber.InstanceHandler;

        wem.AddEventHandler(handler, TestEventName);
        wem.HandleEvent(this, new TestEventArgs("Hello"), TestEventName);

        Assert.Equal(1, subscriber.HandlerCallCount);
        Assert.Equal("Hello", subscriber.LastMessage);
        Assert.Same(this, subscriber.LastSender);
    }

    [Fact]
    public void AddAndHandle_StaticHandler_IsCalled()
    {
        TestSubscriber.ResetStatic();
        var wem = new WeakEventManager();
        EventHandler<TestEventArgs> handler = TestSubscriber.StaticHandler;

        wem.AddEventHandler(handler, TestEventName);
        wem.HandleEvent(this, new TestEventArgs("Static Hello"), TestEventName);

        Assert.Equal(1, TestSubscriber.StaticHandlerCallCount);
        Assert.Equal("Static Hello", TestSubscriber.StaticLastMessage);
        Assert.Same(this, TestSubscriber.StaticLastSender);
    }

    [Fact]
    public void Remove_InstanceHandler_IsNotCalled()
    {
        var wem = new WeakEventManager();
        var subscriber = new TestSubscriber();
        EventHandler<TestEventArgs> handler = subscriber.InstanceHandler;

        wem.AddEventHandler(handler, TestEventName);
        wem.RemoveEventHandler(handler, TestEventName);
        wem.HandleEvent(this, new TestEventArgs("Should not see"), TestEventName);

        Assert.Equal(0, subscriber.HandlerCallCount);
    }

    [Fact]
    public void Remove_StaticHandler_IsNotCalled()
    {
        TestSubscriber.ResetStatic();
        var wem = new WeakEventManager();
        EventHandler<TestEventArgs> handler = TestSubscriber.StaticHandler;

        wem.AddEventHandler(handler, TestEventName);
        wem.RemoveEventHandler(handler, TestEventName);
        wem.HandleEvent(this, new TestEventArgs("Should not see static"), TestEventName);

        Assert.Equal(0, TestSubscriber.StaticHandlerCallCount);
    }

    [Fact]
    public void Add_SameInstanceHandlerMultipleTimes_IsCalledOnce()
    {
        var wem = new WeakEventManager();
        var subscriber = new TestSubscriber();
        EventHandler<TestEventArgs> handler = subscriber.InstanceHandler;

        wem.AddEventHandler(handler, TestEventName);
        wem.AddEventHandler(handler, TestEventName); // Add again
        wem.HandleEvent(this, new TestEventArgs("Instance Multiple Adds"), TestEventName);

        Assert.Equal(1, subscriber.HandlerCallCount); // Should only be called once due to duplicate prevention
    }

    [Fact]
    public void Add_SameStaticHandlerMultipleTimes_IsCalledOnce()
    {
        TestSubscriber.ResetStatic();
        var wem = new WeakEventManager();
        EventHandler<TestEventArgs> handler = TestSubscriber.StaticHandler;

        wem.AddEventHandler(handler, TestEventName);
        wem.AddEventHandler(handler, TestEventName); // Add again
        wem.HandleEvent(this, new TestEventArgs("Static Multiple Adds"), TestEventName);

        Assert.Equal(1, TestSubscriber.StaticHandlerCallCount);
    }

    [Fact]
    public void HandleEvent_NoSubscribers_DoesNotThrow()
    {
        var wem = new WeakEventManager();
        var exception = Record.Exception(() => wem.HandleEvent(this, new TestEventArgs("No one listening"), TestEventName));
        Assert.Null(exception);
    }

    [Fact]
    public void HandleEvent_HandlerThrows_OtherHandlersStillCalled()
    {
        var wem = new WeakEventManager();
        var subscriber1 = new TestSubscriber(); // Will not throw
        var subscriber2 = new TestSubscriber(); // Will throw

        EventHandler<TestEventArgs> handler1 = subscriber1.InstanceHandler;
        EventHandler<TestEventArgs> throwingHandler = (s, e) => { throw new InvalidOperationException("Test Exception"); };
        EventHandler<TestEventArgs> handler2_delegate = subscriber2.InstanceHandler;


        wem.AddEventHandler(handler1, TestEventName);
        wem.AddEventHandler(throwingHandler, TestEventName);
        wem.AddEventHandler(handler2_delegate, TestEventName);

        // Assuming Console.WriteLine for errors, check for actual logging if you implement it
        wem.HandleEvent(this, new TestEventArgs("Testing exceptions"), TestEventName);

        Assert.Equal(1, subscriber1.HandlerCallCount);
        Assert.Equal(1, subscriber2.HandlerCallCount); // Should still be called
    }

    [Fact]
    public void DifferentEventNames_AreIsolated()
    {
        var wem = new WeakEventManager();
        var subscriber1 = new TestSubscriber();
        var subscriber2 = new TestSubscriber();
        EventHandler<TestEventArgs> handler1 = subscriber1.InstanceHandler;
        EventHandler<TestEventArgs> handler2 = subscriber2.InstanceHandler;

        wem.AddEventHandler(handler1, TestEventName);
        wem.AddEventHandler(handler2, AnotherTestEventName);

        wem.HandleEvent(this, new TestEventArgs("For Event 1"), TestEventName);
        Assert.Equal(1, subscriber1.HandlerCallCount);
        Assert.Equal(0, subscriber2.HandlerCallCount); // Not called for this event

        wem.HandleEvent(this, new TestEventArgs("For Event 2"), AnotherTestEventName);
        Assert.Equal(1, subscriber1.HandlerCallCount); // Still 1
        Assert.Equal(1, subscriber2.HandlerCallCount); // Now called
    }
}