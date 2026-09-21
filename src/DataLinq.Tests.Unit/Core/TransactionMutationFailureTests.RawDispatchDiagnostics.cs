using System;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test, NotInParallel]
    [Arguments("non-query", false, false)]
    [Arguments("non-query", false, true)]
    [Arguments("non-query", true, false)]
    [Arguments("non-query", true, true)]
    [Arguments("scalar", false, false)]
    [Arguments("scalar", false, true)]
    [Arguments("scalar", true, false)]
    [Arguments("scalar", true, true)]
    [Arguments("typed", false, false)]
    [Arguments("typed", false, true)]
    [Arguments("typed", true, false)]
    [Arguments("typed", true, true)]
    [Arguments("reader", false, false)]
    [Arguments("reader", false, true)]
    [Arguments("reader", true, false)]
    [Arguments("reader", true, true)]
    [Arguments("rows", false, false)]
    [Arguments("rows", false, true)]
    [Arguments("rows", true, false)]
    [Arguments("rows", true, true)]
    [Arguments("models", false, false)]
    [Arguments("models", false, true)]
    [Arguments("models", true, false)]
    [Arguments("models", true, true)]
    public async Task RawDiagnostics_SynchronousTelemetryKeepsActualDispatchEvidence(string kind, bool borrowed, bool afterDispatch)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        transaction.Delete(fixture.CreateExistingMutable(1, "pending"));
        var factory = EnableSyncRaw(fixture);
        factory.Telemetry = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var native = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 2, "raw"));
        factory.Reader = () => native;
        using var command = new ControlledCommand { CommandText = "UPDATE rows RETURNING value" };
        var expected = new Exception("reporting");
        Exception failure;
        using (var activities = new CommandActivityProbe(
            starting: _ => { if (!afterDispatch) throw expected; },
            stopping: _ => { if (afterDispatch) throw expected; }))
        {
            failure = await AsyncEnumerationFailureOf(() =>
            {
                if (kind != "models") return RunStandaloneRaw(transaction.DatabaseAccess, kind, false, borrowed ? command : null);
                using var rows = SyncRawModels(transaction, borrowed, command).GetEnumerator();
                _ = rows.MoveNext();
                return Task.CompletedTask;
            });
        }
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(ExecutionFailureContexts.Get(failure)).IsSameReferenceAs(context);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Notification);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsEqualTo(!afterDispatch);
        await Assert.That(context.CommandDispatch!.Dispatched).IsEqualTo(afterDispatch);
        await Assert.That(factory.Executions.Count).IsEqualTo(afterDispatch ? 1 : 0);
        await Assert.That(factory.CommandDisposals).IsEqualTo(borrowed ? 0 : 1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(factory.Assessments).IsEqualTo(1);
        if (kind is "reader" or "rows" or "models")
            await Assert.That(native.Disposals).IsEqualTo(afterDispatch ? 1 : 0);
        if (afterDispatch) _ = Capture<InvalidOperationException>(() => transaction.Query());
        else _ = transaction.Query();
    }

    [Test, NotInParallel]
    [Arguments("cleanup")]
    [Arguments("assessment")]
    [Arguments("initialization")]
    [Arguments("lost")]
    public async Task RawDiagnostics_NoDispatchCannotBypassOtherRecoveryRestrictions(string restriction)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        factory.Telemetry = (ScriptedDatabaseTransaction)transaction.DatabaseAccess;
        var expected = new Exception("reporting");
        var secondary = new Exception(restriction);
        if (restriction == "cleanup") factory.CommandDisposing = () => throw secondary;
        if (restriction == "assessment") factory.EvidenceFailure = secondary;
        if (restriction == "initialization") factory.Evidence = factory.Evidence with { Effects = ExecutionEffects.Initialization };
        if (restriction == "lost") factory.Evidence = factory.Evidence with { Integrity = TransactionIntegrity.Lost };
        Exception failure;
        using (var activities = new CommandActivityProbe(starting: _ => throw expected))
            failure = await AsyncEnumerationFailureOf(() => RunStandaloneRaw(transaction.DatabaseAccess, "scalar", false, null));
        await Assert.That(failure).IsSameReferenceAs(expected);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.CommandDispatch!.Dispatched).IsFalse();
        await Assert.That(factory.Executions).IsEmpty();
        await Assert.That(factory.Assessments).IsEqualTo(1);
        await Assert.That(factory.CommandDisposals).IsEqualTo(1);
        if (restriction is "cleanup" or "assessment")
            await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(secondary);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }
}
