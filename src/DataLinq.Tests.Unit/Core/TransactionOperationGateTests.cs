using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class TransactionOperationGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task SuspendedOperation_RejectsAnotherCallWithoutDisturbingItsOwner()
    {
        var gate = new TransactionOperationGate(42);
        var pause = new AsyncCheckpoint(paused: true);
        var pending = Execute();
        await pause.Entered.WaitAsync(Timeout);
        try
        {
            var failure = Capture(() => gate.Enter("commit"));
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(failure.Message).Contains("42");
            await Assert.That(failure.Message).Contains("commit");
            await Assert.That(failure.Message).Contains("read");
            await Assert.That(pending.IsCompleted).IsFalse();
        }
        finally { pause.Release(); await pending.WaitAsync(Timeout); }
        using var next = gate.Enter("commit");

        async Task Execute()
        {
            using var lease = gate.Enter("read");
            using var step = gate.EnterStep(lease);
            await pause.ReachAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Lease_CanResumeOnAnotherThread_WithoutGrantingPublicAdmission()
    {
        var gate = new TransactionOperationGate(9);
        using var lease = gate.Enter("mutation");
        var callerThread = Environment.CurrentManagedThreadId;
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                using (gate.EnterStep(lease))
                {
                    if (Capture(() => gate.Enter("public callback")) is not InvalidOperationException)
                        throw new Exception("Public reentry was admitted.");
                }
                done.SetResult(Environment.CurrentManagedThreadId);
            }
            catch (Exception exception) { done.SetException(exception); }
        }) { IsBackground = true };
        worker.Start();
        await Assert.That(await done.Task.WaitAsync(Timeout)).IsNotEqualTo(callerThread);
        // The worker's private step ended, but the enclosing mutation still owns the slot.
        await Assert.That(Capture(() => gate.Enter("query"))).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task ActiveStep_RejectsOverlapReleaseAndTransfer_WithoutChangingOwnership()
    {
        var gate = new TransactionOperationGate(1);
        using var lease = gate.Enter("reader");
        using (var step = gate.EnterStep(lease))
        {
            await Assert.That(Capture(() => gate.EnterStep(lease))).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(lease.Dispose)).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(() => lease.Transfer())).IsTypeOf<InvalidOperationException>();
        }
        using var resumed = gate.EnterStep(lease);
    }

    [Test]
    public async Task ReaderHandoff_HoldsOwnershipBetweenMovesAndThroughAsyncCleanup()
    {
        var gate = new TransactionOperationGate(7);
        using var acquisition = gate.Enter("reader");
        using var readerOwner = acquisition.Transfer();
        acquisition.Dispose();
        var reader = new ControlledAsyncDataReader { Cleanup = new AsyncCheckpoint(paused: true) };
        using (var move = gate.EnterStep(readerOwner))
            await Assert.That(await reader.ReadNextRowAsync(CancellationToken.None)).IsTrue();
        await Assert.That(Capture(() => gate.Enter("query between moves"))).IsTypeOf<InvalidOperationException>();
        await Assert.That(Capture(() => gate.EnterStep(acquisition))).IsTypeOf<InvalidOperationException>();

        var cleanup = Close();
        await reader.Cleanup.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(Capture(readerOwner.Dispose)).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(() => gate.Enter("dispose transaction"))).IsTypeOf<InvalidOperationException>();
        }
        finally { reader.Cleanup.Release(); await cleanup.WaitAsync(Timeout); }
        using var next = gate.Enter("query after cleanup");

        async Task Close()
        {
            using (gate.EnterStep(readerOwner))
                await reader.DisposeAsync();
            readerOwner.Dispose();
        }
    }

    [Test]
    public async Task ForeignAndReleasedOwners_CannotDispatch_AndStaleDisposalCannotReleaseNewWork()
    {
        var first = new TransactionOperationGate(1);
        var second = new TransactionOperationGate(2);
        var old = first.Enter("old");
        await Assert.That(Capture(() => second.EnterStep(old))).IsTypeOf<InvalidOperationException>();
        old.Dispose();
        using var current = first.Enter("current");
        old.Dispose();
        await Assert.That(Capture(() => first.EnterStep(old))).IsTypeOf<InvalidOperationException>();
        await Assert.That(Capture(() => old.Transfer())).IsTypeOf<InvalidOperationException>();
        using var step = first.EnterStep(current);
        step.Dispose();
        using var nextStep = first.EnterStep(current);
        step.Dispose();
        await Assert.That(Capture(current.Dispose)).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async Task SeparateTransactions_DoNotShareAdmission_AndCompletedUnawaitedWorkReleasesIt()
    {
        var first = new TransactionOperationGate(1);
        var second = new TransactionOperationGate(2);
        using var independent = second.Enter("read");
        var done = Execute();
        using var next = first.Enter("read");
        await Assert.That(done.IsCompletedSuccessfully).IsTrue();
        await done;

        async Task Execute()
        {
            using var owner = first.Enter("completed operation");
            using var step = first.EnterStep(owner);
            await Task.CompletedTask;
        }
    }

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new Exception("Expected a failure.");
    }
}
