using System;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Core;

public sealed class ExecutionFailureOccurrenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MaterializationOccurrences_CheckpointRetiresOnlyEarlierAttachment(bool freshReport)
    {
        using var diagnostics = ExecutionFailureScope.Begin();
        var exception = new Exception("reused");
        var earlier = Context(ExecutionFailureCause.Timeout);
        ExecutionFailureContexts.Attach(exception, earlier);
        var captured = new ExecutionFailures();
        captured.AddReported(exception, ExecutionFailureStage.RowLoading);
        var checkpoint = ExecutionFailureContexts.CaptureOccurrence();
        var current = Context(ExecutionFailureCause.ProviderError);
        if (freshReport) ExecutionFailureContexts.Attach(exception, current);
        // A report for another exception must not make the old attachment current.
        var unrelated = new Exception("unrelated");
        ExecutionFailureContexts.Attach(unrelated, Context(ExecutionFailureCause.Unknown));
        ExecutionFailureContexts.DiscardEarlierReport(exception, checkpoint);
        await Assert.That(ExecutionFailureContexts.Get(exception)).IsSameReferenceAs(freshReport ? current : null);
        await Assert.That(captured.PrimaryContext).IsSameReferenceAs(earlier);
        await Assert.That(captured.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, null).Cause)
            .IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(earlier.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(ExecutionFailureContexts.Get(unrelated)).IsNotNull();
    }

    [Test]
    public async Task MaterializationOccurrences_CheckpointDoesNotBypassInvocationScope()
    {
        using var root = ExecutionFailureScope.Begin();
        var exception = new Exception("sibling report");
        var checkpoint = ExecutionFailureContexts.CaptureOccurrence();
        ExecutionFailureContext sibling;
        using (ExecutionFailureScope.Begin())
        {
            sibling = Context(ExecutionFailureCause.ProviderError);
            ExecutionFailureContexts.Attach(exception, sibling);
        }
        using (ExecutionFailureScope.Begin())
        {
            ExecutionFailureContexts.DiscardEarlierReport(exception, checkpoint);
            await Assert.That(ExecutionFailureContexts.Get(exception)).IsSameReferenceAs(sibling);
            await Assert.That(ExecutionFailureContexts.GetCurrent(exception)).IsNull();
        }
    }

    [Test]
    public async Task MaterializationOccurrences_CheckpointCaptureAllocatesNoPerRowObject()
    {
        for (var i = 0; i < 10000; i++) _ = ExecutionFailureContexts.CaptureOccurrence();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) _ = ExecutionFailureContexts.CaptureOccurrence();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsEqualTo(0L);
    }

    private static ExecutionFailureContext Context(ExecutionFailureCause cause) => new(cause,
        ExecutionFailureStage.RowLoading, ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, null, [],
        operation: ExecutionOperationKind.Query);
}
