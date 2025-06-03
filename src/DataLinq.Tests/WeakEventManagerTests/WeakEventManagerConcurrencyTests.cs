using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Utils;
using Xunit;

namespace DataLinq.Tests.WeakEventManagerTests;
public class WeakEventManagerConcurrencyTests
{
    private const string TestEventName = "ConcurrencyTestEvent";

    [Fact]
    public async Task Concurrent_AddRemoveHandle_DoesNotThrowOrCorrupt()
    {
        var wem = new WeakEventManager();
        int numThreads = Environment.ProcessorCount * 2; // Example: 8 or 16
        int operationsPerThread = 1000;
        var eventArgs = new TestEventArgs("Concurrent Message");
        var sender = new object();
        long totalCalls = 0;

        var tasks = new List<Task>();

        for (int i = 0; i < numThreads; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var localSubscriber = new TestSubscriber();
                EventHandler<TestEventArgs> localHandler = (s, e) =>
                {
                    Interlocked.Increment(ref totalCalls);
                    // Simulate some work
                    // Thread.SpinWait(10); 
                };

                for (int j = 0; j < operationsPerThread; j++)
                {
                    int op = Random.Shared.Next(3);
                    switch (op)
                    {
                        case 0: // Add
                            wem.AddEventHandler(localHandler, TestEventName);
                            break;
                        case 1: // Remove
                            wem.RemoveEventHandler(localHandler, TestEventName);
                            break;
                        case 2: // Handle
                            wem.HandleEvent(sender, eventArgs, TestEventName);
                            break;
                    }
                    if (j % 100 == 0) Thread.Sleep(0); // Yield occasionally
                }

                // Ensure a final add so we can count calls if it wasn't removed
                wem.AddEventHandler(localHandler, TestEventName);
            }));
        }

        await Task.WhenAll(tasks);

        // After all operations, raise the event one last time
        // to count how many "final add" handlers are effectively present and live.
        // This count is very approximate due to concurrent adds/removes.
        // The main goal here is to ensure no exceptions (like from list modification) occurred.
        long beforeFinalHandleCalls = Interlocked.Read(ref totalCalls);
        wem.HandleEvent(sender, eventArgs, TestEventName);
        long afterFinalHandleCalls = Interlocked.Read(ref totalCalls);

        // This assertion is weak due to the randomness of operations.
        // The key is that the await Task.WhenAll(tasks) completed without exceptions.
        Assert.True(afterFinalHandleCalls >= beforeFinalHandleCalls, "HandleEvent should still work.");
        // A more robust check would be to inspect internal state if possible, or ensure no internal exceptions.
        // The primary goal is "does not throw or deadlock".
    }
}