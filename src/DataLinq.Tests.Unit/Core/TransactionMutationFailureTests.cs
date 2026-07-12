using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Tests.Unit.Core;

public sealed class TransactionMutationFailureTests
{
    [Test]
    public async Task CommandConstructionFailure_PoisonsAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(401, "before");
        mutable["Value"] = "after";
        var expected = new InjectedMutationException("command construction");
        fixture.Scenario.CommandFailure = expected;

        var observed = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            expected);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task CommandPreparationMutableDrift_PoisonsBeforeProviderExecution()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(400, "before");
        mutable["Value"] = "captured";
        fixture.Scenario.CommandCreated = () => mutable["Value"] = "command callback";

        var observed = Capture<InvalidOperationException>(() => transaction.Update(mutable));

        await Assert.That(observed.Message).Contains(
            "assignments changed during provider command preparation");
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            observed);
        await Assert.That(mutable["Value"]).IsEqualTo("command callback");
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task ProviderStatementFailure_PoisonsAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(402, "before");
        mutable["Value"] = "after";
        var expected = new InjectedMutationException("provider statement");
        fixture.Scenario.EnqueueNonQueryFailure(expected);

        var observed = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            expected);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task GeneratedIdDecodeFailure_PoisonsBeforeCacheOrTouch()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateNewAutoMutable("keep assignment");
        fixture.Scenario.ScalarResult = long.MaxValue;

        var observed = Capture<GeneratedValueDecodingException>(() =>
            transaction.Insert(mutable));

        await AssertPoisoned(
            transaction,
            TransactionFailureStage.Hydration,
            observed);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await Assert.That(mutable["Id"]).IsNull();
        await Assert.That(mutable["Value"]).IsEqualTo("keep assignment");
        await Assert.That(mutable.GetChanges()).IsNotEmpty();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.ScalarExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task TransactionCacheNotificationFailure_PoisonsAfterStatementAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(403, "before");
        mutable["Value"] = "after";
        var expected = new InjectedMutationException("transaction cache notification");
        var notification = new ThrowingNotification(expected);
        fixture.RowCache.SubscribeToChanges(notification, transaction);

        var observed = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.PendingCacheApplication,
            expected);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(notification.ClearCalls).IsEqualTo(1);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);

        transaction.Rollback();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(1);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
    }

    [Test]
    public async Task RowReloadMiss_PoisonsAfterCacheApplicationAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(404, "before");
        mutable["Value"] = "after";

        var observed = Capture<ModelLoadFailureException>(() => transaction.Update(mutable));

        await AssertPoisoned(
            transaction,
            TransactionFailureStage.Hydration,
            observed);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(1);
    }

    [Test]
    public async Task EarlierSuccessfulDeleteThenFailedUpdate_RetainsOnlySuccessAndGatesReuse()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(405, "delete");
        var failed = fixture.CreateExistingMutable(406, "before");
        failed["Value"] = "after";
        var expected = new InjectedMutationException("second statement");
        fixture.Scenario.EnqueueNonQueryResult(1);
        fixture.Scenario.EnqueueNonQueryFailure(expected);
        var deferredRead = transaction.GetFromQuery<TransactionMutationGuardRow>(
            "SELECT id, value FROM transaction_mutation_guard_rows");

        transaction.Delete(deleted);
        var successfulChange = transaction.Changes.Single();
        var observed = Capture<InjectedMutationException>(() => transaction.Update(failed));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            expected);
        await Assert.That(transaction.Changes).Count().IsEqualTo(1);
        await Assert.That(transaction.Changes.Single()).IsSameReferenceAs(successfulChange);
        await Assert.That(successfulChange.Model).IsSameReferenceAs(deleted);
        await Assert.That(transaction.TouchedMutables).Count().IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables.Single()).IsSameReferenceAs(deleted);
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(deleted.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await AssertInvalidMutation(failed, expectedValue: "after");

        var countsBeforeGates = fixture.Scenario.SnapshotCounts();
        var readFailure = Capture<TransactionPoisonedException>(() => deferredRead.ToArray());
        var queryRootFailure = Capture<TransactionPoisonedException>(() => transaction.Query());
        var callbackInvoked = false;
        var writeFailure = Capture<TransactionPoisonedException>(() =>
            transaction.Update(failed, _ => callbackInvoked = true));
        var commitFailure = Capture<TransactionPoisonedException>(transaction.Commit);

        await AssertPoisonDiagnostic(readFailure, transaction.TransactionID);
        await AssertPoisonDiagnostic(queryRootFailure, transaction.TransactionID);
        await AssertPoisonDiagnostic(writeFailure, transaction.TransactionID);
        await AssertPoisonDiagnostic(commitFailure, transaction.TransactionID);
        await Assert.That(callbackInvoked).IsFalse();
        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(countsBeforeGates);

        transaction.Rollback();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(deleted.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
    }

    [Test]
    public async Task PoisonedTransaction_DisposeRemainsLegalAndIdempotent()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(407, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("dispose recovery"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        transaction.Dispose();
        transaction.Dispose();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(1);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
    }

    [Test]
    public async Task SuccessfulMutableDelete_RollbackInvalidatesAndDiscardsScopedStateBeforeStatusPublication()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(430, "delete then roll back");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var scopedNotification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(scopedNotification, transaction);
        RollbackPublicationSnapshot? observed = null;
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status == DatabaseTransactionStatus.RolledBack)
            {
                observed = CaptureRollbackPublication(
                    fixture,
                    transaction,
                    deleted,
                    scopedNotification);
            }
        };

        transaction.Rollback();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await AssertRollbackInvalidation(
            transaction,
            deleted,
            MutableTransactionOutcome.RolledBack,
            MutableInvalidationReason.RolledBack);
        await AssertFinalizedRollbackPublication(
            observed,
            MutableTransactionOutcome.RolledBack,
            MutableInvalidationReason.RolledBack);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);

        fixture.RowCache.ClearRows();

        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);
        var resetFailure = Capture<InvalidOperationException>(deleted.Reset);
        using var retry = fixture.Database.Transaction();
        var countsBeforeRetry = fixture.Scenario.SnapshotCounts();
        var retryFailure = Capture<MutationGuardException>(() => retry.Delete(deleted));

        await Assert.That(resetFailure.Message).Contains("RolledBack");
        await Assert.That(retryFailure.Message).Contains("transaction was rolled back");
        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(countsBeforeRetry);
        await Assert.That(fixture.RowCache.IsTransactionInCache(retry)).IsFalse();
    }

    [Test]
    public async Task SuccessfulMutableDelete_OpenDisposeInvalidatesAndDiscardsScopedStateBeforeStatusPublication()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(431, "delete then dispose");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var scopedNotification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(scopedNotification, transaction);
        RollbackPublicationSnapshot? observed = null;
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status == DatabaseTransactionStatus.RolledBack)
            {
                observed = CaptureRollbackPublication(
                    fixture,
                    transaction,
                    deleted,
                    scopedNotification);
            }
        };

        transaction.Dispose();
        transaction.Dispose();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(transaction.IsDisposed).IsTrue();
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(1);
        await AssertRollbackInvalidation(
            transaction,
            deleted,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await AssertFinalizedRollbackPublication(
            observed,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);

        fixture.RowCache.ClearRows();

        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);
        var resetFailure = Capture<InvalidOperationException>(deleted.Reset);
        using var retry = fixture.Database.Transaction();
        var countsBeforeRetry = fixture.Scenario.SnapshotCounts();
        var retryFailure = Capture<MutationGuardException>(() => retry.Delete(deleted));

        await Assert.That(resetFailure.Message).Contains("OpenTransactionDisposed");
        await Assert.That(retryFailure.Message).Contains("open transaction was disposed");
        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(countsBeforeRetry);
        await Assert.That(fixture.RowCache.IsTransactionInCache(retry)).IsFalse();
    }

    [Test]
    public async Task ThrowingRolledBackStatusObserver_CannotInterruptRollbackFinalization()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(432, "throwing rollback observer");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var scopedNotification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(scopedNotification, transaction);
        var expected = new InjectedMutationException("rolled-back status observer");
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status == DatabaseTransactionStatus.RolledBack)
                throw expected;
        };

        var observed = Capture<InjectedMutationException>(transaction.Rollback);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await AssertRollbackInvalidation(
            transaction,
            deleted,
            MutableTransactionOutcome.RolledBack,
            MutableInvalidationReason.RolledBack);
        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);

        fixture.RowCache.ClearRows();

        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);
    }

    [Test]
    public async Task SuccessfulMutableDelete_RollbackProviderFailureInvalidatesAndGatesUntilDispose()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(434, "rollback provider failure");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var scopedNotification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(scopedNotification, transaction);
        var expected = new InjectedMutationException("provider rollback");
        fixture.Scenario.RollbackFailure = expected;

        var observed = Capture<InjectedMutationException>(transaction.Rollback);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await AssertRollbackInvalidation(
            transaction,
            deleted,
            MutableTransactionOutcome.RollbackOutcomeUnknown,
            MutableInvalidationReason.RollbackOutcomeUnknown);
        await AssertManagedCompletionContext(
            expected,
            transaction,
            "Rollback",
            MutableInvalidationReason.RollbackOutcomeUnknown);
        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);

        fixture.RowCache.ClearRows();

        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);
        var countsBeforeGates = fixture.Scenario.SnapshotCounts();
        var readFailure = Capture<InvalidOperationException>(() => _ = transaction.Query());
        var writeFailure = Capture<InvalidOperationException>(() => transaction.Delete(deleted));
        var commitFailure = Capture<InvalidOperationException>(transaction.Commit);
        var secondRollbackFailure = Capture<InvalidOperationException>(transaction.Rollback);

        foreach (var gateFailure in new[]
        {
            readFailure,
            writeFailure,
            commitFailure,
            secondRollbackFailure
        })
        {
            await Assert.That(gateFailure.Message)
                .Contains("rollback outcome is unknown");
            await Assert.That(gateFailure.Message)
                .Contains("Only Dispose() remains legal");
        }

        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(countsBeforeGates);

        transaction.Dispose();

        await Assert.That(transaction.IsDisposed).IsTrue();
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(1);
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.RollbackOutcomeUnknown);
        await Assert.That(deleted.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.RollbackOutcomeUnknown);
    }

    [Test]
    public async Task SuccessfulMutableDelete_DisposeRollbackFailureStillInvalidatesAndCleans()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(435, "dispose rollback failure");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var scopedNotification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(scopedNotification, transaction);
        var expected = new InjectedMutationException("dispose rollback");
        fixture.Scenario.DisposeRollbackFailure = expected;

        var observed = Capture<InjectedMutationException>(transaction.Dispose);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(transaction.IsDisposed).IsTrue();
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await AssertRollbackInvalidation(
            transaction,
            deleted,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await AssertManagedCompletionContext(
            expected,
            transaction,
            "Dispose",
            MutableInvalidationReason.OpenTransactionDisposed);
        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(1);

        fixture.RowCache.ClearRows();
        transaction.Dispose();

        await Assert.That(scopedNotification.ClearCalls).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task CommittedTransaction_DisposeDoesNotReclassifyPromotedMutable()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(433, "commit then dispose");
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);

        transaction.Commit();
        transaction.Dispose();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(transaction.IsDisposed).IsTrue();
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.Committed);
        await Assert.That(deleted.Lifecycle.RowKind).IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(deleted.Lifecycle.InvalidationReason).IsNull();
        await Assert.That(deleted.HasStoredCommittedBaseline).IsTrue();
    }

    [Test]
    public async Task StateChangeChangedAfterCapture_RejectsWithoutPoisoning()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(408, "before");
        mutable["Value"] = "captured";
        var stateChange = new StateChange(
            mutable,
            fixture.RowTable,
            TransactionChangeType.Update);

        mutable["Value"] = "later";

        var observed = Capture<MutationGuardException>(() =>
            stateChange.ExecuteQuery(transaction));

        await Assert.That(observed.Message).Contains(
            "assignments changed after this state change was captured");
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(mutable["Value"]).IsEqualTo("later");
    }

    [Test]
    public async Task SuccessfulImmutableDeleteStateChange_CanBeInspectedButCannotExecuteTwice()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var immutable = fixture.CreateImmutable(418, "delete once");
        var stateChange = new StateChange(
            immutable,
            fixture.RowTable,
            TransactionChangeType.Delete);

        stateChange.ExecuteQuery(transaction);
        _ = stateChange.GetQuery(transaction);
        using var command = stateChange.GetDbCommand(transaction);
        var observed = Capture<MutationGuardException>(() =>
            stateChange.ExecuteQuery(transaction));

        await Assert.That(observed.Message).Contains(
            "state change has already started provider execution");
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes).Count().IsEqualTo(1);
        await Assert.That(transaction.Changes.Single()).IsSameReferenceAs(stateChange);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);

        transaction.Rollback();
    }

    [Test]
    public async Task StateChangeArrayChangedInPlaceAfterCapture_RejectsWithoutPoisoning()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingBinaryMutable(
            [0x01, 0x02],
            [0x10, 0x20]);
        var assignedPayload = new byte[] { 0x30, 0x40 };
        mutable["Payload"] = assignedPayload;
        var stateChange = new StateChange(
            mutable,
            fixture.BinaryTable,
            TransactionChangeType.Update);

        assignedPayload[0] = 0x31;

        var observed = Capture<MutationGuardException>(() =>
            stateChange.ExecuteQuery(transaction));

        await Assert.That(observed.Message).Contains(
            "assignments changed after this state change was captured");
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(((byte[])mutable["Payload"]!).SequenceEqual(
            new byte[] { 0x31, 0x40 })).IsTrue();
    }

    [Test]
    public async Task MutationCacheCallback_ReentrantManagedOperationsAreRejected()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(409, "delete");
        var reentrantUpdate = fixture.CreateExistingMutable(410, "before");
        reentrantUpdate["Value"] = "after";
        var notification = new ReentrantTransactionNotification(
            transaction,
            reentrantUpdate);
        fixture.RowCache.SubscribeToChanges(notification, transaction);

        transaction.Delete(deleted);

        await Assert.That(notification.ClearCalls).IsEqualTo(1);
        await Assert.That(notification.Failures).Count().IsEqualTo(5);
        foreach (var failure in notification.Failures)
        {
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(failure.Message).Contains(
                "another managed transaction operation is being finalized");
        }

        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(transaction.Changes).Count().IsEqualTo(1);
        await Assert.That(transaction.Changes.Single().Model).IsSameReferenceAs(deleted);
        await Assert.That(transaction.TouchedMutables).Count().IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables.Single()).IsSameReferenceAs(deleted);
        await Assert.That(reentrantUpdate.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(reentrantUpdate["Value"]).IsEqualTo("after");
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(0);

        transaction.Rollback();
    }

    [Test]
    public async Task MutationCacheCallback_DirectCurrentMutableDriftPoisonsBeforeHydration()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(415, "before");
        mutable["Value"] = "statement";
        var notification = new MutatingNotification(
            () => mutable["Value"] = "callback");
        fixture.RowCache.SubscribeToChanges(notification, transaction);

        var observed = Capture<InvalidOperationException>(() => transaction.Update(mutable));

        await Assert.That(observed.Message).Contains(
            "assignments changed while the transaction-local cache effect was being applied");
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.PendingCacheApplication,
            observed);
        await Assert.That(notification.ClearCalls).IsEqualTo(1);
        await Assert.That(mutable["Value"]).IsEqualTo("callback");
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task PoisonedTransaction_AfterRollbackReportsTerminalState()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(411, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("terminal diagnostics"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        transaction.Rollback();

        var readFailure = Capture<InvalidOperationException>(() => transaction.Query());
        var writeFailure = Capture<MutationGuardException>(() => transaction.Update(mutable));
        var commitFailure = Capture<InvalidOperationException>(transaction.Commit);
        var rollbackFailure = Capture<InvalidOperationException>(transaction.Rollback);

        await Assert.That(readFailure is TransactionPoisonedException).IsFalse();
        await Assert.That(commitFailure is TransactionPoisonedException).IsFalse();
        await Assert.That(rollbackFailure is TransactionPoisonedException).IsFalse();
        await Assert.That(readFailure.Message).Contains("already rolled back");
        await Assert.That(writeFailure.Message).Contains("already rolled back");
        await Assert.That(commitFailure.Message).Contains("already rolled back");
        await Assert.That(rollbackFailure.Message).Contains("already rolled back");
    }

    [Test]
    public async Task RawDatabaseAccess_RemainsOutsideManagedPoisonGates()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(412, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("raw escape characterization"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));
        var managedFailure = Capture<TransactionPoisonedException>(transaction.Commit);

        var rawAffectedRows = transaction.DatabaseAccess.ExecuteNonQuery(
            new ScriptedDbCommand());
        transaction.DatabaseAccess.Commit();

        await Assert.That(managedFailure.Message).Contains(
            "Low-level DatabaseAccess and underlying IDbTransaction handles are outside this managed guard");
        await Assert.That(rawAffectedRows).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(2);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
    }

    [Test]
    public async Task PoisonedTransaction_FirstNullForeignKeyAccessIsRejected()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(414, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("null foreign key gate"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));
        var foreignKey = new ImmutableForeignKey<TransactionMutationGuardOtherRow>(
            DataLinqKey.Null,
            transaction,
            property: null!);

        var observed = Capture<TransactionPoisonedException>(() => _ = foreignKey.Value);

        await AssertPoisonDiagnostic(observed, transaction.TransactionID);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutedStateChange_FreezesRelationImpactKeys()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(413, "before");
        mutable["Value"] = "executed";
        var stateChange = new StateChange(
            mutable,
            fixture.RowTable,
            TransactionChangeType.Update);
        var valueIndex = fixture.RowTable.ColumnIndices.Single(
            index => index.Name == "idx_transaction_mutation_guard_value");

        stateChange.ExecutePreflightedQuery(transaction);
        mutable["Value"] = "later";

        await Assert.That(stateChange.GetCurrentRelationKey(valueIndex).GetValue(0))
            .IsEqualTo("executed");
        await Assert.That(mutable["Value"]).IsEqualTo("later");
    }

    [Test]
    public async Task SuccessfulRelationKeyFinalization_UsesAuthoritativeReloadValues()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(416, "before");
        mutable["Value"] = "statement";
        var stateChange = new StateChange(
            mutable,
            fixture.RowTable,
            TransactionChangeType.Update);
        var valueIndex = fixture.RowTable.ColumnIndices.Single(
            index => index.Name == "idx_transaction_mutation_guard_value");

        stateChange.ExecutePreflightedQuery(transaction);
        var authoritative = fixture.CreateImmutable(416, "database-generated");
        stateChange.FinalizeSuccessfulRelationKeys(authoritative);
        mutable["Value"] = "later";

        await Assert.That(stateChange.GetCurrentRelationKey(valueIndex).GetValue(0))
            .IsEqualTo("database-generated");
        await Assert.That(mutable["Value"]).IsEqualTo("later");
    }

    [Test]
    public async Task DeleteImpactKey_UsesStoredBaselineInsteadOfUnsavedMutableValue()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(417, "stored");
        mutable["Value"] = "unsaved";
        var valueIndex = fixture.RowTable.ColumnIndices.Single(
            index => index.Name == "idx_transaction_mutation_guard_value");

        transaction.Delete(mutable);
        var stateChange = transaction.Changes.Single();

        await Assert.That(stateChange.GetCurrentRelationKey(valueIndex).GetValue(0))
            .IsEqualTo("stored");
        await Assert.That(mutable["Value"]).IsEqualTo("unsaved");
    }

    [Test]
    public async Task CommitStatusPublication_ObservesFullyFinalizedCommitState()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var firstDeleted = fixture.CreateExistingMutable(418, "delete one");
        var secondDeleted = fixture.CreateExistingMutable(424, "delete two");
        var transactionBound = fixture.CreateImmutable(419, "bound", transaction);
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(firstDeleted);
        transaction.Delete(secondDeleted);
        CommitPublicationSnapshot? observed = null;
        var statusCalls = 0;
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status != DatabaseTransactionStatus.Committed)
                return;

            statusCalls++;
            observed = new CommitPublicationSnapshot(
                fixture.RowCache.IsTransactionInCache(transaction),
                transaction.TouchedMutables.Count,
                transaction.MutableOwnership.Outcome,
                firstDeleted.Lifecycle,
                transactionBound.GetReadSource());
        };

        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsTrue();
        await Assert.That(transaction.TouchedMutables.Count).IsEqualTo(2);
        await Assert.That(firstDeleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(firstDeleted.HasStoredCommittedBaseline).IsFalse();
        await Assert.That(secondDeleted.HasStoredCommittedBaseline).IsFalse();

        transaction.Commit();

        await Assert.That(statusCalls).IsEqualTo(1);
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.TransactionCachePresent).IsFalse();
        await Assert.That(observed.TouchedMutableCount).IsEqualTo(0);
        await Assert.That(observed.OwnershipOutcome)
            .IsEqualTo(MutableTransactionOutcome.Committed);
        await Assert.That(observed.MutableLifecycle.RowKind)
            .IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(observed.MutableLifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(observed.MutableLifecycle.TransactionOwner).IsNull();
        await Assert.That(firstDeleted.HasStoredCommittedBaseline).IsTrue();
        await Assert.That(secondDeleted.HasStoredCommittedBaseline).IsTrue();
        await Assert.That(observed.ReadSource)
            .IsSameReferenceAs(fixture.Provider.ReadOnlyAccess);
    }

    [Test]
    public async Task CommitFailureAfterProviderStatus_DoesNotPublishPromoteOrRaiseWrapperStatus()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(425, "delete");
        var transactionBound = fixture.CreateImmutable(428, "bound", transaction);
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var notification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(notification);
        var wrapperCommittedCalls = 0;
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status == DatabaseTransactionStatus.Committed)
                wrapperCommittedCalls++;
        };
        var expected = new InjectedMutationException("provider commit after status");
        fixture.Scenario.CommitFailureAfterStatus = expected;

        var observed = Capture<InjectedMutationException>(transaction.Commit);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(wrapperCommittedCalls).IsEqualTo(0);
        await Assert.That(notification.ClearCalls).IsEqualTo(0);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsTrue();
        await Assert.That(transaction.Changes).Count().IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables.Count).IsEqualTo(1);
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.CommitOutcomeUnknown);
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(deleted.Lifecycle.TransactionOwner).IsNull();
        await Assert.That(deleted.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.CommitOutcomeUnknown);
        await Assert.That(deleted.HasStoredCommittedBaseline).IsFalse();

        var countsBeforeGates = fixture.Scenario.SnapshotCounts();
        var fallbackFailure = Capture<InvalidOperationException>(() =>
            _ = transactionBound.GetReadSource());
        var writeFailure = Capture<InvalidOperationException>(() => transaction.Delete(deleted));
        var rollbackFailure = Capture<InvalidOperationException>(transaction.Rollback);

        foreach (var gateFailure in new[] { fallbackFailure, writeFailure, rollbackFailure })
        {
            await Assert.That(gateFailure.Message).Contains("provider commit call failed");
            await Assert.That(gateFailure.Message).Contains("only Dispose() remains legal");
        }

        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(countsBeforeGates);

        fixture.Scenario.CommitFailureAfterStatus = null;
        transaction.Dispose();

        await Assert.That(transaction.IsDisposed).IsTrue();
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.CommitOutcomeUnknown);
        await Assert.That(deleted.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.CommitOutcomeUnknown);
    }

    [Test]
    public async Task GlobalCommitNotificationFailure_InvalidatesLocalStateAndClearsProviderCaches()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(426, "delete");
        fixture.PrimeCommittedRow(901, "unrelated committed row");
        fixture.PrimeCommittedOtherRow(902);
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var expected = new InjectedMutationException("global commit notification");
        var throwingNotification = new ThrowingNotification(expected);
        var laterNotification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(throwingNotification);
        fixture.RowCache.SubscribeToChanges(laterNotification);
        var wrapperCommittedCalls = 0;
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status == DatabaseTransactionStatus.Committed)
                wrapperCommittedCalls++;
        };

        await Assert.That(fixture.RowCache.RowCount).IsEqualTo(1);
        await Assert.That(fixture.OtherRowCache.RowCount).IsEqualTo(1);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsTrue();
        await Assert.That(transaction.TouchedMutables.Count).IsEqualTo(1);
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);

        var observed = Capture<TransactionCommitFinalizationException>(transaction.Commit);

        await Assert.That(observed.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(observed.InnerException).IsSameReferenceAs(expected);
        await Assert.That(observed.CleanupFailures).IsEmpty();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(wrapperCommittedCalls).IsEqualTo(0);
        await Assert.That(throwingNotification.ClearCalls).IsEqualTo(1);
        await Assert.That(laterNotification.ClearCalls).IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.CommittedStateFinalizationFailed);
        await Assert.That(deleted.Lifecycle.RowKind).IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(deleted.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.CommittedStateFinalizationFailed);
        await Assert.That(deleted.HasStoredCommittedBaseline).IsFalse();
        await Assert.That(fixture.Provider.State.Cache.TableCaches.Values.All(
            cache => !cache.IsTransactionInCache(transaction))).IsTrue();
        await Assert.That(fixture.Provider.State.Cache.TableCaches.Values.All(
            IsStructurallyEmpty)).IsTrue();

        using var retry = fixture.Database.Transaction();
        var countsBeforeRetry = fixture.Scenario.SnapshotCounts();
        var retryFailure = Capture<MutationGuardException>(() => retry.Update(deleted));

        await Assert.That(retryFailure.Message)
            .Contains("the database committed but local state finalization failed");
        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(countsBeforeRetry);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(0);
    }

    [Test]
    public async Task RecoveryNotificationFailure_IsReportedWithoutMaskingPrimaryFailure()
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(427, "delete");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var recoveryTables = fixture.Provider.State.Cache.TableCaches.Values
            .Where(cache => !ReferenceEquals(cache, fixture.RowCache))
            .ToArray();
        var recoveryFailureTable = recoveryTables[^2];
        var laterRecoveryTable = recoveryTables[^1];
        var primaryFailure = new InjectedMutationException("primary global commit notification");
        var recoveryFailure = new InjectedMutationException("recovery-only notification");
        var primaryNotification = new ThrowingNotification(primaryFailure);
        var recoveryNotification = new ThrowingNotification(recoveryFailure);
        var laterNotification = new CountingNotification();
        fixture.RowCache.SubscribeToChanges(primaryNotification);
        recoveryFailureTable.SubscribeToChanges(recoveryNotification);
        laterRecoveryTable.SubscribeToChanges(laterNotification);
        var wrapperCommittedCalls = 0;
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status == DatabaseTransactionStatus.Committed)
                wrapperCommittedCalls++;
        };

        var observed = Capture<TransactionCommitFinalizationException>(transaction.Commit);

        await Assert.That(observed.InnerException).IsSameReferenceAs(primaryFailure);
        await Assert.That(observed.CleanupFailures.Count).IsEqualTo(1);
        await Assert.That(observed.CleanupFailures[0]).IsSameReferenceAs(recoveryFailure);
        await Assert.That(primaryNotification.ClearCalls).IsEqualTo(1);
        await Assert.That(recoveryNotification.ClearCalls).IsEqualTo(1);
        await Assert.That(laterNotification.ClearCalls).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(wrapperCommittedCalls).IsEqualTo(0);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(fixture.Provider.State.Cache.TableCaches.Values.All(
            cache => !cache.IsTransactionInCache(transaction))).IsTrue();
    }

    [Test]
    public async Task GlobalCommitNotification_CannotSwitchImmutableReadSourceBeforeFinalization()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(420, "delete");
        var transactionBound = fixture.CreateImmutable(421, "bound", transaction);
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var notification = new InspectingNotification(() =>
        {
            var cachePresent = fixture.RowCache.IsTransactionInCache(transaction);
            var failure = Capture<InvalidOperationException>(() =>
                _ = transactionBound.GetReadSource());
            return new PrePublicationSnapshot(
                transaction.Status,
                cachePresent,
                transaction.TouchedMutables.Count,
                transaction.MutableOwnership.Outcome,
                deleted.Lifecycle,
                failure);
        });
        fixture.RowCache.SubscribeToChanges(notification);

        transaction.Commit();

        await Assert.That(notification.ClearCalls).IsEqualTo(1);
        await Assert.That(notification.Snapshot).IsNotNull();
        await Assert.That(notification.Snapshot!.ProviderStatus)
            .IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(notification.Snapshot.TransactionCachePresent).IsTrue();
        await Assert.That(notification.Snapshot.TouchedMutableCount).IsEqualTo(1);
        await Assert.That(notification.Snapshot.OwnershipOutcome)
            .IsEqualTo(MutableTransactionOutcome.Unresolved);
        await Assert.That(notification.Snapshot.MutableLifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(notification.Snapshot.MutableLifecycle.TransactionOwner)
            .IsSameReferenceAs(transaction.MutableOwnership);
        await Assert.That(notification.Snapshot.FallbackFailure.Message)
            .Contains("while another managed transaction operation is being finalized");
        await Assert.That(transactionBound.GetReadSource())
            .IsSameReferenceAs(fixture.Provider.ReadOnlyAccess);
    }

    [Test]
    public async Task ThrowingCommittedStatusObserver_CannotInterruptCommitFinalization()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(422, "delete");
        var transactionBound = fixture.CreateImmutable(423, "bound", transaction);
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        fixture.Scenario.EnqueueNonQueryResult(1);
        transaction.Delete(deleted);
        var expected = new InjectedMutationException("committed status observer");
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status == DatabaseTransactionStatus.Committed)
                throw expected;
        };

        var observed = Capture<InjectedMutationException>(transaction.Commit);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.Committed);
        await Assert.That(deleted.Lifecycle.RowKind).IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(deleted.Lifecycle.TransactionOwner).IsNull();
        await Assert.That(deleted.HasStoredCommittedBaseline).IsTrue();
        await Assert.That(transactionBound.GetReadSource())
            .IsSameReferenceAs(fixture.Provider.ReadOnlyAccess);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
    }

    private static async Task AssertPoisoned(
        Transaction transaction,
        TransactionFailureStage expectedStage,
        Exception expectedCause)
    {
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.Failure).IsNotNull();
        await Assert.That(transaction.Failure!.Stage).IsEqualTo(expectedStage);
        await Assert.That(transaction.Failure.Cause).IsSameReferenceAs(expectedCause);
    }

    private static RollbackPublicationSnapshot CaptureRollbackPublication(
        ScriptedFixture fixture,
        Transaction transaction,
        Mutable<TransactionMutationGuardRow> mutable,
        CountingNotification scopedNotification) =>
        new(
            fixture.RowCache.IsTransactionInCache(transaction),
            transaction.TouchedMutables.Count,
            transaction.MutableOwnership.Outcome,
            mutable.Lifecycle,
            scopedNotification.ClearCalls);

    private static async Task AssertRollbackInvalidation(
        Transaction transaction,
        Mutable<TransactionMutationGuardRow> mutable,
        MutableTransactionOutcome expectedOutcome,
        MutableInvalidationReason expectedReason)
    {
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(transaction.MutableOwnership.Outcome).IsEqualTo(expectedOutcome);
        await Assert.That(mutable.Lifecycle.RowKind).IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.TransactionOwner).IsNull();
        await Assert.That(mutable.Lifecycle.InvalidationReason).IsEqualTo(expectedReason);
        await Assert.That(mutable.HasStoredCommittedBaseline).IsFalse();
    }

    private static async Task AssertFinalizedRollbackPublication(
        RollbackPublicationSnapshot? observation,
        MutableTransactionOutcome expectedOutcome,
        MutableInvalidationReason expectedReason)
    {
        await Assert.That(observation).IsNotNull();
        await Assert.That(observation!.TransactionCachePresent).IsFalse();
        await Assert.That(observation.TouchedMutableCount).IsEqualTo(0);
        await Assert.That(observation.OwnershipOutcome).IsEqualTo(expectedOutcome);
        await Assert.That(observation.MutableLifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(observation.MutableLifecycle.TransactionOwner).IsNull();
        await Assert.That(observation.MutableLifecycle.InvalidationReason)
            .IsEqualTo(expectedReason);
        await Assert.That(observation.ScopedNotificationClearCalls).IsEqualTo(0);
    }

    private static async Task AssertManagedCompletionContext(
        Exception exception,
        Transaction transaction,
        string operation,
        MutableInvalidationReason expectedReason)
    {
        await Assert.That(exception.Data["DataLinq.TransactionId"])
            .IsEqualTo(transaction.TransactionID);
        await Assert.That(exception.Data["DataLinq.CompletionOperation"])
            .IsEqualTo(operation);
        await Assert.That(
            exception.Data["DataLinq.LocalFinalizationAttempted"] is true)
            .IsTrue();
        await Assert.That(exception.Data["DataLinq.MutableInvalidationReason"])
            .IsEqualTo(expectedReason.ToString());
        await Assert.That(exception.Data.Contains("DataLinq.SecondaryCompletionFailures"))
            .IsFalse();
    }

    private static bool IsStructurallyEmpty(TableCache cache) =>
        cache.RowCount == 0 &&
        cache.TransactionRowsCount == 0 &&
        cache.IndicesCount.All(index => index.count == 0);

    private static async Task AssertInvalidMutation(
        Mutable<TransactionMutationGuardRow> mutable,
        string expectedValue)
    {
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await Assert.That(mutable["Value"]).IsEqualTo(expectedValue);
        await Assert.That(mutable.GetChanges()).IsNotEmpty();
    }

    private static async Task AssertPoisonDiagnostic(
        TransactionPoisonedException exception,
        uint transactionId)
    {
        await Assert.That(exception.Message).Contains($"transaction {transactionId}");
        await Assert.That(exception.Message).Contains("Rollback() or Dispose()");
        await Assert.That(exception.Message).DoesNotContain("Data Source=");
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class ScriptedFixture : IDisposable
    {
        internal ScriptedFixture()
        {
            Scenario = new ScriptedMutationScenario();
            Provider = new ScriptedMutationProvider(Scenario);
            Database = new ScriptedDatabase(Provider);
            RowTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardRow)).Table;
            BinaryTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardBinaryRow)).Table;
            AutoTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardAutoRow)).Table;
            OtherRowTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardOtherRow)).Table;
            RowCache = Provider.GetTableCache(RowTable);
            OtherRowCache = Provider.GetTableCache(OtherRowTable);
        }

        internal ScriptedMutationScenario Scenario { get; }
        internal ScriptedMutationProvider Provider { get; }
        internal ScriptedDatabase Database { get; }
        internal TableDefinition RowTable { get; }
        internal TableDefinition BinaryTable { get; }
        internal TableDefinition AutoTable { get; }
        internal TableDefinition OtherRowTable { get; }
        internal TableCache RowCache { get; }
        internal TableCache OtherRowCache { get; }

        internal Mutable<TransactionMutationGuardRow> CreateExistingMutable(
            int id,
            string value) =>
            new(CreateImmutable(id, value));

        internal TransactionMutationGuardRow CreateImmutable(
            int id,
            string value,
            IDataSourceAccess? dataSource = null) =>
            InstanceFactory.NewImmutableRow<TransactionMutationGuardRow>(
                new ScriptedRowData(RowTable, id, value),
                dataSource ?? Provider.ReadOnlyAccess);

        internal Mutable<TransactionMutationGuardAutoRow> CreateNewAutoMutable(
            string value)
        {
            var mutable = new Mutable<TransactionMutationGuardAutoRow>();
            mutable["Value"] = value;
            return mutable;
        }

        internal Mutable<TransactionMutationGuardBinaryRow> CreateExistingBinaryMutable(
            byte[] id,
            byte[] payload) =>
            new(InstanceFactory.NewImmutableRow<TransactionMutationGuardBinaryRow>(
                new ScriptedBinaryRowData(BinaryTable, id, payload),
                Provider.ReadOnlyAccess));

        internal void PrimeCommittedRow(int id, string value)
        {
            var rows = new RowCache();
            if (!rows.TryAddRow(id, 128, CreateImmutable(id, value)))
                throw new InvalidOperationException("Could not prime the committed row cache.");

            SetCommittedRows(RowCache, rows);
        }

        internal void PrimeCommittedOtherRow(int id)
        {
            var immutable = InstanceFactory.NewImmutableRow<TransactionMutationGuardOtherRow>(
                new ScriptedOtherRowData(OtherRowTable, id),
                Provider.ReadOnlyAccess);
            var rows = new RowCache();
            if (!rows.TryAddRow(id, 128, immutable))
                throw new InvalidOperationException("Could not prime the other committed row cache.");

            SetCommittedRows(OtherRowCache, rows);
        }

        private static void SetCommittedRows(TableCache cache, RowCache rows)
        {
            var rowCacheField = typeof(TableCache).GetField(
                "rowCache",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            rowCacheField!.SetValue(cache, rows);
        }

        public void Dispose() => Database.Dispose();
    }

    private sealed class ScriptedDatabase(ScriptedMutationProvider provider)
        : Database<TransactionMutationGuardDb>(provider);

    private readonly record struct ScriptedCounts(
        int CommandCreations,
        int NonQueryExecutions,
        int ScalarExecutions,
        int ReaderExecutions,
        int Commits,
        int Rollbacks,
        int Disposals);

    private sealed class ScriptedMutationScenario
    {
        private readonly Queue<object> nonQuerySteps = [];

        internal Exception? CommandFailure { get; set; }
        internal Action? CommandCreated { get; set; }
        internal object? ScalarResult { get; set; } = 1L;
        internal int CommandCreations { get; set; }
        internal int NonQueryExecutions { get; set; }
        internal int ScalarExecutions { get; set; }
        internal int ReaderExecutions { get; set; }
        internal int Commits { get; set; }
        internal int Rollbacks { get; set; }
        internal int Disposals { get; set; }
        internal Exception? CommitFailureAfterStatus { get; set; }
        internal Exception? RollbackFailure { get; set; }
        internal Exception? DisposeRollbackFailure { get; set; }

        internal void EnqueueNonQueryResult(int result) => nonQuerySteps.Enqueue(result);
        internal void EnqueueNonQueryFailure(Exception exception) => nonQuerySteps.Enqueue(exception);

        internal int ExecuteNonQuery()
        {
            NonQueryExecutions++;
            if (nonQuerySteps.Count == 0)
                return 1;

            var step = nonQuerySteps.Dequeue();
            if (step is Exception exception)
                throw exception;

            return (int)step;
        }

        internal ScriptedCounts SnapshotCounts() =>
            new(
                CommandCreations,
                NonQueryExecutions,
                ScalarExecutions,
                ReaderExecutions,
                Commits,
                Rollbacks,
                Disposals);
    }

    private sealed class ScriptedMutationProvider : DatabaseProvider<TransactionMutationGuardDb>
    {
        private readonly ScriptedMutationScenario scenario;
        private readonly ScriptedDatabaseAccess databaseAccess;
        private readonly ScriptedWriter writer = new();

        internal ScriptedMutationProvider(ScriptedMutationScenario scenario)
            : base(
                "scripted-transaction-mutation-failure-tests",
                DatabaseType.SQLite,
                DataLinqLoggingConfiguration.NullConfiguration,
                "transaction-mutation-failure-tests")
        {
            this.scenario = scenario;
            databaseAccess = new ScriptedDatabaseAccess(this);
        }

        public override IDatabaseProviderConstants Constants { get; } = new ScriptedProviderConstants();
        public override DatabaseAccess DatabaseAccess => databaseAccess;

        public override IDbCommand ToDbCommand(IQuery query)
        {
            scenario.CommandCreations++;
            if (scenario.CommandFailure is not null)
                throw scenario.CommandFailure;

            var command = new ScriptedDbCommand();
            scenario.CommandCreated?.Invoke();
            return command;
        }

        public override IDataLinqDataWriter GetWriter() => writer;

        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) =>
            new ScriptedDatabaseTransaction(this, type, scenario);

        public override DatabaseTransaction AttachDatabaseTransaction(
            IDbTransaction dbTransaction,
            TransactionType type) =>
            throw new NotSupportedException();

        public override string GetLastIdQuery() => throw new NotSupportedException();
        public override string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public override string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public override Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public override Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public override string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public override Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public override Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public override Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public override Sql GetCreateSql() => throw new NotSupportedException();
        public override bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public override bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public override bool FileOrServerExists() => throw new NotSupportedException();
        public override IDbConnection GetDbConnection() => throw new NotSupportedException();
    }

    private sealed class ScriptedDatabaseAccess(IDatabaseProvider provider) : DatabaseAccess(provider)
    {
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
    }

    private sealed class ScriptedProviderConstants : IDatabaseProviderConstants
    {
        public string ParameterSign => "@";
        public string LastInsertCommand => string.Empty;
        public string EscapeCharacter => "\"";
        public bool SupportsMultipleDatabases => false;
    }

    private sealed class ScriptedWriter : IDataLinqDataWriter
    {
        public object? ConvertValue(ColumnDefinition column, object? value) => value;
    }

    private sealed class ScriptedDbCommand : IDbCommand
    {
        [AllowNull]
        public string CommandText { get; set; } = "SCRIPTED MUTATION";
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters => throw new NotSupportedException();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel() => throw new NotSupportedException();
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public int ExecuteNonQuery() => throw new NotSupportedException();
        public IDataReader ExecuteReader() => throw new NotSupportedException();
        public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
        public object? ExecuteScalar() => throw new NotSupportedException();
        public void Prepare() => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class ScriptedDatabaseTransaction : DatabaseTransaction
    {
        private readonly ScriptedMutationScenario scenario;

        internal ScriptedDatabaseTransaction(
            IDatabaseProvider provider,
            TransactionType type,
            ScriptedMutationScenario scenario)
            : base(provider, type)
        {
            this.scenario = scenario;
            SetStatus(DatabaseTransactionStatus.Open);
        }

        public override IDataLinqDataReader ExecuteReader(IDbCommand command)
        {
            scenario.ReaderExecutions++;
            return EmptyReader.Instance;
        }

        public override IDataLinqDataReader ExecuteReader(string query)
        {
            scenario.ReaderExecutions++;
            return EmptyReader.Instance;
        }

        public override object? ExecuteScalar(IDbCommand command)
        {
            scenario.ScalarExecutions++;
            return scenario.ScalarResult;
        }

        public override T ExecuteScalar<T>(IDbCommand command) =>
            (T)Convert.ChangeType(ExecuteScalar(command)!, typeof(T));

        public override object? ExecuteScalar(string query)
        {
            scenario.ScalarExecutions++;
            return scenario.ScalarResult;
        }

        public override T ExecuteScalar<T>(string query) =>
            (T)Convert.ChangeType(ExecuteScalar(query)!, typeof(T));

        public override int ExecuteNonQuery(IDbCommand command) =>
            scenario.ExecuteNonQuery();

        public override int ExecuteNonQuery(string query) =>
            scenario.ExecuteNonQuery();

        public override void Commit()
        {
            scenario.Commits++;
            SetStatus(DatabaseTransactionStatus.Committed);
            if (scenario.CommitFailureAfterStatus is not null)
                throw scenario.CommitFailureAfterStatus;
        }

        public override void Rollback()
        {
            scenario.Rollbacks++;
            if (scenario.RollbackFailure is not null)
                throw scenario.RollbackFailure;

            SetStatus(DatabaseTransactionStatus.RolledBack);
        }

        public override void Dispose()
        {
            scenario.Disposals++;
            if (Status == DatabaseTransactionStatus.Open)
            {
                if (scenario.DisposeRollbackFailure is not null)
                    throw scenario.DisposeRollbackFailure;

                SetStatus(DatabaseTransactionStatus.RolledBack);
            }
        }
    }

    private sealed class ScriptedRowData(
        TableDefinition table,
        int id,
        string value) : IRowData
    {
        public TableDefinition Table { get; } = table;

        public object? this[ColumnDefinition column] =>
            column.DbName switch
            {
                "id" => id,
                "value" => value,
                _ => null
            };

        public object? this[int columnIndex] => this[Table.Columns[columnIndex]];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(
                column,
                this[column]));
    }

    private sealed class ScriptedBinaryRowData(
        TableDefinition table,
        byte[] id,
        byte[] payload) : IRowData
    {
        public TableDefinition Table { get; } = table;

        public object? this[ColumnDefinition column] =>
            column.DbName switch
            {
                "id" => id,
                "payload" => payload,
                _ => null
            };

        public object? this[int columnIndex] => this[Table.Columns[columnIndex]];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(
                column,
                this[column]));
    }

    private sealed class ScriptedOtherRowData(
        TableDefinition table,
        int id) : IRowData
    {
        public TableDefinition Table { get; } = table;

        public object? this[ColumnDefinition column] =>
            column.DbName == "id" ? id : null;

        public object? this[int columnIndex] => this[Table.Columns[columnIndex]];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(
                column,
                this[column]));
    }

    private sealed class EmptyReader : IDataLinqDataReader
    {
        internal static EmptyReader Instance { get; } = new();

        public bool ReadNextRow() => false;
        public void Dispose() { }
        public object GetValue(int ordinal) => throw new NotSupportedException();
        public int GetOrdinal(string name) => throw new NotSupportedException();
        public string GetString(int ordinal) => throw new NotSupportedException();
        public bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public int GetInt32(int ordinal) => throw new NotSupportedException();
        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();
        public Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
        public T? GetValue<T>(ColumnDefinition column) => throw new NotSupportedException();
        public T? GetValue<T>(ColumnDefinition column, int ordinal) => throw new NotSupportedException();
        public bool IsDbNull(int ordinal) => throw new NotSupportedException();
    }

    private sealed class ThrowingNotification(Exception exception) : ICacheNotification
    {
        internal int ClearCalls { get; private set; }

        public void Clear()
        {
            ClearCalls++;
            throw exception;
        }
    }

    private sealed class ReentrantTransactionNotification(
        Transaction<TransactionMutationGuardDb> transaction,
        Mutable<TransactionMutationGuardRow> mutable) : ICacheNotification
    {
        internal int ClearCalls { get; private set; }
        internal List<Exception> Failures { get; } = [];

        public void Clear()
        {
            ClearCalls++;
            Failures.Add(Capture<InvalidOperationException>(transaction.Commit));
            Failures.Add(Capture<InvalidOperationException>(() => _ = transaction.Query()));
            Failures.Add(Capture<InvalidOperationException>(() => transaction.Update(mutable)));
            Failures.Add(Capture<InvalidOperationException>(transaction.Rollback));
            Failures.Add(Capture<InvalidOperationException>(transaction.Dispose));
        }
    }

    private sealed class MutatingNotification(Action mutation) : ICacheNotification
    {
        internal int ClearCalls { get; private set; }

        public void Clear()
        {
            ClearCalls++;
            mutation();
        }
    }

    private sealed class InspectingNotification(
        Func<PrePublicationSnapshot> inspect) : ICacheNotification
    {
        internal int ClearCalls { get; private set; }
        internal PrePublicationSnapshot? Snapshot { get; private set; }

        public void Clear()
        {
            ClearCalls++;
            Snapshot = inspect();
        }
    }

    private sealed class CountingNotification : ICacheNotification
    {
        internal int ClearCalls { get; private set; }

        public void Clear() => ClearCalls++;
    }

    private sealed record CommitPublicationSnapshot(
        bool TransactionCachePresent,
        int TouchedMutableCount,
        MutableTransactionOutcome OwnershipOutcome,
        MutableLifecycleSnapshot MutableLifecycle,
        IDataLinqReadSource ReadSource);

    private sealed record PrePublicationSnapshot(
        DatabaseTransactionStatus ProviderStatus,
        bool TransactionCachePresent,
        int TouchedMutableCount,
        MutableTransactionOutcome OwnershipOutcome,
        MutableLifecycleSnapshot MutableLifecycle,
        InvalidOperationException FallbackFailure);

    private sealed record RollbackPublicationSnapshot(
        bool TransactionCachePresent,
        int TouchedMutableCount,
        MutableTransactionOutcome OwnershipOutcome,
        MutableLifecycleSnapshot MutableLifecycle,
        int ScopedNotificationClearCalls);

    private sealed class InjectedMutationException(string stage)
        : Exception($"Injected mutation failure during {stage}.");
}
