using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(ServiceLifetime.Singleton)]
    [Arguments(ServiceLifetime.Scoped)]
    [Arguments(ServiceLifetime.Transient)]
    public async Task ServiceRegistration_PreservesKeysIdentityAndRequestedLifetimes(ServiceLifetime lifetime)
    {
        foreach (var defaultFirst in new[] { false, true })
        foreach (var resolveDefaultFirst in new[] { false, true })
        {
            var services = new ServiceCollection();
            var probe = new GeneratedServiceProbe();
            services.AddSingleton(probe);
            NonIdempotentRootProvider? root = null;
            var builder = services.AddDataLinq<TransactionMutationGuardDb>("selected").UseFactory(_ => new ScriptedDatabase(root = new()));
            if (defaultFirst) builder.AsDefault();
            AddService<GeneratedPlainService>(builder, lifetime);
            if (!defaultFirst) builder.AsDefault();
            await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            await Assert.That(probe.Activations).IsEqualTo(0);
            await Assert.That(root).IsNull();
            GeneratedPlainService? priorScope = null;
            for (var scopeIndex = 0; scopeIndex < 2; scopeIndex++)
            {
                await using var scope = container.CreateAsyncScope();
                var first = resolveDefaultFirst ? scope.ServiceProvider.GetRequiredService<GeneratedPlainService>()
                    : scope.ServiceProvider.GetRequiredKeyedService<GeneratedPlainService>("selected");
                var keyed = scope.ServiceProvider.GetRequiredKeyedService<GeneratedPlainService>("selected");
                var unnamed = scope.ServiceProvider.GetRequiredService<GeneratedPlainService>();
                if (lifetime == ServiceLifetime.Transient)
                {
                    await Assert.That(first).IsNotSameReferenceAs(keyed);
                    await Assert.That(keyed).IsNotSameReferenceAs(unnamed);
                }
                else
                {
                    await Assert.That(first).IsSameReferenceAs(keyed);
                    await Assert.That(keyed).IsSameReferenceAs(unnamed);
                }
                if (priorScope is not null)
                {
                    if (lifetime == ServiceLifetime.Singleton) await Assert.That(first).IsSameReferenceAs(priorScope);
                    else await Assert.That(first).IsNotSameReferenceAs(priorScope);
                }
                await Assert.That(first.Provider()).IsEqualTo(root!.TelemetryInstanceId);
                await Assert.That(root.Calls).IsEqualTo(0);
                priorScope = first;
            }
            await Assert.That(probe.Activations).IsEqualTo(lifetime == ServiceLifetime.Singleton ? 1 : lifetime == ServiceLifetime.Scoped ? 2 : 6);
            await Assert.That(root!.Calls).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(ServiceLifetime.Singleton, false)]
    [Arguments(ServiceLifetime.Singleton, true)]
    [Arguments(ServiceLifetime.Scoped, false)]
    [Arguments(ServiceLifetime.Scoped, true)]
    [Arguments(ServiceLifetime.Transient, false)]
    [Arguments(ServiceLifetime.Transient, true)]
    public async Task ServiceRegistration_DisposableActivationsReceiveOneAppropriateCleanup(ServiceLifetime lifetime, bool asynchronous)
    {
        await VerifyCleanup<GeneratedSyncService>(lifetime, asynchronous, expectedAsync: false);
        await VerifyCleanup<GeneratedDualService>(lifetime, asynchronous, expectedAsync: asynchronous);
        if (asynchronous) await VerifyCleanup<GeneratedAsyncService>(lifetime, true, expectedAsync: true);
    }

    private static async Task VerifyCleanup<TService>(ServiceLifetime lifetime, bool asynchronous, bool expectedAsync)
        where TService : class, IDataLinqService<TransactionMutationGuardDb>, IDataLinqServiceFactory<TransactionMutationGuardDb, TService>, IGeneratedTrackedService
    {
        await VerifyCleanupIdentity<TService>(lifetime, asynchronous, expectedAsync, keyed: false);
        await VerifyCleanupIdentity<TService>(lifetime, asynchronous, expectedAsync, keyed: true);
    }

    private static async Task VerifyCleanupIdentity<TService>(ServiceLifetime lifetime, bool asynchronous, bool expectedAsync, bool keyed)
        where TService : class, IDataLinqService<TransactionMutationGuardDb>, IDataLinqServiceFactory<TransactionMutationGuardDb, TService>, IGeneratedTrackedService
    {
        var services = new ServiceCollection();
        var probe = new GeneratedServiceProbe();
        services.AddSingleton(probe);
        var root = new NonIdempotentRootProvider();
        var builder = services.AddDataLinq<TransactionMutationGuardDb>(keyed ? "selected" : null).UseFactory(_ => new ScriptedDatabase(root));
        // A disposable transient has direct independent recipes for both names.
        if (lifetime == ServiceLifetime.Transient && keyed) builder.AsDefault();
        AddService<TService>(builder, lifetime);
        var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var ids = new HashSet<int>();
        for (var index = 0; index < 2; index++)
        {
            var scope = container.CreateAsyncScope();
            TService Resolve() => keyed ? scope.ServiceProvider.GetRequiredKeyedService<TService>("selected") : scope.ServiceProvider.GetRequiredService<TService>();
            ids.Add(Resolve().Id);
            ids.Add(Resolve().Id);
            if (lifetime == ServiceLifetime.Transient && keyed) ids.Add(scope.ServiceProvider.GetRequiredService<TService>().Id);
            if (asynchronous) await scope.DisposeAsync(); else scope.Dispose();
            await Assert.That(root.Calls).IsEqualTo(0);
        }
        if (lifetime == ServiceLifetime.Singleton) await Assert.That(probe.Cleanup).IsEmpty();
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(root.Calls).IsEqualTo(1);
        await Assert.That(probe.Activations).IsEqualTo(ids.Count);
        await Assert.That(probe.Cleanup.Count).IsEqualTo(ids.Count);
        await Assert.That(probe.Cleanup.Select(entry => entry.Id).Distinct().Count()).IsEqualTo(ids.Count);
        await Assert.That(probe.Cleanup.All(entry => entry.Async == expectedAsync)).IsTrue();
    }

    [Test]
    [Arguments(ServiceLifetime.Singleton, false)]
    [Arguments(ServiceLifetime.Singleton, true)]
    [Arguments(ServiceLifetime.Scoped, false)]
    [Arguments(ServiceLifetime.Scoped, true)]
    public async Task ServiceRegistration_RejectsDisposableDefaultAliasesBeforeActivationInEitherOrder(ServiceLifetime lifetime, bool defaultFirst)
    {
        await RejectAliases<GeneratedSyncService>();
        await RejectAliases<GeneratedAsyncService>();
        await RejectAliases<GeneratedDualService>();
        async Task RejectAliases<TService>() where TService : class, IDataLinqService<TransactionMutationGuardDb>, IDataLinqServiceFactory<TransactionMutationGuardDb, TService>
        {
            var services = new ServiceCollection();
            var probe = new GeneratedServiceProbe();
            services.AddSingleton(probe);
            var constructions = 0;
            var builder = services.AddDataLinq<TransactionMutationGuardDb>("selected").UseFactory(_ => { constructions++; return new ScriptedDatabase(new NonIdempotentRootProvider()); });
            if (defaultFirst)
            {
                builder.AsDefault();
                await Assert.That(() => AddService<TService>(builder, lifetime)).Throws<NotSupportedException>();
            }
            else
            {
                AddService<TService>(builder, lifetime);
                await Assert.That(() => builder.AsDefault()).Throws<NotSupportedException>();
            }
            await using var container = services.BuildServiceProvider();
            await Assert.That(constructions).IsEqualTo(0);
            await Assert.That(probe.Activations).IsEqualTo(0);
            if (!defaultFirst)
            {
                await Assert.That(container.GetService<TService>()).IsNull();
                await Assert.That(container.GetService<Database<TransactionMutationGuardDb>>()).IsNull();
                await using var scope = container.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredKeyedService<TService>("selected");
                await Assert.That(probe.Activations).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task ServiceRegistration_IndependentKeysBindFactoriesAndKeepOtherDependenciesUnkeyed()
    {
        var services = new ServiceCollection();
        var ordinary = new GeneratedServiceProbe();
        var keyed = new GeneratedServiceProbe();
        services.AddSingleton(ordinary);
        services.AddKeyedSingleton("second", keyed);
        var firstRoot = new NonIdempotentRootProvider();
        var secondRoot = new NonIdempotentRootProvider();
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseFactory(_ => new ScriptedDatabase(firstRoot)).AddSingletonService<GeneratedPlainService>();
        services.AddDataLinq<TransactionMutationGuardDb>("second").UseFactory(_ => new ScriptedDatabase(secondRoot)).AddSingletonService<GeneratedPlainService>();
        await using var container = services.BuildServiceProvider();
        await Assert.That(container.GetService<GeneratedPlainService>()).IsNull();
        var first = container.GetRequiredKeyedService<GeneratedPlainService>("first");
        var second = container.GetRequiredKeyedService<GeneratedPlainService>("second");
        await Assert.That(first.Provider()).IsEqualTo(firstRoot.TelemetryInstanceId);
        await Assert.That(second.Provider()).IsEqualTo(secondRoot.TelemetryInstanceId);
        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(ordinary.Activations).IsEqualTo(2);
        await Assert.That(keyed.Activations).IsEqualTo(0);
    }

    [Test]
    public async Task ServiceRegistration_DuplicateAndDefaultConflictsAreStructuralAndCaptiveScopesStillFail()
    {
        var services = new ServiceCollection();
        services.AddScoped<GeneratedServiceProbe>();
        var calls = 0;
        var builder = services.AddDataLinq<TransactionMutationGuardDb>("selected").UseFactory(_ => { calls++; return new ScriptedDatabase(new NonIdempotentRootProvider()); });
        builder.AddSingletonService<GeneratedPlainService>();
        await Assert.That(() => builder.AddTransientService<GeneratedPlainService>()).Throws<InvalidOperationException>();
        services.AddSingleton<GeneratedPlainService>(_ => throw new Exception("Unselected unrelated recipe."));
        await Assert.That(() => builder.AsDefault()).Throws<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(0);
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await Assert.That(() => container.GetRequiredKeyedService<GeneratedPlainService>("selected")).Throws<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    [Arguments(ServiceLifetime.Singleton)]
    [Arguments(ServiceLifetime.Scoped)]
    [Arguments(ServiceLifetime.Transient)]
    public async Task ServiceRegistration_AsyncOnlyServiceRejectsSynchronousShutdownWithoutFallback(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();
        var probe = new GeneratedServiceProbe();
        services.AddSingleton(probe);
        var root = new NonIdempotentRootProvider();
        AddService<GeneratedAsyncService>(services.AddDataLinq<TransactionMutationGuardDb>().UseFactory(_ => new ScriptedDatabase(root)), lifetime);
        var container = services.BuildServiceProvider();
        var service = container.GetRequiredService<GeneratedAsyncService>();
        try
        {
            await Assert.That(container.Dispose).Throws<InvalidOperationException>();
            await Assert.That(probe.Cleanup).IsEmpty();
        }
        finally
        {
            // DI's unsupported shutdown can stop before its other resources. This
            // fixture explicitly cleans up; registration never invents a fallback.
            await service.DisposeAsync();
            root.Dispose();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ServiceRegistration_AuthoredCleanupFailurePropagatesWithoutReplay(bool asynchronous)
    {
        var failure = new FormatException("authored service cleanup");
        var probe = new GeneratedServiceProbe { Failure = failure };
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        var root = new NonIdempotentRootProvider();
        services.AddDataLinq<TransactionMutationGuardDb>().UseFactory(_ => new ScriptedDatabase(root)).AddScopedService<GeneratedDualService>();
        var container = services.BuildServiceProvider();
        var scope = container.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<GeneratedDualService>();
        var observed = asynchronous ? await AsyncEnumerationFailureOf(() => scope.DisposeAsync().AsTask()) : Capture<FormatException>(scope.Dispose);
        await Assert.That(observed).IsSameReferenceAs(failure);
        await scope.DisposeAsync();
        await Assert.That(probe.Cleanup.Count).IsEqualTo(1);
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(root.Calls).IsEqualTo(1);
    }

    private static void AddService<TService>(DataLinqRegistrationBuilder<TransactionMutationGuardDb> builder, ServiceLifetime lifetime)
        where TService : class, IDataLinqService<TransactionMutationGuardDb>, IDataLinqServiceFactory<TransactionMutationGuardDb, TService>
    {
        if (lifetime == ServiceLifetime.Singleton) builder.AddSingletonService<TService>();
        else if (lifetime == ServiceLifetime.Scoped) builder.AddScopedService<TService>();
        else builder.AddTransientService<TService>();
    }

    [Test]
    [Arguments(false, DataLinqOwnership.Application)]
    [Arguments(true, DataLinqOwnership.Application)]
    [Arguments(false, DataLinqOwnership.Container)]
    [Arguments(true, DataLinqOwnership.Container)]
    public async Task ServiceRegistration_DependencyFailureAfterInfrastructureActivationDoesNotUndoHandoff(bool asynchronous, DataLinqOwnership ownership)
    {
        foreach (var activate in new[] { false, true })
        {
            var root = new NonIdempotentRootProvider();
            var database = new ScriptedDatabase(root);
            var services = new ServiceCollection();
            // Missing GeneratedServiceProbe fails after the selected DataLinq
            // factory/read graph was successfully activated and handed off.
            services.AddDataLinq<TransactionMutationGuardDb>().UseInstance(database, ownership).AddSingletonService<GeneratedPlainService>();
            var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
            if (activate) await Assert.That(() => container.GetRequiredService<GeneratedPlainService>()).Throws<InvalidOperationException>();
            if (asynchronous) await container.DisposeAsync(); else container.Dispose();
            var transferred = activate && ownership == DataLinqOwnership.Container;
            await Assert.That(root.Calls).IsEqualTo(transferred ? 1 : 0);
            if (!transferred) database.Dispose();
        }
    }
}

public sealed class GeneratedServiceProbe
{
    public int Activations { get; private set; }
    public List<(int Id, bool Async)> Cleanup { get; } = [];
    public Exception? Failure { get; init; }
    public int Next() => ++Activations;
    public void Record(int id, bool asynchronous) { Cleanup.Add((id, asynchronous)); if (Failure is not null) throw Failure; }
}
public interface IGeneratedTrackedService { int Id { get; } }

public sealed partial class GeneratedPlainService(IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb> factory, GeneratedServiceProbe probe)
    : IDataLinqService<TransactionMutationGuardDb>, IGeneratedTrackedService
{
    public int Id { get; } = probe.Next();
    [DataLinqOperation] private string ProviderCore(IDataLinqSession<TransactionMutationGuardDb> session) => ((Transaction)session).Provider.TelemetryInstanceId;
}

public sealed partial class GeneratedSyncService(IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb> factory, GeneratedServiceProbe probe)
    : IDataLinqService<TransactionMutationGuardDb>, IGeneratedTrackedService, IDisposable
{
    private GeneratedServiceProbe Probe { get; } = probe;
    public int Id { get; } = probe.Next();
    [DataLinqOperation] private int ValueCore(IDataLinqSession<TransactionMutationGuardDb> session) => Id;
    public void Dispose() => Probe.Record(Id, false);
}

public sealed partial class GeneratedAsyncService(IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb> factory, GeneratedServiceProbe probe)
    : IDataLinqService<TransactionMutationGuardDb>, IGeneratedTrackedService, IAsyncDisposable
{
    private GeneratedServiceProbe Probe { get; } = probe;
    public int Id { get; } = probe.Next();
    [DataLinqOperation] private int ValueCore(IDataLinqSession<TransactionMutationGuardDb> session) => Id;
    public ValueTask DisposeAsync() { Probe.Record(Id, true); return default; }
}

public sealed partial class GeneratedDualService(IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb> factory, GeneratedServiceProbe probe)
    : IDataLinqService<TransactionMutationGuardDb>, IGeneratedTrackedService, IDisposable, IAsyncDisposable
{
    private GeneratedServiceProbe Probe { get; } = probe;
    public int Id { get; } = probe.Next();
    [DataLinqOperation] private int ValueCore(IDataLinqSession<TransactionMutationGuardDb> session) => Id;
    public void Dispose() => Probe.Record(Id, false);
    public ValueTask DisposeAsync() { Probe.Record(Id, true); return default; }
}
