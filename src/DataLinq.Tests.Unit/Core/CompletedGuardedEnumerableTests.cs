using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Core;

public sealed class CompletedGuardedEnumerableTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FinishedCalls_DoNotAllocateDiagnosticScopes(bool disposeBeforeFirstMove)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var caller = ExecutionFailureScope.Current;
        var inner = new ProbeEnumerator();
        using var rows = Wrap(inner);
        if (disposeBeforeFirstMove) rows.Dispose();
        else
        {
            await Assert.That(rows.MoveNext()).IsTrue();
            await Assert.That(rows.MoveNext()).IsFalse();
        }
        var moveCalls = inner.MoveCalls;
        RepeatFinishedCalls(rows, 1000);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var unexpectedRows = RepeatFinishedCalls(rows, 10000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(unexpectedRows).IsEqualTo(0);
        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(inner.MoveCalls).IsEqualTo(moveCalls);
        await Assert.That(inner.DisposeCalls).IsEqualTo(1);
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(caller);
    }

    [Test]
    public async Task HelperDrainedDisposal_IsAllocationFreeWithoutReopeningAdmission()
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var inner = new ProbeEnumerator();
        using var rows = Wrap(inner);
        var helper = (IHelperTrackedReader)rows;
        await helper.DrainAsync();
        for (var i = 0; i < 1000; i++) rows.Dispose();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) rows.Dispose();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(inner.MoveCalls).IsEqualTo(0);
        await Assert.That(inner.DisposeCalls).IsEqualTo(1);
        await Assert.That(Capture(() => rows.MoveNext())).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FinishingCleanup_RejectsOverlapEvenAfterFinishedFlagIsSet(bool finishByMove)
    {
        var inner = new ProbeEnumerator();
        using var rows = Wrap(inner);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.OnDispose = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Cleanup was not released.");
        };
        if (finishByMove) await Assert.That(rows.MoveNext()).IsTrue();
        var finishing = Task.Run(() =>
        {
            if (finishByMove)
            {
                if (rows.MoveNext()) throw new InvalidOperationException("Expected terminal move.");
            }
            else rows.Dispose();
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(Capture(() => rows.MoveNext())).IsTypeOf<InvalidOperationException>();
            await Assert.That(Capture(rows.Dispose)).IsTypeOf<InvalidOperationException>();
            await Assert.That(inner.DisposeCalls).IsEqualTo(1);
        }
        finally
        {
            release.Set();
            await finishing.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await Assert.That(rows.MoveNext()).IsFalse();
        rows.Dispose();
        await Assert.That(inner.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task LiveMoveAndCleanup_KeepSeparateDiagnosticOccurrences()
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var caller = ExecutionFailureScope.Current;
        var reused = new Exception("earlier successful callback report");
        ExecutionFailureScope? moveScope = null;
        ExecutionFailureScope? cleanupScope = null;
        var sawStaleReport = true;
        var inner = new ProbeEnumerator
        {
            OnMove = () =>
            {
                moveScope = ExecutionFailureScope.Current;
                ExecutionFailureContexts.Attach(reused, new ExecutionFailureContext(ExecutionFailureCause.Timeout,
                    ExecutionFailureStage.RowLoading, ExecutionCompletion.NotAttempted,
                    ExecutionRecoveryActions.Dispose, null, [], operation: ExecutionOperationKind.Query));
            },
            OnDispose = () =>
            {
                cleanupScope = ExecutionFailureScope.Current;
                sawStaleReport = ExecutionFailureContexts.GetCurrent(reused) is not null;
            }
        };
        using var rows = Wrap(inner);
        await Assert.That(rows.MoveNext()).IsTrue();
        rows.Dispose();
        await Assert.That(moveScope).IsNotNull();
        await Assert.That(cleanupScope).IsNotNull();
        await Assert.That(ReferenceEquals(moveScope, caller)).IsFalse();
        await Assert.That(ReferenceEquals(cleanupScope, caller)).IsFalse();
        await Assert.That(ReferenceEquals(moveScope, cleanupScope)).IsFalse();
        await Assert.That(sawStaleReport).IsFalse();
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(caller);
        await Assert.That(inner.DisposeCalls).IsEqualTo(1);
    }

    private static IEnumerator<int> Wrap(ProbeEnumerator inner) =>
        new GuardedEnumerable<int>(new ProbeEnumerable(inner)).GetEnumerator();

    private static int RepeatFinishedCalls(IEnumerator<int> rows, int count)
    {
        var unexpectedRows = 0;
        for (var i = 0; i < count; i++)
        {
            if (rows.MoveNext()) unexpectedRows++;
            rows.Dispose();
        }
        return unexpectedRows;
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception failure) { return failure; }
    }

    private sealed class ProbeEnumerable(ProbeEnumerator inner) : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => inner;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ProbeEnumerator : IEnumerator<int>
    {
        internal int MoveCalls;
        internal int DisposeCalls;
        internal Action? OnMove;
        internal Action? OnDispose;
        public int Current => 42;
        object IEnumerator.Current => Current;
        public bool MoveNext() { MoveCalls++; OnMove?.Invoke(); return MoveCalls == 1; }
        public void Dispose() { DisposeCalls++; OnDispose?.Invoke(); }
        public void Reset() => throw new NotSupportedException();
    }
}
