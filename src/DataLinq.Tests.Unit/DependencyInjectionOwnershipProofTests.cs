using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Tests.Unit;

// Consumer-shaped descriptor proofs. These intentionally precede the production
// registration API: passing identity alone does not establish ownership safety.
public sealed class DependencyInjectionOwnershipProofTests
{
    private const string Key = "primary";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BorrowedInstanceDescriptors_NeverCaptureAnyAlias(bool asynchronous)
    {
        foreach (var order in Orders())
        {
            var instance = new DualService();
            var services = new ServiceCollection();
            services.AddSingleton(instance);
            services.AddSingleton<IService>(instance);
            services.AddKeyedSingleton(Key, instance);
            services.AddKeyedSingleton<IService>(Key, instance);
            var provider = Build(services);
            await VerifyAliases(provider, instance, order);
            await Shutdown(provider, asynchronous);
            await Assert.That(instance.SyncCalls + instance.AsyncCalls).IsEqualTo(0);
            instance.EnsureUsable();
            await instance.DisposeAsync();
            await Assert.That(instance.AsyncCalls).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FactoryAliases_CaptureBorrowedInstanceDespiteNonDisposableInterface(bool asynchronous)
    {
        var instance = new DualService();
        var services = new ServiceCollection();
        services.AddSingleton(instance);
        services.AddSingleton<IService>(sp => sp.GetRequiredService<DualService>());
        services.AddKeyedSingleton<IService>(Key, (sp, _) => sp.GetRequiredService<DualService>());
        var provider = Build(services);
        await Assert.That(provider.GetRequiredService<IService>()).IsSameReferenceAs(instance);
        await Assert.That(provider.GetRequiredKeyedService<IService>(Key)).IsSameReferenceAs(instance);
        await Shutdown(provider, asynchronous);
        await Assert.That(instance.SyncCalls).IsEqualTo(asynchronous ? 0 : 2);
        await Assert.That(instance.AsyncCalls).IsEqualTo(asynchronous ? 2 : 0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LazyOwnedAliases_ActivateOnceButCaptureEachResolvedDescriptor(bool asynchronous)
    {
        foreach (var order in Orders())
        {
            var activations = 0;
            var services = new ServiceCollection();
            services.AddKeyedSingleton<DualService>(Key, (_, _) => { activations++; return new(); });
            services.AddKeyedSingleton<IService>(Key, (sp, _) => sp.GetRequiredKeyedService<DualService>(Key));
            services.AddSingleton(sp => sp.GetRequiredKeyedService<DualService>(Key));
            services.AddSingleton<IService>(sp => sp.GetRequiredKeyedService<DualService>(Key));
            var provider = Build(services);
            await Assert.That(activations).IsEqualTo(0);
            var instance = (DualService)Resolve(provider, order[0]);
            await VerifyAliases(provider, instance, order);
            await Assert.That(activations).IsEqualTo(1);
            await Shutdown(provider, asynchronous);
            await Assert.That(instance.SyncCalls).IsEqualTo(asynchronous ? 0 : 4);
            await Assert.That(instance.AsyncCalls).IsEqualTo(asynchronous ? 4 : 0);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitInstanceTransfer_IsLazyAndFailureBeforeReturnDoesNotCapture(bool asynchronous)
    {
        foreach (var order in Orders())
        {
            foreach (var activate in new[] { false, true })
            {
                var instance = new DualService();
                var fail = true;
                var activations = 0;
                var services = new ServiceCollection();
                services.AddKeyedSingleton<DualService>(Key, (_, _) =>
                {
                    if (fail) throw new InvalidOperationException("before handoff");
                    activations++;
                    return instance;
                });
                services.AddKeyedSingleton<IService>(Key, (sp, _) => sp.GetRequiredKeyedService<DualService>(Key));
                services.AddSingleton(sp => sp.GetRequiredKeyedService<DualService>(Key));
                services.AddSingleton<IService>(sp => sp.GetRequiredKeyedService<DualService>(Key));
                var provider = Build(services);
                await Assert.That(activations).IsEqualTo(0);
                if (activate)
                {
                    await Assert.That(() => Resolve(provider, order[0])).Throws<InvalidOperationException>();
                    await Assert.That(instance.SyncCalls + instance.AsyncCalls).IsEqualTo(0);
                    fail = false;
                    await VerifyAliases(provider, instance, order);
                    // A later failed activation cannot return ownership to the application.
                    fail = true;
                    await Assert.That(Resolve(provider, order[0])).IsSameReferenceAs(instance);
                }
                await Shutdown(provider, asynchronous);
                await Assert.That(activations).IsEqualTo(activate ? 1 : 0);
                await Assert.That(instance.SyncCalls + instance.AsyncCalls).IsEqualTo(activate ? 4 : 0);
                if (!activate) { instance.EnsureUsable(); instance.Dispose(); }
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SingleServiceIdentifier_PreservesAllLifetimesAndDisposalModes(bool asynchronous)
    {
        foreach (var lifetime in Enum.GetValues<ServiceLifetime>())
        foreach (var kind in new[] { "sync", "async", "dual" })
        foreach (var keyed in new[] { false, true })
        {
            var created = new List<Service>();
            Service Activate()
            {
                Service service = kind switch { "sync" => new SyncService(), "async" => new AsyncService(), _ => new DualService() };
                created.Add(service);
                return service;
            }
            IServiceCollection services = new ServiceCollection();
            services.Add(keyed
                ? new ServiceDescriptor(typeof(IService), Key, (_, _) => Activate(), lifetime)
                : new ServiceDescriptor(typeof(IService), _ => Activate(), lifetime));
            var provider = Build(services);
            await Assert.That(created.Count).IsEqualTo(0);
            IService? firstScope = null;
            for (var scopeIndex = 0; scopeIndex < 2; scopeIndex++)
            {
                var scope = provider.CreateAsyncScope();
                var first = keyed ? scope.ServiceProvider.GetRequiredKeyedService<IService>(Key) : scope.ServiceProvider.GetRequiredService<IService>();
                var second = keyed ? scope.ServiceProvider.GetRequiredKeyedService<IService>(Key) : scope.ServiceProvider.GetRequiredService<IService>();
                await Assert.That(ReferenceEquals(first, second)).IsEqualTo(lifetime != ServiceLifetime.Transient);
                if (firstScope is not null)
                    await Assert.That(ReferenceEquals(firstScope, first)).IsEqualTo(lifetime == ServiceLifetime.Singleton);
                firstScope = first;
                if (!asynchronous && kind == "async" && lifetime != ServiceLifetime.Singleton)
                    await Assert.That(scope.Dispose).Throws<InvalidOperationException>();
                else if (asynchronous) await scope.DisposeAsync();
                else scope.Dispose();
            }
            if (!asynchronous && kind == "async" && lifetime == ServiceLifetime.Singleton)
                await Assert.That(provider.Dispose).Throws<InvalidOperationException>();
            else await Shutdown(provider, asynchronous);
            await Assert.That(created.Count).IsEqualTo(lifetime switch { ServiceLifetime.Singleton => 1, ServiceLifetime.Scoped => 2, _ => 4 });
            foreach (var service in created)
            {
                await Assert.That(service.SyncCalls).IsEqualTo(kind == "sync" || (!asynchronous && kind == "dual") ? 1 : 0);
                await Assert.That(service.AsyncCalls).IsEqualTo(asynchronous && kind != "sync" ? 1 : 0);
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TransientDefaultAndKeyedFactories_CreateIndependentSinglyOwnedActivations(bool asynchronous)
    {
        var created = new List<DualService>();
        DualService Activate() { var value = new DualService(); created.Add(value); return value; }
        var services = new ServiceCollection();
        // Both descriptors invoke the recipe directly. No factory forwards a captured result.
        services.AddTransient(_ => Activate());
        services.AddKeyedTransient(Key, (_, _) => Activate());
        var provider = Build(services);
        for (var index = 0; index < 2; index++)
        {
            var scope = provider.CreateAsyncScope();
            var first = scope.ServiceProvider.GetRequiredService<DualService>();
            var second = scope.ServiceProvider.GetRequiredKeyedService<DualService>(Key);
            await Assert.That(ReferenceEquals(first, second)).IsFalse();
            if (asynchronous) await scope.DisposeAsync(); else scope.Dispose();
        }
        await Shutdown(provider, asynchronous);
        await Assert.That(created.Count).IsEqualTo(4);
        foreach (var value in created)
            await Assert.That(value.SyncCalls + value.AsyncCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(ServiceLifetime.Singleton)]
    [Arguments(ServiceLifetime.Scoped)]
    public async Task NonDisposableDefaultAndKeyedAliases_ShareLifetime(ServiceLifetime lifetime)
    {
        var created = 0;
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(Service), Key, (_, _) => { created++; return new Service(); }, lifetime));
        services.Add(new ServiceDescriptor(typeof(Service), sp => sp.GetRequiredKeyedService<Service>(Key), lifetime));
        await using var provider = Build(services);
        Service? previous = null;
        for (var index = 0; index < 2; index++)
        {
            await using var scope = provider.CreateAsyncScope();
            var keyed = scope.ServiceProvider.GetRequiredKeyedService<Service>(Key);
            await Assert.That(scope.ServiceProvider.GetRequiredService<Service>()).IsSameReferenceAs(keyed);
            if (previous is not null)
                await Assert.That(ReferenceEquals(previous, keyed)).IsEqualTo(lifetime == ServiceLifetime.Singleton);
            previous = keyed;
        }
        await Assert.That(created).IsEqualTo(lifetime == ServiceLifetime.Singleton ? 1 : 2);
    }

    private static ServiceProvider Build(IServiceCollection services) => services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,
        ValidateScopes = true
    });

    private static async Task VerifyAliases(ServiceProvider provider, DualService instance, int[] order)
    {
        for (var scopeIndex = 0; scopeIndex < 2; scopeIndex++)
        {
            using var scope = provider.CreateScope();
            foreach (var alias in order.Concat(order))
                await Assert.That(Resolve(scope.ServiceProvider, alias)).IsSameReferenceAs(instance);
        }
        await Assert.That(instance.SyncCalls + instance.AsyncCalls).IsEqualTo(0);
    }

    private static object Resolve(IServiceProvider provider, int alias) => alias switch
    {
        0 => provider.GetRequiredService<DualService>(),
        1 => provider.GetRequiredService<IService>(),
        2 => provider.GetRequiredKeyedService<DualService>(Key),
        _ => provider.GetRequiredKeyedService<IService>(Key)
    };

    private static IEnumerable<int[]> Orders()
    {
        for (var first = 0; first < 4; first++)
        for (var second = 0; second < 4; second++)
        for (var third = 0; third < 4; third++)
        for (var fourth = 0; fourth < 4; fourth++)
        {
            int[] order = [first, second, third, fourth];
            if (order.Distinct().Count() == 4) yield return order;
        }
    }

    private static async Task Shutdown(ServiceProvider provider, bool asynchronous)
    {
        if (asynchronous) await provider.DisposeAsync(); else provider.Dispose();
    }

    private interface IService;
    private class Service : IService
    {
        public int SyncCalls { get; protected set; }
        public int AsyncCalls { get; protected set; }
        public void EnsureUsable() => ObjectDisposedException.ThrowIf(SyncCalls + AsyncCalls != 0, this);
    }
    private sealed class SyncService : Service, IDisposable { public void Dispose() => SyncCalls++; }
    private sealed class AsyncService : Service, IAsyncDisposable
    {
        public ValueTask DisposeAsync() { AsyncCalls++; return ValueTask.CompletedTask; }
    }
    private sealed class DualService : Service, IDisposable, IAsyncDisposable
    {
        public void Dispose() => SyncCalls++;
        public ValueTask DisposeAsync() { AsyncCalls++; return ValueTask.CompletedTask; }
    }
}
