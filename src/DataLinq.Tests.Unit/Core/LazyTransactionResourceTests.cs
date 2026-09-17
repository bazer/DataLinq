using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class LazyTransactionResourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task UnusedAndPreCanceledInitialization_DoNoWork_AndRemainReusable()
    {
        var gate = new TransactionOperationGate(1);
        var resource = new ControlledTransactionResource();
        var creates = 0;
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => { creates++; return resource; });
        using var owner = gate.Enter("first read");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var failure = await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, canceled.Token));
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Unused);
        await Assert.That(lazy.PublishedResource).IsNull();
        await Assert.That(creates).IsEqualTo(0);
        await Assert.That(resource.Calls).IsEmpty();

        await Assert.That(await lazy.GetOrInitializeAsync(owner, CancellationToken.None)).IsSameReferenceAs(resource);
        await Assert.That(creates).IsEqualTo(1);
        await lazy.DisposeAsync(owner);
    }

    [Test]
    [Arguments("open")]
    [Arguments("configure")]
    [Arguments("begin")]
    public async Task ResourcesRemainPrivateUntilEveryInitializationStageSucceeds(string stage)
    {
        var gate = new TransactionOperationGate(12);
        var resource = new ControlledTransactionResource();
        var checkpoint = Pause(resource, stage);
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        using var owner = gate.Enter("first query");
        var pending = lazy.GetOrInitializeAsync(owner, CancellationToken.None);
        await checkpoint.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Initializing);
            await Assert.That(lazy.PublishedResource).IsNull();
            await Assert.That(await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, CancellationToken.None)))
                .IsTypeOf<InvalidOperationException>();
            await Assert.That(await CaptureAsync(() => lazy.DisposeAsync(owner).AsTask()))
                .IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(owner.Dispose)).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(() => gate.Enter("second query"))).IsTypeOf<InvalidOperationException>();
        }
        finally { checkpoint.Release(); await pending.WaitAsync(Timeout); }

        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        await Assert.That(lazy.PublishedResource).IsSameReferenceAs(resource);
        await Assert.That(await lazy.GetOrInitializeAsync(owner, CancellationToken.None)).IsSameReferenceAs(resource);
        await Assert.That(resource.Calls.ToArray()).IsEquivalentTo(new[] { "async-open", "async-configure", "async-begin" });
        await lazy.DisposeAsync(owner);
    }

    [Test]
    [Arguments("open", false)]
    [Arguments("configure", false)]
    [Arguments("begin", false)]
    [Arguments("open", true)]
    [Arguments("configure", true)]
    [Arguments("begin", true)]
    public async Task InterruptedInitialization_IsTerminal_AndRetainsOwnershipUntilCleanup(string stage, bool cancel)
    {
        var gate = new TransactionOperationGate(5);
        var resource = new ControlledTransactionResource { Cleanup = new AsyncCheckpoint(paused: true) };
        var checkpoint = Pause(resource, stage);
        var creates = 0;
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => { creates++; return resource; });
        using var owner = gate.Enter("first read");
        using var cancellation = new CancellationTokenSource();
        var expected = new InvalidOperationException("initialization failed");
        var pending = lazy.GetOrInitializeAsync(owner, cancellation.Token);
        await checkpoint.Entered.WaitAsync(Timeout);
        if (cancel) cancellation.Cancel();
        else checkpoint.Fail(expected);
        await resource.Cleanup.Entered.WaitAsync(Timeout);
        Exception failure;
        try
        {
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(lazy.PublishedResource).IsNull();
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(resource.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
            await Assert.That(Capture(owner.Dispose)).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(() => gate.Enter("dispose transaction"))).IsTypeOf<InvalidOperationException>();
        }
        finally { resource.Cleanup.Release(); failure = await CaptureAsync(() => pending); }

        if (cancel)
        {
            await Assert.That(failure is OperationCanceledException).IsTrue();
            await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        }
        else await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(lazy.Failure!.Cause).IsSameReferenceAs(failure);
        await Assert.That(lazy.Failure.CleanupFailure).IsNull();
        // State validation wins even if a later caller supplies a canceled token.
        cancellation.Cancel();
        var rejected = await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, cancellation.Token));
        await Assert.That(rejected).IsTypeOf<InvalidOperationException>();
        await Assert.That(rejected.InnerException).IsSameReferenceAs(failure);
        await Assert.That(Capture(() => lazy.GetOrInitialize(owner))).IsTypeOf<InvalidOperationException>();
        await Assert.That(creates).IsEqualTo(1);
        await lazy.DisposeAsync(owner);
        await Assert.That(resource.Calls.Count(x => x == "async-dispose")).IsEqualTo(1);
    }

    [Test]
    public async Task SuccessfulInitializationWithLateCancellation_IsReady_ButNextBoundaryHonorsCancellation()
    {
        var gate = new TransactionOperationGate(3);
        using var cancellation = new CancellationTokenSource();
        var resource = new ControlledTransactionResource { Initialized = cancellation.Cancel };
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        using var owner = gate.Enter("query");

        await Assert.That(await lazy.GetOrInitializeAsync(owner, cancellation.Token)).IsSameReferenceAs(resource);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
        var preCommand = await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, cancellation.Token));
        await Assert.That(preCommand is OperationCanceledException).IsTrue();
        await Assert.That(lazy.Failure).IsNull();
        await Assert.That(await lazy.GetOrInitializeAsync(owner, CancellationToken.None)).IsSameReferenceAs(resource);
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
        await lazy.DisposeAsync(owner);
    }

    [Test]
    public async Task InitializationCleanupFailure_PreservesBothErrors_AndRetainsResourceForDisposal()
    {
        var gate = new TransactionOperationGate(8);
        var resource = new ControlledTransactionResource();
        var expected = new InvalidOperationException("open failed");
        var cleanup = new Exception("cleanup failed");
        resource.Open = new AsyncCheckpoint(paused: true);
        resource.Open.Fail(expected);
        resource.Cleanup = new AsyncCheckpoint(paused: true);
        resource.Cleanup.Fail(cleanup);
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        using var owner = gate.Enter("read");

        await Assert.That(await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, CancellationToken.None))).IsSameReferenceAs(expected);
        var failureSnapshot = lazy.Failure!;
        await Assert.That(failureSnapshot.Cause).IsSameReferenceAs(expected);
        await Assert.That(failureSnapshot.CleanupFailure).IsSameReferenceAs(cleanup);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures[0].Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(lazy.PublishedResource).IsNull();
        resource.Cleanup = new AsyncCheckpoint();
        await lazy.DisposeAsync(owner);
        await lazy.DisposeAsync(owner);
        await Assert.That(resource.Calls.Count(x => x == "async-dispose")).IsEqualTo(2);
        await Assert.That(lazy.Failure).IsSameReferenceAs(failureSnapshot);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Disposed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncAndAsyncFirstUseShareOneResource_WithoutSyncOverAsync(bool asyncFirst)
    {
        var gate = new TransactionOperationGate(2);
        var resource = new ControlledTransactionResource();
        var creates = 0;
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => { creates++; return resource; });
        using (var owner = gate.Enter("first"))
        {
            if (asyncFirst) await lazy.GetOrInitializeAsync(owner, CancellationToken.None);
            else lazy.GetOrInitialize(owner);
        }
        using (var next = gate.Enter("next"))
        {
            if (asyncFirst) await Assert.That(lazy.GetOrInitialize(next)).IsSameReferenceAs(resource);
            else await Assert.That(await lazy.GetOrInitializeAsync(next, CancellationToken.None)).IsSameReferenceAs(resource);
            lazy.Dispose(next);
        }
        await Assert.That(creates).IsEqualTo(1);
        await Assert.That(resource.Calls.Contains("sync-initialize")).IsEqualTo(!asyncFirst);
        await Assert.That(resource.Calls.Contains("async-open")).IsEqualTo(asyncFirst);
        await Assert.That(resource.Calls.Contains("async-dispose")).IsFalse();
    }

    [Test]
    public async Task SyncInitializationFailure_IsNotReplayedByAsync_AndKeepsCleanupFailure()
    {
        var gate = new TransactionOperationGate(2);
        var expected = new Exception("sync initialization");
        var cleanup = new Exception("sync cleanup");
        var resource = new ControlledTransactionResource { SyncInitializationFailure = expected, SyncCleanupFailure = cleanup };
        ExecutionFailureContexts.Attach(expected, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 2, []));
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        using var owner = gate.Enter("sync");
        await Assert.That(Capture(() => lazy.GetOrInitialize(owner))).IsSameReferenceAs(expected);
        await Assert.That(lazy.Failure!.CleanupFailure).IsSameReferenceAs(cleanup);
        var context = ExecutionFailureContexts.Get(expected)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, CancellationToken.None))).IsTypeOf<InvalidOperationException>();
        await lazy.DisposeAsync(owner);
        await Assert.That(resource.Calls.ToArray()).IsEquivalentTo(new[] { "sync-initialize", "sync-dispose", "async-dispose" });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnusedDisposalAndFactoryFailure_DoNotCreateOrReplayResources(bool failFactory)
    {
        var gate = new TransactionOperationGate(2);
        var creates = 0;
        var expected = new Exception("factory failed");
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => { creates++; throw expected; });
        using var owner = gate.Enter("read");
        if (failFactory)
            await Assert.That(await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, CancellationToken.None))).IsSameReferenceAs(expected);
        await lazy.DisposeAsync(owner);
        lazy.Dispose(owner);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.That(await CaptureAsync(() => lazy.GetOrInitializeAsync(owner, cancellation.Token))).IsTypeOf<ObjectDisposedException>();
        await Assert.That(creates).IsEqualTo(failFactory ? 1 : 0);
    }

    [Test]
    public async Task DisposalFailure_IsReportedOnce_WithoutRepublishingOrReinitializing()
    {
        var gate = new TransactionOperationGate(1);
        var resource = new ControlledTransactionResource { Cleanup = new AsyncCheckpoint(paused: true) };
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        using var owner = gate.Enter("read");
        lazy.GetOrInitialize(owner);
        var pending = lazy.DisposeAsync(owner).AsTask();
        await resource.Cleanup.Entered.WaitAsync(Timeout);
        var expected = new Exception("disposal failed");
        try
        {
            await Assert.That(lazy.PublishedResource).IsNull();
            await Assert.That(Capture(owner.Dispose)).IsTypeOf<InvalidOperationException>();
            await Assert.That(await CaptureAsync(() => lazy.DisposeAsync(owner).AsTask())).IsTypeOf<InvalidOperationException>();
        }
        finally { resource.Cleanup.Fail(expected); }
        await Assert.That(await CaptureAsync(() => pending)).IsSameReferenceAs(expected);
        await lazy.DisposeAsync(owner);
        await Assert.That(resource.Calls.Count(x => x == "async-dispose")).IsEqualTo(1);
        await Assert.That(Capture(() => lazy.GetOrInitialize(owner))).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PrivateStep_RejectsForeignAndExpiredOwnersBeforeCancellationOrResourceWork(bool foreign)
    {
        var gate = new TransactionOperationGate(61);
        var other = foreign ? new TransactionOperationGate(62) : gate;
        var resource = new ControlledTransactionResource();
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        using var lease = other.Enter("read");
        using var step = other.EnterStep(lease);
        if (!foreign) step.Dispose();
        await Assert.That(await CaptureAsync(() => lazy.GetOrInitializeAsync(step, new(true)))).IsTypeOf<InvalidOperationException>();
        await Assert.That(Capture(() => lazy.GetOrInitialize(step))).IsTypeOf<InvalidOperationException>();
        await Assert.That(await CaptureAsync(() => lazy.DisposeAsync(step).AsTask())).IsTypeOf<InvalidOperationException>();
        await Assert.That(Capture(() => lazy.Dispose(step))).IsTypeOf<InvalidOperationException>();
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Unused);
    }

    [Test]
    [Arguments("initialization")]
    [Arguments("failed-cleanup")]
    [Arguments("disposal")]
    public async Task PrivateStep_DoesNotAuthorizeOverlappingResourceCalls(string phase)
    {
        var gate = new TransactionOperationGate(63);
        var resource = new ControlledTransactionResource { Open = new(paused: true), Cleanup = new(paused: true) };
        var lazy = new LazyTransactionResource<ControlledTransactionResource>(gate, () => resource);
        using var lease = gate.Enter("read");
        using var step = gate.EnterStep(lease);
        var pending = lazy.GetOrInitializeAsync(step, CancellationToken.None);
        await resource.Open.Entered.WaitAsync(Timeout);
        var expected = new Exception("open failed");
        Task active = pending;
        if (phase == "failed-cleanup")
        {
            resource.Open.Fail(expected);
            await resource.Cleanup.Entered.WaitAsync(Timeout);
        }
        else if (phase == "disposal")
        {
            resource.Open.Release();
            await pending.WaitAsync(Timeout);
            active = lazy.DisposeAsync(step).AsTask();
            await resource.Cleanup.Entered.WaitAsync(Timeout);
        }
        try
        {
            await Assert.That(await CaptureAsync(() => lazy.GetOrInitializeAsync(step, CancellationToken.None))).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(() => lazy.GetOrInitialize(step))).IsTypeOf<InvalidOperationException>();
            await Assert.That(await CaptureAsync(() => lazy.DisposeAsync(step).AsTask())).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(() => lazy.Dispose(step))).IsTypeOf<InvalidOperationException>();
        }
        finally { resource.Open.Release(); resource.Cleanup.Release(); }
        if (phase == "failed-cleanup")
        {
            await Assert.That(await CaptureAsync(() => active)).IsSameReferenceAs(expected);
            var context = ExecutionFailureContexts.Get(expected)!;
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
            await Assert.That(context.TransactionId).IsEqualTo((uint?)63);
        }
        else await active.WaitAsync(Timeout);
        await lazy.DisposeAsync(step);
        await Assert.That(resource.Calls.Count(x => x == "async-open")).IsEqualTo(1);
        await Assert.That(resource.Calls.Count(x => x == "async-dispose")).IsEqualTo(1);
    }

    private static AsyncCheckpoint Pause(ControlledTransactionResource resource, string stage)
    {
        var checkpoint = new AsyncCheckpoint(paused: true);
        switch (stage)
        {
            case "open": resource.Open = checkpoint; break;
            case "configure": resource.Configure = checkpoint; break;
            case "begin": resource.Begin = checkpoint; break;
            default: throw new ArgumentOutOfRangeException(nameof(stage));
        }
        return checkpoint;
    }

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new Exception("Expected a failure.");
    }

    private static async Task<Exception> CaptureAsync(Func<Task> action)
    {
        try { await action().WaitAsync(Timeout); }
        catch (Exception exception) { return exception; }
        throw new Exception("Expected a failure.");
    }
}
