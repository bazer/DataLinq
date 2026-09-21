using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("commit", false)]
    [Arguments("commit", true)]
    [Arguments("callback", false)]
    [Arguments("callback", true)]
    [Arguments("helper", false)]
    [Arguments("helper", true)]
    [Arguments("dispose", false)]
    [Arguments("dispose", true)]
    public async Task SettledOccurrences_RecoveryPolicyOwnsItsOccurrence(string kind, bool freshReport)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var occurrence = new SettledFailureOccurrence();
        var provider = new ControlledCompletionProvider
        {
            RecoveryFailure = occurrence.Reused,
            InspectRecovery = () => { if (freshReport) occurrence.ReportCurrent(); }
        };
        fixture.Scenario.AsyncCompletion = provider;
        if (kind == "commit")
        {
            provider.Commit = new(paused: true) { ReportingFailure = _ => occurrence.RecordEarlier() };
            provider.Commit.Fail(occurrence.Primary);
        }
        if (kind == "helper")
        {
            var mutations = EnableAsyncMutations(fixture);
            mutations.CreateAccess = _ => { occurrence.RecordEarlier(); throw occurrence.Primary; };
        }
        if (kind == "dispose") provider.Validating = _ => occurrence.RecordEarlier();
        var operation = kind switch
        {
            "commit" => ExecutionOperationKind.Commit, "helper" => ExecutionOperationKind.Delete,
            "dispose" => ExecutionOperationKind.Dispose, _ => ExecutionOperationKind.TransactionCallback
        };
        try
        {
            var failure = await AsyncEnumerationFailureOf(async () =>
            {
                switch (kind)
                {
                    case "commit": await transaction.CommitAsyncCore(); break;
                    case "callback": await transaction.RunCallbackAsyncCore<int>(_ =>
                        { occurrence.RecordEarlier(); throw occurrence.Primary; }, new()); break;
                    case "helper": await transaction.RunDeleteHelperAsyncCore(fixture.CreateExistingMutable(1, "row"), default); break;
                    default: await transaction.DisposeAsyncCore(); break;
                }
            });
            await Assert.That(failure).IsSameReferenceAs(kind == "dispose" ? occurrence.Reused : occurrence.Primary);
            var context = ExecutionFailureContexts.Get(failure)!;
            if (kind == "dispose")
            {
                await Assert.That(context.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
                await Assert.That(context.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Recovery);
                await Assert.That(context.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : operation);
                await Assert.That(context.SecondaryFailures).IsEmpty();
            }
            else
            {
                await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
                var secondary = context.SecondaryFailures.Single();
                await Assert.That(secondary.Exception).IsSameReferenceAs(occurrence.Reused);
                await Assert.That(secondary.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
                await Assert.That(secondary.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Recovery);
                await Assert.That(secondary.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : operation);
                await Assert.That(context.Operation).IsEqualTo(operation);
            }
            await Assert.That(context.HasCleanupFailure).IsFalse();
            await Assert.That(context.Recovery).IsEqualTo(kind == "commit" ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.None);
            await Assert.That(context.Completion).IsEqualTo(kind == "commit" ? ExecutionCompletion.Unknown : ExecutionCompletion.NotAttempted);
            await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
            await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
            await Assert.That(provider.RecoveryReads).IsEqualTo(1);
            await Assert.That(provider.Calls.Contains("rollback")).IsFalse();
            await occurrence.AssertEarlier();
        }
        finally
        {
            provider.RecoveryFailure = null;
            provider.InspectRecovery = null;
            provider.Validating = null;
            await transaction.DisposeAsyncCore();
        }
        await Assert.That(provider.Calls.Count(x => x == "commit")).IsEqualTo(kind == "commit" ? 1 : 0);
        await Assert.That(provider.Calls.Count(x => x == "dispose-transaction")).IsEqualTo(1);
        await Assert.That(provider.Calls.Count(x => x == "dispose-connection")).IsEqualTo(1);
    }
}
