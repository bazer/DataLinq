using System;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Mutation;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ContainerOwnership_CustomProviderCleanupRunsOnceAcrossAliasesAndWrappers(bool asynchronous, bool fail)
    {
        var expected = fail ? new Exception("custom root cleanup") : null;
        var provider = new NonIdempotentRootProvider { Failure = expected };
        var first = new ScriptedDatabase(provider);
        var second = new ScriptedDatabase(provider);
        var activations = 0;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ScriptedDatabase>("primary", (_, _) =>
        {
            activations++;
            first.ValidateContainerOwnership();
            first.EnableContainerOwnership();
            return first;
        });
        services.AddSingleton(sp => sp.GetRequiredKeyedService<ScriptedDatabase>("primary"));
        services.AddKeyedSingleton<Database<TransactionMutationGuardDb>>("primary", (sp, _) => sp.GetRequiredService<ScriptedDatabase>());
        services.AddSingleton<Database<TransactionMutationGuardDb>>(sp => sp.GetRequiredService<ScriptedDatabase>());
        services.AddSingleton<ReadOnlyAccess<TransactionMutationGuardDb>>(sp => sp.GetRequiredService<ScriptedDatabase>().Provider.ReadOnlyAccess);
        services.AddSingleton(sp => sp.GetRequiredService<ReadOnlyAccess<TransactionMutationGuardDb>>().Query());
        var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await Assert.That(activations).IsEqualTo(0);
        using (var scope = container.CreateScope())
        {
            await Assert.That(scope.ServiceProvider.GetRequiredService<TransactionMutationGuardDb>()).IsSameReferenceAs(first.Query());
            await Assert.That(scope.ServiceProvider.GetRequiredKeyedService<Database<TransactionMutationGuardDb>>("primary")).IsSameReferenceAs(first);
            await Assert.That(scope.ServiceProvider.GetRequiredService<Database<TransactionMutationGuardDb>>()).IsSameReferenceAs(first);
        }
        await Assert.That(activations).IsEqualTo(1);
        await Assert.That(provider.Calls).IsEqualTo(0);
        // Separate wrappers must not manufacture a second cleanup owner for one provider.
        second.EnableContainerOwnership();
        Exception? actual = null;
        try { if (asynchronous) await container.DisposeAsync(); else container.Dispose(); }
        catch (Exception failure) { actual = failure; }
        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(provider.Calls).IsEqualTo(1);
        await Assert.That(provider.LastWasAsync).IsEqualTo(asynchronous);
        first.Dispose();
        await first.DisposeAsync();
        second.Dispose();
        await second.DisposeAsync();
        await Assert.That(provider.Calls).IsEqualTo(1);
        if (fail)
        {
            var context = DataLinqFailure.GetContext(actual!)!;
            await Assert.That(context.Stage).IsEqualTo(DataLinqFailureStage.Cleanup);
            await Assert.That(context.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.NotApplicable);
            await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ContainerOwnership_NeverResolvedOrRejectedActivationDoesNotTransferInstance(bool asynchronous, bool resolve)
    {
        var provider = new NonIdempotentRootProvider();
        var database = new ScriptedDatabase(provider);
        var services = new ServiceCollection();
        services.AddSingleton<ScriptedDatabase>(_ =>
        {
            database.ValidateContainerOwnership();
            throw new InvalidOperationException("registration validation failed before handoff");
        });
        services.AddSingleton<Database<TransactionMutationGuardDb>>(sp => sp.GetRequiredService<ScriptedDatabase>());
        var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        if (resolve)
            await Assert.That(() => container.GetRequiredService<Database<TransactionMutationGuardDb>>()).Throws<InvalidOperationException>();
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(provider.Calls).IsEqualTo(0);
        await Assert.That(database.Query()).IsSameReferenceAs(database.Provider.ReadOnlyAccess.Query());
        database.Dispose();
        await Assert.That(provider.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task ContainerOwnership_LegacyAsyncFailureLeavesExplicitSyncCleanupAvailable()
    {
        var provider = new LegacyRootTestProvider();
        var database = new ScriptedDatabase(provider);
        database.EnableContainerOwnership();
        var failure = await AsyncEnumerationFailureOf(() => database.DisposeAsync().AsTask());
        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(provider.Disposals).IsEqualTo(0);
        database.Dispose();
        database.Dispose();
        await database.DisposeAsync();
        await Assert.That(provider.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ContainerOwnership_RejectsCustomDatabaseDisposalButBorrowedDescriptorsRemainSafe(bool asynchronous)
    {
        var provider = new NonIdempotentRootProvider();
        var database = new CustomDisposalDatabase(provider);
        await Assert.That(database.ValidateContainerOwnership).Throws<NotSupportedException>();
        await Assert.That(database.EnableContainerOwnership).Throws<NotSupportedException>();
        var services = new ServiceCollection();
        services.AddSingleton(database);
        services.AddSingleton<Database<TransactionMutationGuardDb>>(database);
        services.AddKeyedSingleton<Database<TransactionMutationGuardDb>>("borrowed", database);
        var container = services.BuildServiceProvider();
        await Assert.That(container.GetRequiredService<CustomDisposalDatabase>()).IsSameReferenceAs(database);
        await Assert.That(container.GetRequiredService<Database<TransactionMutationGuardDb>>()).IsSameReferenceAs(database);
        await Assert.That(container.GetRequiredKeyedService<Database<TransactionMutationGuardDb>>("borrowed")).IsSameReferenceAs(database);
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(database.Calls).IsEqualTo(0);
        await Assert.That(provider.Calls).IsEqualTo(0);
        ((IDisposable)database).Dispose();
        await Assert.That(database.Calls).IsEqualTo(1);
        await Assert.That(provider.Calls).IsEqualTo(1);
    }

    private class NonIdempotentRootProvider() : ScriptedMutationProvider<TransactionMutationGuardDb>(new())
    {
        internal int Calls { get; private set; }
        internal bool LastWasAsync { get; private set; }
        internal Exception? Failure { get; init; }
        public override void Dispose()
        {
            Calls++;
            base.Dispose();
            if (Failure is not null) throw Failure;
        }
        public override async ValueTask DisposeAsync()
        {
            Calls++;
            LastWasAsync = true;
            await State.DisposeAsyncCore();
            if (Failure is not null) throw Failure;
        }
    }

    private sealed class CustomDisposalDatabase(NonIdempotentRootProvider provider)
        : Database<TransactionMutationGuardDb>(provider), IDisposable, IAsyncDisposable
    {
        internal int Calls { get; private set; }
        void IDisposable.Dispose() { Calls++; base.Dispose(); }
        ValueTask IAsyncDisposable.DisposeAsync() { Calls++; return base.DisposeAsync(); }
    }
}
