using DataLinq.Extensions.DependencyInjection;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.SQLite.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace SessionConsumer;

internal static class ServiceRegistrationConsumer
{
    internal static async Task Run()
    {
        foreach (var asynchronous in new[] { false, true })
        foreach (var defaultFirst in new[] { false, true })
        {
            var services = new ServiceCollection();
            var disposal = new ServiceDisposalCounter();
            services.AddSingleton(disposal);
            services.AddSingleton<IServicePolicy>(new ServicePolicy(10));
            var setups = 0;
            var registration = services.AddDataLinq<TestDb>("selected").UseSQLite("Data Source=:memory:")
                .Configure(_ => setups++);
            if (defaultFirst) registration.AsDefault();
            registration.AddSingletonService<BindingService>().AddScopedService<TraditionalService>().AddTransientService<DisposableService>();
            if (!defaultFirst) registration.AsDefault();
            services.AddDataLinq<TestDb>("other").UseSQLite("Data Source=:memory:").AddSingletonService<BindingService>();
            var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            if (setups != 0 || disposal.Created != 0) throw new Exception("Application service registration activated eagerly.");
            BindingService? singleton = null;
            TraditionalService? previousScope = null;
            for (var index = 0; index < 2; index++)
            {
                var scope = container.CreateAsyncScope();
                var selected = scope.ServiceProvider.GetRequiredKeyedService<BindingService>("selected");
                if (!ReferenceEquals(selected, scope.ServiceProvider.GetRequiredService<BindingService>()) || singleton is not null && !ReferenceEquals(selected, singleton))
                    throw new Exception("Generated singleton aliases lost identity.");
                singleton = selected;
                var scoped = scope.ServiceProvider.GetRequiredService<TraditionalService>();
                if (!ReferenceEquals(scoped, scope.ServiceProvider.GetRequiredKeyedService<TraditionalService>("selected")) || ReferenceEquals(scoped, previousScope))
                    throw new Exception("Generated scoped aliases lost their scope lifetime.");
                previousScope = scoped;
                if (scoped.Echo(1) != 11 || scoped.Label != "fallback" || scoped.Dependency is not null)
                    throw new Exception("Ordinary optional constructor dependencies changed.");
                var transient = scope.ServiceProvider.GetRequiredService<DisposableService>();
                var keyedTransient = scope.ServiceProvider.GetRequiredKeyedService<DisposableService>("selected");
                if (ReferenceEquals(transient, keyedTransient) || transient.Identity() != transient.Id) throw new Exception("Transient activation was cached or lost its factory.");
                if (asynchronous) await scope.DisposeAsync(); else scope.Dispose();
                if (disposal.Cleanup.Count != (index + 1) * 2) throw new Exception("Scoped shutdown duplicated or missed transient disposal.");
            }
            var other = container.GetRequiredKeyedService<BindingService>("other");
            string? selectedIdentity = null;
            string? otherIdentity = null;
            await singleton!.ObserveAsync(session => { selectedIdentity = ((Transaction)session).Provider.TelemetryInstanceId; return Task.FromResult(0); });
            await other.ObserveAsync(session => { otherIdentity = ((Transaction)session).Provider.TelemetryInstanceId; return Task.FromResult(0); });
            if (ReferenceEquals(other, singleton) || selectedIdentity == otherIdentity || setups != 1)
                throw new Exception("Generated owning factories were not bound to their selected registration.");
            if (asynchronous) await container.DisposeAsync(); else container.Dispose();
            if (disposal.Created != 4 || disposal.Cleanup.Count != 4 || disposal.Cleanup.Select(item => item.Id).Distinct().Count() != 4 ||
                disposal.Cleanup.Any(item => item.Async != asynchronous))
                throw new Exception("Disposable generated transient cleanup was not once per activation in the selected mode.");
        }
    }
}

public sealed class ServiceDisposalCounter
{
    public int Created { get; private set; }
    public List<(int Id, bool Async)> Cleanup { get; } = new();
    public int Next() => ++Created;
}

public sealed partial class DisposableService(IDataLinqUnitOfWorkFactory<TestDb> factory, ServiceDisposalCounter counter)
    : IDataLinqService<TestDb>, IDisposable, IAsyncDisposable
{
    private ServiceDisposalCounter Counter { get; } = counter;
    public int Id { get; } = counter.Next();
    [DataLinqOperation] private int IdentityCore(IDataLinqSession<TestDb> session) => Id;
    public void Dispose() => Counter.Cleanup.Add((Id, false));
    public ValueTask DisposeAsync() { Counter.Cleanup.Add((Id, true)); return default; }
}
