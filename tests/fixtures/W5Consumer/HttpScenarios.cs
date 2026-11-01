#if NET10_0
using System.Net;
using System.Net.Sockets;
using DataLinq.Exceptions;
using DataLinq.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SessionConsumer;

namespace W5Consumer;

internal static class HttpScenarios
{
    internal static async Task Run()
    {
        foreach (var concurrent in new[] { false, true })
        foreach (var drift in new[] { false, true })
        {
            using var store = Fixture.Database();
            Fixture.CreateSchema(store);
            if (drift) store.Provider.DatabaseAccess.ExecuteNonQuery("ALTER TABLE rows RENAME COLUMN value TO wrong");
            // A native catalog diagnostic provides a deterministic pause while
            // the validator still owns its read, without private test hooks.
            store.Provider.DatabaseAccess.ExecuteNonQuery("CREATE INDEX expression_index ON rows(id + 1)");
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            var address = $"http://127.0.0.1:{port}";
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(address);
            builder.Services.Configure<HostOptions>(options => options.ServicesStartConcurrently = concurrent);
            var worker = new HostScenarios.Worker();
            builder.Services.AddSingleton<IHostedService>(worker);
            builder.Services.AddDataLinq<TestDb>().UseInstance(store).ValidateSchemaOnStartup(options =>
            {
                options.Validation.TreatValidationIssuesAsFailures = false;
                options.Validation.MetadataReaderLog = _ => { entered.TrySetResult(); release.Wait(deadline.Token); };
            });
            await using var app = builder.Build();
            app.MapGet("/", () => "ready");
            var pending = Task.Run(() => app.StartAsync(deadline.Token));
            try
            {
                await entered.Task.WaitAsync(deadline.Token);
                Fixture.Check(worker.Starts == 0, "Worker started while metadata was pending.");
                Fixture.Check(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Count == 0,
                    "HTTP addresses were published before validation settled.");
                using (var socket = new TcpClient())
                    await Fixture.ExpectAsync<SocketException>(async () => await socket.ConnectAsync(IPAddress.Loopback, port, deadline.Token));
                release.Set();
                if (drift)
                {
                    var failure = await Fixture.ExpectAsync<DataLinqSchemaStartupException>(() => pending);
                    Fixture.Check(failure.Outcomes.Single().Result!.HasFailures && worker.Starts == 0, "HTTP failure lost completed comparison or started worker.");
                    using var socket = new TcpClient();
                    await Fixture.ExpectAsync<SocketException>(async () => await socket.ConnectAsync(IPAddress.Loopback, port, deadline.Token));
                }
                else
                {
                    await pending;
                    using var client = new HttpClient { BaseAddress = new(address), Timeout = TimeSpan.FromSeconds(10) };
                    Fixture.Check(await client.GetStringAsync("/", deadline.Token) == "ready" && worker.Starts == 1,
                        "Successful validation did not allow real HTTP requests.");
                    await app.StopAsync(deadline.Token);
                }
            }
            finally { release.Set(); try { await pending; } catch { /* observed above, or preserve an earlier fixture failure */ } }
        }
        Console.WriteLine("HTTP: real listener withheld until validation settled; drift prevented serving for both concurrency settings.");
    }
}
#endif
