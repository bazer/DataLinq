using System;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Core;

public sealed class ExecutionAttributionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Attribution_OnlyCurrentAndDescendantReportsContributeFacts(bool child)
    {
        using var outer = ExecutionFailureScope.Begin();
        var failure = new Exception("reused");
        var cleanup = new Exception("cleanup");
        using (ExecutionFailureScope.Begin()) Report(failure, cleanup);
        using (ExecutionFailureScope.Begin())
        {
            if (child)
            {
                using var nested = ExecutionFailureScope.Begin();
                Report(failure, cleanup);
                await Task.Yield();
            }
            var failures = new ExecutionFailures();
            failures.AddReported(failure, ExecutionFailureStage.Callback, ExecutionFailureCause.ApplicationError, ExecutionOperationKind.TransactionCallback);
            var current = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.None, 1);
            await Assert.That(current.Cause).IsEqualTo(child ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.ApplicationError);
            await Assert.That(current.Operation).IsEqualTo(child ? ExecutionOperationKind.Query : ExecutionOperationKind.TransactionCallback);
            await Assert.That(current.HasCleanupFailure).IsEqualTo(child);
            await Assert.That(current.SecondaryFailures.Count).IsEqualTo(child ? 1 : 0);
        }
    }

    [Test]
    public async Task Attribution_ScopeRestoresCallerAcrossSuspensionAndGrantsNoAdmission()
    {
        var before = ExecutionFailureScope.Current;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TransactionOperationGate(1);
        using var owner = gate.Enter("active");
        var pending = Child();
        await entered.Task;
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(before);
        resume.SetResult();
        await pending;
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(before);

        async Task Child()
        {
            using var scope = ExecutionFailureScope.Begin();
            var marker = ExecutionFailureScope.Current;
            entered.SetResult();
            await resume.Task.ConfigureAwait(false);
            await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(marker);
            Exception? conflict = null;
            try { gate.Enter("competing"); }
            catch (Exception failure) { conflict = failure; }
            await Assert.That(conflict).IsTypeOf<InvalidOperationException>();
            await Assert.That(ExecutionFailureContexts.Get(conflict!)!.Recovery).IsEqualTo(ExecutionRecoveryActions.FinishActiveOperation);
        }
    }

    [Test]
    public async Task Attribution_RecoveryCannotImportCallbackFactsFromSameException()
    {
        using var callback = ExecutionFailureScope.Begin();
        var original = new Exception("callback");
        var reused = new Exception("earlier query then rollback");
        using (ExecutionFailureScope.Begin()) Report(reused, new Exception("old cleanup"));
        var failures = new ExecutionFailures();
        failures.Add(original, ExecutionFailureCause.ApplicationError, ExecutionFailureStage.Callback, ExecutionOperationKind.TransactionCallback);
        using (ExecutionFailureScope.Begin())
            failures.AddReported(reused, ExecutionFailureStage.Recovery, ExecutionFailureCause.Timeout, ExecutionOperationKind.Rollback);
        var context = failures.Snapshot(new(), ExecutionCompletion.Unknown, ExecutionRecoveryActions.None, 1);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Operation).IsEqualTo(ExecutionOperationKind.Rollback);
        await Assert.That(context.SecondaryFailures[0].Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(context.HasCleanupFailure).IsFalse();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Attribution_RootCleanupSeparatesSiblingFactsAndKeepsFreshSecondaryEvidence(bool asynchronous, bool fresh)
    {
        var primary = new Exception("first resource");
        var secondary = new Exception("second resource");
        var oldCleanup = new Exception("unrelated earlier cleanup");
        var root = new OwnedRootDisposal(() =>
        [
            RootCleanupStep.Local(() => { Report(secondary, oldCleanup); throw primary; }),
            RootCleanupStep.Local(() =>
            {
                if (fresh) ExecutionFailureContexts.Attach(secondary,
                    new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, ExecutionCompletion.NotApplicable,
                        ExecutionRecoveryActions.None, null, [], operation: ExecutionOperationKind.Dispose));
                throw secondary;
            })
        ]);
        Exception? failure = null;
        try { if (asynchronous) await root.DisposeAsync(); else root.Dispose(); }
        catch (Exception error) { failure = error; }
        await Assert.That(failure).IsSameReferenceAs(primary);
        var observed = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(observed.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(observed.SecondaryFailures[0].Exception).IsSameReferenceAs(secondary);
        await Assert.That(observed.SecondaryFailures[0].Cause).IsEqualTo(fresh ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        await Assert.That(observed.SecondaryFailures[0].Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(observed.HasCleanupFailure).IsTrue();
    }

    private static void Report(Exception failure, Exception cleanup) => ExecutionFailureContexts.Attach(failure,
        new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.RowLoading, ExecutionCompletion.NotAttempted,
            ExecutionRecoveryActions.Dispose, 1,
            [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, cleanup, ExecutionOperationKind.Dispose)],
            operation: ExecutionOperationKind.Query));
}
