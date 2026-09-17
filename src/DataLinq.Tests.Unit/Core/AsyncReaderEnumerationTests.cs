using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class AsyncReaderEnumerationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Capture_IsPerEnumerator_AndUnusedDisposalDoesNoIo()
    {
        using var command = new ControlledCommand();
        var readers = new List<ControlledAsyncDataReader>();
        var accesses = new List<ControlledAsyncDatabaseAccess>();
        var argument = 1;
        var sequence = new AsyncReaderEnumerable<int>(() =>
        {
            var access = new ControlledAsyncDatabaseAccess { Reader = new([argument]) };
            accesses.Add(access);
            readers.Add(access.Reader);
            return new BorrowedCommandReaderSource(access, command);
        }, reader => reader.GetInt32(0));
        await Assert.That(accesses.Count).IsEqualTo(0);
        var unused = sequence.GetAsyncEnumerator();
        await unused.DisposeAsync();
        await unused.DisposeAsync();
        await Assert.That(await unused.MoveNextAsync()).IsFalse();
        await Assert.That(accesses[0].Calls.IsEmpty).IsTrue();
        await Assert.That(readers[0].AsyncDisposeCalls).IsEqualTo(0);

        argument = 2;
        await using var first = sequence.GetAsyncEnumerator();
        argument = 3;
        await Assert.That(await first.MoveNextAsync()).IsTrue();
        await Assert.That(first.Current).IsEqualTo(2);
        await Assert.That(await first.MoveNextAsync()).IsFalse();
        await using var second = sequence.GetAsyncEnumerator();
        await Assert.That(await second.MoveNextAsync()).IsTrue();
        await Assert.That(second.Current).IsEqualTo(3);
        await Assert.That(accesses.Count).IsEqualTo(3);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("none")]
    [Arguments("method")]
    [Arguments("enumerator")]
    [Arguments("equal")]
    [Arguments("different")]
    public async Task Tokens_AreCombinedOnlyWhenNecessary_AndUnlinkedAfterCleanup(string mode)
    {
        using var method = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var methodToken = mode is "method" or "equal" or "different" ? method.Token : default;
        var enumerationToken = mode == "equal" ? method.Token
            : mode is "enumerator" or "different" ? enumeration.Token : default;
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess();
        await using var rows = Sequence(access, command, methodToken).GetAsyncEnumerator(enumerationToken);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var observed = access.Dispatch.ObservedToken;
        await Assert.That(access.Reader.Advance.ObservedToken).IsEqualTo(observed);
        if (mode == "different")
        {
            await Assert.That(observed).IsNotEqualTo(method.Token);
            await Assert.That(observed).IsNotEqualTo(enumeration.Token);
        }
        else
            await Assert.That(observed).IsEqualTo(methodToken.CanBeCanceled ? methodToken : enumerationToken);
        await rows.DisposeAsync();
        method.Cancel();
        enumeration.Cancel();
        if (mode == "different")
            await Assert.That(observed.IsCancellationRequested).IsFalse();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("method", "acquire")]
    [Arguments("enumerator", "acquire")]
    [Arguments("method", "read")]
    [Arguments("enumerator", "read")]
    [Arguments("method", "buffered")]
    [Arguments("enumerator", "buffered")]
    public async Task EitherToken_CancelsAcquisitionAdvancementAndBufferedRows(string cancel, string stage)
    {
        using var method = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess(new AsyncCheckpoint(paused: stage == "acquire"));
        access.Reader.Advance = new AsyncCheckpoint(paused: stage == "read");
        await using var rows = Sequence(access, command, method.Token).GetAsyncEnumerator(enumeration.Token);
        var move = rows.MoveNextAsync().AsTask();
        if (stage == "acquire") await access.Dispatch.Entered.WaitAsync(Timeout);
        else if (stage == "read") await access.Reader.Advance.Entered.WaitAsync(Timeout);
        else await Assert.That(await move.WaitAsync(Timeout)).IsTrue();
        (cancel == "method" ? method : enumeration).Cancel();
        var failure = await Fails(() => stage == "buffered" ? rows.MoveNextAsync().AsTask() : move);
        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(access.Dispatch.ObservedToken);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(stage == "acquire" ? 0 : 1);
        await Assert.That(access.Reader.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Validation_PrecedesPreCancellation_AndValidCancellationDoesNotDispatch()
    {
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var expected = new NotSupportedException("unsupported reader");
        var access = new ControlledAsyncDatabaseAccess { ValidationFailure = expected };
        await using var invalid = Sequence(access, command, cancellation.Token).GetAsyncEnumerator();
        await Assert.That(await Fails(() => invalid.MoveNextAsync().AsTask())).IsSameReferenceAs(expected);
        access.ValidationFailure = null;
        await using var valid = Sequence(access, command, cancellation.Token).GetAsyncEnumerator();
        await Assert.That(await Fails(() => valid.MoveNextAsync().AsTask()) is OperationCanceledException).IsTrue();
        await Assert.That(access.Calls.Any(call => call.StartsWith("dispatch:"))).IsFalse();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationAfterSuccessfulAcquisition_DisposesTransferredReaderBeforeReporting()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new ControlledAsyncDataReader { Cleanup = new AsyncCheckpoint(paused: true) };
        var sequence = new AsyncReaderEnumerable<int>(() => new CallbackSource(() =>
        {
            cancellation.Cancel(); // Provider completes successfully despite the request.
            return Task.FromResult<IAsyncDataReader>(reader);
        }), row => row.GetInt32(0), cancellationToken: cancellation.Token);
        await using var rows = sequence.GetAsyncEnumerator();
        var move = rows.MoveNextAsync().AsTask();
        await reader.Cleanup.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(move.IsCompleted).IsFalse();
            await Assert.That(reader.AsyncReadCalls).IsEqualTo(0);
        }
        finally { reader.Cleanup.Release(); }
        await Assert.That(await Fails(() => move) is OperationCanceledException).IsTrue();
        await Assert.That(reader.AsyncDisposeCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments("read")]
    [Arguments("materialize")]
    [Arguments("cancel")]
    public async Task PrimaryFailure_SurvivesCleanupFailure_WithBothOriginalExceptions(string stage)
    {
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        var access = new ControlledAsyncDatabaseAccess();
        var primary = new FormatException("row failure");
        var cleanup = new InvalidOperationException("reader cleanup failure");
        access.Reader.Cleanup = new AsyncCheckpoint(paused: true);
        access.Reader.Cleanup.Fail(cleanup);
        if (stage == "read")
        {
            access.Reader.Advance = new AsyncCheckpoint(paused: true);
            access.Reader.Advance.Fail(primary);
        }
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command), reader =>
        {
            if (stage == "materialize") throw primary;
            cancellation.Cancel();
            return reader.GetInt32(0);
        }, cancellationToken: cancellation.Token);
        await using var rows = (AsyncReaderEnumerator<int>)sequence.GetAsyncEnumerator();
        var reported = await Fails(() => rows.MoveNextAsync().AsTask());
        if (stage == "cancel") await Assert.That(reported is OperationCanceledException).IsTrue();
        else await Assert.That(reported).IsSameReferenceAs(primary);
        await Assert.That(reported.StackTrace).IsNotNull();
        await Assert.That(rows.Failure!.Cause).IsSameReferenceAs(reported);
        await Assert.That(rows.Failure.CleanupFailure).IsSameReferenceAs(cleanup);
        await rows.DisposeAsync();
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("dispose")]
    [Arguments("exhaustion")]
    public async Task CleanupOnlyFailure_IsReportedOnce_AndLeavesNoCurrentRow(string exit)
    {
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess { Reader = new([11]) };
        var failure = new InvalidOperationException("cleanup");
        access.Reader.Cleanup = new AsyncCheckpoint(paused: true);
        access.Reader.Cleanup.Fail(failure);
        await using var rows = (AsyncReaderEnumerator<int>)Sequence(access, command).GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(await Fails(() => exit == "dispose" ? rows.DisposeAsync().AsTask() : rows.MoveNextAsync().AsTask()))
            .IsSameReferenceAs(failure);
        await Assert.That(rows.Failure!.Cause).IsSameReferenceAs(failure);
        await Assert.That(rows.Failure.CleanupFailure).IsNull();
        await Assert.That(Capture(() => _ = rows.Current)).IsTypeOf<InvalidOperationException>();
        await rows.DisposeAsync();
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task AwaitForeachBreak_AwaitsCleanup_AndKeepsBorrowedCommand()
    {
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Cleanup = new AsyncCheckpoint(paused: true);
        var consumer = Consume();
        await access.Reader.Cleanup.Entered.WaitAsync(Timeout);
        try { await Assert.That(consumer.IsCompleted).IsFalse(); }
        finally { access.Reader.Cleanup.Release(); }
        await consumer.WaitAsync(Timeout);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(0);
        async Task Consume()
        {
            await foreach (var value in Sequence(access, command))
            {
                await Assert.That(value).IsEqualTo(11);
                break;
            }
        }
    }

    [Test]
    public async Task IndependentRootEnumerators_CanBePendingTogether()
    {
        using var command = new ControlledCommand();
        var first = new ControlledAsyncDatabaseAccess(new AsyncCheckpoint(paused: true));
        var second = new ControlledAsyncDatabaseAccess(new AsyncCheckpoint(paused: true));
        await using var one = Sequence(first, command).GetAsyncEnumerator();
        using var otherCommand = new ControlledCommand();
        await using var two = Sequence(second, otherCommand).GetAsyncEnumerator();
        var moveOne = one.MoveNextAsync().AsTask();
        var moveTwo = two.MoveNextAsync().AsTask();
        await Task.WhenAll(first.Dispatch.Entered, second.Dispatch.Entered).WaitAsync(Timeout);
        first.Dispatch.Release();
        second.Dispatch.Release();
        await Assert.That(await moveOne.WaitAsync(Timeout)).IsTrue();
        await Assert.That(await moveTwo.WaitAsync(Timeout)).IsTrue();
    }

    [Test]
    public async Task ReentrantMaterialization_RejectsCallsBeforeChangingCurrentOrCleanup()
    {
        using var command = new ControlledCommand();
        var access = new ControlledAsyncDatabaseAccess();
        IAsyncEnumerator<int>? rows = null;
        var sequence = new AsyncReaderEnumerable<int>(() => new BorrowedCommandReaderSource(access, command), reader =>
        {
            _ = Capture(() => rows!.MoveNextAsync());
            _ = Capture(() => rows!.DisposeAsync());
            _ = Capture(() => _ = rows!.Current);
            return reader.GetInt32(0);
        });
        await using var owned = rows = sequence.GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current).IsEqualTo(11);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
    }

    private static AsyncReaderEnumerable<int> Sequence(ControlledAsyncDatabaseAccess access,
        ControlledCommand command, CancellationToken token = default) =>
        new(() => new BorrowedCommandReaderSource(access, command), reader => reader.GetInt32(0), cancellationToken: token);

    private sealed class CallbackSource(Func<Task<IAsyncDataReader>> open) : IAsyncReaderSource
    {
        public void Validate() { }
        public Task<IAsyncDataReader> OpenReaderAsync(CancellationToken token) => open();
    }

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (InvalidOperationException exception) { return exception; }
        throw new Exception("Expected overlap/current-position rejection.");
    }

    private static async Task<Exception> Fails(Func<Task> action)
    {
        try { await action().WaitAsync(Timeout); }
        catch (Exception exception) when (exception is not TimeoutException) { return exception; }
        throw new Exception("Expected failure.");
    }
}
