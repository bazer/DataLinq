using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class TransactionCallbackRunnerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task CallbackRunsOnceWithoutHoldingExecutionSlot_ResultWaitsForCommitAndCleanup()
    {
        var gate = new TransactionOperationGate(1);
        var resource = new ControlledHelperTransaction { Commit = new(paused: true), ConnectionCleanup = new(paused: true) };
        var invocations = 0;
        var pending = Run(gate, resource, async token =>
        {
            invocations++;
            using (gate.Enter("first")) { }
            await Task.Yield();
            using (gate.Enter("second")) { }
            return 17;
        });
        await resource.Commit.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(Capture(() => gate.Enter("escaped work"))).IsTypeOf<InvalidOperationException>();
            resource.Commit.Release();
            await resource.ConnectionCleanup.Entered.WaitAsync(Timeout);
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(resource.Finalizations).IsEqualTo(1);
        }
        finally { resource.Commit.Release(); resource.ConnectionCleanup.Release(); }
        await Assert.That(await pending.WaitAsync(Timeout)).IsEqualTo(17);
        await Assert.That(invocations).IsEqualTo(1);
        await Assert.That(resource.Calls.ToArray()).IsEquivalentTo(new[] { "commit", "dispose-transaction", "dispose-connection" });
        await Assert.That(Capture(() => gate.Enter("after helper"))).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnfinishedOperationIsDrainedButNeverCommitted(bool operationFails)
    {
        var gate = new TransactionOperationGate(1);
        var resource = new ControlledHelperTransaction();
        var clock = new ControlledRecoveryTimeProvider();
        var paused = new AsyncCheckpoint(paused: true);
        var expected = new Exception("escaped operation failure");
        Task? escaped = null;
        var pending = Run(gate, resource, _ =>
        {
            escaped = Operation();
            return Task.FromResult(17);
        }, clock: clock);
        await paused.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(resource.Calls).IsEmpty();
            await Assert.That(clock.Created).IsEqualTo(0);
            await Assert.That(Capture(() => gate.Enter("escaped later work"))).IsTypeOf<InvalidOperationException>();
        }
        finally { paused.Release(); }
        if (operationFails) await Assert.That(await Failure(() => escaped!)).IsSameReferenceAs(expected);
        else await escaped!.WaitAsync(Timeout);
        var error = await Failure(() => pending);
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(operationFails ? 1 : 0);
        if (operationFails) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(expected);
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
        await Assert.That(clock.Created).IsEqualTo(1);

        async Task Operation()
        {
            using var owner = gate.Enter("escaped command");
            using var step = gate.EnterStep(owner);
            try
            {
                await paused.ReachAsync(CancellationToken.None);
                if (operationFails) throw expected;
            }
            catch (Exception failure) { owner.ReportFailure(failure); throw; }
        }
    }

    [Test]
    public async Task CallbackFailureWinsOverUnfinishedWorkAndCleanupFailures()
    {
        var gate = new TransactionOperationGate(1);
        var callbackFailure = new FormatException("callback failed");
        var cleanupFailure = new Exception("cleanup failed");
        var resource = new ControlledHelperTransaction { DisposingConnection = () => throw cleanupFailure };
        TransactionOperationGate.Lease? escaped = null;
        var pending = Run(gate, resource, _ =>
        {
            escaped = gate.Enter("escaped command");
            throw callbackFailure;
        });
        await Assert.That(pending.IsCompleted).IsFalse();
        escaped!.Dispose();
        var reported = await Failure(() => pending);
        await Assert.That(reported).IsSameReferenceAs(callbackFailure);
        var context = ExecutionFailureContexts.Get(reported)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Callback);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(2);
        await Assert.That(context.SecondaryFailures[0].Exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(cleanupFailure);
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
    }

    [Test]
    [Arguments("before-callback", 0, false)]
    [Arguments("before-commit", 1, false)]
    [Arguments("after-commit", 1, true)]
    public async Task CancellationUsesExplicitCheckpointsWithoutRewritingConfirmedCommit(string when, int count, bool succeeds)
    {
        var gate = new TransactionOperationGate(1);
        using var request = new CancellationTokenSource();
        var resource = new ControlledHelperTransaction();
        if (when == "before-callback") request.Cancel();
        if (when == "after-commit") resource.Committed = request.Cancel;
        var invocations = 0;
        var pending = Run(gate, resource, token =>
        {
            invocations++;
            if (token != request.Token) throw new Exception("wrong callback token");
            if (when == "before-commit") request.Cancel();
            return Task.FromResult(17);
        }, request.Token);
        if (succeeds)
        {
            await Assert.That(await pending).IsEqualTo(17);
            await Assert.That(resource.Finalizations).IsEqualTo(1);
            await Assert.That(resource.Commit.ObservedToken).IsEqualTo(request.Token);
            await Assert.That(resource.Calls.Contains("rollback")).IsFalse();
        }
        else
        {
            var error = await Failure(() => pending);
            await Assert.That(error is OperationCanceledException).IsTrue();
            await Assert.That(error.StackTrace!).Contains("CheckCancellation");
            await Assert.That(ExecutionFailureContexts.Get(error)!.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
            await Assert.That(resource.RollbackToken == request.Token).IsFalse();
            await Assert.That(resource.RollbackToken.IsCancellationRequested).IsFalse();
            await Assert.That(resource.Calls.Contains("commit")).IsFalse();
        }
        await Assert.That(invocations).IsEqualTo(count);
    }

    [Test]
    [Arguments("commit", "Unknown")]
    [Arguments("finalization", "Committed")]
    [Arguments("cleanup", "Committed")]
    public async Task FailureWithholdsResultAndRetainsCompletion(string phase, string completion)
    {
        var gate = new TransactionOperationGate(1);
        var expected = new Exception("injected " + phase);
        var resource = new ControlledHelperTransaction();
        if (phase == "commit") { resource.Commit = new(paused: true); resource.Commit.Fail(expected); }
        if (phase == "finalization") resource.FinalizationFailure = expected;
        if (phase == "cleanup") resource.DisposingConnection = () => throw expected;
        var failure = await Failure(() => Run(gate, resource, _ => Task.FromResult(17)));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Completion).IsEqualTo(Enum.Parse<ExecutionCompletion>(completion));
        await Assert.That(resource.Calls.Count(x => x == "commit")).IsEqualTo(1);
        await Assert.That(resource.Calls.Contains("rollback")).IsEqualTo(phase == "commit");
    }

    [Test]
    public async Task ValidationPrecedesCancellation_AndPoisonedCommitIsRejected()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var invalid = new NotSupportedException("callback unsupported");
        var resource = new ControlledHelperTransaction { CallbackValidationFailure = invalid };
        var gate = new TransactionOperationGate(1);
        await Assert.That(await Failure(() => Run(gate, resource, _ => throw new Exception("must not invoke"), canceled.Token)))
            .IsSameReferenceAs(invalid);
        await Assert.That(resource.Calls).IsEmpty();
        resource.CallbackValidationFailure = null;
        var poison = new InvalidOperationException("poisoned transaction");
        resource.CommitValidationFailure = poison;
        await Assert.That(await Failure(() => Run(gate, resource, _ => { canceled.Cancel(); return Task.FromResult(17); })))
            .IsSameReferenceAs(poison);
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
    }

    [Test]
    public async Task ACompletedUnawaitedTaskIsNotMistakenForActiveWork()
    {
        var gate = new TransactionOperationGate(1);
        var resource = new ControlledHelperTransaction();
        var result = await Run(gate, resource, token =>
        {
            using (gate.Enter("already completed operation")) { }
            _ = Task.FromResult(5); // Tracking does not claim to detect this missing await.
            return Task.FromResult(17);
        });
        await Assert.That(result).IsEqualTo(17);
        await Assert.That(resource.Calls.Contains("commit")).IsTrue();
    }

    [Test]
    public async Task NullCallbackTaskFailsWithoutCommitOrReplay()
    {
        var gate = new TransactionOperationGate(1);
        var resource = new ControlledHelperTransaction();
        var calls = 0;
        var failure = await Failure(() => Run(gate, resource, _ => { calls++; return null!; }));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
    }

    [Test]
    public async Task ReaderRegisteredAfterCallbackClosesIsStillStoppedAndDrained()
    {
        var gate = new TransactionOperationGate(1);
        var helper = gate.BeginHelperLifetime();
        using var operation = gate.Enter("acquiring reader");
        var step = gate.EnterStep(operation);
        var failures = new ExecutionFailures();
        var pending = helper.CloseAndDrainAsync(failures);
        var reader = new LateReader(operation, step);
        operation.RegisterReader(reader);
        using var owner = await pending.WaitAsync(Timeout);
        await Assert.That(reader.Stopped).IsTrue();
        await Assert.That(reader.Drains).IsEqualTo(1);
        await Assert.That(failures.Primary).IsTypeOf<InvalidOperationException>();
        await Assert.That(Capture(() => helper.CloseAndDrainAsync(new()))).IsTypeOf<InvalidOperationException>();
    }

    private sealed class LateReader(TransactionOperationGate.Lease owner, TransactionOperationGate.Step step) : IHelperTrackedReader
    {
        internal bool Stopped { get; private set; }
        internal int Drains { get; private set; }
        public void StopAdmission() => Stopped = true;
        public ValueTask DrainAsync()
        {
            if (!Stopped) throw new Exception("Reader admission was not closed.");
            Drains++;
            step.Dispose();
            owner.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static Task<int> Run(TransactionOperationGate gate, ControlledHelperTransaction resource,
        Func<CancellationToken, Task<int>> callback, CancellationToken token = default, ControlledRecoveryTimeProvider? clock = null) =>
        TransactionCallbackRunner.RunAsync(gate, resource, new(), 1, callback, token, clock);

    private static Exception Capture(Action action)
    {
        try { action(); } catch (Exception failure) { return failure; }
        throw new Exception("Expected failure.");
    }

    private static async Task<Exception> Failure(Func<Task> action)
    {
        try { await action().WaitAsync(Timeout); }
        catch (Exception failure) when (failure is not TimeoutException) { return failure; }
        throw new Exception("Expected failure.");
    }
}
