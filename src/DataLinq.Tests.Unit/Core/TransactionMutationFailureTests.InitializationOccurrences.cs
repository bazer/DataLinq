using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(false, false, true)]
    [Arguments(false, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, false, false)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    [Arguments(true, true, true)]
    public async Task SettledOccurrences_InitializationKeepsPrimaryBeforeCleanup(bool asynchronous, bool sameException, bool freshReport)
    {
        var primary = new Exception("initialization");
        var cleanup = sameException ? primary : new Exception("cleanup");
        var oldSecondary = new Exception("old nested cleanup");
        var freshSecondary = new Exception("current nested cleanup");
        ExecutionFailureContext? earlier = null;
        ExecutionFailureContext? work = null;
        var resource = new ControlledTransactionResource();
        resource.Initialized = () =>
        {
            using (ExecutionFailureScope.Begin())
            {
                earlier = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                    ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
                    [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, oldSecondary)],
                    operation: ExecutionOperationKind.Commit);
                ExecutionFailureContexts.Attach(cleanup, earlier);
            }
            work = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [],
                operation: ExecutionOperationKind.Query, providerInstanceId: "initialization-provider");
            ExecutionFailureContexts.Attach(primary, work);
            throw primary;
        };
        void ReportCleanup()
        {
            if (!freshReport) return;
            ExecutionFailureContexts.Attach(cleanup, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null,
                [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, freshSecondary, ExecutionOperationKind.Dispose)],
                operation: ExecutionOperationKind.Rollback));
        }
        resource.SyncCleanupFailure = cleanup;
        resource.SyncDisposing = ReportCleanup;
        resource.Cleanup = new(paused: true) { ReportingFailure = _ => ReportCleanup() };
        resource.Cleanup.Fail(cleanup);
        var gate = new TransactionOperationGate(73, "initialization-provider");
        using var owner = gate.Enter("initialize", operationKind: ExecutionOperationKind.Save);
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            if (asynchronous) await lazy.GetOrInitializeAsync(owner, default);
            else lazy.GetOrInitialize(owner);
        });
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(failure.StackTrace!).Contains(asynchronous ? "ControlledTransactionResource.InitializeAsync" : "ControlledTransactionResource.Initialize");
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.ProviderInstanceId).IsEqualTo("initialization-provider");
        await Assert.That(context.TransactionId).IsEqualTo((uint?)73);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo((sameException ? 0 : 1) + (freshReport ? 1 : 0));
        if (!sameException)
        {
            var secondary = context.SecondaryFailures[0];
            await Assert.That(secondary.Exception).IsSameReferenceAs(cleanup);
            await Assert.That(secondary.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
            await Assert.That(secondary.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
            await Assert.That(secondary.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Dispose);
        }
        if (freshReport) await Assert.That(context.SecondaryFailures[^1].Exception).IsSameReferenceAs(freshSecondary);
        await Assert.That(work!.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(work.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(earlier!.SecondaryFailures.Single().Exception).IsSameReferenceAs(oldSecondary);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
        await Assert.That(lazy.PublishedResource).IsNull();
        await Assert.That(lazy.Failure!.Cause).IsSameReferenceAs(primary);
        await Assert.That(lazy.Failure.CleanupFailure).IsSameReferenceAs(cleanup);
        _ = Capture<InvalidOperationException>(() => lazy.GetOrInitialize(owner));
        // A failed initialization cleanup deliberately retains the resource for later
        // disposal. Diagnostics must not silently turn that existing policy into a leak.
        resource.SyncCleanupFailure = null;
        resource.SyncDisposing = null;
        resource.Cleanup = new();
        if (asynchronous) { await lazy.DisposeAsync(owner); await lazy.DisposeAsync(owner); }
        else { lazy.Dispose(owner); lazy.Dispose(owner); }
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Disposed);
        await Assert.That(resource.Calls.Count(x => x == (asynchronous ? "async-open" : "sync-initialize"))).IsEqualTo(1);
        await Assert.That(resource.Calls.Count(x => x == (asynchronous ? "async-dispose" : "sync-dispose"))).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SettledOccurrences_InitializationCapturesOwnerIdentity(bool asynchronous)
    {
        var expected = new Exception("initialization");
        var resource = new ControlledTransactionResource { Initialized = () => throw expected };
        var gate = new TransactionOperationGate(74, "owner-provider");
        using var owner = gate.Enter("initialize", operationKind: ExecutionOperationKind.Save);
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        var failure = await AsyncEnumerationFailureOf(async () =>
        {
            if (asynchronous) await lazy.GetOrInitializeAsync(owner, default);
            else lazy.GetOrInitialize(owner);
        });
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.ProviderInstanceId).IsEqualTo("owner-provider");
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)74);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        if (asynchronous) await lazy.DisposeAsync(owner);
        else lazy.Dispose(owner);
        await Assert.That(resource.Calls.Count(x => x == (asynchronous ? "async-dispose" : "sync-dispose"))).IsEqualTo(1);
    }
}
