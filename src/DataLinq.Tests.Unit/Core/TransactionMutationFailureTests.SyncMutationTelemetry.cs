using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMutationTelemetry_OriginalAndCleanupFailuresSurviveEveryReporter(bool hydration)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var original = new Exception("mutation execution");
        var cleanup = new Exception("command cleanup");
        var counter = new Exception("counter");
        var affected = new Exception("affected");
        var duration = new Exception("duration");
        var stop = new Exception("stop");
        if (hydration)
        {
            fixture.Scenario.ReaderFactory = () => throw original;
            fixture.Scenario.CommandDisposed = () => { if (fixture.Scenario.CommandDisposals == 2) throw cleanup; };
        }
        else
        {
            fixture.Scenario.EnqueueNonQueryFailure(original);
            fixture.Scenario.CommandDisposeFailure = cleanup;
        }
        using var caller = new Activity("caller").Start();
        using var activities = new MutationActivityProbe(stopping: _ => throw stop);
        using var metrics = new MutationMeterProbe(name => throw name switch
        {
            "datalinq.db.mutations" => counter,
            "datalinq.db.mutation.affected_rows" => affected,
            _ => duration
        });
        var failure = Capture<Exception>(() => transaction.Save(mutable));
        await Assert.That(failure).IsSameReferenceAs(original);
        var context = ExecutionFailureContexts.Get(failure)!;
        Exception[] expected = hydration ? [cleanup, counter, affected, duration, stop] : [cleanup, counter, duration, stop];
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual(expected)).IsTrue();
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(metrics.Reports.Count).IsEqualTo(hydration ? 3 : 2);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
    }

    [Test, NotInParallel]
    [Arguments("start")]
    [Arguments("sample")]
    public async Task SyncMutationTelemetry_StartFailureDoesNotPoisonUndispatchedWork(string phase)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var expected = new Exception("mutation start");
        using var caller = new Activity("caller").Start();
        using var activities = new MutationActivityProbe(starting: phase == "start" ? _ => throw expected : null,
            samplingFailure: phase == "sample" ? expected : null);
        using var metrics = new MutationMeterProbe();
        var failure = Capture<Exception>(() => transaction.Save(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(ExecutionFailureContexts.Get(failure)?.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(ExecutionFailureContexts.Get(failure)?.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
        await Assert.That(activities.Stopped.Count).IsEqualTo(phase == "start" ? 1 : 0);
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        _ = transaction.Query();
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_SuccessReportsAfterHydrationAndLifecycleUnderAdmission()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        var observed = 0;
        void Check()
        {
            if (mutable.Lifecycle.BaselineKind != MutableBaselineKind.TransactionLocal || fixture.Scenario.CommandDisposals != 2)
                throw new Exception("Mutation reported before hydration and local finalization.");
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            observed++;
        }
        using var activities = new MutationActivityProbe(stopping: _ => Check());
        using var metrics = new MutationMeterProbe(_ => Check());
        var result = transaction.Save(mutable);
        await Assert.That(result.Value).IsEqualTo("stored");
        await Assert.That(observed).IsEqualTo(4);
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(metrics.Reports.All(item => item.Outcome == "success")).IsTrue();
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_ProviderFailureKeepsRequestedSaveKind()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var expected = new Exception("provider");
        fixture.Scenario.EnqueueNonQueryFailure(expected);
        var failure = Capture<Exception>(() => transaction.Save(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)?.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(ExecutionFailureContexts.Get(failure)?.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_UnchangedSaveCountsWithoutInventingAWrite()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        using var activities = new MutationActivityProbe();
        using var metrics = new MutationMeterProbe();
        await Assert.That(transaction.Save(mutable).Value).IsEqualTo("stored");
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(MutationCounts(fixture).AffectedRows).IsEqualTo(0);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
    }

    [Test, NotInParallel]
    [Arguments("insert", "none")]
    [Arguments("insert", "normal")]
    [Arguments("insert", "fail")]
    [Arguments("update", "none")]
    [Arguments("update", "normal")]
    [Arguments("update", "fail")]
    [Arguments("save-new", "none")]
    [Arguments("save-new", "normal")]
    [Arguments("save-new", "fail")]
    [Arguments("save-existing", "none")]
    [Arguments("save-existing", "normal")]
    [Arguments("save-existing", "fail")]
    [Arguments("delete", "none")]
    [Arguments("delete", "normal")]
    [Arguments("delete", "fail")]
    [Arguments("generated", "none")]
    [Arguments("generated", "normal")]
    [Arguments("generated", "fail")]
    public async Task SyncMutationTelemetry_PhysicalAndRequestedKindsSurviveFinalReporting(string route, string observation)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var inserted = route is "insert" or "save-new" or "generated";
        var kind = route switch
        {
            "insert" or "generated" => ExecutionOperationKind.Insert,
            "update" => ExecutionOperationKind.Update,
            "delete" => ExecutionOperationKind.Delete,
            _ => ExecutionOperationKind.Save
        };
        var mutable = inserted ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
        if (inserted) mutable["Id"] = 1;
        mutable["Value"] = "submitted";
        var generated = fixture.CreateNewAutoMutable("submitted");
        var table = route == "generated" ? fixture.AutoTable : fixture.RowTable;
        fixture.Scenario.ScalarResult = 1;
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(table, 1, "stored"));
        var owners = new List<ExecutionOperationKind>();
        fixture.Scenario.SyncOwnedDispatch = (_, step) => owners.Add(step.Kind);
        using var caller = new Activity("caller").Start();
        var expected = new Exception("reporting");
        using var activities = observation == "none" ? null : new MutationActivityProbe();
        using var metrics = observation == "none" ? null : new MutationMeterProbe(name =>
        {
            if (observation == "fail" && name == "datalinq.db.mutations") throw expected;
        });
        void Execute()
        {
            switch (route)
            {
                case "insert": transaction.Insert(mutable); break;
                case "generated": transaction.Insert(generated); break;
                case "update": transaction.Update(mutable); break;
                case "delete": transaction.Delete(mutable); break;
                default: transaction.Save(mutable); break;
            }
        }
        if (observation == "fail")
        {
            var failure = Capture<Exception>(Execute);
            await Assert.That(failure).IsSameReferenceAs(expected);
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Operation).IsEqualTo(kind);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
            await Assert.That(transaction.IsPoisoned).IsTrue();
            await Assert.That((route == "generated" ? generated.Lifecycle : mutable.Lifecycle).BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
            _ = Capture<TransactionPoisonedException>(transaction.Commit);
        }
        else Execute();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(owners.Count).IsEqualTo(route == "delete" ? 1 : 2);
        await Assert.That(owners.All(owner => owner == kind)).IsTrue();
        await Assert.That(MutationCounts(fixture).TotalExecutions).IsEqualTo(1);
        await Assert.That(MutationCounts(fixture).AffectedRows).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        if (activities is not null)
        {
            await Assert.That(activities.Stopped.Single().GetTagItem("datalinq.mutation.type") as string)
                .IsEqualTo(inserted ? "insert" : route == "delete" ? "delete" : "update");
            await Assert.That(metrics!.Reports.Count).IsEqualTo(3);
            await Assert.That(metrics.Reports.All(item => item.Outcome == "success")).IsTrue();
        }
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncMutationTelemetry_CommandStartFailurePreservesUndispatchedBaseline(bool generated, bool cleanupFails)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var auto = fixture.CreateNewAutoMutable("submitted");
        var expected = new Exception("command start");
        var cleanup = new Exception("command cleanup");
        using var activities = new CommandActivityProbe(starting: _ => throw expected);
        using var metrics = new MutationMeterProbe();
        fixture.Scenario.SyncOwnedDispatch = (command, _) => ((ScriptedDatabaseTransaction)transaction.DatabaseAccess)
            .RunCommandTelemetry<int>((IDbCommand)command, generated ? "scalar" : "non_query", () => throw new Exception("must not dispatch"));
        if (cleanupFails) fixture.Scenario.CommandDisposeFailure = cleanup;
        var failure = Capture<Exception>(() => { if (generated) transaction.Save(auto); else transaction.Save(mutable); });
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(auto.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.NoneForNew);
        await Assert.That(fixture.Scenario.ScalarExecutions + fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.HasCleanupFailure).IsEqualTo(cleanupFails);
        await Assert.That(context.Recovery).IsEqualTo(cleanupFails ? ExecutionRecoveryActions.Dispose
            : ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        if (cleanupFails)
        {
            await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        else _ = transaction.Query();
    }

    [Test, NotInParallel]
    [Arguments("mutation")]
    [Arguments("command")]
    public async Task SyncMutationTelemetry_SecondUndispatchedInputPoisonsWrittenBatchPrefixOnly(string phase)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var first = new Mutable<TransactionMutationGuardRow>(); first["Id"] = 1; first["Value"] = "first";
        var second = new Mutable<TransactionMutationGuardRow>(); second["Id"] = 2; second["Value"] = "second";
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        var expected = new Exception("second start");
        var starts = 0;
        using var mutations = new MutationActivityProbe(starting: _ => { if (++starts == 2 && phase == "mutation") throw expected; });
        using var commands = new CommandActivityProbe(starting: _ => throw expected);
        fixture.Scenario.SyncOwnedDispatch = (command, _) =>
        {
            if (phase == "command" && fixture.Scenario.CommandCreations == 3)
                ((ScriptedDatabaseTransaction)transaction.DatabaseAccess).RunCommandTelemetry<int>((IDbCommand)command, "non_query", () => 0);
        };
        var failure = Capture<Exception>(() => transaction.Insert([first, second]));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(first.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(second.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.NoneForNew);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        _ = Capture<TransactionPoisonedException>(transaction.Commit);
    }

    [Test, NotInParallel]
    [Arguments("hydration")]
    [Arguments("counter")]
    public async Task SyncMutationTelemetry_UnchangedFailureDoesNotPoisonAWrite(string phase)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "old");
        var expected = new Exception(phase);
        fixture.Scenario.ReaderFactory = () => phase == "hydration" ? throw expected : new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 1, "stored"));
        using var metrics = new MutationMeterProbe(name => { if (phase == "counter" && name == "datalinq.db.mutations") throw expected; });
        var failure = Capture<Exception>(() => transaction.Save(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Operation).IsEqualTo(ExecutionOperationKind.Save);
        if (phase == "counter") _ = transaction.Query();
        else _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_ReusedExecutionCleanupAndReporterFailureRetainsCleanupRestriction()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("reused");
        fixture.Scenario.EnqueueNonQueryFailure(expected);
        fixture.Scenario.CommandDisposeFailure = expected;
        using var activities = new MutationActivityProbe(stopping: _ => throw expected);
        using var metrics = new MutationMeterProbe(_ => throw expected);
        var failure = Capture<Exception>(() => transaction.Delete(fixture.CreateExistingMutable(1, "old")));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Delete);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(metrics.Reports.Count).IsEqualTo(2);
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_ReporterRejectsStaleFailureFacts()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var reused = new Exception("reporter");
        ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.Committed, ExecutionRecoveryActions.Continue, transaction.TransactionID,
            [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, new Exception("stale"))], operation: ExecutionOperationKind.Query));
        using var metrics = new MutationMeterProbe(_ => throw reused);
        var failure = Capture<Exception>(() => transaction.Delete(fixture.CreateExistingMutable(1, "old")));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(reused);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Delete);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.SecondaryFailures).IsEmpty();
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_PrimaryCleanupKeepsDisposeAttribution()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("cleanup");
        var stop = new Exception("stop");
        fixture.Scenario.CommandDisposeFailure = expected;
        using var activities = new MutationActivityProbe(stopping: _ => throw stop);
        var failure = Capture<Exception>(() => transaction.Delete(fixture.CreateExistingMutable(1, "old")));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(stop);
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_StoppedCallerIsNotRestored()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("caller").Start();
        using var activities = new MutationActivityProbe(stopping: _ => caller.Stop());
        transaction.Delete(fixture.CreateExistingMutable(1, "old"));
        await Assert.That(Activity.Current).IsNull();
        await Assert.That(caller.IsStopped).IsTrue();
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_EmptyAndRejectedWorkEmitNothing()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var activities = new MutationActivityProbe();
        using var metrics = new MutationMeterProbe();
        await Assert.That(transaction.Insert(Array.Empty<Mutable<TransactionMutationGuardRow>>())).IsEmpty();
        _ = Capture<ArgumentNullException>(() => transaction.Save<TransactionMutationGuardRow>(null!));
        using (transaction.ExecutionGate.Enter("active", operationKind: ExecutionOperationKind.Query))
            _ = Capture<InvalidOperationException>(() => transaction.Delete(fixture.CreateExistingMutable(1, "old")));
        await Assert.That(activities.Stopped).IsEmpty();
        await Assert.That(metrics.Reports).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
    }

    [Test, NotInParallel]
    [Arguments("trusted", false)]
    [Arguments("trusted", true)]
    [Arguments("lost", false)]
    [Arguments("lost", true)]
    [Arguments("initialization", false)]
    [Arguments("initialization", true)]
    [Arguments("null", false)]
    [Arguments("null", true)]
    [Arguments("throws", false)]
    [Arguments("throws", true)]
    public async Task SyncMutationTelemetry_NoDispatchStillAssessesSettledProviderFailure(string assessment, bool noDispatch)
    {
        var scenario = new ScriptedMutationScenario();
        var expected = new Exception("execution");
        var secondary = new Exception("assessment");
        var calls = 0;
        using var provider = new ClassifiedSyncMutationProvider(scenario, expected, _ =>
        {
            calls++;
            if (scenario.CommandDisposals != 1) throw new Exception("Assessment preceded cleanup.");
            return assessment switch
            {
                "throws" => throw secondary,
                "null" => null!,
                "lost" => new(Integrity: TransactionIntegrity.Lost, RollbackAvailable: true),
                "initialization" => new(Effects: ExecutionEffects.Initialization),
                _ => new(ExecutionFailureCause.ProviderError, ExecutionEffects.OrdinaryRead, TransactionIntegrity.Confirmed, true)
            };
        });
        using var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        var mutable = new Mutable<TransactionMutationGuardRow>(); mutable["Id"] = 1; mutable["Value"] = "new";
        using var activities = noDispatch ? new CommandActivityProbe(starting: _ => throw expected) : null;
        var failure = Capture<Exception>(() => transaction.Save(mutable));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(calls).IsEqualTo(1);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.Recovery).IsEqualTo(assessment == "trusted"
            ? ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose | (noDispatch ? ExecutionRecoveryActions.Continue : ExecutionRecoveryActions.None)
            : ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.IsPoisoned).IsEqualTo(!noDispatch);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(noDispatch ? MutableBaselineKind.NoneForNew : MutableBaselineKind.Invalid);
        if (assessment is "null" or "throws")
        {
            await Assert.That(context.SecondaryFailures.Single().Stage).IsEqualTo(ExecutionFailureStage.Recovery);
            if (assessment == "throws") await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(secondary);
        }
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMutationTelemetry_CaughtFailureAllowsHelperCommitOnlyWithoutWriteEffects(bool noDispatch)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("reporter");
        using var activities = noDispatch ? new MutationActivityProbe(starting: _ => throw expected) : null;
        using var metrics = new MutationMeterProbe(name => { if (!noDispatch && name == "datalinq.db.mutations") throw expected; });
        var pending = transaction.RunCallbackAsyncCore(_ =>
        {
            var observed = Capture<Exception>(() => transaction.Delete(fixture.CreateExistingMutable(1, "old")));
            if (!ReferenceEquals(observed, expected)) throw observed;
            return Task.FromResult(17);
        }, new());
        if (noDispatch) await Assert.That(await pending).IsEqualTo(17);
        else await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<InvalidOperationException>();
        await Assert.That(provider.Calls.Contains("commit")).IsEqualTo(noDispatch);
        await Assert.That(provider.Calls.Count(item => item == "rollback")).IsEqualTo(noDispatch ? 0 : 1);
        await Assert.That(transaction.IsDisposed).IsTrue();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMutationTelemetry_HelperPreservesFailureThroughIndependentRollbackAndCleanup(bool noDispatch)
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("reporter");
        var rollback = new Exception("rollback");
        var cleanup = new Exception("connection cleanup");
        var provider = fixture.Scenario.AsyncCompletion = new()
            { Rollback = JournalFault(rollback), ConnectionCleanup = JournalFault(cleanup) };
        var transaction = fixture.Database.Transaction();
        using var activities = new MutationActivityProbe(starting: noDispatch ? _ => throw expected : null,
            stopping: noDispatch ? null : _ => throw expected);
        var failure = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(_ =>
        {
            transaction.Delete(fixture.CreateExistingMutable(1, "old"));
            return Task.FromResult(17);
        }, new()));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Delete);
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([rollback, cleanup])).IsTrue();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(provider.Calls.Contains("commit")).IsFalse();
        await Assert.That(provider.Calls.Count(item => item == "rollback")).IsEqualTo(1);
        await Assert.That(transaction.IsDisposed).IsTrue();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncMutationTelemetry_CurrentChangedFailureStillFinalizesReporting(bool duringStart)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var caller = new Activity("caller").Start();
        var expected = new Exception("current changed");
        var armed = true;
        using var activities = new MutationActivityProbe();
        using var metrics = new MutationMeterProbe();
        EventHandler<ActivityChangedEventArgs> handler = (_, change) =>
        {
            if (armed && (duringStart ? change.Current?.OperationName == "datalinq.db.mutation"
                : ReferenceEquals(change.Current, caller) && change.Previous?.OperationName == "datalinq.db.mutation"))
            {
                armed = false;
                throw expected;
            }
        };
        Activity.CurrentChanged += handler;
        Exception failure;
        try { failure = Capture<Exception>(() => transaction.Delete(fixture.CreateExistingMutable(1, "old"))); }
        finally { Activity.CurrentChanged -= handler; }
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(transaction.IsPoisoned).IsEqualTo(!duringStart);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(duringStart ? 0 : 1);
        await Assert.That(metrics.Reports.Count).IsEqualTo(duringStart ? 2 : 3);
    }

    [Test, NotInParallel]
    public async Task SyncMutationTelemetry_UndispatchedSeparateOperationPreservesEarlierSuccessfulWork()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var first = fixture.CreateExistingMutable(1, "old");
        var second = fixture.CreateExistingMutable(2, "untouched");
        transaction.Delete(first);
        var expected = new Exception("later start");
        using (var activities = new MutationActivityProbe(starting: _ => throw expected))
            _ = Capture<Exception>(() => transaction.Delete(second));
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(second.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        transaction.Commit();
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
    }

    [Test, NotInParallel]
    [Arguments("failed")]
    [Arguments("disposed")]
    [Arguments("inspection")]
    public async Task SyncMutationTelemetry_InitializationFailureCannotAdvertiseRollback(string phase)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("initialization");
        var inspection = new Exception("initialization inspection");
        fixture.Scenario.SyncOwnedDispatch = (_, _) =>
        {
            if (phase == "inspection") provider.InspectInitialization = () => throw inspection;
            else provider.InitializationState = phase == "failed" ? TransactionInitializationState.Failed : TransactionInitializationState.Disposed;
            throw expected;
        };
        var failure = Capture<Exception>(() => transaction.Delete(fixture.CreateExistingMutable(1, "old")));
        // Keep public legacy cleanup independent of the synthetic inspection failure.
        provider.InspectInitialization = null;
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        if (phase == "inspection")
            await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(inspection);
        else await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
    }

    private sealed class ClassifiedSyncMutationProvider(ScriptedMutationScenario scenario, Exception failure,
        Func<Exception, ReadFailureEvidence> assess) : ScriptedMutationProvider(scenario)
    {
        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) =>
            new ClassifiedSyncMutationTransaction(this, type, failure, assess);
    }

    // Separate controllable adapter: the ordinary legacy fixture deliberately
    // does not implement optional provider evidence.
    private sealed class ClassifiedSyncMutationTransaction : DatabaseTransaction, IAsyncReadFailureEvidence
    {
        private readonly Exception failure;
        private readonly Func<Exception, ReadFailureEvidence> assess;
        internal ClassifiedSyncMutationTransaction(IDatabaseProvider provider, TransactionType type, Exception failure,
            Func<Exception, ReadFailureEvidence> assess) : base(provider, type)
        {
            this.failure = failure; this.assess = assess;
            SetStatus(DatabaseTransactionStatus.Open);
        }
        public ReadFailureEvidence GetReadFailureEvidence(Exception observed) => assess(observed);
        public override int ExecuteNonQuery(IDbCommand command) => ExecuteCommandWithTelemetry<int>(command, "non_query", true, Type, () => throw failure);
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override void Commit() => SetStatus(DatabaseTransactionStatus.Committed);
        public override void Rollback() => SetStatus(DatabaseTransactionStatus.RolledBack);
        public override void Dispose() { if (Status == DatabaseTransactionStatus.Open) Rollback(); }
    }
}
