using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Execution;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments("object", false)]
    [Arguments("object", true)]
    [Arguments("typed", false)]
    [Arguments("typed", true)]
    [Arguments("plan", false)]
    [Arguments("plan", true)]
    public async Task AsyncQueryTelemetry_ScalarCountsOnceAfterOwnedCleanup(string route, bool managed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        IDataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        IDataSourceAccess<TransactionMutationGuardDb> planSource = managed ? transaction : fixture.Database;
        using var caller = new Activity("query-telemetry-caller").Start();
        using var observed = new QueryActivityProbe();
        Activity? providerActivity = null;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => { providerActivity = Activity.Current; return new() { ScalarResult = 7, FailureEvidence = TrustedScalarRead }; },
            ConfigureCommand = command => command.Resource.Cleanup = new(paused: true)
        };
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = new SqlQuery<TransactionMutationGuardRow>(source).SelectQuery().What("COUNT(*)");
        Task pending = route switch
        {
            "object" => select.ExecuteScalarAsyncCore(),
            "typed" => select.ExecuteScalarAsyncCore<int>(),
            _ => fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Count()).ExecuteAsyncCore(planSource, 0)
        };
        var cleanup = factory.Commands.Single().Resource.Cleanup;
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(observed.Stopped).IsEmpty();
            await Assert.That(Activity.Current).IsSameReferenceAs(caller);
            if (managed) _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { cleanup.Release(); await pending; }
        var activity = observed.Stopped.Single();
        await Assert.That(activity.Parent).IsSameReferenceAs(caller);
        await Assert.That(activity.ParentSpanId).IsEqualTo(caller.SpanId);
        await Assert.That(activity.GetTagItem("datalinq.query.kind")).IsEqualTo("scalar");
        await Assert.That(activity.GetTagItem("datalinq.outcome")).IsEqualTo("success");
        await Assert.That(activity.GetTagItem("datalinq.transactional")).IsEqualTo(managed);
        await Assert.That(QueryCounts(fixture).ScalarExecutions).IsEqualTo(1);
        await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(0);
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        // Binding is capture-only: it must not run under an execution activity.
        await Assert.That(providerActivity).IsSameReferenceAs(caller);
    }

    [Test, NotInParallel]
    [Arguments("batch", false)]
    [Arguments("batch", true)]
    [Arguments("key", false)]
    [Arguments("key", true)]
    [Arguments("plan", false)]
    [Arguments("plan", true)]
    [Arguments("terminal", false)]
    [Arguments("terminal", true)]
    public async Task AsyncQueryTelemetry_EntityCompositionCountsOneLogicalQuery(string route, bool managed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        IDataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        IDataSourceAccess<TransactionMutationGuardDb> planSource = managed ? transaction : fixture.Database;
        using var caller = new Activity("query-telemetry-caller").Start();
        using var observed = new QueryActivityProbe();
        var calls = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new()
            {
                ReaderOverride = route is "key" or "terminal" || calls++ > 0
                    ? new ControlledRowDataReader([1, "stored"]) : new ControlledRowDataReader([1]),
                FailureEvidence = TrustedScalarRead
            }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        if (route == "terminal")
            _ = await fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Single(row => row.Id == 1)).ExecuteAsyncCore(planSource, 0);
        else if (route == "plan")
            _ = await PlanRows(fixture.Database.PrepareSequenceQuery(0, _ => fixture.Database.Query().Rows).ExecuteAsyncCore(planSource, 0));
        else
        {
            var query = new SqlQuery<TransactionMutationGuardRow>(source);
            if (route == "key") query.Where("id").EqualTo(1);
            var rows = await query.SelectQuery().ExecuteBufferedAsyncCore();
            await Assert.That(rows.Count).IsEqualTo(1);
        }
        var activity = observed.Stopped.Single();
        await Assert.That(activity.Parent).IsSameReferenceAs(caller);
        await Assert.That(activity.ParentSpanId).IsEqualTo(caller.SpanId);
        await Assert.That(activity.GetTagItem("datalinq.query.kind")).IsEqualTo("entity");
        await Assert.That(activity.GetTagItem("datalinq.outcome")).IsEqualTo("success");
        await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(1);
        await Assert.That(QueryCounts(fixture).ScalarExecutions).IsEqualTo(0);
        await Assert.That(factory.Commands.Sum(command => command.Resource.AsyncDisposals)).IsEqualTo(route is "key" or "terminal" ? 1 : 2);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncQueryTelemetry_ListenerFailuresKeepOriginalAndAttemptEveryReport(bool scalar, bool executionFails)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("query-telemetry-caller").Start();
        var original = new InjectedMutationException("query failed");
        var counter = new InjectedMutationException("counter listener");
        var duration = new InjectedMutationException("duration listener");
        var stopped = new InjectedMutationException("stop listener");
        using var activities = new QueryActivityProbe(stopping: activity =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            throw stopped;
        });
        using var metrics = new QueryMeterProbe(name =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            throw name == "datalinq.queries" ? counter : duration;
        });
        var dispatch = new AsyncCheckpoint(paused: true);
        if (executionFails) dispatch.Fail(original); else dispatch.Release();
        var factory = new ControlledSqlReaderFactory
        {
            WrapScalar = source => new TelemetryScalarSource(source),
            CreateAccess = _ => new(dispatch)
            {
                ScalarResult = 1, ReaderOverride = new ControlledRowDataReader([1, "stored"]),
                FailureEvidence = TrustedScalarRead
            }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var query = transaction.From<TransactionMutationGuardRow>();
        query.Where("id").EqualTo(1);
        var select = query.SelectQuery();
        var error = await AsyncEnumerationFailureOf(() => scalar
            ? (Task)select.ExecuteScalarAsyncCore<int>() : select.ExecuteBufferedAsyncCore());
        await Assert.That(error).IsSameReferenceAs(executionFails ? original : counter);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        Exception[] expectedSecondary = executionFails ? [counter, duration, stopped] : [duration, stopped];
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(expectedSecondary.Length);
        for (var index = 0; index < expectedSecondary.Length; index++)
            await Assert.That(context.SecondaryFailures[index].Exception).IsSameReferenceAs(expectedSecondary[index]);
        await Assert.That(context.SecondaryFailures.All(failure => failure.Stage == ExecutionFailureStage.Finalization)).IsTrue();
        await Assert.That(metrics.Counters.Count).IsEqualTo(1);
        await Assert.That(metrics.Durations.Count).IsEqualTo(1);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(activities.Stopped[0].GetTagItem("datalinq.outcome")).IsEqualTo("failure");
        await Assert.That(factory.Commands.Sum(command => command.Resource.AsyncDisposals)).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        using (transaction.ExecutionGate.Enter("reporting finished")) { }
    }

    [Test, NotInParallel]
    [Arguments(false, "start")]
    [Arguments(true, "start")]
    [Arguments(false, "sample")]
    [Arguments(true, "sample")]
    public async Task AsyncQueryTelemetry_StartFailuresReleaseOwnershipBeforeProviderDispatch(bool scalar, string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("query-telemetry-caller").Start();
        var expected = new InjectedMutationException("activity listener");
        using var activities = new QueryActivityProbe(
            starting: phase == "start" ? _ => throw expected : null,
            samplingFailure: phase == "sample" ? expected : null);
        using var metrics = new QueryMeterProbe();
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var error = await AsyncEnumerationFailureOf(() => scalar
            ? (Task)select.ExecuteScalarAsyncCore<int>() : select.ExecuteBufferedAsyncCore());
        await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(0);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        await Assert.That(metrics.Durations.Count).IsEqualTo(1);
        await Assert.That(activities.Stopped.Count).IsEqualTo(phase == "start" ? 1 : 0);
        await Assert.That(ExecutionFailureContexts.Get(error)!.Stage).IsEqualTo(ExecutionFailureStage.Finalization);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        using (transaction.ExecutionGate.Enter("reporting finished")) { }
        // The owned command source establishes no-statement evidence before dispatch,
        // so this listener failure does not prohibit another query.
        _ = transaction.Query();
    }

    [Test, NotInParallel]
    [Arguments(false, "canceled")]
    [Arguments(true, "canceled")]
    [Arguments(false, "cleanup")]
    [Arguments(true, "cleanup")]
    [Arguments(false, "provider")]
    [Arguments(true, "provider")]
    public async Task AsyncQueryTelemetry_FailuresRecordOneFailedQueryAfterCleanup(bool scalar, string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        using var activities = new QueryActivityProbe();
        using var metrics = new QueryMeterProbe();
        var expected = new InjectedMutationException(mode);
        var dispatch = new AsyncCheckpoint(paused: true);
        var cleanup = new AsyncCheckpoint(paused: true);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new(dispatch)
            {
                ScalarResult = 1, ReaderOverride = new ControlledRowDataReader([1, "stored"]), FailureEvidence = TrustedScalarRead
            },
            ConfigureCommand = command => command.Resource.Cleanup = cleanup
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        Task pending = scalar ? select.ExecuteScalarAsyncCore<int>(cancellation.Token) : select.ExecuteBufferedAsyncCore(cancellation.Token);
        await dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        if (mode == "canceled") cancellation.Cancel();
        else if (mode == "provider") dispatch.Fail(expected);
        else dispatch.Release();
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(metrics.Counters).IsEmpty();
            await Assert.That(activities.Stopped).IsEmpty();
        }
        finally { if (mode == "cleanup") cleanup.Fail(expected); else cleanup.Release(); }
        var failure = await AsyncEnumerationFailureOf(() => pending);
        if (mode == "canceled") await Assert.That(failure is OperationCanceledException).IsTrue();
        else await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        await Assert.That(metrics.Durations.Single().Outcome).IsEqualTo("failure");
        await Assert.That(activities.Stopped.Single().Status).IsEqualTo(ActivityStatusCode.Error);
        await Assert.That(factory.Commands.Sum(command => command.Resource.AsyncDisposals)).IsEqualTo(1);
        if (scalar) await Assert.That(QueryCounts(fixture).ScalarExecutions).IsEqualTo(1);
        else await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(1);
    }

    [Test, NotInParallel]
    [Arguments("unused")]
    [Arguments("pre-canceled")]
    [Arguments("early")]
    [Arguments("complete")]
    public async Task AsyncQueryTelemetry_EnumerationLifetimeDoesNotCountUnusedOrDuplicateDisposal(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var activities = new QueryActivityProbe();
        using var metrics = new QueryMeterProbe();
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, "stored"]) } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var query = fixture.Database.From<TransactionMutationGuardRow>();
        query.Where("id").EqualTo(1);
        var sequence = query.SelectQuery().ExecuteAsyncCore(new(mode == "pre-canceled"));
        var rows = sequence.GetAsyncEnumerator();
        if (mode == "pre-canceled") _ = await AsyncEnumerationFailureOf(async () => { _ = await rows.MoveNextAsync(); });
        else if (mode != "unused")
        {
            await Assert.That(await rows.MoveNextAsync()).IsTrue();
            if (mode == "complete") await Assert.That(await rows.MoveNextAsync()).IsFalse();
        }
        await rows.DisposeAsync();
        await rows.DisposeAsync();
        var expectedCount = mode is "unused" or "pre-canceled" ? 0 : 1;
        await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(expectedCount);
        await Assert.That(activities.Stopped.Count).IsEqualTo(expectedCount);
        await Assert.That(metrics.Counters.Count).IsEqualTo(expectedCount);
        if (expectedCount != 0) await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo(mode == "complete" ? "success" : "failure");
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryTelemetry_CacheOnlyQueriesCountWithoutProviderExecution(bool observed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        fixture.PrimeCommittedRow(1, "cached");
        using var activities = observed ? new QueryActivityProbe() : null;
        using var metrics = observed ? new QueryMeterProbe() : null;
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var query = fixture.Database.From<TransactionMutationGuardRow>();
        query.Where("id").EqualTo(1);
        var result = await query.SelectQuery().ExecuteBufferedAsyncCore();
        await Assert.That(((TransactionMutationGuardRow)result.Single()).Value).IsEqualTo("cached");
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(0);
        await Assert.That(QueryCounts(fixture).EntityExecutions).IsEqualTo(1);
        if (observed)
        {
            await Assert.That(activities!.Stopped.Count).IsEqualTo(1);
            await Assert.That(metrics!.Counters.Single().Outcome).IsEqualTo("success");
        }
    }

    [Test, NotInParallel]
    public async Task AsyncQueryTelemetry_ResumesOriginalActivityWithoutChangingLaterConsumerContext()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var firstCaller = new Activity("first-caller").Start();
        using var activities = new QueryActivityProbe(stopping: activity =>
        {
            if (!ReferenceEquals(Activity.Current, activity)) throw new Exception("Query activity was not current while reporting completion.");
        });
        var calls = 0;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = calls++ == 0
                ? new ControlledRowDataReader([1], [2]) : new ControlledRowDataReader([1, "one"], [2, "two"]) }
        };
        await using var rows = fixture.Database.From<TransactionMutationGuardRow>().SelectQuery().ExecuteAsyncCore().GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(Activity.Current).IsSameReferenceAs(firstCaller);
        using var laterCaller = new Activity("later-caller").Start();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(Activity.Current).IsSameReferenceAs(laterCaller);
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
        await Assert.That(Activity.Current).IsSameReferenceAs(laterCaller);
        await Assert.That(activities.Stopped.Single().Parent).IsSameReferenceAs(firstCaller);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryTelemetry_HelperDrainReportsIncompleteQueryExactlyOnce(bool listenerFails)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var notification = new InjectedMutationException("helper query listener");
        using var activities = new QueryActivityProbe(stopping: _ => { if (listenerFails) throw notification; });
        using var metrics = new QueryMeterProbe();
        var child = new ControlledRowDataReader([1, "stored"]) { Cleanup = new(paused: true) };
        var calls = 0;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? new ControlledRowDataReader([1]) : child }
        };
        IAsyncEnumerator<IImmutableInstance>? escaped = null;
        Task<bool>? move = null;
        var helper = transaction.RunCallbackAsyncCore(_ =>
        {
            escaped = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteAsyncCore().GetAsyncEnumerator();
            move = escaped.MoveNextAsync().AsTask();
            return Task.FromResult(9);
        }, new());
        await child.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try { await Assert.That(metrics.Counters).IsEmpty(); }
        finally { child.Cleanup.Release(); }
        await move!;
        var failure = await AsyncEnumerationFailureOf(() => helper);
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await escaped!.DisposeAsync();
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        if (listenerFails)
            await Assert.That(ExecutionFailureContexts.Get(failure)!.SecondaryFailures.Any(item => ReferenceEquals(item.Exception, notification))).IsTrue();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryTelemetry_LocalResultFailureIsNotReportedAsSuccessfulExecution(bool scalar)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var activities = new QueryActivityProbe();
        using var metrics = new QueryMeterProbe();
        var expected = new InjectedMutationException("scalar conversion");
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ScalarResult = 1, ReaderOverride = new ControlledRowDataReader(), FailureEvidence = TrustedScalarRead },
            ScalarConverting = _ => throw expected
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var failure = await AsyncEnumerationFailureOf(() => scalar
            ? (Task)transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore<int>()
            : fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Single(row => row.Id == 1)).ExecuteAsyncCore(transaction, 0));
        if (scalar) await Assert.That(failure).IsSameReferenceAs(expected);
        else await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(metrics.Counters.Single().Outcome).IsEqualTo("failure");
        var activity = activities.Stopped.Single();
        await Assert.That(activity.GetTagItem("datalinq.outcome")).IsEqualTo("failure");
        await Assert.That(activity.GetTagItem("datalinq.table")).IsEqualTo(fixture.RowTable.DbName);
        await Assert.That(activity.GetTagItem("db.namespace")).IsEqualTo(fixture.Provider.DatabaseName);
    }

    private static QueryMetricsSnapshot QueryCounts(ScriptedFixture fixture) =>
        DataLinqMetrics.Snapshot().Providers.SingleOrDefault(provider => provider.ProviderInstanceId == fixture.Provider.TelemetryInstanceId).Queries;

    private sealed class TelemetryScalarSource(IAsyncScalarSource source) : IAsyncScalarSource, IAsyncReadFailureEvidence
    {
        public void Validate() => source.Validate();
        public Task<object?> ExecuteScalarAsync(CancellationToken token) => source.ExecuteScalarAsync(token);
        public ReadFailureEvidence GetReadFailureEvidence(Exception failure)
        {
            if (Activity.Current?.IsStopped == true) throw new Exception("A stopped query activity leaked into failure assessment.");
            return ((IAsyncReadFailureEvidence)source).GetReadFailureEvidence(failure);
        }
    }

    private sealed class QueryActivityProbe : IDisposable
    {
        internal List<Activity> Stopped { get; } = [];
        private readonly ActivityListener listener;
        internal QueryActivityProbe(Action<Activity>? starting = null, Action<Activity>? stopping = null, Exception? samplingFailure = null)
        {
            listener = new()
            {
                ShouldListenTo = source => source.Name == "DataLinq",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => options.Name == "datalinq.query" && samplingFailure is not null
                    ? throw samplingFailure : ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => { if (activity.OperationName == "datalinq.query") starting?.Invoke(activity); },
                ActivityStopped = activity => { if (activity.OperationName == "datalinq.query") { Stopped.Add(activity); stopping?.Invoke(activity); } }
            };
            ActivitySource.AddActivityListener(listener);
        }
        public void Dispose() => listener.Dispose();
    }

    private sealed class QueryMeterProbe : IDisposable
    {
        internal List<(string? Outcome, double Value)> Counters { get; } = [];
        internal List<(string? Outcome, double Value)> Durations { get; } = [];
        private readonly MeterListener listener = new();
        internal QueryMeterProbe(Action<string>? recorded = null)
        {
            listener.InstrumentPublished = (instrument, meter) =>
            {
                if (instrument.Meter.Name == "DataLinq" && instrument.Name is "datalinq.queries" or "datalinq.query.duration")
                    meter.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                Counters.Add((tags.ToArray().Single(tag => tag.Key == "datalinq.outcome").Value as string, value));
                recorded?.Invoke(instrument.Name);
            });
            listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                Durations.Add((tags.ToArray().Single(tag => tag.Key == "datalinq.outcome").Value as string, value));
                recorded?.Invoke(instrument.Name);
            });
            listener.Start();
        }
        public void Dispose() => listener.Dispose();
    }
}
