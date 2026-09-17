using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task AsyncCommit_HoldsOwnershipThroughConfirmationAndFinalizedNotifications()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { Commit = new(paused: true) };
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(501, "delete");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        transaction.Delete(deleted);
        var notifications = 0;
        transaction.OnStatusChanged += (_, args) =>
        {
            if (args.Status != DatabaseTransactionStatus.Committed) return;
            notifications++;
            if (deleted.Lifecycle.BaselineKind != MutableBaselineKind.Committed ||
                fixture.RowCache.IsTransactionInCache(transaction) || transaction.TouchedMutables.Count != 0)
                throw new Exception("Status observer ran before finalization.");
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        };
        var pending = transaction.CommitAsyncCore();
        await provider.Commit.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
            await Assert.That(deleted.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
            _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(501)));
            await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DisposeAsyncCore().AsTask())).IsTypeOf<InvalidOperationException>();
            await Assert.That(transaction.IsDisposed).IsFalse();
        }
        finally { provider.Commit.Release(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(notifications).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        await Assert.That(provider.Calls.ToArray()).IsEquivalentTo(new[] { "commit" });
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCompletion_ValidationWinsAndPreCancellationPreservesPendingWork(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(502, "delete");
        transaction.Delete(deleted);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invalid = provider.ValidationFailure = new InvalidOperationException("capability validation");
        Task Complete() => rollback ? transaction.RollbackAsyncCore(cancellation.Token) : transaction.CommitAsyncCore(cancellation.Token);
        await Assert.That(await AsyncEnumerationFailureOf(Complete)).IsSameReferenceAs(invalid);
        provider.ValidationFailure = null;
        var error = await AsyncEnumerationFailureOf(Complete);
        await Assert.That(error).IsTypeOf<OperationCanceledException>();
        await Assert.That(provider.Calls).IsEmpty();
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        await Assert.That(deleted.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
        await transaction.CommitAsyncCore();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncUnusedCompletion_DoesNotDispatchOrInitialize(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { InitializationState = TransactionInitializationState.Unused };
        var transaction = fixture.Database.Transaction();
        if (rollback) await transaction.RollbackAsyncCore();
        else await transaction.CommitAsyncCore();
        await Assert.That(provider.Calls).IsEmpty();
        await Assert.That(transaction.Status).IsEqualTo(rollback ? DatabaseTransactionStatus.RolledBack : DatabaseTransactionStatus.Committed);
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.ToArray()).IsEquivalentTo(new[] { "dispose-transaction", "dispose-connection" });
    }

    [Test]
    public async Task AsyncCommit_LateCancellationDoesNotInterruptCommittedFinalization()
    {
        using var fixture = new ScriptedFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Scenario.AsyncCompletion = new() { Committed = cancellation.Cancel };
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(503, "delete");
        transaction.Delete(deleted);
        await transaction.CommitAsyncCore(cancellation.Token);
        await Assert.That(deleted.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncConfirmedCompletion_ObserverFailurePreservesOutcomeAndFinalizedState(bool rollback)
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(504, "delete");
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        transaction.Delete(deleted);
        var expected = new Exception("status observer");
        transaction.OnStatusChanged += (_, _) => throw expected;
        var error = await AsyncEnumerationFailureOf(() => rollback ? transaction.RollbackAsyncCore() : transaction.CommitAsyncCore());
        await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(error)!.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
        await Assert.That(deleted.Lifecycle.BaselineKind).IsEqualTo(rollback ? MutableBaselineKind.Invalid : MutableBaselineKind.Committed);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.Count(x => x == "rollback")).IsEqualTo(rollback ? 1 : 0);
    }

    [Test]
    public async Task AsyncCommittedCacheFailure_PreservesSpecialExceptionAndNeverRollsBack()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(505, "delete");
        transaction.Delete(deleted);
        var expected = new InjectedMutationException("committed cache publication");
        var notification = new ThrowingNotification(expected);
        fixture.RowCache.SubscribeToChanges(notification);
        var error = await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore());
        await Assert.That(error).IsTypeOf<TransactionCommitFinalizationException>();
        await Assert.That(error.InnerException).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(error)!.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(deleted.Lifecycle.InvalidationReason).IsEqualTo(MutableInvalidationReason.CommittedStateFinalizationFailed);
        await Assert.That(fixture.Provider.State.Cache.TableCaches.Values.All(cache => !cache.IsTransactionInCache(transaction))).IsTrue();
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.Contains("rollback")).IsFalse();
    }

    [Test]
    public async Task AsyncUncertainCommit_RemainsUnknownAfterConfirmedRollback_AndRejectsBusinessWork()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { Commit = new(paused: true) };
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(506, "delete");
        transaction.Delete(deleted);
        var expected = new Exception("confirmation lost");
        provider.Commit.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(deleted.Lifecycle.InvalidationReason).IsEqualTo(MutableInvalidationReason.CommitOutcomeUnknown);
        _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(506)));
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())).IsTypeOf<InvalidOperationException>();
        await transaction.RollbackAsyncCore();
        await Assert.That(transaction.AsyncFailureContext!.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.ToArray()).IsEquivalentTo(new[] { "commit", "rollback", "dispose-transaction", "dispose-connection" });
    }

    [Test]
    public async Task AsyncFailedRollback_AllowsOnlyDisposal_WithoutRollbackRetry()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { Rollback = new(paused: true) };
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("rollback interrupted");
        provider.Rollback.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.RollbackAsyncCore())).IsSameReferenceAs(expected);
        _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(501)));
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.RollbackAsyncCore())).IsTypeOf<InvalidOperationException>();
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.Count(x => x == "rollback")).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncDisposal_UsesRecoveryThenAttemptsBothCleanupsAndDoesNotReplayFailure()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { TransactionCleanup = new(paused: true), ConnectionCleanup = new(paused: true) };
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(507, "delete");
        transaction.Delete(deleted);
        var first = new Exception("transaction cleanup");
        var second = new Exception("connection cleanup");
        provider.TransactionCleanup.Fail(first);
        provider.ConnectionCleanup.Fail(second);
        var error = await AsyncEnumerationFailureOf(() => transaction.DisposeAsyncCore().AsTask());
        await Assert.That(error).IsSameReferenceAs(first);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(second);
        await Assert.That(deleted.Lifecycle.InvalidationReason).IsEqualTo(MutableInvalidationReason.RolledBack);
        await transaction.DisposeAsyncCore();
        transaction.Dispose();
        await Assert.That(provider.Calls.ToArray()).IsEquivalentTo(new[] { "rollback", "dispose-transaction", "dispose-connection" });
    }

    [Test]
    public async Task ManagedAsyncHelper_ResultWaitsForCleanupAndUsesRealMutableFinalization()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { ConnectionCleanup = new(paused: true) };
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(508, "delete");
        var pending = transaction.RunCallbackAsyncCore(token =>
        {
            transaction.Delete(deleted);
            _ = Capture<InvalidOperationException>(transaction.Commit);
            return Task.FromResult(17);
        }, new());
        await provider.ConnectionCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(deleted.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
            await Assert.That(transaction.IsDisposed).IsTrue();
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DisposeAsyncCore().AsTask())).IsTypeOf<InvalidOperationException>();
        }
        finally { provider.ConnectionCleanup.Release(); }
        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo(17);
    }

    [Test]
    public async Task ManagedAsyncHelper_PreservesCallbackFailureAndConfirmedRollbackDespiteObserverFailure()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var primary = new Exception("callback");
        var secondary = new Exception("rollback observer");
        transaction.OnStatusChanged += (_, _) => throw secondary;
        var error = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore<int>(_ =>
        {
            cancellation.Cancel();
            throw primary;
        }, new(), cancellation.Token));
        await Assert.That(error).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(error)!;
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(secondary);
        await Assert.That(provider.RollbackToken == cancellation.Token).IsFalse();
        await Assert.That(provider.Calls.ToArray()).IsEquivalentTo(new[] { "rollback", "dispose-transaction", "dispose-connection" });
        await Assert.That(transaction.IsDisposed).IsTrue();
    }

    [Test]
    public async Task AsyncCommit_ProviderAndManagedObserversFailIndependentlyAfterCachePublication()
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(509, "delete");
        transaction.Delete(deleted);
        var first = new Exception("provider observer");
        var second = new Exception("managed observer");
        transaction.DatabaseAccess.OnStatusChanged += (_, _) => throw first;
        transaction.OnStatusChanged += (_, _) => throw second;
        var error = await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore());
        await Assert.That(error).IsSameReferenceAs(first);
        await Assert.That(ExecutionFailureContexts.Get(error)!.SecondaryFailures.Single().Exception).IsSameReferenceAs(second);
        await Assert.That(ExecutionFailureContexts.Get(error)!.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(deleted.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task FailedInitialization_DisallowsSyncAndAsyncBusinessCompletion_ButDisposesWithoutRollback()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { InitializationState = TransactionInitializationState.Failed };
        var transaction = fixture.Database.Transaction();
        _ = Capture<InvalidOperationException>(() => transaction.Get<TransactionMutationGuardRow>(DataLinqKey.FromValue(501)));
        _ = Capture<InvalidOperationException>(transaction.Commit);
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())).IsTypeOf<InvalidOperationException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.RollbackAsyncCore())).IsTypeOf<InvalidOperationException>();
        await transaction.DisposeAsyncCore();
        await Assert.That(provider.Calls.ToArray()).IsEquivalentTo(new[] { "dispose-transaction", "dispose-connection" });
    }

    [Test]
    public async Task UnsupportedAsyncCompletion_NeverFallsBackToSync()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var counts = fixture.Scenario.SnapshotCounts();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())).IsTypeOf<NotSupportedException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.RollbackAsyncCore())).IsTypeOf<NotSupportedException>();
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.DisposeAsyncCore().AsTask())).IsTypeOf<NotSupportedException>();
        await Assert.That(transaction.IsDisposed).IsFalse();
        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(counts);
    }

    [Test]
    public async Task ManagedAsyncHelper_SwallowedMutationFailureStillPreventsCommit()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(510, "delete");
        fixture.Scenario.EnqueueNonQueryFailure(new InjectedMutationException("write failure"));
        var error = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(token =>
        {
            _ = Capture<InjectedMutationException>(() => transaction.Delete(mutable));
            return Task.FromResult(17);
        }, new()));
        await Assert.That(error).IsTypeOf<InvalidOperationException>();
        await Assert.That(provider.Calls.Contains("commit")).IsFalse();
        await Assert.That(provider.Calls.Count(x => x == "rollback")).IsEqualTo(1);
        await Assert.That(mutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(transaction.IsDisposed).IsTrue();
    }

    [Test]
    public async Task ManagedAsyncHelper_CommittedCleanupFailurePreventsResultButNotKnownCommit()
    {
        using var fixture = new ScriptedFixture();
        var provider = fixture.Scenario.AsyncCompletion = new() { ConnectionCleanup = new(paused: true) };
        var transaction = fixture.Database.Transaction();
        var expected = new Exception("committed connection cleanup");
        provider.ConnectionCleanup.Fail(expected);
        var error = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(_ => Task.FromResult(17), new()));
        await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(ExecutionFailureContexts.Get(error)!.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(provider.Calls.Contains("rollback")).IsFalse();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncRecoveryInspectionFailure_DoesNotSkipIndependentResourceDisposal()
    {
        using var fixture = new ScriptedFixture();
        var expected = new Exception("recovery inspection");
        var provider = fixture.Scenario.AsyncCompletion = new() { RecoveryFailure = expected };
        var transaction = fixture.Database.Transaction();
        var error = await AsyncEnumerationFailureOf(() => transaction.DisposeAsyncCore().AsTask());
        await Assert.That(error).IsSameReferenceAs(expected);
        await Assert.That(provider.Calls.ToArray()).IsEquivalentTo(new[] { "dispose-transaction", "dispose-connection" });
        await Assert.That(transaction.IsDisposed).IsTrue();
        await Assert.That(ExecutionFailureContexts.Get(error)!.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
    }
}
