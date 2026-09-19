using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private static ExecutionOperationKind ExpectedMutationKind(string kind) => kind switch
    {
        "insert" or "batch" => ExecutionOperationKind.Insert,
        "update" or "unchanged-update" => ExecutionOperationKind.Update,
        "delete" => ExecutionOperationKind.Delete,
        _ => ExecutionOperationKind.Save
    };

    private static Task RunCorrelationMutation(string kind, Transaction transaction, Mutable<TransactionMutationGuardRow> mutable,
        bool edits = false) => kind switch
    {
        "insert" => edits ? transaction.InsertAsyncCore(mutable, _ => { }) : transaction.InsertAsyncCore(mutable),
        "update" or "unchanged-update" => edits ? transaction.UpdateAsyncCore(mutable, _ => { }) : transaction.UpdateAsyncCore(mutable),
        "batch" => transaction.InsertAsyncCore([mutable]),
        "delete" => transaction.DeleteAsyncCore(mutable),
        _ => edits ? transaction.SaveAsyncCore(mutable, _ => { }) : transaction.SaveAsyncCore(mutable)
    };

    [Test]
    [Arguments("insert", false)]
    [Arguments("insert", true)]
    [Arguments("update", false)]
    [Arguments("update", true)]
    [Arguments("save-new", false)]
    [Arguments("save-new", true)]
    [Arguments("save-existing", false)]
    [Arguments("save-existing", true)]
    [Arguments("delete", false)]
    [Arguments("batch", false)]
    public async Task MutationCorrelation_DispatchAndCleanupKeepRequestedOperation(string kind, bool edits)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = kind is "insert" or "save-new" or "batch" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
        if (mutable.IsNew()) mutable["Id"] = 1;
        mutable["Value"] = "submitted";
        var primary = new Exception("mutation dispatch");
        var cleanup = new Exception("command cleanup");
        var factory = EnableAsyncMutations(fixture, new(JournalFault(primary))
            { FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true) });
        factory.ConfigureCommand = command => command.Resource.Cleanup = JournalFault(cleanup);
        var failure = await AsyncEnumerationFailureOf(() => RunCorrelationMutation(kind, transaction, mutable, edits));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExpectedMutationKind(kind));
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures.Single().Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("insert")]
    [Arguments("update")]
    [Arguments("save-new")]
    [Arguments("save-existing")]
    [Arguments("unchanged-update")]
    [Arguments("unchanged-save")]
    public async Task MutationCorrelation_HydrationKeepsOwnerIdentityAndWriteDependentRecovery(string kind)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = kind is "insert" or "save-new" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
        if (mutable.IsNew()) mutable["Id"] = 1;
        var unchanged = kind.StartsWith("unchanged", StringComparison.Ordinal);
        if (!unchanged) mutable["Value"] = "submitted";
        var factory = EnableAsyncMutations(fixture);
        var primary = new Exception("hydration dispatch");
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new(JournalFault(primary)) { FailureEvidence = TrustedScalarRead });
        var failure = await AsyncEnumerationFailureOf(() => RunCorrelationMutation(kind, transaction, mutable));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExpectedMutationKind(kind));
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.Recovery).IsEqualTo(unchanged
            ? ExecutionRecoveryPolicy.ForReadFailure(TrustedScalarRead, true) : ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.IsPoisoned).IsEqualTo(!unchanged);
        await Assert.That(factory.Commands.Count).IsEqualTo(unchanged ? 0 : 1);
        if (unchanged) _ = transaction.Query();
    }

    [Test]
    [Arguments("insert", false)]
    [Arguments("update", false)]
    [Arguments("save-new", false)]
    [Arguments("save-existing", false)]
    [Arguments("save-existing", true)]
    [Arguments("delete", false)]
    [Arguments("batch", false)]
    public async Task MutationCorrelation_OverlapKeepsRequestedAndActiveKindsWithoutPoisoning(string rejected, bool edits)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var owner = fixture.CreateExistingMutable(1, "old");
        owner["Value"] = "saved";
        var candidate = rejected is "insert" or "save-new" or "batch" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(2, "old");
        if (candidate.IsNew()) candidate["Id"] = 2;
        candidate["Value"] = "rejected";
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { NonQueryResult = 1 };
        var factory = EnableAsyncMutations(fixture, access, [1, "saved"]);
        var pending = transaction.SaveAsyncCore(owner);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => RunCorrelationMutation(rejected, transaction, candidate, edits));
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Operation).IsEqualTo(ExpectedMutationKind(rejected));
            await Assert.That(context.ActiveOperation).IsEqualTo((ExecutionOperationKind?)ExecutionOperationKind.Save);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.FinishActiveOperation);
            await Assert.That(transaction.AsyncFailureContext).IsNull();
            await Assert.That(transaction.IsPoisoned).IsFalse();
            await Assert.That(factory.Commands.Count).IsEqualTo(1);
        }
        finally { access.Dispatch.Release(); await pending; }
        transaction.Commit();
    }

    [Test]
    [Arguments("dispatch")]
    [Arguments("binding")]
    [Arguments("canceled")]
    public async Task MutationCorrelation_SaveHelperKeepsPrimaryThroughRecoveryAndCleanup(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new();
        var primary = new Exception("save " + phase);
        var cleanup = new Exception("connection cleanup");
        completion.ConnectionCleanup = JournalFault(cleanup);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var access = phase == "dispatch" ? new ControlledAsyncDatabaseAccess(JournalFault(primary))
            { FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true) } : new();
        var factory = EnableAsyncMutations(fixture, access, [1, "stored"]);
        if (phase == "binding") factory.ConfigureCommand = command => command.ValidationFailure = primary;
        var failure = await AsyncEnumerationFailureOf(() => fixture.Database.SaveAsyncCore(mutable, token: new(phase == "canceled")));
        var context = ExecutionFailureContexts.Get(failure)!;
        if (phase != "canceled") await Assert.That(failure).IsSameReferenceAs(primary);
        else await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsNotNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures.Single().Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(completion.Calls.SequenceEqual(["rollback", "dispose-transaction", "dispose-connection"])).IsTrue();
    }

    [Test]
    [Arguments("commit")]
    [Arguments("cleanup")]
    public async Task MutationCorrelation_SaveHelperRetainsMoreSpecificCompletionFailure(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var completion = fixture.Scenario.AsyncCompletion = new();
        var primary = new Exception(phase);
        if (phase == "commit") completion.Commit = JournalFault(primary);
        else completion.ConnectionCleanup = JournalFault(primary);
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        EnableAsyncMutations(fixture, rows: [[1, "stored"]]);
        var failure = await AsyncEnumerationFailureOf(() => fixture.Database.SaveAsyncCore(mutable));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(phase == "commit" ? ExecutionOperationKind.Commit : ExecutionOperationKind.Dispose);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.Completion).IsEqualTo(phase == "commit" ? ExecutionCompletion.Unknown : ExecutionCompletion.Committed);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
    }

    [Test]
    public async Task MutationCorrelation_LegacyDeleteFinalizationIsNotMaterializationOrProviderFailure()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("local delete lifecycle");
        var legacy = new LegacyAsyncMutable(fixture.CreateExistingMutable(1, "old")) { Deleting = () => throw primary };
        var factory = EnableAsyncMutations(fixture);
        var change = new StateChange(legacy, fixture.RowTable, TransactionChangeType.Delete);
        var failure = await AsyncEnumerationFailureOf(() => change.ExecuteQueryAsyncCore(transaction));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Delete);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.LocalFinalizationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Finalization);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MutationCorrelation_GeneratedKeyScalarKeepsInsertOrSaveIdentity(bool save)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("generated key statement");
        var access = new ControlledAsyncDatabaseAccess(JournalFault(primary))
            { FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true) };
        EnableAsyncMutations(fixture, access);
        var mutable = fixture.CreateNewAutoMutable("submitted");
        var failure = await AsyncEnumerationFailureOf(() => save ? transaction.SaveAsyncCore(mutable) : transaction.InsertAsyncCore(mutable));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(save ? ExecutionOperationKind.Save : ExecutionOperationKind.Insert);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(access.Calls.Contains("dispatch:Scalar")).IsTrue();
        await Assert.That(access.Calls.Contains("dispatch:NonQuery")).IsFalse();
    }

    [Test]
    public async Task MutationCorrelation_HelperRecoveryPreservesHistoricalSnapshotAndOrderedFailures()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("save statement");
        var rollback = new Exception("rollback");
        var transactionCleanup = new Exception("transaction cleanup");
        var connectionCleanup = new Exception("connection cleanup");
        var completion = fixture.Scenario.AsyncCompletion = new()
        {
            Rollback = new(paused: true), TransactionCleanup = JournalFault(transactionCleanup), ConnectionCleanup = JournalFault(connectionCleanup)
        };
        EnableAsyncMutations(fixture, new(JournalFault(primary))
            { FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true) });
        var mutable = fixture.CreateExistingMutable(1, "old");
        mutable["Value"] = "submitted";
        var pending = transaction.RunMutationHelperAsyncCore(mutable, null, default);
        await completion.Rollback.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var before = transaction.AsyncFailureContext!;
        completion.Rollback.Fail(rollback);
        var failure = await AsyncEnumerationFailureOf(() => pending);
        var after = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(before.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(before.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(before.SecondaryFailures).IsEmpty();
        await Assert.That(after.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(after.ProviderInstanceId).IsEqualTo(before.ProviderInstanceId);
        await Assert.That(after.TransactionId).IsEqualTo(before.TransactionId);
        await Assert.That(after.Completion).IsEqualTo(ExecutionCompletion.Unknown);
        await Assert.That(after.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(after.SecondaryFailures.Select(item => item.Exception).SequenceEqual([rollback, transactionCleanup, connectionCleanup])).IsTrue();
        await Assert.That(after.SecondaryFailures.Select(item => item.Operation).SequenceEqual(
            [ExecutionOperationKind.Rollback, ExecutionOperationKind.Dispose, ExecutionOperationKind.Dispose])).IsTrue();
        await Assert.That(transaction.IsDisposed).IsTrue();
    }
}
