using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class OwnedAsyncCommandTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectCleanupEvidence_IsNotLostWhenExceptionWasAlreadyReported(bool samePrimary)
    {
        var primary = new Exception("execution");
        var cleanup = samePrimary ? primary : new Exception("cleanup");
        var nested = new Exception("nested cleanup");
        ExecutionFailureContexts.Attach(cleanup, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Continue, 19,
            [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, nested)]));
        var failures = new ExecutionFailures();
        failures.Add(primary, ExecutionFailureCause.Unknown, ExecutionFailureStage.CommandExecution);
        failures.AddCleanup(cleanup);
        await Assert.That(failures.HasCleanupFailure).IsTrue();
        await Assert.That(failures.FirstCleanupFailure).IsSameReferenceAs(cleanup);
        var context = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 19);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(samePrimary ? 1 : 2);
        if (!samePrimary) await Assert.That(context.SecondaryFailures[0].Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.SecondaryFailures[^1].Exception).IsSameReferenceAs(nested);
    }

    [Test]
    public async Task SynchronousReaderCleanup_AttemptsBothResourcesWithoutAsyncFallback()
    {
        var factory = new ControlledOwnedCommandFactory();
        var expected = factory.Resource.SyncCleanupFailure = new Exception("sync command cleanup");
        var access = new ControlledAsyncDatabaseAccess();
        var reader = await new OwnedCommandExecution(access, factory).OpenReaderAsync(default);
        var error = await Fails(() => { reader.Dispose(); return Task.CompletedTask; });
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(ExecutionFailureContexts.Get(error)!.SecondaryFailures[0].Exception).IsSameReferenceAs(expected);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
        await Assert.That(access.Reader.SyncCalls).IsEqualTo(1);
        await reader.DisposeAsync();
        reader.Dispose();
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task ValidationPrecedesCancellation_AndNeitherCreatesACommand(string kind)
    {
        var factory = new ControlledOwnedCommandFactory { ValidationFailure = new NotSupportedException("unsupported owned commands") };
        var access = new ControlledAsyncDatabaseAccess();
        var invocation = new OwnedCommandExecution(access, factory);
        await Assert.That(await Fails(() => Execute(invocation, kind, new(true)))).IsSameReferenceAs(factory.ValidationFailure);
        factory.ValidationFailure = null;
        await Assert.That(await Fails(() => Execute(invocation, kind, new(true))) is OperationCanceledException).IsTrue();
        await Assert.That(factory.Creates).IsEqualTo(0);
        await Assert.That(access.Calls).IsEmpty();
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
        await Execute(invocation, kind, CancellationToken.None);
        await Assert.That(factory.Creates).IsEqualTo(1);
    }

    [Test]
    [Arguments("Reader", false)]
    [Arguments("Scalar", false)]
    [Arguments("NonQuery", false)]
    [Arguments("Reader", true)]
    [Arguments("Scalar", true)]
    [Arguments("NonQuery", true)]
    public async Task DispatchFailureOrCancellation_WaitsForIndependentCleanup_PreservingBothErrors(string kind, bool cancel)
    {
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Cleanup = new(paused: true);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true));
        var invocation = new OwnedCommandExecution(access, factory, 17);
        using var cancellation = new CancellationTokenSource();
        var expected = new Exception("dispatch");
        var cleanup = new Exception("command cleanup");
        var pending = Execute(invocation, kind, cancellation.Token);
        await access.Dispatch.Entered.WaitAsync(Timeout);
        if (cancel) cancellation.Cancel();
        else access.Dispatch.Fail(expected);
        await factory.Resource.Cleanup.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(factory.Resource.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
            await Assert.That(await Fails(() => Execute(invocation, kind, new(true)))).IsTypeOf<InvalidOperationException>();
            await Assert.That(factory.Creates).IsEqualTo(1);
        }
        finally { factory.Resource.Cleanup.Fail(cleanup); }
        var error = await Fails(() => pending);
        if (cancel) await Assert.That(error is OperationCanceledException).IsTrue();
        else await Assert.That(error).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Cause).IsEqualTo(cancel ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)17);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures[0].Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(0);
        await Assert.That(factory.Resource.Borrowed.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("Scalar", false)]
    [Arguments("NonQuery", false)]
    [Arguments("Scalar", true)]
    [Arguments("NonQuery", true)]
    public async Task SuccessfulTerminal_WaitsForCleanup_AndDoesNotRetroactivelyCancel(string kind, bool cleanupFails)
    {
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Cleanup = new(paused: true);
        var scalar = new object();
        var access = new ControlledAsyncDatabaseAccess { ScalarResult = scalar, NonQueryResult = 12 };
        using var cancellation = new CancellationTokenSource();
        var invocation = new OwnedCommandExecution(access, factory);
        var pending = kind == "Scalar" ? invocation.ExecuteScalarAsync(cancellation.Token)
            : Box(invocation.ExecuteNonQueryAsync(cancellation.Token));
        await factory.Resource.Cleanup.Entered.WaitAsync(Timeout);
        var cleanup = new Exception("cleanup");
        try
        {
            cancellation.Cancel();
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(factory.Resource.Cleanup.ObservedToken).IsEqualTo(CancellationToken.None);
        }
        finally
        {
            if (cleanupFails) factory.Resource.Cleanup.Fail(cleanup);
            else factory.Resource.Cleanup.Release();
        }
        if (cleanupFails)
        {
            await Assert.That(await Fails(() => pending)).IsSameReferenceAs(cleanup);
            await Assert.That(ExecutionFailureContexts.Get(cleanup)!.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        }
        else if (kind == "Scalar") await Assert.That(await pending).IsSameReferenceAs(scalar);
        else await Assert.That(await pending).IsEqualTo((object)12);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(0);
    }

    [Test]
    [Arguments("Reader")]
    [Arguments("Scalar")]
    [Arguments("NonQuery")]
    public async Task PostConstructionCancellation_DisposesWithoutDispatch_AndValidationStillWins(string kind)
    {
        using var cancellation = new CancellationTokenSource();
        var factory = new ControlledOwnedCommandFactory();
        factory.Creating = () => { cancellation.Cancel(); return factory.Resource; };
        var expected = new NotSupportedException("command unsupported");
        var access = new ControlledAsyncDatabaseAccess { ValidationFailure = expected };
        var first = new OwnedCommandExecution(access, factory);
        await Assert.That(await Fails(() => Execute(first, kind, cancellation.Token))).IsSameReferenceAs(expected);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(first.GetReadFailureEvidence(expected).Effects).IsEqualTo(ExecutionEffects.NoStatement);

        using var secondCancellation = new CancellationTokenSource();
        var secondFactory = new ControlledOwnedCommandFactory();
        secondFactory.Creating = () => { secondCancellation.Cancel(); return secondFactory.Resource; };
        access.ValidationFailure = null;
        var second = new OwnedCommandExecution(access, secondFactory);
        var error = await Fails(() => Execute(second, kind, secondCancellation.Token));
        await Assert.That(error is OperationCanceledException).IsTrue();
        await Assert.That(secondFactory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(access.ObservedCommand).IsNull();
        await Assert.That(ExecutionFailureContexts.Get(error)!.Stage).IsEqualTo(ExecutionFailureStage.Validation);
    }

    [Test]
    public async Task ReaderTransfer_KeepsCommandUntilReaderCleanup_AndRejectsOverlappingDisposal()
    {
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Cleanup = new(paused: true);
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Cleanup = new(paused: true);
        var reader = await new OwnedCommandExecution(access, factory).OpenReaderAsync(default);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
        await Assert.That(await reader.ReadNextRowAsync(default)).IsTrue();
        var pending = reader.DisposeAsync().AsTask();
        await access.Reader.Cleanup.Entered.WaitAsync(Timeout);
        var readerFailure = new Exception("reader cleanup");
        var commandFailure = new Exception("command cleanup");
        try
        {
            await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
            await Assert.That(await Fails(() => reader.DisposeAsync().AsTask())).IsTypeOf<InvalidOperationException>();
            await Assert.That(await Fails(() => { reader.Dispose(); return Task.CompletedTask; })).IsTypeOf<InvalidOperationException>();
        }
        finally { access.Reader.Cleanup.Fail(readerFailure); }
        await factory.Resource.Cleanup.Entered.WaitAsync(Timeout);
        try { await Assert.That(pending.IsCompleted).IsFalse(); }
        finally { factory.Resource.Cleanup.Fail(commandFailure); }
        await Assert.That(await Fails(() => pending)).IsSameReferenceAs(readerFailure);
        var context = ExecutionFailureContexts.Get(readerFailure)!;
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(commandFailure);
        await reader.DisposeAsync();
        reader.Dispose();
        await Assert.That(access.Reader.AsyncDisposeCalls).IsEqualTo(1);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(0);
        await Assert.That(await Fails(() => reader.ReadNextRowAsync(default))).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task ReaderAdvanceAndDisposal_ShareAdmissionWithoutAbandoningWork()
    {
        var factory = new ControlledOwnedCommandFactory();
        var access = new ControlledAsyncDatabaseAccess();
        access.Reader.Advance = new(paused: true);
        var reader = await new OwnedCommandExecution(access, factory).OpenReaderAsync(default);
        var move = reader.ReadNextRowAsync(default);
        await access.Reader.Advance.Entered.WaitAsync(Timeout);
        try
        {
            await Assert.That(await Fails(() => reader.DisposeAsync().AsTask())).IsTypeOf<InvalidOperationException>();
            await Assert.That(await Fails(() => reader.ReadNextRowAsync(default))).IsTypeOf<InvalidOperationException>();
            await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
        }
        finally { access.Reader.Advance.Release(); }
        await Assert.That(await move).IsTrue();
        await reader.DisposeAsync();
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("factory")]
    [Arguments("null-command")]
    [Arguments("command-property")]
    [Arguments("null-reader")]
    public async Task AcquisitionFailures_CleanTransferredResources_WithoutReplay(string phase)
    {
        var factory = new ControlledOwnedCommandFactory();
        var expected = new Exception("setup failure");
        if (phase == "factory") factory.Creating = () => throw expected;
        if (phase == "null-command") factory.Creating = () => null!;
        if (phase == "command-property") factory.Resource.CommandFailure = expected;
        var access = new ControlledAsyncDatabaseAccess();
        if (phase == "null-reader") access.Reader = null!;
        var invocation = new OwnedCommandExecution(access, factory);
        var error = await Fails(() => invocation.OpenReaderAsync(default));
        if (phase is "factory" or "command-property") await Assert.That(error).IsSameReferenceAs(expected);
        else await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(phase is "factory" or "null-command" ? 0 : 1);
        await Assert.That(await Fails(() => invocation.OpenReaderAsync(default))).IsTypeOf<InvalidOperationException>();
        await Assert.That(factory.Creates).IsEqualTo(1);
    }

    [Test]
    public async Task UnusedEnumeration_CreatesNoCommand_AndRepeatEnumerationCreatesFreshOwnership()
    {
        var factories = new System.Collections.Generic.List<ControlledOwnedCommandFactory>();
        var sequence = new AsyncReaderEnumerable<int>(() =>
        {
            var factory = new ControlledOwnedCommandFactory();
            factories.Add(factory);
            return new OwnedCommandExecution(new ControlledAsyncDatabaseAccess(), factory);
        }, reader => reader.GetInt32(0));
        await using (var unused = sequence.GetAsyncEnumerator()) { }
        await Assert.That(factories[0].Creates).IsEqualTo(0);
        for (var i = 0; i < 2; i++)
        {
            var values = new System.Collections.Generic.List<int>();
            await foreach (var value in sequence) values.Add(value);
            await Assert.That(values.SequenceEqual(new[] { 11, 22 })).IsTrue();
        }
        await Assert.That(factories.Skip(1).All(x => x.Creates == 1 && x.Resource.AsyncDisposals == 1)).IsTrue();
    }

    internal static async Task<Exception> Fails(Func<Task> action)
    {
        try { await action().WaitAsync(Timeout); }
        catch (Exception exception) when (exception is not TimeoutException) { return exception; }
        throw new Exception("Expected a failure.");
    }

    private static async Task<object?> Box(Task<int> task) => await task;

    private static async Task Execute(OwnedCommandExecution invocation, string kind, CancellationToken token)
    {
        switch (kind)
        {
            case "Reader": await using (await invocation.OpenReaderAsync(token)) { } break;
            case "Scalar": await invocation.ExecuteScalarAsync(token); break;
            case "NonQuery": await invocation.ExecuteNonQueryAsync(token); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
