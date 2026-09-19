using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed class ExecutionCorrelationTests
{
    [Test]
    public async Task Correlation_PrimaryIdentitySurvivesOuterReportingAndRecoveryWithoutChangingEarlierSnapshots()
    {
        var primary = new Exception("query failed");
        var original = new ExecutionFailureContext(ExecutionFailureCause.ProviderError, ExecutionFailureStage.RowLoading,
            ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose, 17, [],
            operation: ExecutionOperationKind.KeyLookup, providerInstanceId: "inner-provider");
        ExecutionFailureContexts.Attach(primary, original);
        var failures = new ExecutionFailures();
        failures.AddReported(primary, ExecutionFailureStage.Callback, ExecutionFailureCause.ApplicationError,
            ExecutionOperationKind.TransactionCallback);
        // A later report on the exception cannot mutate what this collector observed.
        ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup,
            ExecutionCompletion.Unknown, ExecutionRecoveryActions.None, 99, [], operation: ExecutionOperationKind.Dispose,
            providerInstanceId: "replacement"));
        var observed = failures.Snapshot(new(), ExecutionCompletion.RolledBack, ExecutionRecoveryActions.None, 17,
            ExecutionOperationKind.TransactionCallback, "inner-provider");
        var recovered = observed.AfterRecovery(ExecutionCompletion.Unknown, ExecutionRecoveryActions.Dispose);
        await Assert.That(observed.Operation).IsEqualTo(ExecutionOperationKind.KeyLookup);
        await Assert.That(observed.ProviderInstanceId).IsEqualTo("inner-provider");
        await Assert.That(observed.TransactionId).IsEqualTo((uint?)17);
        await Assert.That(observed.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(observed.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(observed.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(observed.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(recovered.Operation).IsEqualTo(observed.Operation);
        await Assert.That(recovered.ProviderInstanceId).IsEqualTo(observed.ProviderInstanceId);
        await Assert.That(recovered.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(original.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(Capture(failures.ThrowIfAny)).IsSameReferenceAs(primary);
    }

    [Test]
    public async Task Correlation_SecondarySnapshotsKeepTheirOriginalOperationEvenWhenExceptionContextChanges()
    {
        var primary = new Exception("primary");
        var secondary = new AggregateException(new Exception("unflattened"));
        ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 7,
            [new(ExecutionFailureCause.Timeout, ExecutionFailureStage.Recovery, secondary, ExecutionOperationKind.Rollback)],
            operation: ExecutionOperationKind.Update, providerInstanceId: "original"));
        ExecutionFailureContexts.Attach(secondary, new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Callback,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.TransactionCallback));
        var failures = new ExecutionFailures();
        failures.AddReported(primary, ExecutionFailureStage.Callback);
        failures.AddCleanup(primary); // Same exception object must still establish failed cleanup.
        failures.AddCleanup(secondary);
        var context = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 7);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Update);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Operation).IsEqualTo(ExecutionOperationKind.Rollback);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(secondary);
        await Assert.That(context.SecondaryFailures[0].Cause).IsEqualTo(ExecutionFailureCause.Timeout);
    }

    [Test]
    public async Task Correlation_SecondaryIdentityCannotReplaceUnclassifiedPrimaryAndUnknownIsNotInferredFromText()
    {
        var failures = new ExecutionFailures();
        failures.Add(new InvalidOperationException("commit timeout cancellation query"), ExecutionFailureCause.Unknown, ExecutionFailureStage.Callback);
        var cleanup = new Exception("cleanup");
        ExecutionFailureContexts.Attach(cleanup, new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup,
            ExecutionCompletion.Unknown, ExecutionRecoveryActions.None, 999, [], operation: ExecutionOperationKind.Dispose,
            providerInstanceId: "secondary-provider"));
        failures.AddCleanup(cleanup);
        var context = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.None, 8,
            ExecutionOperationKind.TransactionCallback, "outer-provider");
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.TransactionCallback);
        await Assert.That(context.ProviderInstanceId).IsEqualTo("outer-provider");
        await Assert.That(context.TransactionId).IsEqualTo((uint?)8);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(context.SecondaryFailures[0].Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(failures.Snapshot(new(), ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null).Operation)
            .IsEqualTo(ExecutionOperationKind.Unknown);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correlation_ReusedExceptionCannotImportForeignTransactionOrProviderIdentity(bool sameTransaction)
    {
        var reused = new Exception("reused by provider");
        ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.Unknown, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.RolledBack, ExecutionRecoveryActions.FinishActiveOperation, sameTransaction ? 8u : 999u, [],
            operation: ExecutionOperationKind.KeyLookup, providerInstanceId: "old-provider", activeOperation: ExecutionOperationKind.Commit));
        var failures = new ExecutionFailures();
        failures.AddReported(reused, ExecutionFailureStage.Recovery, fallbackOperation: ExecutionOperationKind.Rollback);
        var current = failures.Snapshot(new(), ExecutionCompletion.Unknown, ExecutionRecoveryActions.None, 8,
            ExecutionOperationKind.TransactionCallback, "current-provider");
        await Assert.That(current.TransactionId).IsEqualTo((uint?)8);
        await Assert.That(current.ProviderInstanceId).IsEqualTo("current-provider");
        await Assert.That(current.Operation).IsEqualTo(ExecutionOperationKind.Rollback);
        await Assert.That(current.ActiveOperation).IsNull();
        await Assert.That(current.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(Capture(failures.ThrowIfAny)).IsSameReferenceAs(reused);
    }

    [Test]
    [Arguments("enter")]
    [Arguments("busy")]
    [Arguments("active")]
    public async Task Correlation_OverlapHasItsOwnImmutableContextWithoutChangingOwnership(string entry)
    {
        var gate = new TransactionOperationGate(41, "provider-41");
        var first = gate.Enter("opaque label", operationKind: ExecutionOperationKind.RelationLoad);
        using var transferred = first.Transfer();
        first.Dispose();
        var failure = Capture(() =>
        {
            if (entry == "enter") gate.Enter("another opaque label", operationKind: ExecutionOperationKind.Commit);
            else if (entry == "busy") gate.ThrowIfBusy("another opaque label", ExecutionOperationKind.Commit);
            else gate.ThrowIfActive("another opaque label", ExecutionOperationKind.Commit);
        });
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(context.ActiveOperation).IsEqualTo((ExecutionOperationKind?)ExecutionOperationKind.RelationLoad);
        await Assert.That(context.ProviderInstanceId).IsEqualTo("provider-41");
        await Assert.That(context.TransactionId).IsEqualTo((uint?)41);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.FinishActiveOperation);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.InvalidOperation);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        using (var step = gate.EnterStep(transferred))
        {
            await Assert.That(step.Kind).IsEqualTo(ExecutionOperationKind.RelationLoad);
            await Assert.That(step.ProviderInstanceId).IsEqualTo("provider-41");
        }
        transferred.Dispose();
        using var next = gate.Enter("next", operationKind: ExecutionOperationKind.Insert);
        await Assert.That(context.ActiveOperation).IsEqualTo((ExecutionOperationKind?)ExecutionOperationKind.RelationLoad);
        await Assert.That(context.AfterRecovery(ExecutionCompletion.RolledBack, ExecutionRecoveryActions.None).ActiveOperation)
            .IsEqualTo(context.ActiveOperation);
    }

    [Test]
    public async Task Correlation_HelperOwnershipAndClosedAdmissionDoNotAdvertiseRecoveryWithoutActiveWork()
    {
        var gate = new TransactionOperationGate(52, "provider-52");
        var helper = gate.BeginHelperLifetime();
        var borrowed = ExecutionFailureContexts.Get(Capture(() => gate.Enter("commit", completion: true,
            operationKind: ExecutionOperationKind.Commit)))!;
        await Assert.That(borrowed.ActiveOperation).IsNull();
        await Assert.That(borrowed.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        var duplicate = ExecutionFailureContexts.Get(Capture(() => gate.BeginHelperLifetime()))!;
        await Assert.That(duplicate.Operation).IsEqualTo(ExecutionOperationKind.TransactionCallback);
        using (await helper.CloseAndDrainAsync(new())) { }
        var closed = ExecutionFailureContexts.Get(Capture(() => gate.Enter("query", operationKind: ExecutionOperationKind.Query)))!;
        await Assert.That(closed.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(closed.ActiveOperation).IsNull();
        await Assert.That(closed.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(closed.ProviderInstanceId).IsEqualTo("provider-52");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correlation_CallbackRecoveryPreservesInnerOperationAndOrderedCleanup(bool inner)
    {
        var gate = new TransactionOperationGate(63, "provider-63");
        var primary = new Exception("callback or query");
        var rollback = new Exception("rollback");
        var disposal = new Exception("dispose");
        if (inner) ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.MaterializationError,
            ExecutionFailureStage.RowLoading, ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 63, [],
            operation: ExecutionOperationKind.Query, providerInstanceId: "provider-63"));
        var resource = new ControlledHelperTransaction { Rollback = Fault(rollback), TransactionCleanup = Fault(disposal) };
        var failure = await FailureAsync(() => TransactionCallbackRunner.RunAsync<int>(gate, resource, new(), 63,
            _ => Task.FromException<int>(primary)));
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(context.Operation).IsEqualTo(inner ? ExecutionOperationKind.Query : ExecutionOperationKind.TransactionCallback);
        await Assert.That(context.Cause).IsEqualTo(inner ? ExecutionFailureCause.MaterializationError : ExecutionFailureCause.ApplicationError);
        await Assert.That(context.ProviderInstanceId).IsEqualTo("provider-63");
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Select(x => x.Operation).SequenceEqual(
            [ExecutionOperationKind.Rollback, ExecutionOperationKind.Dispose])).IsTrue();
        await Assert.That(context.SecondaryFailures.Select(x => x.Exception).SequenceEqual([rollback, disposal])).IsTrue();
        await Assert.That(resource.Calls.ToArray()).IsEquivalentTo(new[] { "rollback", "dispose-transaction", "dispose-connection" });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Correlation_HelperCommitOrFinalizationFailureRetainsCommitIdentity(bool local)
    {
        var expected = new Exception("completion failed");
        var resource = new ControlledHelperTransaction();
        if (local) resource.FinalizationFailure = expected;
        else resource.Commit = Fault(expected);
        var failure = await FailureAsync(() => TransactionCallbackRunner.RunAsync<int>(new(74, "provider-74"), resource, new(), 74,
            _ => Task.FromResult(1)));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(context.ProviderInstanceId).IsEqualTo("provider-74");
        await Assert.That(context.Completion).IsEqualTo(local ? ExecutionCompletion.Committed : ExecutionCompletion.Unknown);
        await Assert.That(context.Cause).IsEqualTo(local ? ExecutionFailureCause.LocalFinalizationError : ExecutionFailureCause.Unknown);
    }

    private static AsyncCheckpoint Fault(Exception failure)
    { var checkpoint = new AsyncCheckpoint(paused: true); checkpoint.Fail(failure); return checkpoint; }

    private static Exception Capture(Action action)
    { try { action(); } catch (Exception failure) { return failure; } throw new Exception("Expected a failure."); }

    private static async Task<Exception> FailureAsync(Func<Task> action)
    { try { await action(); } catch (Exception failure) { return failure; } throw new Exception("Expected a failure."); }
}
