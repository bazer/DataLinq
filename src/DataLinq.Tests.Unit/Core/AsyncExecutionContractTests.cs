using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

/// <summary>
/// W1 orchestration contracts only. No production provider is wired to these internal
/// capabilities, and these tests cannot close the native SQLite W0-F1 finding.
/// </summary>
public class AsyncExecutionContractTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task MissingCapability_IsExplicit_AndDoesNotInferSupportFromDbCommand()
    {
        using var command = new UnverifiedCommand();
        await Assert.That(Capture(() => AsyncDatabaseAccess.Require(command))).IsTypeOf<NotSupportedException>();
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(Capture(() => AsyncDatabaseAccess.Require(null!))).IsTypeOf<ArgumentNullException>();
        var access = new ControlledAsyncDatabaseAccess();
        await Assert.That(AsyncDatabaseAccess.Require(access)).IsSameReferenceAs(access);
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task NullCommand_PrecedesPreCancellation_WithoutProviderValidation(string operation)
    {
        var access = new ControlledAsyncDatabaseAccess();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await CaptureAsync(() => Execute(access, operation, null!, cancellation.Token));

        await Assert.That(failure).IsTypeOf<ArgumentNullException>();
        await Assert.That(access.Calls.IsEmpty).IsTrue();
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task UnverifiedDbCommand_PrecedesPreCancellation_AndNeverDispatches(string operation)
    {
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new UnverifiedCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await CaptureAsync(() => Execute(access, operation, command, cancellation.Token));

        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(access.Calls.ToArray()).IsEquivalentTo(new[] { $"validate:{operation}" });
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task UnsupportedOperation_PrecedesPreCancellation_EvenForKnownCommand(string operation)
    {
        var access = new ControlledAsyncDatabaseAccess { UnsupportedKind = Enum.Parse<AsyncCommandKind>(operation) };
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await CaptureAsync(() => Execute(access, operation, command, cancellation.Token));

        await Assert.That(failure).IsTypeOf<NotSupportedException>();
        await Assert.That(access.Calls.ToArray()).IsEquivalentTo(new[] { $"validate:{operation}" });
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task LifecycleValidationFailure_PrecedesPreCancellation_AndKeepsIdentity(string operation)
    {
        var expected = new InvalidOperationException("Terminal transaction.");
        var access = new ControlledAsyncDatabaseAccess { ValidationFailure = expected };
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await CaptureAsync(() => Execute(access, operation, command, cancellation.Token));

        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(access.Calls.ToArray()).IsEquivalentTo(new[] { $"validate:{operation}" });
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task ValidPreCanceledCommand_DoesNotDispatchOrDisposeBorrowedResources(string operation)
    {
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await CaptureAsync(() => Execute(access, operation, command, cancellation.Token));

        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(access.Calls.ToArray()).IsEquivalentTo(new[] { $"validate:{operation}" });
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task SuspendedDispatch_ReceivesOriginalToken_AndCancelsWithoutSyncFallback(string operation)
    {
        var checkpoint = new AsyncCheckpoint(paused: true);
        var access = new ControlledAsyncDatabaseAccess(checkpoint);
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        var pending = Execute(access, operation, command, cancellation.Token);
        await checkpoint.Entered.WaitAsync(TestTimeout);

        await Assert.That(pending.IsCompleted).IsFalse();
        await Assert.That(checkpoint.ObservedToken).IsEqualTo(cancellation.Token);
        cancellation.Cancel();
        var failure = await CaptureAsync(() => pending);

        await Assert.That(failure is OperationCanceledException).IsTrue();
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(access.ObservedCommand).IsSameReferenceAs(command);
        await Assert.That(access.Calls.ToArray()).IsEquivalentTo(new[] { $"validate:{operation}", $"dispatch:{operation}" });
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task SuspendedProviderFailure_KeepsOriginalException_WithoutReplay(string operation)
    {
        var checkpoint = new AsyncCheckpoint(paused: true);
        var access = new ControlledAsyncDatabaseAccess(checkpoint);
        using var command = new ControlledCommand();
        var expected = new InvalidOperationException("Injected provider failure.");
        var pending = Execute(access, operation, command, CancellationToken.None);
        await checkpoint.Entered.WaitAsync(TestTimeout);
        checkpoint.Fail(expected);

        var failure = await CaptureAsync(() => pending);

        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(access.Calls.Count(c => c.StartsWith("dispatch:", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SuccessfulDispatch_PreservesResults_AndLaterCancellationDoesNotChangeSuccess()
    {
        var scalar = new object();
        var access = new ControlledAsyncDatabaseAccess { ScalarResult = scalar, NonQueryResult = 7 };
        using var command = new ControlledCommand();
        using var cancellation = new CancellationTokenSource();
        var pending = access.ExecuteScalarAsync(command, cancellation.Token);
        await Assert.That(await pending.WaitAsync(TestTimeout)).IsSameReferenceAs(scalar);
        cancellation.Cancel();
        await Assert.That(pending.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await access.ExecuteNonQueryAsync(command, CancellationToken.None)).IsEqualTo(7);
        access.ScalarResult = null;
        await Assert.That(await access.ExecuteScalarAsync(command, CancellationToken.None)).IsNull();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ReaderSource_IsDeferred_AndBorrowsTheOriginalCommand()
    {
        var checkpoint = new AsyncCheckpoint(paused: true);
        var access = new ControlledAsyncDatabaseAccess(checkpoint);
        using var command = new ControlledCommand { CommandText = "initial" };
        IAsyncReaderSource source = new BorrowedCommandReaderSource(access, command);
        await Assert.That(access.Calls.IsEmpty).IsTrue();
        // Borrowed commands have no generated-query parameter snapshot guarantee.
        command.CommandText = "changed before execution";
        var pending = source.OpenReaderAsync(CancellationToken.None);
        await checkpoint.Entered.WaitAsync(TestTimeout);
        await Assert.That(pending.IsCompleted).IsFalse();
        await Assert.That(access.ObservedCommand).IsSameReferenceAs(command);
        await Assert.That(access.ObservedCommand!.CommandText).IsEqualTo("changed before execution");
        checkpoint.Release();
        await using var reader = await pending.WaitAsync(TestTimeout);
        await Assert.That(reader).IsSameReferenceAs(access.Reader);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ReaderSource_RejectsNullInputs_WithoutOpeningAnything()
    {
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new ControlledCommand();
        await Assert.That(Capture(() => new BorrowedCommandReaderSource(null!, command))).IsTypeOf<ArgumentNullException>();
        await Assert.That(Capture(() => new BorrowedCommandReaderSource(access, null!))).IsTypeOf<ArgumentNullException>();
        await Assert.That(access.Calls.IsEmpty).IsTrue();
    }

    [Test]
    public async Task ReaderSource_SequentialOpensDoNotCacheTheReader_OrTakeCommandOwnership()
    {
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new ControlledCommand();
        var source = new BorrowedCommandReaderSource(access, command);
        var first = await source.OpenReaderAsync(CancellationToken.None);
        await first.DisposeAsync();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);

        var nextReader = new ControlledAsyncDataReader();
        access.Reader = nextReader;
        var second = await source.OpenReaderAsync(CancellationToken.None);
        await Assert.That(second).IsSameReferenceAs(nextReader);
        await second.DisposeAsync();

        await Assert.That(access.Calls.Count(c => c == "dispatch:Reader")).IsEqualTo(2);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ControllableReader_PausesAdvancement_AndExposesAnEphemeralCurrentRow()
    {
        var reader = new ControlledAsyncDataReader { Advance = new AsyncCheckpoint(paused: true) };
        await using var owned = reader;
        using var cancellation = new CancellationTokenSource();
        var pending = reader.ReadNextRowAsync(cancellation.Token);
        await reader.Advance.Entered.WaitAsync(TestTimeout);
        await Assert.That(pending.IsCompleted).IsFalse();
        await Assert.That(Capture(() => reader.GetInt32(0))).IsTypeOf<InvalidOperationException>();
        reader.Advance.Release();
        await Assert.That(await pending.WaitAsync(TestTimeout)).IsTrue();
        IDataLinqDataReader borrowed = reader;
        await Assert.That(borrowed.GetInt32(0)).IsEqualTo(11);
        await Assert.That(await reader.ReadNextRowAsync(cancellation.Token)).IsTrue();
        await Assert.That(borrowed.GetInt32(0)).IsEqualTo(22);
        await Assert.That(await reader.ReadNextRowAsync(cancellation.Token)).IsFalse();
        await Assert.That(reader.Advance.ObservedToken).IsEqualTo(cancellation.Token);
        await Assert.That(reader.SyncCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ControllableReader_CancellationDoesNotCancelOwnedAsyncCleanup()
    {
        var reader = new ControlledAsyncDataReader
        {
            Advance = new AsyncCheckpoint(paused: true),
            Cleanup = new AsyncCheckpoint(paused: true)
        };
        using var cancellation = new CancellationTokenSource();
        var pending = reader.ReadNextRowAsync(cancellation.Token);
        await reader.Advance.Entered.WaitAsync(TestTimeout);
        cancellation.Cancel();
        await Assert.That(await CaptureAsync(() => pending) is OperationCanceledException).IsTrue();

        var cleanup = reader.DisposeAsync().AsTask();
        await reader.Cleanup.Entered.WaitAsync(TestTimeout);
        await Assert.That(cleanup.IsCompleted).IsFalse();
        await Assert.That(reader.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        reader.Cleanup.Release();
        await cleanup.WaitAsync(TestTimeout);
        await Assert.That(reader.IsDisposed).IsTrue();
        await Assert.That(reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(reader.SyncCalls).IsEqualTo(0);
    }

    [Test]
    public async Task ControllableReader_CanFailAsyncCleanup_WithoutHidingItsIdentity()
    {
        var reader = new ControlledAsyncDataReader { Cleanup = new AsyncCheckpoint(paused: true) };
        var failure = new InvalidOperationException("Injected cleanup failure.");
        var pending = reader.DisposeAsync().AsTask();
        await reader.Cleanup.Entered.WaitAsync(TestTimeout);
        reader.Cleanup.Fail(failure);
        await Assert.That(await CaptureAsync(() => pending)).IsSameReferenceAs(failure);
        await Assert.That(reader.IsDisposed).IsFalse();
        await Assert.That(reader.SyncCalls).IsEqualTo(0);
    }

    private static Task Execute(IAsyncDatabaseAccess access, string operation, IDbCommand command, CancellationToken cancellationToken)
        => operation switch
        {
            "Reader" => access.ExecuteReaderAsync(command, cancellationToken),
            "Scalar" => access.ExecuteScalarAsync(command, cancellationToken),
            "NonQuery" => access.ExecuteNonQueryAsync(command, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected an exception.");
    }

    private static async Task<Exception> CaptureAsync(Func<Task> action)
    {
        try { await action().WaitAsync(TestTimeout); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected an exception.");
    }
}
