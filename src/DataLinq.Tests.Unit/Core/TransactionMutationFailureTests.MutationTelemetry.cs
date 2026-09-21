using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments("provider")]
    [Arguments("provider-cleanup")]
    [Arguments("cleanup")]
    [Arguments("hydration")]
    [Arguments("success")]
    public async Task AsyncMutationTelemetry_ReportingFailuresPreservePrimaryAndAttemptAllReports(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("mutation-caller").Start();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var primary = new Exception("mutation " + phase);
        var commandCleanup = new Exception("owned command cleanup");
        var counter = new Exception("counter");
        var affected = new Exception("affected rows");
        var duration = new Exception("duration");
        var stop = new Exception("stop");
        using var activities = new MutationActivityProbe(stopping: _ => throw stop);
        using var metrics = new MutationMeterProbe(name =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "listener edit");
            throw name switch { "datalinq.db.mutations" => counter, "datalinq.db.mutation.affected_rows" => affected, _ => duration };
        });
        void Assess()
        {
            if (Activity.Current?.IsStopped == true) throw new Exception("Stopped mutation activity leaked into assessment.");
        }
        var access = new ControlledAsyncDatabaseAccess(phase is "provider" or "provider-cleanup" ? JournalFault(primary) : null)
        {
            NonQueryResult = 1,
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true),
            AssessingFailure = Assess
        };
        var factory = EnableAsyncMutations(fixture, access, [1, "stored"]);
        if (phase == "cleanup") factory.ConfigureCommand = command => command.Resource.Cleanup = JournalFault(primary);
        if (phase == "provider-cleanup") factory.ConfigureCommand = command => command.Resource.Cleanup = JournalFault(commandCleanup);
        if (phase == "hydration") fixture.Scenario.AsyncSqlReaders = RawFactory(() => new(JournalFault(primary))
            { FailureEvidence = TrustedScalarRead, AssessingFailure = Assess });
        var failure = await AsyncEnumerationFailureOf(() => transaction.SaveAsyncCore(mutable));
        await Assert.That(failure).IsSameReferenceAs(phase == "success" ? counter : primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        var hasAffectedRows = phase is "success" or "hydration";
        var reports = hasAffectedRows ? new Exception[] { counter, affected, duration, stop } : [counter, duration, stop];
        var secondary = phase == "success" ? reports.Skip(1).ToArray()
            : phase == "provider-cleanup" ? new[] { commandCleanup }.Concat(reports).ToArray() : reports;
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual(secondary)).IsTrue();
        await Assert.That(context.SecondaryFailures.Skip(phase == "provider-cleanup" ? 1 : 0).All(item => item.Stage == ExecutionFailureStage.Notification &&
            item.Cause == ExecutionFailureCause.LocalFinalizationError && item.Operation == ExecutionOperationKind.Save)).IsTrue();
        await Assert.That(context.Operation).IsEqualTo(phase == "cleanup" ? ExecutionOperationKind.Dispose : ExecutionOperationKind.Save);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.Recovery).IsEqualTo(phase is "cleanup" or "provider-cleanup"
            ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(metrics.Reports.Count).IsEqualTo(hasAffectedRows ? 3 : 2);
        await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo("failure");
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(MutationCounts(fixture).Failures).IsEqualTo(phase == "success" ? 0 : 1);
        using (transaction.ExecutionGate.Enter("reporting settled")) { }
        mutable["Value"] = "reservation released";
    }

    [Test, NotInParallel]
    [Arguments("sample")]
    [Arguments("start")]
    public async Task AsyncMutationTelemetry_StartFailureIsLocalAndDoesNotPoisonUndispatchedWork(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("mutation-caller").Start();
        var expected = new Exception("mutation activity " + phase);
        using var activities = new MutationActivityProbe(starting: phase == "start" ? _ => throw expected : null,
            samplingFailure: phase == "sample" ? expected : null);
        using var metrics = new MutationMeterProbe();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var factory = EnableAsyncMutations(fixture);
        var failure = await AsyncEnumerationFailureOf(() => transaction.SaveAsyncCore(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(factory.Commands.Single().Creates).IsEqualTo(0);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
        await Assert.That(metrics.Reports.All(item => item.Outcome == "failure")).IsTrue();
        await Assert.That(activities.Stopped.Count).IsEqualTo(phase == "start" ? 1 : 0);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        _ = transaction.Query();
        mutable["Value"] = "reservation released";
    }

    [Test, NotInParallel]
    [Arguments("insert", true)]
    [Arguments("update", true)]
    [Arguments("save-new", true)]
    [Arguments("save-existing", true)]
    [Arguments("delete", true)]
    [Arguments("generated", true)]
    [Arguments("unchanged-save", true)]
    [Arguments("unchanged-save", false)]
    public async Task AsyncMutationTelemetry_SuccessKeepsStatementDimensionsAndCountsOnce(string kind, bool observed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("mutation-caller").Start();
        using var activities = observed ? new MutationActivityProbe() : null;
        using var metrics = observed ? new MutationMeterProbe() : null;
        var access = new ControlledAsyncDatabaseAccess { ScalarResult = 1L, NonQueryResult = 1 };
        var factory = EnableAsyncMutations(fixture, access, [1, "stored"]);
        if (kind == "generated") await transaction.SaveAsyncCore(fixture.CreateNewAutoMutable("submitted"));
        else
        {
            var mutable = kind is "insert" or "save-new" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
            if (mutable.IsNew()) mutable["Id"] = 1;
            if (kind != "unchanged-save") mutable["Value"] = "submitted";
            await RunCorrelationMutation(kind, transaction, mutable);
        }
        var statement = kind is "insert" or "save-new" or "generated" ? "insert" : kind == "delete" ? "delete" : "update";
        var counts = MutationCounts(fixture);
        await Assert.That(counts.TotalExecutions).IsEqualTo(1);
        await Assert.That(counts.Failures).IsEqualTo(0);
        await Assert.That(counts.AffectedRows).IsEqualTo(kind == "unchanged-save" ? 0 : 1);
        await Assert.That(factory.Commands.Sum(command => command.Resource.AsyncDisposals)).IsEqualTo(kind == "unchanged-save" ? 0 : 1);
        await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(0);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        if (observed)
        {
            var activity = activities!.Stopped.Single();
            await Assert.That(activity.Parent).IsSameReferenceAs(caller);
            await Assert.That(activity.GetTagItem("datalinq.mutation.type")).IsEqualTo(statement);
            await Assert.That(activity.GetTagItem("datalinq.outcome")).IsEqualTo("success");
            await Assert.That(activity.GetTagItem("db.operation.rows_affected")).IsEqualTo(kind == "unchanged-save" ? 0 : 1);
            await Assert.That(metrics!.Reports.Count).IsEqualTo(kind == "unchanged-save" ? 2 : 3);
            await Assert.That(metrics.Reports.All(report => report.Outcome == "success")).IsTrue();
        }
        transaction.Commit();
    }

    [Test, NotInParallel]
    public async Task AsyncMutationTelemetry_CompletionWaitsForCommandAndHydrationCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var activities = new MutationActivityProbe();
        using var metrics = new MutationMeterProbe();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var factory = EnableAsyncMutations(fixture);
        var commandCleanup = new AsyncCheckpoint(paused: true);
        var reader = new ControlledRowDataReader([1, "stored"]) { Cleanup = new(paused: true) };
        factory.ConfigureCommand = command => command.Resource.Cleanup = commandCleanup;
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead });
        var pending = transaction.SaveAsyncCore(mutable);
        try
        {
            await commandCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(metrics.Reports).IsEmpty();
            await Assert.That(activities.Stopped).IsEmpty();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            commandCleanup.Release();
            await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(metrics.Reports).IsEmpty();
            await Assert.That(activities.Stopped).IsEmpty();
            _ = Capture<InvalidOperationException>(() => mutable["Value"] = "cleanup edit");
        }
        finally { commandCleanup.Release(); reader.Cleanup.Release(); await pending; }
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(mutable["Value"]).IsEqualTo("stored");
    }

    [Test, NotInParallel]
    [Arguments("start-second")]
    [Arguments("counter-first")]
    [Arguments("cancel-second")]
    public async Task AsyncMutationTelemetry_BatchReportingFailurePoisonsWrittenPrefixOnly(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var expected = new Exception(phase);
        var starts = 0;
        using var activities = new MutationActivityProbe(starting: _ =>
        {
            if (++starts == 2 && phase == "start-second") throw expected;
        }, stopping: _ => { if (phase == "cancel-second") cancellation.Cancel(); });
        using var metrics = new MutationMeterProbe(name =>
        {
            if (phase == "counter-first" && name == "datalinq.db.mutations") throw expected;
        });
        var first = new Mutable<TransactionMutationGuardRow>();
        first["Id"] = 1; first["Value"] = "first";
        var second = new Mutable<TransactionMutationGuardRow>();
        second["Id"] = 2; second["Value"] = "second";
        var factory = EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var failure = await AsyncEnumerationFailureOf(() => transaction.InsertAsyncCore([first, second], cancellation.Token));
        if (phase == "cancel-second") await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Insert);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(first.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(second.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.NoneForNew);
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(1);
        await Assert.That(factory.Commands[1].Creates).IsEqualTo(0);
        await Assert.That(activities.Stopped.Count).IsEqualTo(phase == "start-second" ? 2 : 1);
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(phase == "start-second" ? 2 : 1);
        _ = Capture<Exception>(transaction.Commit);
        first["Value"] = "released"; second["Value"] = "released";
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMutationTelemetry_UnchangedReadReportingFailureDoesNotPoison(bool load)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        if (!load) fixture.PrimeCommittedRow(1, "cached");
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("mutation-caller").Start();
        var expected = new Exception("stop");
        using var activities = new MutationActivityProbe(stopping: _ => throw expected);
        using var metrics = new MutationMeterProbe();
        var factory = EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var mutable = fixture.CreateExistingMutable(1, "old");
        var failure = await AsyncEnumerationFailureOf(() => transaction.SaveAsyncCore(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(factory.Commands).IsEmpty();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        _ = transaction.Query();
    }

    [Test, NotInParallel]
    public async Task AsyncMutationTelemetry_HelperPreservesReportingFailureThroughRollbackAndCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var expected = new Exception("mutation stop");
        var rollback = new Exception("rollback");
        var cleanup = new Exception("connection cleanup");
        var completion = fixture.Scenario.AsyncCompletion = new()
            { Rollback = JournalFault(rollback), ConnectionCleanup = JournalFault(cleanup) };
        using var activities = new MutationActivityProbe(stopping: _ => throw expected);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var failure = await AsyncEnumerationFailureOf(() => fixture.Database.SaveAsyncCore(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([rollback, cleanup])).IsTrue();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(completion.Calls.SequenceEqual(["rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
    }

    [Test, NotInParallel]
    public async Task AsyncMutationTelemetry_CancellationRemainsPrimaryUntilCleanupAndReportingSettle()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var counter = new Exception("counter");
        var stop = new Exception("stop");
        using var activities = new MutationActivityProbe(stopping: _ => throw stop);
        using var metrics = new MutationMeterProbe(name => { if (name == "datalinq.db.mutations") throw counter; });
        var mutable = fixture.CreateExistingMutable(1, "old");
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { FailureEvidence = TrustedScalarRead };
        var factory = EnableAsyncMutations(fixture, access);
        var cleanup = new AsyncCheckpoint(paused: true);
        factory.ConfigureCommand = command => command.Resource.Cleanup = cleanup;
        var pending = transaction.DeleteAsyncCore(mutable, cancellation.Token);
        try
        {
            await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(metrics.Reports).IsEmpty();
            await Assert.That(activities.Stopped).IsEmpty();
        }
        finally { access.Dispatch.Release(); cleanup.Release(); }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Delete);
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([counter, stop])).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(metrics.Reports.All(item => item.Outcome == "failure")).IsTrue();
        await Assert.That(transaction.IsPoisoned).IsTrue();
    }

    [Test, NotInParallel]
    [Arguments("empty")]
    [Arguments("pre-canceled")]
    [Arguments("validation")]
    public async Task AsyncMutationTelemetry_UnstartedInputsDoNotEmit(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var activities = new MutationActivityProbe();
        using var metrics = new MutationMeterProbe();
        var factory = EnableAsyncMutations(fixture);
        if (phase == "empty") await transaction.InsertAsyncCore(Array.Empty<Mutable<TransactionMutationGuardRow>>());
        else
        {
            if (phase == "validation") factory.ConfigureCommand = command => command.ValidationFailure = new Exception("invalid input");
            var mutable = fixture.CreateExistingMutable(1, "old");
            _ = await AsyncEnumerationFailureOf(() => transaction.DeleteAsyncCore(mutable, new(phase == "pre-canceled")));
            mutable["Value"] = "released";
        }
        await Assert.That(metrics.Reports).IsEmpty();
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(0);
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(0);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        _ = transaction.Query();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMutationTelemetry_CancellationAfterLocalSuccessDoesNotUndoResult(bool delete)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        using var activities = new MutationActivityProbe(stopping: _ => cancellation.Cancel());
        using var metrics = new MutationMeterProbe();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        if (delete) await transaction.DeleteAsyncCore(mutable, cancellation.Token);
        else await transaction.SaveAsyncCore(mutable, cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(metrics.Reports.All(item => item.Outcome == "success")).IsTrue();
        transaction.Commit();
    }

    [Test, NotInParallel]
    public async Task AsyncMutationTelemetry_ReusedExecutionCleanupAndListenerExceptionRetainsCleanupRestriction()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("reused failure");
        using var activities = new MutationActivityProbe(stopping: _ => throw expected);
        using var metrics = new MutationMeterProbe(_ => throw expected);
        var factory = EnableAsyncMutations(fixture, new(JournalFault(expected)) { FailureEvidence = TrustedScalarRead });
        factory.ConfigureCommand = command => command.Resource.Cleanup = JournalFault(expected);
        var failure = await AsyncEnumerationFailureOf(() => transaction.DeleteAsyncCore(fixture.CreateExistingMutable(1, "old")));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Delete);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
    }

    private static MutationMetricsSnapshot MutationCounts(ScriptedFixture fixture) =>
        DataLinqMetrics.Snapshot().Providers.SingleOrDefault(provider => provider.ProviderInstanceId == fixture.Provider.TelemetryInstanceId).Mutations;

    private sealed class MutationActivityProbe : IDisposable
    {
        internal List<Activity> Stopped { get; } = [];
        private readonly ActivityListener listener;
        internal MutationActivityProbe(Action<Activity>? starting = null, Action<Activity>? stopping = null, Exception? samplingFailure = null)
        {
            listener = new()
            {
                ShouldListenTo = source => source.Name == "DataLinq",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => options.Name == "datalinq.db.mutation" && samplingFailure is not null
                    ? throw samplingFailure : ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => { if (activity.OperationName == "datalinq.db.mutation") starting?.Invoke(activity); },
                ActivityStopped = activity => { if (activity.OperationName == "datalinq.db.mutation") { Stopped.Add(activity); stopping?.Invoke(activity); } }
            };
            ActivitySource.AddActivityListener(listener);
        }
        public void Dispose() => listener.Dispose();
    }

    private sealed class MutationMeterProbe : IDisposable
    {
        internal List<(string Name, string? Outcome, double Value)> Reports { get; } = [];
        private readonly MeterListener listener = new();
        internal MutationMeterProbe(Action<string>? recorded = null)
        {
            listener.InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == "DataLinq" && instrument.Name is "datalinq.db.mutations" or
                    "datalinq.db.mutation.affected_rows" or "datalinq.db.mutation.duration") meter.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                Reports.Add((instrument.Name, tags.ToArray().Single(tag => tag.Key == "datalinq.outcome").Value as string, value));
                recorded?.Invoke(instrument.Name);
            });
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                Reports.Add((instrument.Name, tags.ToArray().Single(tag => tag.Key == "datalinq.outcome").Value as string, value));
                recorded?.Invoke(instrument.Name);
            });
            listener.Start();
        }
        public void Dispose() => listener.Dispose();
    }
}
