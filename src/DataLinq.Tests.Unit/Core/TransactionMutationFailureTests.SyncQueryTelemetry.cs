using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments("object")]
    [Arguments("typed")]
    [Arguments("entity")]
    public async Task SyncQueryTelemetry_OriginalFailureSurvivesCleanupAndAllReporters(string route)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("query execution");
        var cleanup = new Exception("command cleanup");
        var counter = new Exception("query counter");
        var duration = new Exception("query duration");
        var stop = new Exception("query stop");
        fixture.Scenario.ScalarExecuting = () => throw primary;
        fixture.Scenario.ReaderFactory = () => throw primary;
        fixture.Scenario.CommandDisposeFailure = cleanup;
        using var caller = new Activity("query-caller").Start();
        using var activities = new QueryActivityProbe(stopping: _ => throw stop);
        using var metrics = new QueryMeterProbe(name => { throw name == "datalinq.queries" ? counter : duration; });
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var failure = Capture<Exception>(() =>
        {
            if (route == "object") select.ExecuteScalar();
            else if (route == "typed") select.ExecuteScalar<int>();
            else select.Execute().ToArray();
        });
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([cleanup, counter, duration, stop])).IsTrue();
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(metrics.Counters.Count).IsEqualTo(1);
        await Assert.That(metrics.Durations.Count).IsEqualTo(1);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
    }

    [Test, NotInParallel]
    [Arguments("object", "start")]
    [Arguments("typed", "start")]
    [Arguments("entity", "start")]
    [Arguments("terminal", "start")]
    [Arguments("object", "sample")]
    [Arguments("typed", "sample")]
    [Arguments("entity", "sample")]
    [Arguments("terminal", "sample")]
    public async Task SyncQueryTelemetry_StartFailureIsOwnedAndClassified(string route, string phase)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("query start");
        using var caller = new Activity("query-caller").Start();
        using var activities = new QueryActivityProbe(starting: phase == "start" ? _ => throw expected : null,
            samplingFailure: phase == "sample" ? expected : null);
        using var metrics = new QueryMeterProbe();
        var failure = Capture<Exception>(() => ExecuteSyncTelemetryQuery(fixture, transaction, route));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)?.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(activities.Stopped.Count).IsEqualTo(phase == "start" ? 1 : 0);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(fixture.Scenario.ScalarExecutions).IsEqualTo(0);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        using (transaction.ExecutionGate.Enter("reporting settled")) { }
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncQueryTelemetry_IteratorRestoresCallerBetweenMovesAndOnDisposal(bool exhaust, bool stopFails)
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("query-caller").Start();
        var stop = new Exception("stop");
        using var activities = new QueryActivityProbe(stopping: activity =>
        {
            if (!ReferenceEquals(Activity.Current, activity)) throw new Exception("Query is not current during stop.");
            if (stopFails) throw stop;
        });
        using var metrics = new QueryMeterProbe();
        var query = new SqlQuery<TransactionMutationGuardRow>(transaction);
        query.Where("id").EqualTo(1);
        using var rows = query.SelectQuery().Execute().GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(activities.Stopped).IsEmpty();
        _ = Capture<InvalidOperationException>(transaction.Commit);
        using var laterCaller = new Activity("later-query-caller").Start();
        void Finish() { if (exhaust) { if (rows.MoveNext()) throw new Exception("Unexpected extra row."); } else rows.Dispose(); }
        if (stopFails) await Assert.That(Capture<Exception>(Finish)).IsSameReferenceAs(stop);
        else Finish();
        await Assert.That(Activity.Current).IsSameReferenceAs(laterCaller);
        var activity = activities.Stopped.Single();
        await Assert.That(activity.Parent).IsSameReferenceAs(caller);
        await Assert.That(activity.GetTagItem("datalinq.outcome")).IsEqualTo(exhaust ? "success" : "failure");
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo(exhaust ? "success" : "failure");
        rows.Dispose();
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncQueryTelemetry_TerminalSemanticsStayPrimaryThroughReporting(bool managed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var counter = new Exception("query counter");
        using var activities = new QueryActivityProbe();
        using var metrics = new QueryMeterProbe(name => { if (name == "datalinq.queries") throw counter; });
        var failure = Capture<Exception>(() => SqlQueryPlanBackend.ExecuteTerminalPrimaryKeyLookup(
            managed ? transaction : fixture.Provider.ReadOnlyAccess, fixture.RowTable, null, QueryPlanResultKind.Single));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.SecondaryFailures.Single().Exception).IsSameReferenceAs(counter);
        await Assert.That(activities.Stopped.Single().Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test, NotInParallel]
    [Arguments("object", "none")]
    [Arguments("typed", "none")]
    [Arguments("entity", "none")]
    [Arguments("terminal", "none")]
    [Arguments("object", "normal")]
    [Arguments("typed", "normal")]
    [Arguments("entity", "normal")]
    [Arguments("terminal", "normal")]
    [Arguments("object", "counter")]
    [Arguments("typed", "counter")]
    [Arguments("entity", "counter")]
    [Arguments("terminal", "counter")]
    public async Task SyncQueryTelemetry_SuccessReportsOnceAfterCleanupUnderAdmission(string route, string observers)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("caller").Start();
        var failure = new Exception("counter");
        var rejections = 0;
        void CheckAdmission()
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            rejections++;
            if (fixture.Scenario.CommandDisposals != fixture.Scenario.CommandCreations)
                throw new Exception("Query reporting ran before command cleanup.");
        }
        using var activities = observers == "none" ? null : new QueryActivityProbe(stopping: _ => CheckAdmission());
        using var metrics = observers == "none" ? null : new QueryMeterProbe(name =>
        {
            CheckAdmission();
            if (observers == "counter" && name == "datalinq.queries") throw failure;
        });
        if (observers == "counter")
        {
            await Assert.That(Capture<Exception>(() => ExecuteSyncTelemetryQuery(fixture, transaction, route))).IsSameReferenceAs(failure);
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
            await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
            await Assert.That(context.SecondaryFailures).IsEmpty();
        }
        else ExecuteSyncTelemetryQuery(fixture, transaction, route);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(QueryCounts(fixture).ScalarExecutions).IsEqualTo(route is "object" or "typed" ? 1 : 0);
        await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(route is "object" or "typed" ? 0 : 1);
        if (observers != "none")
        {
            await Assert.That(rejections).IsEqualTo(3);
            await Assert.That(metrics!.Counters.Single().Outcome).IsEqualTo("success");
            await Assert.That(metrics.Durations.Single().Outcome).IsEqualTo("success");
            await Assert.That(activities!.Stopped.Single().GetTagItem("datalinq.outcome")).IsEqualTo(observers == "counter" ? "failure" : "success");
        }
        using (transaction.ExecutionGate.Enter("reporting settled")) { }
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncQueryTelemetry_TypedEntityConversionIsInsideTheLogicalQuery(bool managed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        IDataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        var counter = new Exception("counter");
        using var activities = new QueryActivityProbe();
        using var metrics = new QueryMeterProbe(name => { if (name == "datalinq.queries") throw counter; });
        var query = new SqlQuery<TransactionMutationGuardRow>(source);
        query.Where("id").EqualTo(1);
        var failure = Capture<InvalidCastException>(() => query.SelectQuery().ExecuteAs<string>().ToArray());
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(counter);
        await Assert.That(context.TransactionId).IsEqualTo(managed ? transaction.TransactionID : (uint?)null);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        await Assert.That(activities.Stopped.Single().Status).IsEqualTo(ActivityStatusCode.Error);
    }

    [Test, NotInParallel]
    [Arguments("unused")]
    [Arguments("busy")]
    [Arguments("pre-canceled")]
    public async Task SyncQueryTelemetry_UnadmittedQueriesEmitNothing(string mode)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var activities = new QueryActivityProbe();
        using var metrics = new QueryMeterProbe();
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        using var rows = select.Execute().GetEnumerator();
        if (mode == "busy")
        {
            using var lease = transaction.ExecutionGate.Enter("another operation");
            _ = Capture<InvalidOperationException>(() => rows.MoveNext());
        }
        else if (mode == "pre-canceled") _ = Capture<OperationCanceledException>(() => select.ExecuteScalar(new CancellationToken(true)));
        rows.Dispose();
        rows.Dispose();
        await Assert.That(metrics.Counters).IsEmpty();
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(QueryCounts(fixture).ScalarExecutions + QueryCounts(fixture).EntityExecutions).IsEqualTo(0);
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncQueryTelemetry_PrivateOwnerRetainsItsRequestedKind(bool scalar, bool unknown)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var kind = unknown ? ExecutionOperationKind.Unknown : ExecutionOperationKind.Save;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        using var lease = transaction.ExecutionGate.Enter("owned query", operationKind: kind);
        using var step = transaction.ExecutionGate.EnterStep(lease);
        var counter = new Exception("counter");
        using var metrics = new QueryMeterProbe(name => { if (name == "datalinq.queries") throw counter; });
        var failure = Capture<Exception>(() =>
        {
            if (scalar) select.ExecuteScalar(default, step);
            else select.Execute(step).ToArray();
        });
        await Assert.That(failure).IsSameReferenceAs(counter);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(kind);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
    }

    [Test, NotInParallel]
    [Arguments("object")]
    [Arguments("entity")]
    public async Task SyncQueryTelemetry_CleanupFailureRetainsDisposalAttribution(string route)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var cleanup = new Exception("cleanup");
        var stop = new Exception("stop");
        fixture.Scenario.CommandDisposeFailure = cleanup;
        using var activities = new QueryActivityProbe(stopping: _ => throw stop);
        var failure = Capture<Exception>(() => ExecuteSyncTelemetryQuery(fixture, transaction, route));
        await Assert.That(failure).IsSameReferenceAs(cleanup);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(stop);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
    }

    [Test, NotInParallel]
    [Arguments("yield")]
    [Arguments("resume")]
    [Arguments("dispose")]
    public async Task SyncQueryTelemetry_CurrentChangeFailureStillFinalizesTheQuery(string phase)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        using var caller = new Activity("caller").Start();
        var original = new Exception("current changed");
        var stop = new Exception("stop");
        var armed = phase == "yield";
        using var activities = new QueryActivityProbe(stopping: _ => throw stop);
        using var metrics = new QueryMeterProbe();
        var query = new SqlQuery<TransactionMutationGuardRow>(transaction);
        query.Where("id").EqualTo(1);
        using var rows = query.SelectQuery().Execute().GetEnumerator();
        EventHandler<ActivityChangedEventArgs> handler = (_, change) =>
        {
            if (armed && (phase == "yield" ? ReferenceEquals(change.Current, caller) && change.Previous?.OperationName == "datalinq.query"
                : change.Current?.OperationName == "datalinq.query"))
            {
                armed = false;
                throw original;
            }
        };
        Activity.CurrentChanged += handler;
        Exception failure;
        try
        {
            if (phase != "yield") { await Assert.That(rows.MoveNext()).IsTrue(); armed = true; }
            failure = Capture<Exception>(() => { if (phase == "dispose") rows.Dispose(); else rows.MoveNext(); });
        }
        finally { Activity.CurrentChanged -= handler; }
        await Assert.That(failure).IsSameReferenceAs(original);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(stop);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncQueryTelemetry_CacheHitCountsOneQueryWithoutCommands(bool observed)
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        fixture.PrimeCommittedRow(1, "cached");
        using var caller = new Activity("caller").Start();
        using var activities = observed ? new QueryActivityProbe() : null;
        using var metrics = observed ? new QueryMeterProbe() : null;
        var query = new SqlQuery<TransactionMutationGuardRow>(fixture.Provider.ReadOnlyAccess);
        query.Where("id").EqualTo(1);
        var result = query.SelectQuery().ExecuteAs<TransactionMutationGuardRow>().Single();
        await Assert.That(result.Value).IsEqualTo("cached");
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        if (observed)
        {
            await Assert.That(metrics!.Counters.Single().Outcome).IsEqualTo("success");
            await Assert.That((bool)activities!.Stopped.Single().GetTagItem("datalinq.transactional")!).IsFalse();
        }
    }

    [Test, NotInParallel]
    public async Task SyncQueryTelemetry_EscapedHelperReaderIsFinalizedBeforeRecovery()
    {
        using var fixture = new ScriptedFixture();
        // The controlled helper owns completion/disposal; a public using would
        // correctly reject a second completion attempt after its lifetime closed.
        var transaction = fixture.Database.Transaction();
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        fixture.Scenario.ReaderFactory = () => reader;
        var stop = new Exception("stop");
        using var caller = new Activity("caller").Start();
        using var activities = new QueryActivityProbe(stopping: _ => throw stop);
        using var metrics = new QueryMeterProbe();
        IEnumerator<TransactionMutationGuardRow>? escaped = null;
        var resource = new ControlledHelperTransaction { DisposingTransaction = transaction.DatabaseAccess.Dispose };
        var pending = TransactionCallbackRunner.RunAsync(transaction.ExecutionGate, resource, new(), transaction.TransactionID, _ =>
        {
            escaped = transaction.From<TransactionMutationGuardRow>().Where("id").EqualTo(1).SelectQuery()
                .ExecuteAs<TransactionMutationGuardRow>().GetEnumerator();
            if (!escaped.MoveNext()) throw new Exception("Expected a row.");
            if (!ReferenceEquals(Activity.Current, caller)) throw new Exception("Query context escaped into callback.");
            return Task.FromResult(1);
        });
        var failure = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(ExecutionFailureContexts.Get(failure)!.SecondaryFailures.Any(item => ReferenceEquals(item.Exception, stop))).IsTrue();
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        escaped!.Dispose();
    }

    [Test, NotInParallel]
    [Arguments("object")]
    [Arguments("entity")]
    public async Task SyncQueryTelemetry_ReporterDoesNotReuseOldFailureFacts(string route)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var reused = new Exception("observer reused");
        var phantom = new Exception("old cleanup");
        ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.Committed, ExecutionRecoveryActions.Continue, transaction.TransactionID,
            [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, phantom)], operation: ExecutionOperationKind.Save));
        using var metrics = new QueryMeterProbe(_ => throw reused);
        var failure = Capture<Exception>(() => ExecuteSyncTelemetryQuery(fixture, transaction, route));
        await Assert.That(failure).IsSameReferenceAs(reused);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Finalization);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(metrics.Counters.Count).IsEqualTo(1);
        await Assert.That(metrics.Durations.Count).IsEqualTo(1);
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncQueryTelemetry_KeylessReaderKeepsLaterReadAndBothCleanupFailures(bool managed, bool readFails)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new ScriptedMutationProvider<AsyncModelQueryDb>(scenario);
        using var transaction = provider.StartTransaction();
        IDataSourceAccess source = managed ? transaction : provider.ReadOnlyAccess;
        var table = provider.Metadata.GetTableModel(typeof(AsyncModelKeylessRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("value").Index] = "stored";
        values[table.GetColumnByDbName("number").Index] = 7;
        var primary = new Exception("later read");
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var counter = new Exception("counter");
        var stop = new Exception("stop");
        var reader = new OwnedReadProbe(new RelationTestRow(table, values)) { DisposeFailure = readerCleanup };
        scenario.ReaderFactory = () => reader;
        scenario.CommandDisposeFailure = commandCleanup;
        using var caller = new Activity("caller").Start();
        using var activities = new QueryActivityProbe(stopping: _ => throw stop);
        using var metrics = new QueryMeterProbe(name => { if (name == "datalinq.queries") throw counter; });
        using var rows = new SqlQuery<AsyncModelKeylessRow>(source).SelectQuery().ExecuteAs<AsyncModelKeylessRow>().GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current.Value).IsEqualTo("stored");
        await Assert.That(reader.Disposals).IsEqualTo(0);
        await Assert.That(metrics.Counters).IsEmpty();
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        reader.OnRead = () =>
        {
            ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.RowLoading,
                managed ? ExecutionCompletion.NotAttempted : ExecutionCompletion.NotApplicable,
                managed ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.None,
                managed ? transaction.TransactionID : null, []));
            throw primary;
        };
        using var laterScope = ExecutionFailureScope.Begin();
        var scope = ExecutionFailureScope.Current;
        var failure = Capture<Exception>(() => { if (readFails) rows.MoveNext(); else rows.Dispose(); });
        await Assert.That(failure).IsSameReferenceAs(readFails ? primary : readerCleanup);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(readFails ? ExecutionOperationKind.Query : ExecutionOperationKind.Dispose);
        await Assert.That(context.Cause).IsEqualTo(readFails ? ExecutionFailureCause.Timeout : ExecutionFailureCause.Unknown);
        Exception[] expected = readFails ? [readerCleanup, commandCleanup, counter, stop] : [commandCleanup, counter, stop];
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual(expected)).IsTrue();
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(scope);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(scenario.CommandDisposals).IsEqualTo(1);
        rows.Dispose();
    }

    [Test, NotInParallel]
    [Arguments("conversion")]
    [Arguments("matching")]
    [Arguments("foreign")]
    [Arguments("unrequested")]
    public async Task SyncQueryTelemetry_ScalarFailureClassificationSurvivesReporting(string mode)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var counter = new Exception("counter");
        using var metrics = new QueryMeterProbe(name => { if (name == "datalinq.queries") throw counter; });
        var canceled = new OperationCanceledException(mode == "foreign" ? new CancellationToken(true) : cancellation.Token);
        if (mode == "conversion") fixture.Scenario.ScalarResult = "not a number";
        else fixture.Scenario.ScalarExecuting = () =>
        {
            if (mode != "unrequested") cancellation.Cancel();
            throw canceled;
        };
        var failure = Capture<Exception>(() => transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalar<int>(cancellation.Token));
        if (mode == "conversion") await Assert.That(failure).IsTypeOf<FormatException>();
        else await Assert.That(failure).IsSameReferenceAs(canceled);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(mode == "matching" ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(counter);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
    }

    private static void ExecuteSyncTelemetryQuery(ScriptedFixture fixture, Transaction<TransactionMutationGuardDb> transaction, string route)
    {
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        if (route == "object") _ = select.ExecuteScalar();
        else if (route == "typed") _ = select.ExecuteScalar<int>();
        else if (route == "entity") _ = select.Execute().ToArray();
        else _ = SqlQueryPlanBackend.ExecuteTerminalPrimaryKeyLookup(transaction, fixture.RowTable, null, QueryPlanResultKind.SingleOrDefault);
    }
}
