using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("acquire")]
    [Arguments("advance")]
    [Arguments("cleanup")]
    public async Task AsyncEnumeration_RetainsSharedAdmissionAcrossSuspensionAndBetweenRows(string phase)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var pause = new AsyncCheckpoint(paused: true);
        var access = new ControlledAsyncDatabaseAccess(phase == "acquire" ? pause : null);
        if (phase == "advance") access.Reader.Advance = pause;
        if (phase == "cleanup") access.Reader.Cleanup = pause;
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction);
        await using var rows = sequence.GetAsyncEnumerator();
        _ = transaction.Query(); // Cold construction has not acquired admission.
        Task pending;
        if (phase == "cleanup")
        {
            await Assert.That(await rows.MoveNextAsync()).IsTrue();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            pending = rows.DisposeAsync().AsTask();
        }
        else
            pending = rows.MoveNextAsync().AsTask();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            _ = Capture<InvalidOperationException>(() => rows.MoveNextAsync());
            _ = Capture<InvalidOperationException>(() => rows.DisposeAsync());
            _ = Capture<InvalidOperationException>(() => _ = rows.Current);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Commit);
            _ = Capture<InvalidOperationException>(transaction.Rollback);
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await using var other = sequence.GetAsyncEnumerator();
            await Assert.That(await AsyncEnumerationFailureOf(() => other.MoveNextAsync().AsTask()))
                .IsTypeOf<InvalidOperationException>();
            await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(phase == "cleanup" ? 1 : 0);
            await Assert.That(pending.IsCompleted).IsFalse();
        }
        finally { pause.Release(); await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
        if (phase != "cleanup")
        {
            await Assert.That(rows.Current).IsEqualTo(11);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await rows.DisposeAsync();
        }
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncEnumeration_CancellationAwaitsIndependentCleanup_BeforeReleasingAdmission()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Advance = new AsyncCheckpoint(paused: true);
        access.Reader.Cleanup = new AsyncCheckpoint(paused: true);
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await access.Reader.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await access.Reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await Assert.That(access.Reader.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally { access.Reader.Cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending) is OperationCanceledException).IsTrue();
        using var gateAvailable = transaction.ExecutionGate.Enter("verify release only, not provider trust");
    }

    [Test]
    [Arguments("empty")]
    [Arguments("acquisition-failure")]
    [Arguments("cleanup-failure")]
    public async Task AsyncEnumeration_AllTerminalExitsReleaseAdmissionWithoutOwningTransaction(string exit)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess(new AsyncCheckpoint(paused: exit == "acquisition-failure")) { Reader = new([]) };
        var expected = new InvalidOperationException("injected " + exit);
        if (exit == "acquisition-failure") access.Dispatch.Fail(expected);
        if (exit == "cleanup-failure")
        {
            access.Reader.Cleanup = new AsyncCheckpoint(paused: true);
            access.Reader.Cleanup.Fail(expected);
        }
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction);
        await using var rows = sequence.GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        if (exit == "empty") await Assert.That(await pending).IsFalse();
        else await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await rows.DisposeAsync();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(exit == "acquisition-failure" ? 0 : 1);
        await Assert.That(transaction.IsDisposed).IsFalse();
        // This checks admission, not recovery classification of a future native provider.
        using var gateAvailable = transaction.ExecutionGate.Enter("next owner");
    }

    [Test]
    public async Task AsyncEnumeration_CapturedCompletedSourceFailsBeforeCancellationAndDispatch()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess();
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        transaction.Commit();
        cancellation.Cancel();
        await Assert.That(await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask()))
            .IsTypeOf<InvalidOperationException>();
        await Assert.That(access.Calls.IsEmpty).IsTrue();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncEnumeration_UncanceledLaterInvocationCanFollowPreCanceledAttempt()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess();
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command),
            reader => reader.GetInt32(0), transaction);
        cancellation.Cancel();
        await using var canceled = sequence.GetAsyncEnumerator(cancellation.Token);
        await Assert.That(await AsyncEnumerationFailureOf(() => canceled.MoveNextAsync().AsTask()) is OperationCanceledException).IsTrue();
        _ = transaction.Query();
        await using var later = sequence.GetAsyncEnumerator();
        await Assert.That(await later.MoveNextAsync()).IsTrue();
        await later.DisposeAsync();
        _ = transaction.Query();
    }

    private static async Task<Exception> AsyncEnumerationFailureOf(Func<Task> action)
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception exception) when (exception is not TimeoutException) { return exception; }
        throw new Exception("Expected asynchronous enumeration failure.");
    }

    [Test]
    public async Task AsyncEnumeration_CancellationDoesNotAbandonUncooperativeAcquisition()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var pause = new AsyncCheckpoint(paused: true);
        var reader = new ControlledAsyncDataReader();
        var sequence = new AsyncReaderEnumerable<int>(() => new UncooperativeReaderSource(pause, reader),
            row => row.GetInt32(0), transaction, cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        var move = rows.MoveNextAsync().AsTask();
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        try
        {
            await Assert.That(move.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => rows.DisposeAsync());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await Assert.That(reader.AsyncDisposeCalls).IsEqualTo(0);
        }
        finally { pause.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => move) is OperationCanceledException).IsTrue();
        await Assert.That(reader.AsyncReadCalls).IsEqualTo(0);
        await Assert.That(reader.AsyncDisposeCalls).IsEqualTo(1);
        using var gateAvailable = transaction.ExecutionGate.Enter("verify release after actual completion");
    }

    private sealed class UncooperativeReaderSource(AsyncCheckpoint pause, IAsyncDataReader reader) : IAsyncReaderSource
    {
        public void Validate() { }
        public async Task<IAsyncDataReader> OpenReaderAsync(CancellationToken cancellationToken)
        {
            await pause.ReachAsync(CancellationToken.None).ConfigureAwait(false);
            return reader;
        }
    }
}
