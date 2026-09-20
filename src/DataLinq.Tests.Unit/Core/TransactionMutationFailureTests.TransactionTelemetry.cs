using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private sealed partial class ScriptedDatabaseTransaction
    {
        internal async Task StartTelemetryForTestAsync(bool asyncStart = false)
        {
            if (asyncStart) BeginAsyncTransactionTelemetry(ExecutionOperationKind.Query);
            else BeginTransactionTelemetry();
            await Task.CompletedTask;
        }
    }

    [Test, NotInParallel]
    [Arguments(false, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, false)]
    [Arguments(false, true, true)]
    [Arguments(true, true, true)]
    public async Task AsyncTransactionTelemetry_ConfirmedOutcomeSurvivesEveryReportingFailure(bool rollback, bool statusFails, bool managedStatusFails)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        using var caller = new Activity("transaction-caller").Start();
        var status = new Exception("status notification");
        var managedStatus = new Exception("managed status notification");
        var counter = new Exception("completion counter");
        var duration = new Exception("transaction duration");
        var stop = new Exception("transaction stop");
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (name == "datalinq.db.transactions.started") return;
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            throw name == "datalinq.db.transactions.completed" ? counter : duration;
        });
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync();
        if (statusFails) transaction.DatabaseAccess.OnStatusChanged += (_, _) => throw status;
        if (managedStatusFails) transaction.OnStatusChanged += (_, _) => throw managedStatus;
        var failure = await AsyncEnumerationFailureOf(() => rollback ? transaction.RollbackAsyncCore() : transaction.CommitAsyncCore());
        await Assert.That(failure).IsSameReferenceAs(statusFails ? status : counter);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
        await Assert.That(context.Operation).IsEqualTo(rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Finalization);
        var secondary = managedStatusFails ? new Exception[] { managedStatus, counter, duration, stop }
            : statusFails ? [counter, duration, stop] : [duration, stop];
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual(secondary)).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        var outcome = rollback ? "rollback" : "commit";
        await Assert.That(metrics.Reports.Where(report => report.Name != "datalinq.db.transactions.started").All(report => report.Outcome == outcome)).IsTrue();
        await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo(outcome);
        await Assert.That(activities.Stopped[0].Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        var counts = TransactionCounts(fixture);
        await Assert.That(counts.Commits).IsEqualTo(rollback ? 0 : 1);
        await Assert.That(counts.Rollbacks).IsEqualTo(rollback ? 1 : 0);
        await Assert.That(counts.Failures).IsEqualTo(0);
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.Count(call => call == "rollback")).IsEqualTo(rollback ? 1 : 0);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    public async Task AsyncTransactionTelemetry_CommittedLocalFailureStillClosesTelemetry()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "delete"));
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe();
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync();
        var expected = new Exception("committed publication");
        fixture.RowCache.SubscribeToChanges(new ThrowingNotification(expected));
        var failure = await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore());
        await Assert.That(failure).IsTypeOf<TransactionCommitFinalizationException>();
        await Assert.That(failure.InnerException).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo("commit");
        await Assert.That(activities.Stopped[0].Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(TransactionCounts(fixture).Commits).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.Contains("rollback")).IsFalse();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncTransactionTelemetry_UncertainCompletionClosesOnceAndKeepsPrimary(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("native confirmation lost");
        var provider = fixture.Scenario.AsyncCompletion = new();
        if (rollback) provider.Rollback = JournalFault(expected);
        else provider.Commit = JournalFault(expected);
        var transaction = fixture.Database.Transaction();
        var stop = new Exception("stop");
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe();
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync();
        var failure = await AsyncEnumerationFailureOf(() => rollback ? transaction.RollbackAsyncCore() : transaction.CommitAsyncCore());
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(stop);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo("failure");
        await Assert.That(TransactionCounts(fixture).Failures).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
    }

    [Test, NotInParallel]
    public async Task AsyncTransactionTelemetry_InterruptedReportingCannotReportCompletionAgain()
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("counter");
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe(name => { if (name == "datalinq.db.transactions.completed") throw expected; });
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync();
        _ = await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore());
        transaction.DatabaseAccess.NotifyConfirmedAsyncCompletion(new());
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(TransactionCounts(fixture).Commits).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
    }

    [Test, NotInParallel]
    [Arguments("sample")]
    [Arguments("start")]
    [Arguments("counter")]
    public async Task AsyncTransactionTelemetry_FirstUseObserverFailureWaitsForCleanupAndIsTerminal(string phase)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("initialization observer " + phase);
        using var caller = new Activity("initialization-caller").Start();
        using var activities = new TransactionActivityProbe(starting: phase == "start" ? _ => throw expected : null,
            samplingFailure: phase == "sample" ? expected : null);
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (phase == "counter" && name == "datalinq.db.transactions.started") throw expected;
        });
        var resource = new ControlledTransactionResource
        {
            Initialized = () => transaction.DatabaseAccess.BeginAsyncTransactionTelemetry(ExecutionOperationKind.Query),
            Cleanup = new(paused: true)
        };
        var lazy = BindInitialization(fixture, transaction, resource);
        var access = new ControlledAsyncDatabaseAccess();
        using var command = new ControlledCommand();
        await using var rows = InitializedRows(lazy, access, command, transaction).GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(lazy.PublishedResource).IsNull();
            await Assert.That(access.ObservedCommand).IsNull();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { resource.Cleanup.Release(); }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(TransactionCounts(fixture).Starts).IsEqualTo(1);
        await Assert.That(TransactionCounts(fixture).Failures).IsEqualTo(1);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Count).IsEqualTo(phase == "sample" ? 0 : 1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await transaction.DisposeAsyncCore();
        await Assert.That(fixture.Scenario.AsyncCompletion!.Calls.Contains("rollback")).IsFalse();
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncTransactionTelemetry_SuccessCountsConfirmedOutcomeWithOrWithoutObservers(bool rollback, bool observed)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        using var caller = new Activity("transaction-caller").Start();
        using var activities = observed ? new TransactionActivityProbe() : null;
        using var metrics = observed ? new TransactionMeterProbe() : null;
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync(asyncStart: true);
        if (rollback) await transaction.RollbackAsyncCore(); else await transaction.CommitAsyncCore();
        var counts = TransactionCounts(fixture);
        await Assert.That(counts.Starts).IsEqualTo(1);
        await Assert.That(counts.Commits).IsEqualTo(rollback ? 0 : 1);
        await Assert.That(counts.Rollbacks).IsEqualTo(rollback ? 1 : 0);
        await Assert.That(counts.Failures).IsEqualTo(0);
        if (observed)
        {
            await Assert.That(metrics!.Reports.Count).IsEqualTo(3);
            await Assert.That(activities!.Stopped.Single().Parent).IsSameReferenceAs(caller);
            await Assert.That(activities.Stopped[0].Status).IsEqualTo(ActivityStatusCode.Ok);
        }
        await transaction.DisposeAsyncCore();
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncTransactionTelemetry_UnusedCompletionDoesNotInventNativeTransaction(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { InitializationState = TransactionInitializationState.Unused };
        var transaction = fixture.Database.Transaction();
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe();
        if (rollback) await transaction.RollbackAsyncCore(); else await transaction.CommitAsyncCore();
        await transaction.DisposeAsyncCore();
        await Assert.That(TransactionCounts(fixture).Starts).IsEqualTo(0);
        await Assert.That(metrics.Reports).IsEmpty();
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(provider.Calls.SequenceEqual(["dispose-transaction", "dispose-connection"])).IsTrue();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncTransactionTelemetry_ManagedStatusFailureClosesWithConfirmedOutcome(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("managed status");
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe();
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync(asyncStart: true);
        transaction.OnStatusChanged += (_, _) => throw expected;
        var failure = await AsyncEnumerationFailureOf(() => rollback ? transaction.RollbackAsyncCore() : transaction.CommitAsyncCore());
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
        await Assert.That(activities.Stopped.Single().Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(activities.Stopped[0].GetTagItem("datalinq.outcome")).IsEqualTo(rollback ? "rollback" : "commit");
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await transaction.DisposeAsyncCore();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncTransactionTelemetry_HelperCleanupRetainsLaterCallerActivity(bool stopFails)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        using var originalCaller = new Activity("original-caller").Start();
        var expected = new Exception("stop");
        using var activities = new TransactionActivityProbe(stopping: _ => { if (stopFails) throw expected; });
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync(asyncStart: true);
        originalCaller.Stop();
        using var laterCaller = new Activity("later-caller").Start();
        Activity? cleanupCaller = null;
        provider.DisposeResource = _ => { cleanupCaller = Activity.Current; return ValueTask.CompletedTask; };
        var pending = transaction.RunCallbackAsyncCore(_ => Task.FromResult(17), new());
        if (stopFails) await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        else await Assert.That(await pending).IsEqualTo(17);
        await Assert.That(cleanupCaller).IsSameReferenceAs(laterCaller);
        await Assert.That(Activity.Current).IsSameReferenceAs(laterCaller);
        await Assert.That(activities.Stopped.Single().Parent).IsSameReferenceAs(originalCaller);
        await Assert.That(provider.Calls.Contains("rollback")).IsFalse();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncTransactionTelemetry_DisposalWithoutConfirmationDoesNotInventRollback(bool cleanupFails)
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("transaction cleanup");
        var provider = fixture.Scenario.AsyncCompletion = new()
        {
            RecoveryActions = ExecutionRecoveryActions.Dispose,
            TransactionCleanup = cleanupFails ? JournalFault(expected) : new()
        };
        var transaction = fixture.Database.Transaction();
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe();
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync(asyncStart: true);
        var pending = transaction.DisposeAsyncCore().AsTask();
        if (cleanupFails) await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        else await pending;
        await Assert.That(provider.Calls.SequenceEqual(["dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(TransactionCounts(fixture).Rollbacks).IsEqualTo(0);
        await Assert.That(TransactionCounts(fixture).Failures).IsEqualTo(1);
        await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo("failure");
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await transaction.DisposeAsyncCore();
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncTransactionTelemetry_CanceledCompletionRemainsUnknownAfterReporting(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var pause = new AsyncCheckpoint(paused: true);
        if (rollback) provider.Rollback = pause; else provider.Commit = pause;
        using var cancellation = new CancellationTokenSource();
        var transaction = fixture.Database.Transaction();
        var stop = new Exception("stop");
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe();
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync(asyncStart: true);
        var pending = rollback ? transaction.RollbackAsyncCore(cancellation.Token) : transaction.CommitAsyncCore(cancellation.Token);
        await pause.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var failure = await AsyncEnumerationFailureOf(() => pending);
        pause.Release();
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(stop);
        await transaction.DisposeAsyncCore();
        await Assert.That(TransactionCounts(fixture).Failures).IsEqualTo(1);
        await Assert.That(TransactionCounts(fixture).Commits).IsEqualTo(0);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncTransactionTelemetry_CancellationDuringConfirmedReportingCannotUndoOutcome(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (name == "datalinq.db.transactions.completed") cancellation.Cancel();
        });
        await ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).StartTelemetryForTestAsync(asyncStart: true);
        if (rollback) await transaction.RollbackAsyncCore(cancellation.Token); else await transaction.CommitAsyncCore(cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(transaction.Status).IsEqualTo(rollback ? DatabaseTransactionStatus.RolledBack : DatabaseTransactionStatus.Committed);
        await Assert.That(activities.Stopped.Single().Status).IsEqualTo(ActivityStatusCode.Ok);
        await Assert.That(TransactionCounts(fixture).Failures).IsEqualTo(0);
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.Count(call => call == "rollback")).IsEqualTo(rollback ? 1 : 0);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    private static TransactionMetricsSnapshot TransactionCounts(ScriptedFixture fixture) =>
        DataLinqMetrics.Snapshot().Providers.SingleOrDefault(provider => provider.ProviderInstanceId == fixture.Provider.TelemetryInstanceId).Transactions;

    private sealed class TransactionActivityProbe : IDisposable
    {
        internal List<Activity> Stopped { get; } = [];
        private readonly ActivityListener listener;
        internal TransactionActivityProbe(Action<Activity>? starting = null, Action<Activity>? stopping = null, Exception? samplingFailure = null)
        {
            listener = new()
            {
                ShouldListenTo = source => source.Name == "DataLinq",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => options.Name == "datalinq.db.transaction" && samplingFailure is not null
                    ? throw samplingFailure : ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => { if (activity.OperationName == "datalinq.db.transaction") starting?.Invoke(activity); },
                ActivityStopped = activity => { if (activity.OperationName == "datalinq.db.transaction") { Stopped.Add(activity); stopping?.Invoke(activity); } }
            };
            ActivitySource.AddActivityListener(listener);
        }
        public void Dispose() => listener.Dispose();
    }

    private sealed class TransactionMeterProbe : IDisposable
    {
        internal List<(string Name, string? Outcome)> Reports { get; } = [];
        private readonly MeterListener listener = new();
        internal TransactionMeterProbe(Action<string>? recorded = null)
        {
            listener.InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == "DataLinq" && instrument.Name is "datalinq.db.transactions.started" or
                    "datalinq.db.transactions.completed" or "datalinq.db.transaction.duration") meter.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                Reports.Add((instrument.Name, tags.ToArray().SingleOrDefault(tag => tag.Key == "datalinq.outcome").Value as string));
                recorded?.Invoke(instrument.Name);
            });
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                Reports.Add((instrument.Name, tags.ToArray().Single(tag => tag.Key == "datalinq.outcome").Value as string));
                recorded?.Invoke(instrument.Name);
            });
            listener.Start();
        }
        public void Dispose() => listener.Dispose();
    }
}
