using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TelemetryAllocation_QueryMeasurementsSurviveSuppressedActivityAndLateListeners(bool counterFails)
    {
        using var fixture = new ScriptedFixture();
        using var caller = new Activity("late-listener-caller").Start();
        using var diagnostics = ExecutionFailureScope.Begin();
        var expected = new Exception("late query counter");
        var failCounter = counterFails;
        var dimensions = QueryTelemetryContext.Capture(fixture.Provider.ReadOnlyAccess, fixture.RowTable.DbName, scalar: true);
        var telemetry = new QueryExecutionTelemetry(dimensions);
        ExecutionFailures? failures = null;

        // The test host can install activity listeners. Exercise the explicit
        // no-activity decision without assuming the process has no listeners.
        telemetry.Start(createActivity: false);
        using var activities = new QueryActivityProbe();
        using var metrics = new QueryMeterProbe(name =>
        {
            if (failCounter && name == "datalinq.queries") throw expected;
        });
        telemetry.Complete(ref failures, succeeded: true);
        if (counterFails)
        {
            await Assert.That(failures!.Primary).IsSameReferenceAs(expected);
            var context = failures.Snapshot(new(), ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
            await Assert.That(context.HasCleanupFailure).IsFalse();
        }
        else await Assert.That(failures).IsNull();
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("success");
        await Assert.That(metrics.Durations.Single().Value).IsGreaterThanOrEqualTo(0);
        await Assert.That(QueryCounts(fixture).ScalarExecutions).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);

        // Repeated completion remains once-only, including a failed reporter.
        telemetry.Complete(ref failures, succeeded: true);
        await Assert.That(metrics.Counters.Count).IsEqualTo(1);
        await Assert.That(metrics.Durations.Count).IsEqualTo(1);

        // A prior no-activity decision cannot suppress the next query's listener.
        failCounter = false;
        telemetry = new(dimensions);
        failures = null;
        telemetry.Start();
        telemetry.Complete(ref failures, succeeded: true);
        await Assert.That(failures).IsNull();
        await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo("success");
        await Assert.That(metrics.Counters.Count).IsEqualTo(2);
        await Assert.That(metrics.Durations.Count).IsEqualTo(2);
        await Assert.That(QueryCounts(fixture).ScalarExecutions).IsEqualTo(2);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
    }
}
