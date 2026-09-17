using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class AutomaticTransactionRecoveryTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task DefaultBudgetIsThirtySeconds_AndCaptureIsIndependent()
    {
        var requested = TimeSpan.FromSeconds(4);
        var settings = new RecoveryRollbackSettings(requested);
        requested = TimeSpan.FromSeconds(7);
        await Assert.That(settings.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(new RecoveryRollbackSettings(requested).RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(7));
        await Assert.That(new RecoveryRollbackSettings().RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    [Arguments(10_000L)]
    [Arguments(15_000L)]
    [Arguments(42_949_672_940_000L)]
    public async Task SupportedDurationsAreAcceptedByTheRuntimeTimer(long ticks)
    {
        var settings = new RecoveryRollbackSettings(TimeSpan.FromTicks(ticks));
        using var timer = new CancellationTokenSource(settings.RecoveryRollbackTimeout, TimeProvider.System);
        await Assert.That(settings.RecoveryRollbackTimeout.Ticks).IsEqualTo(ticks);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(-10_000L)]
    [Arguments(1L)]
    [Arguments(9_999L)]
    [Arguments(42_949_672_940_001L)]
    [Arguments(long.MaxValue)]
    public async Task InvalidDurationsFailBeforeOwnershipOrResourceWork(long ticks)
    {
        var gate = new TransactionOperationGate(8);
        using var owner = gate.Enter("helper");
        var resource = new ControlledTransactionRecovery();
        var error = Capture(() => new AutomaticTransactionRecovery(gate, owner, resource,
            new RecoveryRollbackSettings(TimeSpan.FromTicks(ticks)), new(), ExecutionCompletion.NotAttempted,
            ExecutionRecoveryActions.Rollback, 8));
        await Assert.That(error).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(((ArgumentOutOfRangeException)error).ParamName).IsEqualTo("RecoveryRollbackTimeout");
        await Assert.That(resource.Calls).IsEmpty();
        using var stillOwned = gate.EnterStep(owner);
    }

    [Test]
    public async Task ActiveWorkMustSettleBeforeTransfer_AndBudgetStartsOnlyAtRollback()
    {
        var gate = new TransactionOperationGate(8);
        using var owner = gate.Enter("helper");
        var resource = new ControlledTransactionRecovery();
        var clock = new ControlledRecoveryTimeProvider();
        var providerWork = new AsyncCheckpoint(paused: true);
        var step = gate.EnterStep(owner);
        var pending = providerWork.ReachAsync(CancellationToken.None);
        await providerWork.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(Capture(() => Create(gate, owner, resource, clock))).IsTypeOf<InvalidOperationException>();
            await Assert.That(clock.Created).IsEqualTo(0);
            await Assert.That(resource.Calls).IsEmpty();
        }
        finally { providerWork.Release(); await pending.WaitAsync(Timeout); step.Dispose(); }

        var recovery = Create(gate, owner, resource, clock);
        owner.Dispose(); // A stale caller cannot release the transferred lease.
        await Assert.That(Capture(() => gate.Enter("another operation"))).IsTypeOf<InvalidOperationException>();
        await Assert.That(clock.Created).IsEqualTo(0);
        await recovery.DisposeAsync();
        await Assert.That(clock.Created).IsEqualTo(1);
        await Assert.That(clock.DueTime).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(clock.Period).IsEqualTo(System.Threading.Timeout.InfiniteTimeSpan);
        await Assert.That(clock.Timer!.IsDisposed).IsTrue();
        using var next = gate.Enter("after cleanup");
    }

    [Test]
    public async Task WrongGateCannotStealTheOperationLease()
    {
        var gate = new TransactionOperationGate(1);
        var other = new TransactionOperationGate(2);
        using var owner = gate.Enter("helper");
        var resource = new ControlledTransactionRecovery();
        var clock = new ControlledRecoveryTimeProvider();
        await Assert.That(Capture(() => Create(other, owner, resource, clock))).IsTypeOf<InvalidOperationException>();
        using (gate.EnterStep(owner)) { }
        await Assert.That(resource.Calls).IsEmpty();
        await Create(gate, owner, resource, clock).DisposeAsync();
    }

    [Test]
    public async Task CanceledRequestCannotCancelRecovery_AndRepeatedDisposalDoesNotReplayThePrimary()
    {
        var gate = new TransactionOperationGate(8);
        using var owner = gate.Enter("helper");
        using var request = new CancellationTokenSource();
        request.Cancel();
        var original = Capture(request.Token.ThrowIfCancellationRequested);
        var failures = new ExecutionFailures();
        failures.Add(original, ExecutionFailureCause.Cancellation, ExecutionFailureStage.CommandExecution);
        var resource = new ControlledTransactionRecovery { Rollback = new(paused: true) };
        var clock = new ControlledRecoveryTimeProvider();
        var recovery = Create(gate, owner, resource, clock, failures);
        var pending = recovery.DisposeAsync().AsTask();
        await resource.Rollback.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(resource.RollbackToken.CanBeCanceled).IsTrue();
            await Assert.That(resource.RollbackToken == request.Token).IsFalse();
            await Assert.That(resource.RollbackToken.IsCancellationRequested).IsFalse();
            await Assert.That(pending.IsCompleted).IsFalse();
        }
        finally { resource.Rollback.Release(); }
        await Assert.That(await CaptureAsync(() => pending)).IsSameReferenceAs(original);
        await Assert.That(recovery.FailureContext!.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(recovery.FailureContext.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(recovery.FailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(ExecutionFailureContexts.Get(original)).IsSameReferenceAs(recovery.FailureContext);
        await recovery.DisposeAsync();
        await Assert.That(resource.Calls.ToArray()).IsEquivalentTo(new[] { "rollback", "dispose-transaction", "dispose-connection" });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiryWaitsForActualRollbackAndEveryCleanup_WithoutRetry(bool ignoresCancellation)
    {
        var gate = new TransactionOperationGate(8);
        using var owner = gate.Enter("helper");
        var resource = new ControlledTransactionRecovery
        {
            Rollback = new(paused: true), TransactionCleanup = new(paused: true),
            ConnectionCleanup = new(paused: true), IgnoreCancellation = ignoresCancellation
        };
        var original = Capture(ThrowOriginal);
        var failures = new ExecutionFailures();
        failures.Add(original, ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution);
        var clock = new ControlledRecoveryTimeProvider();
        var recovery = Create(gate, owner, resource, clock, failures);
        var pending = recovery.DisposeAsync().AsTask();
        await resource.Rollback.Entered.WaitAsync(Timeout);
        try
        {
            clock.Timer!.Fire();
            await Assert.That(resource.RollbackToken.IsCancellationRequested).IsTrue();
            if (ignoresCancellation)
            {
                await Assert.That(resource.TransactionCleanup.Entered.IsCompleted).IsFalse();
                // CancellationTokenSource can retire its timer on cancellation even while
                // provider work continues. Resource/lease lifetime is the relevant boundary.
                await Assert.That(clock.Created).IsEqualTo(1);
                await AssertBlocked();
                resource.Rollback.Release();
            }
            await resource.TransactionCleanup.Entered.WaitAsync(Timeout);
            await AssertBlocked();
            await Assert.That(clock.Timer.IsDisposed).IsTrue();
            await Assert.That(resource.TransactionCleanup.ObservedToken).IsEqualTo(CancellationToken.None);
            resource.TransactionCleanup.Release();
            await resource.ConnectionCleanup.Entered.WaitAsync(Timeout);
            await AssertBlocked();
            await Assert.That(resource.ConnectionCleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally
        {
            resource.Rollback.Release();
            resource.TransactionCleanup.Release();
            resource.ConnectionCleanup.Release();
        }
        await Assert.That(await CaptureAsync(() => pending)).IsSameReferenceAs(original);
        var context = recovery.FailureContext!;
        await Assert.That(context.Completion).IsEqualTo(ignoresCancellation ? ExecutionCompletion.RolledBack : ExecutionCompletion.Unknown);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(ignoresCancellation ? 0 : 1);
        if (!ignoresCancellation)
        {
            await Assert.That(context.SecondaryFailures[0].Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
            await Assert.That(context.SecondaryFailures[0].Exception is OperationCanceledException).IsTrue();
        }
        await Assert.That(clock.Created).IsEqualTo(1);
        await recovery.DisposeAsync();
        await Assert.That(resource.Calls.Count(x => x == "rollback")).IsEqualTo(1);
        using var next = gate.Enter("after settled cleanup");

        async Task AssertBlocked()
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(Capture(() => gate.Enter("competing operation"))).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(() => recovery.DisposeAsync())).IsTypeOf<InvalidOperationException>();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AllIndependentCleanupRuns_WithOrderedFailuresAndOriginalStack(bool synchronousThrows)
    {
        var gate = new TransactionOperationGate(8);
        using var owner = gate.Enter("helper");
        var original = Capture(ThrowOriginal);
        var earlierCleanup = new Exception("reader cleanup");
        var rollback = new TimeoutException("provider timeout");
        var transactionCleanup = new AggregateException(new Exception("transaction cleanup"));
        var connectionCleanup = new Exception("connection cleanup");
        var failures = new ExecutionFailures();
        failures.Add(original, ExecutionFailureCause.MaterializationError, ExecutionFailureStage.Materialization);
        failures.Add(earlierCleanup, ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup);
        var before = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Rollback, 8);
        var resource = new ControlledTransactionRecovery();
        var clock = new ControlledRecoveryTimeProvider();
        if (synchronousThrows)
        {
            resource.RollingBack = () => { clock.Timer!.Fire(); throw rollback; };
            resource.DisposingTransaction = () => throw transactionCleanup;
            resource.DisposingConnection = () => throw connectionCleanup;
        }
        else
        {
            resource.Rollback = Failed(rollback);
            resource.TransactionCleanup = Failed(transactionCleanup);
            resource.ConnectionCleanup = Failed(connectionCleanup);
        }
        var recovery = Create(gate, owner, resource, clock, failures);
        var reported = await CaptureAsync(() => recovery.DisposeAsync().AsTask());
        await Assert.That(reported).IsSameReferenceAs(original);
        await Assert.That(reported.StackTrace!).Contains(nameof(ThrowOriginal));
        var after = recovery.FailureContext!;
        await Assert.That(after.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(after.SecondaryFailures.Select(x => x.Exception).ToArray())
            .IsEquivalentTo(new Exception[] { earlierCleanup, rollback, transactionCleanup, connectionCleanup });
        await Assert.That(after.SecondaryFailures[0].Exception).IsSameReferenceAs(earlierCleanup);
        await Assert.That(after.SecondaryFailures[1].Exception).IsSameReferenceAs(rollback);
        await Assert.That(after.SecondaryFailures[1].Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(after.SecondaryFailures[1].Stage).IsEqualTo(ExecutionFailureStage.Recovery);
        await Assert.That(after.SecondaryFailures[2].Exception).IsSameReferenceAs(transactionCleanup);
        await Assert.That(after.SecondaryFailures[3].Exception).IsSameReferenceAs(connectionCleanup);
        await Assert.That(before.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(before.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback);
        await Assert.That(resource.Calls.Count).IsEqualTo(3);
        await recovery.DisposeAsync();
        await Assert.That(resource.Calls.Count).IsEqualTo(3);
    }

    [Test]
    [Arguments("NotAttempted", true, false, "RolledBack", 1)]
    [Arguments("NotAttempted", true, true, "Unknown", 1)]
    [Arguments("Unknown", true, false, "Unknown", 1)]
    [Arguments("Unknown", true, true, "Unknown", 1)]
    [Arguments("Committed", true, false, "Committed", 0)]
    [Arguments("RolledBack", true, false, "RolledBack", 0)]
    [Arguments("NotAttempted", false, false, "NotAttempted", 0)]
    public async Task CompletionAndRecoveryEvidenceSurviveCleanupFailure(string prior, bool allowed, bool rollbackFails,
        string expected, int attempts)
    {
        var gate = new TransactionOperationGate(8);
        using var owner = gate.Enter("helper");
        var rollback = new Exception("rollback failed");
        var cleanup = new Exception("transaction cleanup failed");
        var resource = new ControlledTransactionRecovery { TransactionCleanup = Failed(cleanup) };
        if (rollbackFails) resource.Rollback = Failed(rollback);
        var clock = new ControlledRecoveryTimeProvider();
        var recovery = Create(gate, owner, resource, clock, completion: Enum.Parse<ExecutionCompletion>(prior),
            actions: allowed ? ExecutionRecoveryActions.Rollback : ExecutionRecoveryActions.Dispose);
        var failure = await CaptureAsync(() => recovery.DisposeAsync().AsTask());
        await Assert.That(failure).IsSameReferenceAs(rollbackFails ? rollback : cleanup);
        await Assert.That(recovery.FailureContext!.Completion).IsEqualTo(Enum.Parse<ExecutionCompletion>(expected));
        await Assert.That(recovery.FailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(recovery.FailureContext.Stage).IsEqualTo(rollbackFails ? ExecutionFailureStage.Recovery : ExecutionFailureStage.Cleanup);
        await Assert.That(resource.Calls.Count(x => x == "rollback")).IsEqualTo(attempts);
        await Assert.That(clock.Created).IsEqualTo(attempts);
        await Assert.That(resource.Calls.Last()).IsEqualTo("dispose-connection");
    }

    [Test]
    public async Task TimerSetupFailureDoesNotDispatchRollbackOrSkipCleanup()
    {
        var gate = new TransactionOperationGate(8);
        using var owner = gate.Enter("helper");
        var expected = new Exception("timer creation");
        var clock = new ControlledRecoveryTimeProvider { CreationFailure = expected };
        var resource = new ControlledTransactionRecovery();
        var recovery = Create(gate, owner, resource, clock);
        await Assert.That(await CaptureAsync(() => recovery.DisposeAsync().AsTask())).IsSameReferenceAs(expected);
        await Assert.That(recovery.FailureContext!.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(resource.Calls.ToArray()).IsEquivalentTo(new[] { "dispose-transaction", "dispose-connection" });
        using var next = gate.Enter("after failure");
    }

    [Test]
    public async Task IndependentCapturedBudgetsDoNotCancelAnotherRecovery()
    {
        var firstGate = new TransactionOperationGate(1);
        var secondGate = new TransactionOperationGate(2);
        using var firstOwner = firstGate.Enter("first helper");
        using var secondOwner = secondGate.Enter("second helper");
        var firstResource = new ControlledTransactionRecovery { Rollback = new(paused: true) };
        var secondResource = new ControlledTransactionRecovery { Rollback = new(paused: true) };
        var firstClock = new ControlledRecoveryTimeProvider();
        var secondClock = new ControlledRecoveryTimeProvider();
        var first = new AutomaticTransactionRecovery(firstGate, firstOwner, firstResource,
            new(TimeSpan.FromSeconds(2)), new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Rollback, 1, firstClock);
        var second = new AutomaticTransactionRecovery(secondGate, secondOwner, secondResource,
            new(TimeSpan.FromSeconds(9)), new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Rollback, 2, secondClock);
        var firstPending = first.DisposeAsync().AsTask();
        var secondPending = second.DisposeAsync().AsTask();
        await Task.WhenAll(firstResource.Rollback.Entered, secondResource.Rollback.Entered).WaitAsync(Timeout);
        try
        {
            firstClock.Timer!.Fire();
            await Assert.That(await CaptureAsync(() => firstPending) is OperationCanceledException).IsTrue();
            await Assert.That(secondPending.IsCompleted).IsFalse();
            await Assert.That(secondResource.RollbackToken.IsCancellationRequested).IsFalse();
            await Assert.That(firstClock.DueTime).IsEqualTo(TimeSpan.FromSeconds(2));
            await Assert.That(secondClock.DueTime).IsEqualTo(TimeSpan.FromSeconds(9));
        }
        finally
        {
            firstResource.Rollback.Release();
            secondResource.Rollback.Release();
        }
        await secondPending.WaitAsync(Timeout);
        await Assert.That(first.FailureContext!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(second.FailureContext).IsNull();
    }

    private static AutomaticTransactionRecovery Create(TransactionOperationGate gate, TransactionOperationGate.Lease owner,
        ControlledTransactionRecovery resource, ControlledRecoveryTimeProvider clock, ExecutionFailures? failures = null,
        ExecutionCompletion completion = ExecutionCompletion.NotAttempted, ExecutionRecoveryActions actions = ExecutionRecoveryActions.Rollback) =>
        new(gate, owner, resource, new(), failures ?? new(), completion, actions, 8, clock);

    private static AsyncCheckpoint Failed(Exception exception)
    {
        var checkpoint = new AsyncCheckpoint(paused: true);
        checkpoint.Fail(exception);
        return checkpoint;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOriginal() => throw new InvalidOperationException("original operation");

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
