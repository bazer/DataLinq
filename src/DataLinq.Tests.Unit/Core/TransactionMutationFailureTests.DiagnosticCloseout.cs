using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments(false, false, false, false)]
    [Arguments(false, false, true, false)]
    [Arguments(false, true, false, false)]
    [Arguments(false, true, true, false)]
    [Arguments(true, false, false, false)]
    [Arguments(true, false, true, false)]
    [Arguments(true, true, false, false)]
    [Arguments(true, true, true, false)]
    [Arguments(false, false, false, true)]
    [Arguments(false, false, true, true)]
    [Arguments(false, true, false, true)]
    [Arguments(false, true, true, true)]
    [Arguments(true, false, false, true)]
    [Arguments(true, false, true, true)]
    [Arguments(true, true, false, true)]
    [Arguments(true, true, true, true)]
    public async Task DiagnosticCloseout_MutationLocalWorkRejectsSettledReports(bool asynchronous, bool fromTelemetry, bool freshReport, bool cacheApplication)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        var mutable = fixture.CreateExistingMutable(1, "stored");
        void FailLocal()
        {
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        }
        var legacy = new LegacyAsyncMutable(mutable) { Deleting = cacheApplication ? null : FailLocal };
        var notification = new MutatingNotification(FailLocal);
        if (cacheApplication) fixture.RowCache.SubscribeToChanges(notification, transaction);
        var factory = EnableAsyncMutations(fixture);
        if (!fromTelemetry)
        {
            factory.ConfigureCommand = command => command.Resource.Disposing = occurrence.RecordEarlier;
            fixture.Scenario.CommandDisposed = occurrence.RecordEarlier;
        }
        using var activities = fromTelemetry ? new MutationActivityProbe(starting: _ => occurrence.RecordEarlier()) : null;
        var change = new StateChange(legacy, fixture.RowTable, TransactionChangeType.Delete);
        var failure = asynchronous
            ? await AsyncEnumerationFailureOf(() => change.ExecuteQueryAsyncCore(transaction))
            : Capture<Exception>(() => change.ExecuteQuery(transaction));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(occurrence.Reused);
        await Assert.That(context.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Finalization);
        await Assert.That(context.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Delete);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(asynchronous ? 0 : 1);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(asynchronous ? 0 : 1);
        await Assert.That(factory.Commands.Sum(command => command.Resource.AsyncDisposals)).IsEqualTo(asynchronous ? 1 : 0);
        await occurrence.AssertEarlier();
        mutable["Value"] = "reservation released";
        GC.KeepAlive(notification);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DiagnosticCloseout_HelperCommitValidationRejectsSuccessfulCallbackReports(bool freshReport)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var provider = fixture.Scenario.AsyncCompletion = new ControlledCompletionProvider();
        var occurrence = new SettledFailureOccurrence();
        var callbackReturned = false;
        provider.Validating = operation =>
        {
            if (operation != AsyncCompletionOperation.Commit || !callbackReturned) return;
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        };
        var failure = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(_ =>
        {
            occurrence.RecordEarlier();
            callbackReturned = true;
            return Task.FromResult(1);
        }, new()));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(occurrence.Reused);
        await Assert.That(context.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Validation);
        await Assert.That(context.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Commit);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(provider.Calls.SequenceEqual(["rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
        await Assert.That(transaction.IsDisposed).IsTrue();
        await occurrence.AssertEarlier();
    }
}
