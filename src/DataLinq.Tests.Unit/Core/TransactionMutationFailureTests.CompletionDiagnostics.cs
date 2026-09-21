using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Execution;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CompletionDiagnostics_CommittedCacheFailureRetainsLocalCauseAndRecoveryStage(bool asynchronous, bool recoveryFails)
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        transaction.Delete(fixture.CreateExistingMutable(427, "delete"));
        var primary = new Exception("cache publication");
        var recovery = new Exception("cache recovery");
        fixture.RowCache.SubscribeToChanges(new ThrowingNotification(primary));
        if (recoveryFails)
            fixture.Provider.State.Cache.TableCaches.Values.Last(cache => !ReferenceEquals(cache, fixture.RowCache))
                .SubscribeToChanges(new ThrowingNotification(recovery));
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())
            : Capture<Exception>(transaction.Commit);
        await Assert.That(failure).IsTypeOf<TransactionCommitFinalizationException>();
        await Assert.That(failure.InnerException).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Finalization);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Committed);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        if (recoveryFails)
        {
            var secondary = context.SecondaryFailures.Single();
            await Assert.That(secondary.Exception).IsSameReferenceAs(recovery);
            await Assert.That(secondary.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
            await Assert.That(secondary.Stage).IsEqualTo(ExecutionFailureStage.CacheRecovery);
            await Assert.That(context.HasCleanupFailure).IsTrue();
        }
        else await Assert.That(context.SecondaryFailures).IsEmpty();
        if (asynchronous) await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CompletionDiagnostics_StatusFailureHasNotificationStage(bool asynchronous, bool rollback)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("status notification");
        transaction.OnStatusChanged += (_, _) => throw expected;
        var failure = asynchronous
            ? await AsyncEnumerationFailureOf(() => rollback ? transaction.RollbackAsyncCore() : transaction.CommitAsyncCore())
            : Capture<Exception>(() => { if (rollback) transaction.Rollback(); else transaction.Commit(); });
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(context.Operation).IsEqualTo(rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
        await Assert.That(context.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
        if (asynchronous) await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompletionDiagnostics_UncertainCommitRetainsNativePrimaryAndLocalRecovery(bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        var provider = fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(427, "delete"));
        var primary = new Exception("native commit");
        var recovery = new Exception("cache recovery");
        fixture.RowCache.SubscribeToChanges(new ThrowingNotification(recovery));
        if (asynchronous) { provider.Commit = new(paused: true); provider.Commit.Fail(primary); }
        else fixture.Scenario.CommitFailureBeforeStatus = primary;
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())
            : Capture<Exception>(transaction.Commit);
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Commit);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        var secondary = context.SecondaryFailures.Single();
        await Assert.That(secondary.Exception).IsSameReferenceAs(recovery);
        await Assert.That(secondary.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(secondary.Stage).IsEqualTo(ExecutionFailureStage.CacheRecovery);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        if (asynchronous) await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CompletionDiagnostics_NotificationPreservesSpecificNestedAdmissionFailure(bool asynchronous, bool rollback)
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        transaction.OnStatusChanged += (_, _) => transaction.Commit();
        var failure = asynchronous
            ? await AsyncEnumerationFailureOf(() => rollback ? transaction.RollbackAsyncCore() : transaction.CommitAsyncCore())
            : Capture<Exception>(() => { if (rollback) transaction.Rollback(); else transaction.Commit(); });
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.InvalidOperation);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Commit);
        await Assert.That(context.ActiveOperation).IsEqualTo(rollback ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
        await Assert.That(context.Completion).IsEqualTo(rollback ? ExecutionCompletion.RolledBack : ExecutionCompletion.Committed);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        if (asynchronous) await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task CompletionDiagnostics_CacheOccurrenceIsCapturedBeforeLaterCallbackReplacesLookup(bool asynchronous, bool committed)
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        var provider = fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        _ = fixture.RowCache.GetRow(int.MaxValue, transaction);
        transaction.Delete(fixture.CreateExistingMutable(427, "delete"));
        var primary = new Exception("primary");
        var recovery = new Exception("earlier recovery callback");
        if (committed) fixture.RowCache.SubscribeToChanges(new ThrowingNotification(primary));
        else if (asynchronous) { provider.Commit = new(paused: true); provider.Commit.Fail(primary); }
        else fixture.Scenario.CommitFailureBeforeStatus = primary;
        var tables = fixture.Provider.State.Cache.TableCaches.Values.Where(cache => !ReferenceEquals(cache, fixture.RowCache)).ToArray();
        tables[^2].SubscribeToChanges(new ThrowingNotification(recovery));
        tables[^1].SubscribeToChanges(new MutatingNotification(() => ExecutionFailureContexts.Attach(recovery,
            new(ExecutionFailureCause.Timeout, ExecutionFailureStage.CommandExecution, ExecutionCompletion.Unknown,
                ExecutionRecoveryActions.Continue, transaction.TransactionID, [], operation: ExecutionOperationKind.Query))));
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())
            : Capture<Exception>(transaction.Commit);
        var context = ExecutionFailureContexts.Get(failure)!;
        var secondary = context.SecondaryFailures.Single();
        await Assert.That(secondary.Exception).IsSameReferenceAs(recovery);
        await Assert.That(secondary.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(secondary.Stage).IsEqualTo(ExecutionFailureStage.CacheRecovery);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Completion).IsEqualTo(committed ? ExecutionCompletion.Committed : ExecutionCompletion.Unknown);
        if (asynchronous) await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompletionDiagnostics_CacheRecoveryKeepsSpecificNestedFailureAndCleanupFact(bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(427, "delete"));
        fixture.RowCache.SubscribeToChanges(new ThrowingNotification(new Exception("publication")));
        fixture.Provider.State.Cache.TableCaches.Values.Last(cache => !ReferenceEquals(cache, fixture.RowCache))
            .SubscribeToChanges(new MutatingNotification(transaction.Rollback));
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())
            : Capture<Exception>(transaction.Commit);
        var context = ExecutionFailureContexts.Get(failure)!;
        var secondary = context.SecondaryFailures.Single();
        await Assert.That(secondary.Cause).IsEqualTo(ExecutionFailureCause.InvalidOperation);
        await Assert.That(secondary.Stage).IsEqualTo(ExecutionFailureStage.Validation);
        await Assert.That(secondary.Operation).IsEqualTo(ExecutionOperationKind.Rollback);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.Committed);
        if (asynchronous) await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompletionDiagnostics_ReusedNativeAndRecoveryFailureRetainsCleanupFact(bool asynchronous)
    {
        using var fixture = new ScriptedFixture();
        fixture.Provider.State.Cache.CleanupScheduler?.Stop();
        var provider = fixture.Scenario.AsyncCompletion = new();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(427, "delete"));
        var primary = new Exception("reused native and cache failure");
        fixture.RowCache.SubscribeToChanges(new ThrowingNotification(primary));
        if (asynchronous) { provider.Commit = new(paused: true); provider.Commit.Fail(primary); }
        else fixture.Scenario.CommitFailureBeforeStatus = primary;
        var failure = asynchronous ? await AsyncEnumerationFailureOf(() => transaction.CommitAsyncCore())
            : Capture<Exception>(transaction.Commit);
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Commit);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        if (asynchronous) await transaction.DisposeAsyncCore();
        else transaction.Dispose();
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        await Assert.That(transaction.AsyncFailureContext!.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
    }
}
