#if NET10_0
using System.Collections.Concurrent;
using System.Net.Http.Json;
using DataLinq;
using DataLinq.Diagnostics;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.SQLite;
using DataLinq.SQLite.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SessionConsumer;

namespace W4HostConsumer;

internal static class WebHostScenario
{
    internal static async Task Run()
    {
        var trace = new HostTrace();
        var observed = new ConcurrentBag<IDataLinqSession<TestDb>>();
        var instances = new ConcurrentBag<HostOperations>();
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var setups = 0;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(trace);
        builder.Services.AddDataLinq<TestDb>("http").UseSQLite("Data Source=:memory:")
            .Configure(_ => setups++).AddSingletonService<HostOperations>();
        await using var app = builder.Build();
        Fixture.Check(setups == 0, "Web host construction eagerly activated DataLinq.");
        var database = app.Services.GetRequiredKeyedService<SQLiteDatabase<TestDb>>("http");
        Fixture.Check(ReferenceEquals(((SQLiteProvider<TestDb>)database.Provider).LoggingConfiguration.LoggerFactory, app.Services.GetRequiredService<ILoggerFactory>()),
            "Provider registration did not select host logging.");
        await Fixture.CreateSchema(database);
        app.MapGet("/observe", async (HttpContext context, [FromKeyedServices("http")] HostOperations operations) =>
            await operations.ObserveAsync(async (session, token) =>
            {
                observed.Add(session);
                instances.Add(operations);
                if (observed.Count == 16) allEntered.TrySetResult();
                await trace.Release.Task.WaitAsync(token);
                return ((Transaction)session).TransactionID;
            }, context.RequestAborted));
        app.MapPost("/write/{id:int}", async (int id, HttpContext context, [FromKeyedServices("http")] HostOperations operations) =>
        {
            try
            {
                var wait = id == 203 ? shutdownRelease.Task : new TaskCompletionSource().Task;
                return await operations.WriteWaitAsync(id, wait, context.RequestAborted);
            }
            catch (OperationCanceledException failure)
            {
                var diagnostic = DataLinqFailure.GetContext(failure);
                if (diagnostic is null || diagnostic.CompletionOutcome != DataLinqCompletionOutcome.RolledBack || diagnostic.SecondaryFailures.Count != 0)
                    trace.Failure = new InvalidOperationException("Aborted request returned before confirmed rollback/cleanup.", failure);
                throw;
            }
            finally { trace.Finished(id).TrySetResult(); }
        });
        using var stopRegistration = app.Lifetime.ApplicationStopping.Register(() => stopping.TrySetResult());
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()), Timeout = TimeSpan.FromSeconds(30) };
        var requests = Enumerable.Range(0, 16).Select(_ => client.GetFromJsonAsync<uint>("observe")).ToArray();
        await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Fixture.Check(observed.Distinct(ReferenceEqualityComparer.Instance).Count() == 16 && instances.Distinct(ReferenceEqualityComparer.Instance).Count() == 1,
            "Concurrent requests shared a current session or constructed per-request singleton services.");
        trace.Release.TrySetResult();
        Fixture.Check((await Task.WhenAll(requests)).Distinct().Count() == 16, "Concurrent request transaction identities were not distinct.");
        foreach (var session in observed) Fixture.Expect<ObjectDisposedException>(session.ValidateParticipation);

        using var abort = new CancellationTokenSource();
        var cancelledRequest = client.PostAsync("write/202", null, abort.Token);
        await trace.Written(202).Task.WaitAsync(TimeSpan.FromSeconds(20));
        abort.Cancel();
        try { using var response = await cancelledRequest; throw new InvalidOperationException("Aborted client request completed normally."); }
        catch (OperationCanceledException) when (abort.IsCancellationRequested) { }
        await trace.Finished(202).Task.WaitAsync(TimeSpan.FromSeconds(20));
        if (trace.Failure is not null) throw trace.Failure;
        Fixture.Check(await database.GetAsync<TestRow>(DataLinqKey.FromValue(202)) is null, "Aborted HTTP request committed its write.");

        var finalRequest = client.PostAsync("write/203", null);
        var active = await trace.Written(203).Task.WaitAsync(TimeSpan.FromSeconds(20));
        var shutdown = app.StopAsync();
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(20));
        active.ValidateParticipation();
        _ = database.Provider.DatabaseAccess;
        shutdownRelease.TrySetResult();
        using (var response = await finalRequest) response.EnsureSuccessStatusCode();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(20));
        Fixture.Expect<ObjectDisposedException>(active.ValidateParticipation);
        Fixture.Check(await database.GetAsync<TestRow>(DataLinqKey.FromValue(203)) is not null, "Graceful shutdown lost the completed request write.");
        await app.DisposeAsync();
        Fixture.Expect<ObjectDisposedException>(() => _ = database.Provider.DatabaseAccess);
        Fixture.Check(setups == 1, "HTTP activation reconfigured the singleton database.");
        Console.WriteLine("ASP.NET Core requests retained independent sessions, cancellation recovery and graceful shutdown ordering.");
    }
}
#endif
