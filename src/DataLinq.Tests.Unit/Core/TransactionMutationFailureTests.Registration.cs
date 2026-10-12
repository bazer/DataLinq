using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, "factory")]
    [Arguments(true, "factory")]
    [Arguments(false, "borrowed")]
    [Arguments(true, "borrowed")]
    [Arguments(false, "transferred")]
    [Arguments(true, "transferred")]
    public async Task Registration_AllFirstAliasesPreserveIdentityActivationAndOwnership(bool asynchronous, string source)
    {
        var types = new[] { typeof(ScriptedDatabase), typeof(Database<TransactionMutationGuardDb>), typeof(ReadOnlyAccess<TransactionMutationGuardDb>),
            typeof(TransactionMutationGuardDb), typeof(IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>), typeof(DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>) };
        var identifiers = new List<(Type Type, object? Key)>();
        foreach (var type in types) { identifiers.Add((type, null)); identifiers.Add((type, "primary")); }
        for (var firstAlias = 0; firstAlias < identifiers.Count; firstAlias++)
        {
            var calls = 0;
            NonIdempotentRootProvider? root = source == "factory" ? null : new();
            ScriptedDatabase? database = root is null ? null : new(root);
            var services = new ServiceCollection();
            var registration = services.AddDataLinq<TransactionMutationGuardDb>("primary");
            if (firstAlias % 2 == 0) registration.AsDefault();
            if (source == "factory") registration.UseFactory(_ => { calls++; root = new(); return database = new(root); });
            else registration.UseInstance(database!, source == "borrowed" ? DataLinqOwnership.Application : DataLinqOwnership.Container);
            if (firstAlias % 2 != 0) registration.AsDefault();
            var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            await Assert.That(calls).IsEqualTo(0);
            await Assert.That(root?.Calls ?? 0).IsEqualTo(0);
            object Resolve(IServiceProvider provider, (Type Type, object? Key) identifier) => identifier.Key is null
                ? provider.GetRequiredService(identifier.Type) : provider.GetRequiredKeyedService(identifier.Type, identifier.Key);
            using (var scope = container.CreateScope())
            {
                Resolve(scope.ServiceProvider, identifiers[firstAlias]);
                var factory = scope.ServiceProvider.GetRequiredService<IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>>();
                foreach (var identifier in identifiers)
                {
                    var resolved = Resolve(scope.ServiceProvider, identifier);
                    object expected = identifier.Type == typeof(TransactionMutationGuardDb) ? database!.Query()
                        : identifier.Type == typeof(ReadOnlyAccess<TransactionMutationGuardDb>) ? database!.Provider.ReadOnlyAccess
                        : identifier.Type == typeof(IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>) || identifier.Type == typeof(DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>)
                            ? factory : database!;
                    await Assert.That(resolved).IsSameReferenceAs(expected);
                    await Assert.That(Resolve(scope.ServiceProvider, identifier)).IsSameReferenceAs(resolved);
                }
                using var owner = factory.Create();
                await Assert.That(((Transaction)owner).Provider).IsSameReferenceAs(root);
                owner.Rollback();
            }
            await Assert.That(calls).IsEqualTo(source == "factory" ? 1 : 0);
            await Assert.That(root!.Calls).IsEqualTo(0);
            await Assert.That(() => registration.UseFactory(_ => database!)).Throws<InvalidOperationException>();
            if (asynchronous) await container.DisposeAsync(); else container.Dispose();
            await Assert.That(root.Calls).IsEqualTo(source == "borrowed" ? 0 : 1);
            if (source == "borrowed")
            {
                await Assert.That(database!.Query()).IsSameReferenceAs(database.Provider.ReadOnlyAccess.Query());
                database.Dispose();
                await Assert.That(root.Calls).IsEqualTo(1);
            }
            else
            {
                await database!.DisposeAsync();
                database.Dispose();
                await Assert.That(root.Calls).IsEqualTo(1);
            }
        }
    }

    [Test]
    public async Task Registration_KeysAreIndependentAndNeverImplicitlySelectADefault()
    {
        var services = new ServiceCollection();
        var calls = 0;
        services.AddDataLinq<TransactionMutationGuardDb>(1).UseFactory(_ => { calls++; return new ScriptedDatabase(new NonIdempotentRootProvider()); });
        services.AddDataLinq<TransactionMutationGuardDb>(2).UseFactory(_ => { calls++; return new ScriptedDatabase(new NonIdempotentRootProvider()); });
        await using var container = services.BuildServiceProvider();
        await Assert.That(() => container.GetRequiredService<TransactionMutationGuardDb>()).Throws<InvalidOperationException>();
        await Assert.That(() => container.GetRequiredKeyedService<TransactionMutationGuardDb>(3)).Throws<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(0);
        var first = container.GetRequiredKeyedService<Database<TransactionMutationGuardDb>>(1);
        var second = container.GetRequiredKeyedService<Database<TransactionMutationGuardDb>>(2);
        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(first.Provider.State).IsNotSameReferenceAs(second.Provider.State);
        await Assert.That(first.Query()).IsNotSameReferenceAs(second.Query());
        await Assert.That(calls).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Registration_AnIndividualReadOrFactoryAliasStillOwnsTheRoot(bool asynchronous)
    {
        foreach (var type in new[] { typeof(TransactionMutationGuardDb), typeof(ReadOnlyAccess<TransactionMutationGuardDb>),
            typeof(IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>), typeof(DataLinqUnitOfWorkFactory<TransactionMutationGuardDb>),
            typeof(Database<TransactionMutationGuardDb>), typeof(ScriptedDatabase) })
        foreach (var key in new object?[] { null, "owned" })
        {
            var root = new NonIdempotentRootProvider();
            var database = new ScriptedDatabase(root);
            var services = new ServiceCollection();
            services.AddDataLinq<TransactionMutationGuardDb>("owned").AsDefault().UseInstance(database, DataLinqOwnership.Container);
            var container = services.BuildServiceProvider();
            using (var scope = container.CreateScope())
            {
                _ = key is null ? scope.ServiceProvider.GetRequiredService(type) : scope.ServiceProvider.GetRequiredKeyedService(type, key);
            }
            await Assert.That(root.Calls).IsEqualTo(0);
            // Failure after the handoff cannot return disposal responsibility to the application.
            await Assert.That(() => container.GetRequiredKeyedService<TransactionMutationGuardDb>("missing")).Throws<InvalidOperationException>();
            if (asynchronous) await container.DisposeAsync(); else container.Dispose();
            await Assert.That(root.Calls).IsEqualTo(1);
            database.Dispose();
            await Assert.That(root.Calls).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Registration_StructuralConflictsFailWithoutConstructingAProvider()
    {
        var services = new ServiceCollection();
        var named = services.AddDataLinq<TransactionMutationGuardDb>("named");
        await Assert.That(() => services.AddDataLinq<TransactionMutationGuardDb>("named")).Throws<InvalidOperationException>();
        named.AsDefault();
        await Assert.That(() => services.AddDataLinq<TransactionMutationGuardDb>()).Throws<InvalidOperationException>();
        var other = services.AddDataLinq<TransactionMutationGuardDb>("other");
        await Assert.That(other.AsDefault).Throws<InvalidOperationException>();
        await Assert.That(() => services.AddDataLinq<TransactionMutationGuardDb>(KeyedService.AnyKey)).Throws<ArgumentException>();
        var calls = 0;
        named.UseFactory(_ => { calls++; return new ScriptedDatabase(new NonIdempotentRootProvider()); });
        await Assert.That(() => named.UseFactory(_ => new ScriptedDatabase(new NonIdempotentRootProvider()))).Throws<InvalidOperationException>();
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        await Assert.That(() => container.GetRequiredKeyedService<TransactionMutationGuardDb>("other")).Throws<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    public async Task Registration_ExistingBaseTypedInstanceExposesItsKnownConcreteType()
    {
        var root = new NonIdempotentRootProvider();
        using Database<TransactionMutationGuardDb> database = new ScriptedDatabase(root);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>().UseInstance(database);
        using var container = services.BuildServiceProvider();
        await Assert.That(container.GetRequiredService<ScriptedDatabase>()).IsSameReferenceAs(database);
    }

    [Test]
    [Arguments(DataLinqOwnership.Application)]
    [Arguments(DataLinqOwnership.Container)]
    public async Task Registration_ConflictingInstanceOwnershipIsRejectedInEitherOrder(DataLinqOwnership firstOwnership)
    {
        var root = new NonIdempotentRootProvider();
        using var first = new ScriptedDatabase(root);
        var second = new ScriptedDatabase(root);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("first").UseInstance(first, firstOwnership);
        var registration = services.AddDataLinq<TransactionMutationGuardDb>("second");
        await Assert.That(() => registration.UseInstance(second, firstOwnership == DataLinqOwnership.Application
            ? DataLinqOwnership.Container : DataLinqOwnership.Application)).Throws<InvalidOperationException>();
        using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        await Assert.That(root.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task Registration_FactoryCannotTakeOwnershipOfAnAlreadyBorrowedProvider()
    {
        var root = new NonIdempotentRootProvider();
        using var database = new ScriptedDatabase(root);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("borrowed").UseInstance(database);
        services.AddDataLinq<TransactionMutationGuardDb>("invalid-owner").UseFactory(_ => new ScriptedDatabase(root));
        var container = services.BuildServiceProvider();
        await Assert.That(() => container.GetRequiredKeyedService<TransactionMutationGuardDb>("invalid-owner")).Throws<InvalidOperationException>();
        await container.DisposeAsync();
        await Assert.That(root.Calls).IsEqualTo(0);
        await Assert.That(database.Query()).IsSameReferenceAs(database.Provider.ReadOnlyAccess.Query());
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Registration_FailedReadActivationPreservesInstanceHandoffAndCleansOwnedFactoryResults(bool asynchronous, bool existing)
    {
        var root = new DisposableAccessRootProvider();
        var database = new ScriptedDatabase(root);
        var services = new ServiceCollection();
        var registration = services.AddDataLinq<TransactionMutationGuardDb>("test").AsDefault();
        if (existing) registration.UseInstance(database, DataLinqOwnership.Container);
        else registration.UseFactory(_ => database);
        var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        await Assert.That(root.Calls).IsEqualTo(0);
        await Assert.That(() => container.GetRequiredService<IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>>()).Throws<NotSupportedException>();
        await Assert.That(() => container.GetRequiredKeyedService<ScriptedDatabase>("test")).Throws<NotSupportedException>();
        await Assert.That(root.Calls).IsEqualTo(existing ? 0 : 1);
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(root.Calls).IsEqualTo(existing ? 0 : 1);
        await Assert.That(root.DisposableAccess.Calls).IsEqualTo(0);
        if (existing) database.Dispose();
        await Assert.That(root.Calls).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Registration_BorrowedCustomDisposableReadAccessIsNeverCaptured(bool asynchronous)
    {
        var root = new DisposableAccessRootProvider();
        var database = new ScriptedDatabase(root);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>("borrowed").UseInstance(database).AsDefault();
        var container = services.BuildServiceProvider();
        await Assert.That(container.GetRequiredService<ReadOnlyAccess<TransactionMutationGuardDb>>()).IsSameReferenceAs(root.DisposableAccess);
        await Assert.That(container.GetRequiredKeyedService<ReadOnlyAccess<TransactionMutationGuardDb>>("borrowed")).IsSameReferenceAs(root.DisposableAccess);
        _ = container.GetRequiredService<IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>>();
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(root.Calls).IsEqualTo(0);
        await Assert.That(root.DisposableAccess.Calls).IsEqualTo(0);
        root.DisposableAccess.Dispose();
        database.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Registration_NeverResolvedTransferRemainsApplicationOwned(bool asynchronous)
    {
        var root = new NonIdempotentRootProvider();
        var database = new ScriptedDatabase(root);
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>().UseInstance(database, DataLinqOwnership.Container);
        var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(root.Calls).IsEqualTo(0);
        database.Dispose();
        await Assert.That(root.Calls).IsEqualTo(1);
    }

    private sealed class DisposableAccessRootProvider : NonIdempotentRootProvider
    {
        internal DisposableReadAccess DisposableAccess { get; }
        internal DisposableAccessRootProvider() => ReadOnlyAccess = DisposableAccess = new(this);
    }

    private sealed class DisposableReadAccess(DatabaseProvider<TransactionMutationGuardDb> provider)
        : ReadOnlyAccess<TransactionMutationGuardDb>(provider), IDisposable, IAsyncDisposable
    {
        internal int Calls { get; private set; }
        public void Dispose() => Calls++;
        public ValueTask DisposeAsync() { Calls++; return default; }
    }
}
