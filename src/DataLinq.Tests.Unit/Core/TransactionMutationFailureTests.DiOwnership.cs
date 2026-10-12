using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Unit.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task DiOwnership_RootAliasesSeparateDisposalInvocationsFromCleanup(bool asynchronous, bool failCleanup)
    {
        var cleanup = new List<string>();
        var expected = failCleanup ? new InvalidOperationException("root cleanup") : null;
        var root = new OwnedRootTestProvider(new(), cleanup, expected);
        var database = new DiTrackingDatabase(root);
        var activations = 0;
        var services = new ServiceCollection();
        services.AddKeyedSingleton<DiTrackingDatabase>("primary", (_, _) => { activations++; return database; });
        services.AddKeyedSingleton<Database<TransactionMutationGuardDb>>("primary",
            (sp, _) => sp.GetRequiredKeyedService<DiTrackingDatabase>("primary"));
        services.AddSingleton(sp => sp.GetRequiredKeyedService<DiTrackingDatabase>("primary"));
        services.AddSingleton<Database<TransactionMutationGuardDb>>(sp => sp.GetRequiredKeyedService<DiTrackingDatabase>("primary"));
        var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await Assert.That(activations).IsEqualTo(0);
        for (var index = 0; index < 2; index++)
        {
            using var scope = container.CreateScope();
            await Assert.That(scope.ServiceProvider.GetRequiredService<Database<TransactionMutationGuardDb>>()).IsSameReferenceAs(database);
            await Assert.That(scope.ServiceProvider.GetRequiredService<DiTrackingDatabase>()).IsSameReferenceAs(database);
            await Assert.That(scope.ServiceProvider.GetRequiredKeyedService<Database<TransactionMutationGuardDb>>("primary")).IsSameReferenceAs(database);
            await Assert.That(scope.ServiceProvider.GetRequiredKeyedService<DiTrackingDatabase>("primary")).IsSameReferenceAs(database);
        }
        await Assert.That(activations).IsEqualTo(1);
        await Assert.That(database.DisposalCalls).IsEqualTo(0);
        Exception? failure = null;
        try { if (asynchronous) await container.DisposeAsync(); else container.Dispose(); }
        catch (Exception caught) { failure = caught; }
        await Assert.That(failure).IsSameReferenceAs(expected);
        // Microsoft DI stops at the first throwing cleanup. It does not promise to
        // invoke the remaining aliases or other services after shutdown fails.
        await Assert.That(database.DisposalCalls).IsEqualTo(failCleanup ? 1 : 4);
        await Assert.That(cleanup.SequenceEqual(asynchronous
            ? new[] { "state:async", "resource:async" } : ["state:sync", "resource:sync"])).IsTrue();
        await Assert.That(root.Lifetime.IsDisposed).IsTrue();
        if (failure is not null)
            await Assert.That(DataLinq.Diagnostics.DataLinqFailure.GetContext(failure)!.Stage)
                .IsEqualTo(DataLinq.Diagnostics.DataLinqFailureStage.Cleanup);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DiOwnership_FailedInstanceActivationLeavesCallerOwnership(bool asynchronous)
    {
        var cleanup = new List<string>();
        var root = new OwnedRootTestProvider(new(), cleanup);
        var database = new DiTrackingDatabase(root);
        var services = new ServiceCollection();
        services.AddSingleton<Database<TransactionMutationGuardDb>>(_ => throw new InvalidOperationException("invalid activation"));
        var container = services.BuildServiceProvider();
        await Assert.That(() => container.GetRequiredService<Database<TransactionMutationGuardDb>>()).Throws<InvalidOperationException>();
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        await Assert.That(database.DisposalCalls).IsEqualTo(0);
        root.Lifetime.EnsureUsable();
        database.Query();
        await database.DisposeAsync();
        await Assert.That(cleanup.Count).IsEqualTo(2);
    }

    private sealed class DiTrackingDatabase(OwnedRootTestProvider provider)
        : Database<TransactionMutationGuardDb>(provider), IDisposable, IAsyncDisposable
    {
        internal int DisposalCalls { get; private set; }
        void IDisposable.Dispose() { DisposalCalls++; base.Dispose(); }
        ValueTask IAsyncDisposable.DisposeAsync() { DisposalCalls++; return base.DisposeAsync(); }
    }
}
