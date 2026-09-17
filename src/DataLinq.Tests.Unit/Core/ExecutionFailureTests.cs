using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Core;

public sealed class ExecutionFailureTests
{
    [Test]
    [Arguments("OrdinaryRead", "Confirmed", true, true, 7)]
    [Arguments("OrdinaryRead", "Confirmed", false, true, 5)]
    [Arguments("NoStatement", "Confirmed", true, true, 7)]
    [Arguments("OrdinaryRead", "Unknown", true, true, 6)]
    [Arguments("OrdinaryRead", "Unknown", false, true, 4)]
    [Arguments("Unknown", "Confirmed", true, true, 6)]
    [Arguments("Mutation", "Confirmed", true, true, 6)]
    [Arguments("Initialization", "Confirmed", true, true, 4)]
    [Arguments("OrdinaryRead", "Lost", true, true, 4)]
    [Arguments("OrdinaryRead", "Confirmed", true, false, 4)]
    public async Task RecoveryRequiresEvidenceAndCleanup(string effects, string integrity, bool rollback, bool cleanup, int expected)
    {
        var evidence = new ReadFailureEvidence(ExecutionFailureCause.ProviderError,
            Enum.Parse<ExecutionEffects>(effects), Enum.Parse<TransactionIntegrity>(integrity), rollback);
        await Assert.That((int)ExecutionRecoveryPolicy.ForReadFailure(evidence, cleanup)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("NotAttempted", "Committed", "Committed")]
    [Arguments("NotAttempted", "RolledBack", "RolledBack")]
    [Arguments("NotAttempted", "Unknown", "Unknown")]
    [Arguments("Unknown", "RolledBack", "Unknown")]
    [Arguments("Committed", "Unknown", "Committed")]
    [Arguments("RolledBack", "Unknown", "RolledBack")]
    public async Task RecoveryCannotRewriteEstablishedCompletion(string previous, string observed, string expected)
    {
        var before = new ExecutionFailureContext(ExecutionFailureCause.Cancellation, ExecutionFailureStage.CommandExecution,
            Enum.Parse<ExecutionCompletion>(previous), ExecutionRecoveryActions.Dispose, 17, []);
        var after = before.AfterRecovery(Enum.Parse<ExecutionCompletion>(observed), ExecutionRecoveryActions.None);
        await Assert.That(after.Completion).IsEqualTo(Enum.Parse<ExecutionCompletion>(expected));
        await Assert.That(before.Completion).IsEqualTo(Enum.Parse<ExecutionCompletion>(previous));
        await Assert.That(after.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(after.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(before.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
    }

    [Test]
    public async Task OrderedSecondarySnapshotsPreserveExceptionsAndPrimaryStack()
    {
        var failures = new ExecutionFailures();
        var primary = Capture(ThrowOriginal);
        var aggregate = new AggregateException(new FormatException("first"), new ArgumentException("second"));
        var assessment = new InvalidOperationException("assessment");
        failures.Add(primary, ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution);
        failures.Add(aggregate, ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup);
        var before = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 9);
        failures.Add(assessment, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
        failures.Add(primary, ExecutionFailureCause.Unknown, ExecutionFailureStage.Recovery);
        var after = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 9);
        await Assert.That(before.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(after.SecondaryFailures.Count).IsEqualTo(2);
        await Assert.That(after.SecondaryFailures[0].Exception).IsSameReferenceAs(aggregate);
        await Assert.That(after.SecondaryFailures[1].Exception).IsSameReferenceAs(assessment);
        await Assert.That(after.SecondaryFailures[1].Stage).IsEqualTo(ExecutionFailureStage.Recovery);
        var list = (IList<ExecutionSecondaryFailure>)after.SecondaryFailures;
        await Assert.That(Capture(() => list.Clear())).IsTypeOf<NotSupportedException>();
        var reported = Capture(failures.ThrowIfAny);
        await Assert.That(reported).IsSameReferenceAs(primary);
        await Assert.That(reported.StackTrace!).Contains(nameof(ThrowOriginal));
    }

    [Test]
    public async Task ContextLookupIsDirectAndDoesNotUseExceptionData()
    {
        var exception = new InvalidOperationException("original");
        var context = new ExecutionFailureContext(ExecutionFailureCause.Timeout, ExecutionFailureStage.RowLoading,
            ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, 23, []);
        ExecutionFailureContexts.Attach(exception, context);
        await Assert.That(ExecutionFailureContexts.Get(exception)).IsSameReferenceAs(context);
        await Assert.That(ExecutionFailureContexts.Get(new Exception("outer", exception))).IsNull();
        await Assert.That(ExecutionFailureContexts.Get(new AggregateException(exception))).IsNull();
        await Assert.That(Capture(() => ExecutionFailureContexts.Get(null!))).IsTypeOf<ArgumentNullException>();
        await Assert.That(exception.Data.Count).IsEqualTo(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOriginal() => throw new InvalidOperationException("original dispatch");

    private static Exception Capture(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new Exception("Expected a failure.");
    }
}
