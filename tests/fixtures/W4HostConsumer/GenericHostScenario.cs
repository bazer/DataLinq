using DataLinq;
using DataLinq.Diagnostics;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Instances;
using DataLinq.Logging;
using DataLinq.SQLite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SessionConsumer;

namespace W4HostConsumer;

internal static class GenericHostScenario
{
    internal static async Task Run(bool asynchronousDisposal)
    {
        var name = "w4_worker_" + Guid.NewGuid().ToString("N");
        await using var store = new SQLiteDatabase<TestDb>($"Data Source={name};Mode=Memory;Cache=Shared", name);
        await Fixture.CreateSchema(store);
        var trace = new HostTrace();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(trace);
        builder.Services.AddDataLinq<TestDb>("worker").AsDefault().UseFactory(services =>
            new TrackedDatabase(new TrackedProvider(store.Provider.ConnectionString, name, services.GetRequiredService<ILoggerFactory>(), trace)))
            .AddSingletonService<HostOperations>();
        builder.Services.AddHostedService<Worker>();
        using var host = builder.Build();
        Fixture.Check(trace.RootCreations == 0, "Host construction eagerly activated the database.");
        await host.StartAsync();
        var session = await trace.Written(101).Task.WaitAsync(TimeSpan.FromSeconds(20));
        Fixture.Check(trace.RootCreations == 1 && trace.RootDisposals == 0, "The worker did not borrow the selected singleton infrastructure.");
        await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await trace.WorkerFinished.Task.WaitAsync(TimeSpan.FromSeconds(20));
        if (trace.Failure is not null) throw new InvalidOperationException("Worker recovery failed.", trace.Failure);
        Fixture.Expect<ObjectDisposedException>(session.ValidateParticipation);
        Fixture.Check(await store.GetAsync<TestRow>(DataLinqKey.FromValue(101)) is null, "Cancelled worker writes survived recovery.");
        Fixture.Check(trace.RootDisposals == 0, "Stopping the worker disposed root infrastructure before host disposal.");
        if (asynchronousDisposal) await ((IAsyncDisposable)host).DisposeAsync(); else host.Dispose();
        var events = trace.Events.ToArray();
        var cleanup = Array.FindIndex(events, item => item.StartsWith("root-", StringComparison.Ordinal));
        Fixture.Check(trace.RootDisposals == 1 && cleanup > Array.IndexOf(events, "worker-finished"),
            "Provider disposal ran before dependent work finished or repeated cleanup.");
        Console.WriteLine($"Generic Host {(asynchronousDisposal ? "DisposeAsync" : "Dispose")} completed worker recovery before {events[cleanup]}.");
    }

    private sealed class Worker(HostOperations operations, HostTrace trace) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await operations.WriteWaitAsync(101, trace.Release.Task, stoppingToken); }
            catch (OperationCanceledException failure) when (stoppingToken.IsCancellationRequested)
            {
                var context = DataLinqFailure.GetContext(failure);
                if (context is null || context.CompletionOutcome != DataLinqCompletionOutcome.RolledBack || context.SecondaryFailures.Count != 0)
                    trace.Failure = new InvalidOperationException("Worker cancellation did not finish confirmed rollback and cleanup.", failure);
            }
            catch (Exception failure) { trace.Failure = failure; }
            finally { trace.Events.Enqueue("worker-finished"); trace.WorkerFinished.TrySetResult(); }
        }
    }

    private sealed class TrackedDatabase(TrackedProvider provider) : Database<TestDb>(provider)
    {
    }

    private sealed class TrackedProvider(string connection, string name, ILoggerFactory logger, HostTrace trace)
        : SQLiteProvider<TestDb>(connection, name, new DataLinqLoggingConfiguration(logger))
    {
        private readonly HostTrace trace = CountCreation(trace);
        private static HostTrace CountCreation(HostTrace trace) { trace.RootCreations++; return trace; }
        public override void Dispose() { base.Dispose(); trace.RootDisposals++; trace.Events.Enqueue("root-sync"); }
        public override async ValueTask DisposeAsync() { await base.DisposeAsync(); trace.RootDisposals++; trace.Events.Enqueue("root-async"); }
    }
}
