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
using DataLinq.SQLite;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private sealed partial class ScriptedDatabaseTransaction
    {
        internal SyncCompletionProbe? SyncCompletionResourceForTest { get; set; }
        internal void StartSyncTelemetryForTest() => BeginTransactionTelemetry();
        internal void CompleteSyncTelemetryForTest(DatabaseTransactionStatus outcome) => CompleteTransactionTelemetry(outcome);
        internal void FailSyncTelemetryForTest(DatabaseTransactionStatus outcome, Exception failure) => FailTransactionTelemetry(outcome, failure);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_OriginalCompletionFailureSurvivesEveryReporter(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var outcome = rollback ? DatabaseTransactionStatus.RolledBack : DatabaseTransactionStatus.Committed;
        var original = new Exception("native completion");
        var counter = new Exception("counter");
        var duration = new Exception("duration");
        var stop = new Exception("stop");
        using var caller = new Activity("caller").Start();
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (name != "datalinq.db.transactions.started") throw name == "datalinq.db.transactions.completed" ? counter : duration;
        });
        access.StartSyncTelemetryForTest();
        var failure = Capture<Exception>(() =>
        {
            try { throw original; }
            catch (Exception error) { access.FailSyncTelemetryForTest(outcome, error); throw; }
        });
        await Assert.That(failure).IsSameReferenceAs(original);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.Operation).IsEqualTo(rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([counter, duration, stop])).IsTrue();
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        access.FailSyncTelemetryForTest(outcome, original);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments("sample")]
    [Arguments("start")]
    [Arguments("counter")]
    public async Task SyncTransactionTelemetry_StartFailureFinalizesOnce(string phase)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var expected = new Exception(phase);
        using var caller = new Activity("caller").Start();
        using var activities = new TransactionActivityProbe(starting: phase == "start" ? _ => throw expected : null,
            samplingFailure: phase == "sample" ? expected : null);
        using var metrics = new TransactionMeterProbe(name => { if (phase == "counter" && name == "datalinq.db.transactions.started") throw expected; });
        var failure = Capture<Exception>(access.StartSyncTelemetryForTest);
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Count).IsEqualTo(phase == "sample" ? 0 : 1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        access.FailSyncTelemetryForTest(DatabaseTransactionStatus.RolledBack, failure);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_RealSQLiteKeepsConfirmedOutcomeAfterReportingFailure(bool rollback)
    {
        using var provider = new SQLiteProvider<TransactionMutationGuardDb>("Data Source=:memory:");
        var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        var expected = new Exception("completion counter");
        var stop = new Exception("stop");
        using var caller = new Activity("caller").Start();
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe(name => { if (name == "datalinq.db.transactions.completed") throw expected; });
        transaction.DatabaseAccess.ExecuteNonQuery("CREATE TABLE completion_probe (id INTEGER)");
        var failure = Capture<Exception>(() => { if (rollback) transaction.Rollback(); else transaction.Commit(); });
        try
        {
            await Assert.That(failure).IsSameReferenceAs(expected);
            await Assert.That(transaction.Status).IsEqualTo(rollback ? DatabaseTransactionStatus.RolledBack : DatabaseTransactionStatus.Committed);
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
            await Assert.That(context.Operation).IsEqualTo(rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
            await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(stop);
            await Assert.That(metrics.Reports.Count).IsEqualTo(3);
            await Assert.That(activities.Stopped.Count).IsEqualTo(1);
            await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        }
        finally
        {
            // A failed baseline must not leave its real SQLite resources alive.
            activities.Dispose(); metrics.Dispose();
            transaction.Dispose();
        }
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncTransactionTelemetry_ConfirmedCompletionFinalizesMutablesAndEveryReporter(bool rollback, bool statusFails)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(1, "deleted");
        transaction.Delete(mutable);
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var resource = access.SyncCompletionResourceForTest = new();
        var status = new Exception("provider status");
        var counter = new Exception("counter");
        var duration = new Exception("duration");
        var stop = new Exception("stop");
        var managed = new Exception("managed status");
        using var caller = new Activity("caller").Start();
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (name == "datalinq.db.transactions.started") return;
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            throw name == "datalinq.db.transactions.completed" ? counter : duration;
        });
        access.StartSyncTelemetryForTest();
        if (statusFails) access.OnStatusChanged += (_, _) => throw status;
        transaction.OnStatusChanged += (_, _) => throw managed;
        var failure = Capture<Exception>(() => { if (rollback) transaction.Rollback(); else transaction.Commit(); });
        await Assert.That(failure).IsSameReferenceAs(statusFails ? status : managed);
        var context = ExecutionFailureContexts.Get(failure)!;
        Exception[] expected = statusFails ? [managed, counter, duration, stop] : [counter, duration, stop];
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual(expected)).IsTrue();
        await Assert.That(context.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
        await Assert.That(context.Operation).IsEqualTo(rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(transaction.MutableOwnership.Outcome).IsEqualTo(rollback ? MutableTransactionOutcome.RolledBack : MutableTransactionOutcome.Committed);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(resource.Calls.SequenceEqual([rollback ? "rollback" : "commit", "close", "dispose-connection", "dispose-transaction"])).IsTrue();
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        transaction.Dispose();
        await Assert.That(resource.Calls.Count).IsEqualTo(4);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_NativeFailureSurvivesReportingAndDoesNotRetryRollback(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var expected = new Exception("native");
        var counter = new Exception("counter");
        var duration = new Exception("duration");
        var stop = new Exception("stop");
        var resource = access.SyncCompletionResourceForTest = new() { NativeFailure = expected };
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (name != "datalinq.db.transactions.started") throw name == "datalinq.db.transactions.completed" ? counter : duration;
        });
        access.StartSyncTelemetryForTest();
        var failure = Capture<Exception>(() => { if (rollback) transaction.Rollback(); else transaction.Commit(); });
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([counter, duration, stop])).IsTrue();
        await Assert.That(resource.Calls.Count).IsEqualTo(1);
        resource.NativeFailure = null;
        transaction.Dispose();
        await Assert.That(resource.Calls.Count(item => item == "rollback")).IsEqualTo(1);
        await Assert.That(resource.Calls.Contains("dispose-transaction")).IsTrue();
        await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_ConfirmedCompletionAttemptsEveryResourceCleanup(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var close = new Exception("close");
        var connection = new Exception("connection");
        var native = new Exception("transaction");
        var resource = access.SyncCompletionResourceForTest = new() { CloseFailure = close, ConnectionFailure = connection, TransactionFailure = native };
        access.StartSyncTelemetryForTest();
        var failure = Capture<Exception>(() => { if (rollback) transaction.Rollback(); else transaction.Commit(); });
        await Assert.That(failure).IsSameReferenceAs(close);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([connection, native])).IsTrue();
        await Assert.That(resource.Calls.Count).IsEqualTo(4);
        transaction.Dispose();
        await Assert.That(resource.Calls.Count).IsEqualTo(4);
    }

    [Test, NotInParallel]
    [Arguments("start")]
    [Arguments("counter")]
    public async Task SyncTransactionTelemetry_RealSQLiteStartFailureCannotLeaveReusableTransaction(string phase)
    {
        using var provider = new SQLiteProvider<TransactionMutationGuardDb>("Data Source=:memory:");
        using var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        var expected = new Exception(phase);
        using var activities = new TransactionActivityProbe(starting: phase == "start" ? _ => throw expected : null);
        using var metrics = new TransactionMeterProbe(name => { if (phase == "counter" && name == "datalinq.db.transactions.started") throw expected; });
        var failure = Capture<Exception>(() => transaction.DatabaseAccess.ExecuteNonQuery("CREATE TABLE failed_start (id INTEGER)"));
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        _ = Capture<InvalidOperationException>(() => transaction.EnsureCanRead("read after failed initialization"));
        _ = Capture<InvalidOperationException>(transaction.Commit);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_ReportingFollowsManagedFinalizationAndCleanup(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "deleted"));
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var resource = access.SyncCompletionResourceForTest = new();
        var observations = 0;
        void Observe()
        {
            if (transaction.MutableOwnership.Outcome != (rollback ? MutableTransactionOutcome.RolledBack : MutableTransactionOutcome.Committed) ||
                transaction.TouchedMutables.Count != 0 || resource.Calls.Count != 4)
                throw new Exception("Reporting preceded managed finalization or cleanup.");
            observations++;
        }
        using var activities = new TransactionActivityProbe(stopping: _ => Observe());
        using var metrics = new TransactionMeterProbe(name => { if (name != "datalinq.db.transactions.started") Observe(); });
        access.StartSyncTelemetryForTest();
        if (rollback) transaction.Rollback(); else transaction.Commit();
        await Assert.That(observations).IsEqualTo(3);
    }

    [Test, NotInParallel]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncTransactionTelemetry_LaterCallerIsRestoredUnlessStopped(bool rollback, bool stopCaller)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        access.SyncCompletionResourceForTest = new();
        using var originalCaller = new Activity("original").Start();
        Activity? later = null;
        using var activities = new TransactionActivityProbe(stopping: _ => { if (stopCaller) later!.Stop(); });
        access.StartSyncTelemetryForTest();
        using var laterCaller = later = new Activity("later").Start();
        if (rollback) transaction.Rollback(); else transaction.Commit();
        if (stopCaller) await Assert.That(Activity.Current).IsNull();
        else await Assert.That(Activity.Current).IsSameReferenceAs(laterCaller);
        await Assert.That(activities.Stopped.Single().Parent).IsSameReferenceAs(originalCaller);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_DisposeKeepsRequestedKindAfterImplicitRollback(bool statusFails)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var resource = access.SyncCompletionResourceForTest = new();
        var expected = new Exception("dispose observer");
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe(name => { if (!statusFails && name == "datalinq.db.transactions.completed") throw expected; });
        access.StartSyncTelemetryForTest();
        if (statusFails) access.OnStatusChanged += (_, _) => throw expected;
        var failure = Capture<Exception>(transaction.Dispose);
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(resource.Calls.Count).IsEqualTo(4);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        transaction.Dispose();
        await Assert.That(resource.Calls.Count).IsEqualTo(4);
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_DisposalUpdatesEarlierFailureSnapshotWithoutRewritingOriginal(bool unknownCommit)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        access.SyncCompletionResourceForTest = new();
        var previous = new Exception("earlier failure");
        var before = new ExecutionFailureContext(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            unknownCommit ? ExecutionCompletion.Unknown : ExecutionCompletion.NotAttempted,
            ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose, transaction.TransactionID, []);
        using (var owner = transaction.ExecutionGate.Enter("earlier controlled failure"))
        using (var step = transaction.ExecutionGate.EnterStep(owner)) transaction.RecordAsyncReadFailure(step, before);
        ExecutionFailureContexts.Attach(previous, before);
        access.StartSyncTelemetryForTest();
        transaction.Dispose();
        await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(unknownCommit ? ExecutionCompletion.Unknown : ExecutionCompletion.RolledBack);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(ExecutionFailureContexts.Get(previous)).IsSameReferenceAs(before);
        await Assert.That(before.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
    }

    [Test, NotInParallel]
    public async Task SyncTransactionTelemetry_ReporterDoesNotImportStaleCompletionOrCleanup()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        access.SyncCompletionResourceForTest = new();
        var expected = new Exception("reused reporter");
        ExecutionFailureContexts.Attach(expected, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.Unknown, ExecutionRecoveryActions.Continue, 999,
            [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, new Exception("old"))], operation: ExecutionOperationKind.Save));
        using var metrics = new TransactionMeterProbe(name => { if (name != "datalinq.db.transactions.started") throw expected; });
        access.StartSyncTelemetryForTest();
        var failure = Capture<Exception>(transaction.Commit);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.SecondaryFailures).IsEmpty();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_RealSQLiteTrackedWriteMatchesConfirmedOutcome(bool rollback)
    {
        using var provider = new SQLiteProvider<TransactionMutationGuardDb>("Data Source=:memory:");
        provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE transaction_mutation_guard_rows (id INTEGER PRIMARY KEY, value TEXT)");
        using var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        var mutable = new Mutable<TransactionMutationGuardRow>(); mutable["Id"] = 1; mutable["Value"] = "stored";
        var expected = new Exception("counter");
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe(name => { if (name == "datalinq.db.transactions.completed") throw expected; });
        transaction.Save(mutable);
        var failure = Capture<Exception>(() => { if (rollback) transaction.Rollback(); else transaction.Commit(); });
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(provider.DatabaseAccess.ExecuteScalar<long>("SELECT COUNT(*) FROM transaction_mutation_guard_rows")).IsEqualTo(rollback ? 0L : 1L);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(rollback ? MutableBaselineKind.Invalid : MutableBaselineKind.Committed);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
    }

    [Test, NotInParallel]
    [Arguments("sample")]
    [Arguments("start")]
    [Arguments("counter")]
    public async Task SyncTransactionTelemetry_FailedStartCleansEveryResourceBeforeReporting(string phase)
    {
        var original = new Exception(phase);
        var close = new Exception("close");
        var connection = new Exception("connection");
        var native = new Exception("transaction");
        var counter = new Exception("completion counter");
        var stop = new Exception("stop");
        var resource = new SyncCompletionProbe { CloseFailure = close, ConnectionFailure = connection, TransactionFailure = native };
        using var access = new StandaloneSyncTransaction(resource);
        using var caller = new Activity("caller").Start();
        using var activities = new TransactionActivityProbe(starting: phase == "start" ? _ => throw original : null,
            stopping: _ => throw stop, samplingFailure: phase == "sample" ? original : null);
        var cleanupWasComplete = false;
        using var metrics = new TransactionMeterProbe(name =>
        {
            if (name == "datalinq.db.transactions.started" && phase == "counter") throw original;
            if (name == "datalinq.db.transactions.completed")
            {
                cleanupWasComplete = resource.Calls.SequenceEqual(["close", "dispose-connection", "dispose-transaction"]);
                throw counter;
            }
        });
        var failure = Capture<Exception>(access.Start);
        await Assert.That(failure).IsSameReferenceAs(original);
        var context = ExecutionFailureContexts.Get(failure)!;
        Exception[] secondary = phase == "sample" ? [close, connection, native, counter] : [close, connection, native, counter, stop];
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual(secondary)).IsTrue();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.ProviderInstanceId).IsNull();
        await Assert.That(cleanupWasComplete).IsTrue();
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        _ = Capture<ObjectDisposedException>(access.Commit);
        access.Dispose();
        await Assert.That(resource.Calls.Count).IsEqualTo(3);
    }

    [Test, NotInParallel]
    public async Task SyncTransactionTelemetry_CommittedCacheFailurePreservesRecoveryAndReporterFailures()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(427, "delete");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        transaction.Delete(deleted);
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var resource = access.SyncCompletionResourceForTest = new();
        var primary = new Exception("cache publication");
        var recovery = new Exception("cache recovery");
        var counter = new Exception("counter");
        var stop = new Exception("stop");
        fixture.RowCache.SubscribeToChanges(new ThrowingNotification(primary));
        fixture.Provider.State.Cache.TableCaches.Values.Last(cache => !ReferenceEquals(cache, fixture.RowCache))
            .SubscribeToChanges(new ThrowingNotification(recovery));
        var committedNotifications = 0;
        transaction.OnStatusChanged += (_, args) => { if (args.Status == DatabaseTransactionStatus.Committed) committedNotifications++; };
        using var activities = new TransactionActivityProbe(stopping: _ => throw stop);
        using var metrics = new TransactionMeterProbe(name => { if (name == "datalinq.db.transactions.completed") throw counter; });
        access.StartSyncTelemetryForTest();
        var failure = Capture<TransactionCommitFinalizationException>(transaction.Commit);
        await Assert.That(failure.InnerException).IsSameReferenceAs(primary);
        await Assert.That(failure.CleanupFailures.Single()).IsSameReferenceAs(recovery);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.SecondaryFailures.Select(item => item.Exception).SequenceEqual([recovery, counter, stop])).IsTrue();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(deleted.Lifecycle.InvalidationReason).IsEqualTo(MutableInvalidationReason.CommittedStateFinalizationFailed);
        await Assert.That(committedNotifications).IsEqualTo(0);
        await Assert.That(resource.Calls.SequenceEqual(["commit", "close", "dispose-connection", "dispose-transaction"])).IsTrue();
    }

    [Test, NotInParallel]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncTransactionTelemetry_CurrentChangedFailureStillCleansAndReports(bool duringStart)
    {
        var resource = new SyncCompletionProbe();
        using var access = new StandaloneSyncTransaction(resource);
        using var caller = new Activity("caller").Start();
        var expected = new Exception("current changed");
        var armed = true;
        using var activities = new TransactionActivityProbe();
        using var metrics = new TransactionMeterProbe();
        EventHandler<ActivityChangedEventArgs> handler = (_, change) =>
        {
            if (armed && (duringStart ? change.Current?.OperationName == "datalinq.db.transaction"
                : ReferenceEquals(change.Current, caller) && change.Previous?.OperationName == "datalinq.db.transaction"))
            {
                armed = false;
                throw expected;
            }
        };
        Activity.CurrentChanged += handler;
        Exception failure;
        try { failure = Capture<Exception>(() => { access.Start(); access.Commit(); }); }
        finally { Activity.CurrentChanged -= handler; }
        await Assert.That(failure).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Completion).IsEqualTo(duringStart ? ExecutionCompletion.NotAttempted : ExecutionCompletion.Committed);
        await Assert.That(ExecutionFailureContexts.Get(failure)!.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(Activity.Current).IsSameReferenceAs(caller);
        await Assert.That(activities.Stopped.Count).IsEqualTo(1);
        await Assert.That(metrics.Reports.Count).IsEqualTo(3);
        await Assert.That(resource.Calls.Count).IsEqualTo(duringStart ? 3 : 4);
    }

    [Test, NotInParallel]
    [Arguments("commit")]
    [Arguments("rollback")]
    [Arguments("dispose")]
    public async Task SyncTransactionTelemetry_StandaloneReportsAfterNativeCleanup(string operation)
    {
        var resource = new SyncCompletionProbe();
        using var access = new StandaloneSyncTransaction(resource);
        var observations = 0;
        void Observe()
        {
            if (resource.Calls.Count != 4) throw new Exception("Reporting preceded cleanup.");
            observations++;
        }
        using var activities = new TransactionActivityProbe(stopping: _ => Observe());
        using var metrics = new TransactionMeterProbe(name => { if (name != "datalinq.db.transactions.started") Observe(); });
        access.Start();
        if (operation == "commit") access.Commit();
        else if (operation == "rollback") access.Rollback();
        else access.Dispose();
        await Assert.That(observations).IsEqualTo(3);
    }

    [Test, NotInParallel]
    public async Task SyncTransactionTelemetry_DisposeNativeFailureKeepsRequestedKindAndCleansResources()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var expected = new Exception("implicit rollback");
        var resource = access.SyncCompletionResourceForTest = new() { NativeFailure = expected };
        var failure = Capture<Exception>(transaction.Dispose);
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Recovery);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(resource.Calls.Count).IsEqualTo(4);
        transaction.Dispose();
        await Assert.That(resource.Calls.Count).IsEqualTo(4);
    }

    [Test, NotInParallel]
    public async Task SyncTransactionTelemetry_ReusedCleanupAndReporterExceptionKeepsCleanupFact()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var access = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var expected = new Exception("reused");
        access.SyncCompletionResourceForTest = new() { CloseFailure = expected, ConnectionFailure = expected, TransactionFailure = expected };
        using var activities = new TransactionActivityProbe(stopping: _ => throw expected);
        using var metrics = new TransactionMeterProbe(name => { if (name != "datalinq.db.transactions.started") throw expected; });
        access.StartSyncTelemetryForTest();
        var failure = Capture<Exception>(transaction.Commit);
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
    }

    // Represents the same owned-resource contract as the built-in providers,
    // without changing the legacy scripted provider used by unrelated tests.
    private sealed class StandaloneSyncTransaction(SyncCompletionProbe resource) : DatabaseTransaction(TransactionType.ReadAndWrite), ISyncTransactionCompletionResource
    {
        internal void Start() { SetStatus(DatabaseTransactionStatus.Open); BeginTransactionTelemetry(); }
        public override void Commit() => CompleteSynchronousTransaction(this, rollback: false);
        public override void Rollback() => CompleteSynchronousTransaction(this, rollback: true);
        public override void Dispose() => DisposeSynchronousTransaction(this);
        void ISyncTransactionCompletionResource.Complete(bool rollback) => resource.Complete(rollback);
        bool ISyncTransactionCompletionResource.RollbackForDisposal() => resource.RollbackForDisposal();
        void ISyncTransactionCompletionResource.CloseConnection() => resource.CloseConnection();
        void ISyncTransactionCompletionResource.DisposeConnection() => resource.DisposeConnection();
        void ISyncTransactionCompletionResource.DisposeTransaction() => resource.DisposeTransaction();
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
    }

    private sealed class SyncCompletionProbe : ISyncTransactionCompletionResource
    {
        internal List<string> Calls { get; } = [];
        internal Exception? NativeFailure { get; set; }
        internal Exception? CloseFailure { get; set; }
        internal Exception? ConnectionFailure { get; set; }
        internal Exception? TransactionFailure { get; set; }
        public void Complete(bool rollback)
        {
            Calls.Add(rollback ? "rollback" : "commit");
            if (NativeFailure is not null) throw NativeFailure;
        }
        public bool RollbackForDisposal() { Complete(rollback: true); return true; }
        public void CloseConnection() { Calls.Add("close"); if (CloseFailure is not null) throw CloseFailure; }
        public void DisposeConnection() { Calls.Add("dispose-connection"); if (ConnectionFailure is not null) throw ConnectionFailure; }
        public void DisposeTransaction() { Calls.Add("dispose-transaction"); if (TransactionFailure is not null) throw TransactionFailure; }
    }
}
