using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TelemetryAllocation_MutationMeasurementsSurviveSuppressedActivityAndLateListeners(bool counterFails)
    {
        using var fixture = new ScriptedFixture();
        using var caller = new Activity("late-mutation-listener-caller").Start();
        using var diagnostics = ExecutionFailureScope.Begin();
        var expected = new Exception("late mutation counter");
        var failCounter = counterFails;
        var dimensions = DataLinqTelemetryContext.FromProvider(fixture.Provider);
        var telemetry = new MutationExecutionTelemetry(dimensions, fixture.RowTable.DbName,
            TransactionChangeType.Update, TransactionType.ReadAndWrite, ExecutionOperationKind.Save);
        ExecutionFailures? failures = null;

        // Exercise the explicit decision without depending on test-host listeners.
        telemetry.Start(createActivity: false);
        using var activities = new MutationActivityProbe();
        using var metrics = new MutationMeterProbe(name =>
        {
            if (failCounter && name == "datalinq.db.mutations") throw expected;
        });
        telemetry.Complete(ref failures, succeeded: true, affectedRows: 3);
        if (counterFails)
        {
            await Assert.That(failures!.Primary).IsSameReferenceAs(expected);
            var context = failures.Snapshot(new(), ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
            await Assert.That(context.HasCleanupFailure).IsFalse();
        }
        else await Assert.That(failures).IsNull();
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(metrics.Reports.All(report => report.Outcome == "success")).IsTrue();
        await Assert.That(metrics.Reports.Single(report => report.Name == "datalinq.db.mutation.affected_rows").Value).IsEqualTo(3d);
        await Assert.That(metrics.Reports.Single(report => report.Name == "datalinq.db.mutation.duration").Value).IsGreaterThanOrEqualTo(0);
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(MutationCounts(fixture).AffectedRows).IsEqualTo(3);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        telemetry.Complete(ref failures, succeeded: true, affectedRows: 3);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(1);

        failCounter = false;
        telemetry = new(dimensions, fixture.RowTable.DbName, TransactionChangeType.Update,
            TransactionType.ReadAndWrite, ExecutionOperationKind.Save);
        failures = null;
        telemetry.Start();
        telemetry.Complete(ref failures, succeeded: true, affectedRows: 3);
        await Assert.That(failures).IsNull();
        await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo("success");
        await Assert.That(metrics.Reports.Count).IsEqualTo(6);
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(2);
        await Assert.That(MutationCounts(fixture).AffectedRows).IsEqualTo(6);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task TelemetryAllocation_TransactionCompletionSurvivesLateMeterListeners(bool asyncStart, bool counterFails)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("late-transaction-listener-caller").Start();
        using var diagnostics = ExecutionFailureScope.Begin();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        if (asyncStart) access.BeginAsyncTransactionTelemetry(ExecutionOperationKind.Query);
        else access.StartSyncTelemetryForTest();
        await Assert.That(TransactionCounts(fixture).Starts).IsEqualTo(1);
        var expected = new Exception("late transaction counter");
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (counterFails && name == "datalinq.db.transactions.completed") throw expected;
        });
        var failures = new ExecutionFailures();
        // This is the shared telemetry component, not a simulated native commit.
        access.CompleteAsyncTransactionTelemetry(ExecutionCompletion.Committed, failures, ExecutionOperationKind.Commit);
        if (counterFails)
        {
            await Assert.That(failures.Primary).IsSameReferenceAs(expected);
            var context = failures.Snapshot(new(), ExecutionCompletion.Committed, ExecutionRecoveryActions.Dispose, null);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
            await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Committed);
        }
        else await Assert.That(failures.Primary).IsNull();
        await Assert.That(metrics.Reports.Select(report => report.Name).SequenceEqual(
            ["datalinq.db.transactions.completed", "datalinq.db.transaction.duration"])).IsTrue();
        await Assert.That(metrics.Reports.All(report => report.Outcome == "commit")).IsTrue();
        await Assert.That(TransactionCounts(fixture).Starts).IsEqualTo(1);
        await Assert.That(TransactionCounts(fixture).Commits).IsEqualTo(1);
        await Assert.That(TransactionCounts(fixture).Failures).IsEqualTo(0);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        access.CompleteAsyncTransactionTelemetry(ExecutionCompletion.Committed, failures, ExecutionOperationKind.Commit);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
        await Assert.That(TransactionCounts(fixture).Commits).IsEqualTo(1);
    }

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
