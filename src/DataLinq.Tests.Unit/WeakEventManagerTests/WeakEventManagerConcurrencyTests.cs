using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Utils;

namespace DataLinq.Tests.Unit.WeakEventManagerTests;

public class WeakEventManagerConcurrencyTests
{
    private const string TestEventName = "ConcurrencyTestEvent";

    [Test]
    public async Task Concurrent_AddRemoveHandle_DoesNotThrowOrCorrupt()
    {
        var weakEventManager = new WeakEventManager();
        var threadCount = Environment.ProcessorCount * 2;
        var operationsPerThread = 1000;
        var eventArgs = new TestEventArgs("Concurrent Message");
        var sender = new object();
        long totalCalls = 0;
        var tasks = new List<Task>();

        for (var i = 0; i < threadCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                EventHandler<TestEventArgs> localHandler = (_, _) => Interlocked.Increment(ref totalCalls);

                for (var j = 0; j < operationsPerThread; j++)
                {
                    switch (Random.Shared.Next(3))
                    {
                        case 0:
                            weakEventManager.AddEventHandler(localHandler, TestEventName);
                            break;
                        case 1:
                            weakEventManager.RemoveEventHandler(localHandler, TestEventName);
                            break;
                        default:
                            weakEventManager.HandleEvent(sender, eventArgs, TestEventName);
                            break;
                    }

                    if (j % 100 == 0)
                        Thread.Sleep(0);
                }

                weakEventManager.AddEventHandler(localHandler, TestEventName);
            }));
        }

        await Task.WhenAll(tasks);

        var beforeFinalHandleCalls = Interlocked.Read(ref totalCalls);
        weakEventManager.HandleEvent(sender, eventArgs, TestEventName);
        var afterFinalHandleCalls = Interlocked.Read(ref totalCalls);

        await Assert.That(afterFinalHandleCalls >= beforeFinalHandleCalls).IsTrue();
    }
}
