using System;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task PublicDiagnostics_RecoveryPublishesNewSnapshotsWithoutRewritingExceptionHistory()
    {
        using var fixture = new ScriptedFixture();
        var completion = fixture.Scenario.AsyncCompletion = new() { Commit = new(paused: true) };
        await using var transaction = fixture.Database.Transaction();
        var expected = new Exception("confirmation lost");
        completion.Commit.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => transaction.CommitAsync())).IsSameReferenceAs(expected);
        var first = transaction.FailureContext!;
        await Assert.That(DataLinqFailure.GetContext(expected)).IsSameReferenceAs(first);
        await Assert.That(first.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.Unknown);
        await Assert.That(first.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.Rollback | DataLinqRecoveryActions.Dispose);
        await Assert.That(first.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(first.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await transaction.RollbackAsync();
        var recovered = transaction.FailureContext!;
        await Assert.That(recovered.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.Unknown);
        await Assert.That(recovered.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.Dispose);
        await transaction.DisposeAsync();
        await Assert.That(transaction.FailureContext!.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.None);
        await Assert.That(recovered.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.Dispose);
        await Assert.That(first.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.Rollback | DataLinqRecoveryActions.Dispose);
        await Assert.That(DataLinqFailure.GetContext(expected)).IsSameReferenceAs(first);
    }

    [Test]
    public async Task PublicDiagnostics_OverlapDoesNotOverwriteTheOwnersFailureState()
    {
        using var fixture = new ScriptedFixture();
        var completion = fixture.Scenario.AsyncCompletion = new() { Commit = new(paused: true) };
        await using var transaction = fixture.Database.Transaction();
        var pending = transaction.CommitAsync();
        try
        {
            await completion.Commit.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var rejected = await AsyncEnumerationFailureOf(() => transaction.RollbackAsync());
            var context = DataLinqFailure.GetContext(rejected)!;
            await Assert.That(context.Cause).IsEqualTo(DataLinqFailureCause.InvalidOperation);
            await Assert.That(context.Operation).IsEqualTo(DataLinqOperationKind.Rollback);
            await Assert.That(context.ActiveOperation).IsEqualTo(DataLinqOperationKind.Commit);
            await Assert.That(context.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.FinishActiveOperation);
            await Assert.That(transaction.FailureContext).IsNull();
        }
        finally { completion.Commit.Release(); }
        await pending;
        await Assert.That(transaction.FailureContext).IsNull();
    }

    [Test]
    public async Task PublicDiagnostics_CommittedFinalizationExceptionRetainsItsContract()
    {
        using var fixture = new ScriptedFixture();
        fixture.Scenario.AsyncCompletion = new();
        await using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(95, "delete"));
        var expected = new InjectedMutationException("cache publication");
        fixture.RowCache.SubscribeToChanges(new ThrowingNotification(expected));
        var failure = await AsyncEnumerationFailureOf(() => transaction.CommitAsync());
        var finalization = (TransactionCommitFinalizationException)failure;
        var context = DataLinqFailure.GetContext(finalization)!;
        await Assert.That(context.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.Committed);
        await Assert.That(context.Cause).IsEqualTo(DataLinqFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(DataLinqFailureStage.LocalFinalization);
        await Assert.That(context.TransactionId).IsEqualTo(finalization.TransactionId);
        await Assert.That(finalization.InnerException).IsSameReferenceAs(expected);
        await Assert.That(finalization.CleanupFailures).IsNotNull();
    }
}
