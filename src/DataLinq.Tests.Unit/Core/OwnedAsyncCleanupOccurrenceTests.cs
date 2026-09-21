using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class OwnedAsyncCleanupOccurrenceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    [Arguments("sync", false)]
    [Arguments("sync", true)]
    [Arguments("async", false)]
    [Arguments("async", true)]
    [Arguments("suspended", false)]
    [Arguments("suspended", true)]
    public async Task OwnedCleanup_CommandFailureCannotBorrowSuccessfulReaderCleanupReport(string mode, bool freshReport)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var reused = new Exception("owned command cleanup");
        var phantom = new Exception("earlier nested cleanup");
        ExecutionFailureContext? earlier = null;
        void ReportEarlier()
        {
            earlier = new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
                ExecutionCompletion.Committed, ExecutionRecoveryActions.Continue, 17,
                [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, phantom)],
                operation: ExecutionOperationKind.Commit);
            ExecutionFailureContexts.Attach(reused, earlier);
        }
        var native = new ControlledAsyncDataReader
        {
            AllowSynchronousCalls = true,
            SyncDisposing = ReportEarlier,
            AsyncDisposing = ReportEarlier,
            Cleanup = new(paused: mode == "suspended")
        };
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Disposing = () =>
        {
            if (freshReport)
                ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.ProviderError,
                    ExecutionFailureStage.CommandExecution, ExecutionCompletion.NotAttempted,
                    ExecutionRecoveryActions.Dispose, 17, [], operation: ExecutionOperationKind.Save));
            throw reused;
        };
        var access = new ControlledAsyncDatabaseAccess { Reader = native };
        var owned = await new OwnedCommandExecution(access, factory, 17).OpenReaderAsync(default);
        Exception failure;
        if (mode == "sync")
            failure = await OwnedAsyncCommandTests.Fails(() => { owned.Dispose(); return Task.CompletedTask; });
        else
        {
            var pending = owned.DisposeAsync().AsTask();
            if (mode == "suspended")
            {
                await native.Cleanup.Entered.WaitAsync(Timeout);
                try
                {
                    await Assert.That(pending.IsCompleted).IsFalse();
                    await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(0);
                }
                finally { native.Cleanup.Release(); }
            }
            failure = await OwnedAsyncCommandTests.Fails(() => pending);
        }
        await Assert.That(failure).IsSameReferenceAs(reused);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        await Assert.That(context.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Save : ExecutionOperationKind.Dispose);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)17);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures).IsEmpty();
        var earlierSnapshot = earlier ?? throw new InvalidOperationException("Reader cleanup did not run.");
        await Assert.That(earlierSnapshot.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(earlierSnapshot.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(earlierSnapshot.SecondaryFailures.Single().Exception).IsSameReferenceAs(phantom);
        // The owner stays terminal even when its first disposal reports failure.
        owned.Dispose();
        await owned.DisposeAsync();
        await Assert.That(native.SyncCalls).IsEqualTo(mode == "sync" ? 1 : 0);
        await Assert.That(native.AsyncDisposeCalls).IsEqualTo(mode == "sync" ? 0 : 1);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(mode == "sync" ? 1 : 0);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(mode == "sync" ? 0 : 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnedCleanup_PreservesBothFreshResourceReportsInOrder(bool asynchronous)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var first = new Exception("reader cleanup");
        var second = new Exception("command cleanup");
        void ReportFirst() => ExecutionFailureContexts.Attach(first, new(ExecutionFailureCause.ProviderError,
            ExecutionFailureStage.CommandExecution, ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose,
            17, [], operation: ExecutionOperationKind.KeyLookup));
        var native = new ControlledAsyncDataReader
        {
            AllowSynchronousCalls = true,
            SyncDisposing = ReportFirst,
            SyncDisposalFailure = first,
            Cleanup = new(paused: true) { ReportingFailure = _ => ReportFirst() }
        };
        native.Cleanup.Fail(first);
        var factory = new ControlledOwnedCommandFactory();
        factory.Resource.Disposing = () =>
        {
            ExecutionFailureContexts.Attach(second, new(ExecutionFailureCause.Timeout,
                ExecutionFailureStage.CommandExecution, ExecutionCompletion.NotAttempted,
                ExecutionRecoveryActions.Dispose, 17, [], operation: ExecutionOperationKind.RawCommand));
            throw second;
        };
        var owned = await new OwnedCommandExecution(new ControlledAsyncDatabaseAccess { Reader = native }, factory, 17)
            .OpenReaderAsync(default);
        var failure = await OwnedAsyncCommandTests.Fails(() =>
        {
            if (asynchronous) return owned.DisposeAsync().AsTask();
            owned.Dispose();
            return Task.CompletedTask;
        });
        await Assert.That(failure).IsSameReferenceAs(first);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.KeyLookup);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        var secondary = context.SecondaryFailures.Single();
        await Assert.That(secondary.Exception).IsSameReferenceAs(second);
        await Assert.That(secondary.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(secondary.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(secondary.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        owned.Dispose();
        await owned.DisposeAsync();
        await Assert.That(native.SyncCalls).IsEqualTo(asynchronous ? 0 : 1);
        await Assert.That(native.AsyncDisposeCalls).IsEqualTo(asynchronous ? 1 : 0);
        await Assert.That(factory.Resource.SyncDisposals).IsEqualTo(asynchronous ? 0 : 1);
        await Assert.That(factory.Resource.AsyncDisposals).IsEqualTo(asynchronous ? 1 : 0);
    }
}
