using System;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("rows", false)]
    [Arguments("rows", true)]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    [Arguments("models", false)]
    [Arguments("models", true)]
    [Arguments("lookup", false)]
    [Arguments("lookup", true)]
    public async Task Attribution_ReusedFailureCannotImportEarlierCleanupOrClassification(string kind, bool managed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        DataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        var primary = new Exception("reused provider failure");
        var oldCleanup = new Exception("earlier cleanup");
        var previous = new ExecutionFailureContext(ExecutionFailureCause.Timeout, ExecutionFailureStage.Cleanup,
            ExecutionCompletion.Unknown, ExecutionRecoveryActions.Dispose, managed ? transaction.TransactionID : null,
            [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, oldCleanup, ExecutionOperationKind.Dispose)],
            operation: ExecutionOperationKind.Save, providerInstanceId: fixture.Provider.TelemetryInstanceId);
        ExecutionFailureContexts.Attach(primary, previous);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new(JournalFault(primary))
            {
                FailureEvidence = TrustedScalarRead with { Cause = ExecutionFailureCause.ProviderError }
            }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = (managed ? transaction.From<TransactionMutationGuardRow>() : fixture.Database.From<TransactionMutationGuardRow>()).SelectQuery();
        var failure = await AsyncEnumerationFailureOf(() => kind switch
        {
            "rows" => PlanRows(select.ReadRowsAsyncCore()),
            "scalar" => select.ExecuteScalarAsyncCore<int>(),
            "models" => select.ExecuteBufferedAsyncCore(),
            _ => AsyncModelLookup.GetByProviderKeyAsyncCore<TransactionMutationGuardRow>(DataLinqKey.FromValue(1), source)
        });
        var current = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(current.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(current.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(current.Operation).IsEqualTo(kind == "lookup" ? ExecutionOperationKind.KeyLookup : ExecutionOperationKind.Query);
        await Assert.That(current.HasCleanupFailure).IsFalse();
        await Assert.That(current.SecondaryFailures).IsEmpty();
        await Assert.That(current.Completion).IsEqualTo(managed ? ExecutionCompletion.NotAttempted : ExecutionCompletion.NotApplicable);
        await Assert.That(current.Recovery).IsEqualTo(managed
            ? ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose
            : ExecutionRecoveryActions.None);
        await Assert.That(previous.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(previous.SecondaryFailures[0].Exception).IsSameReferenceAs(oldCleanup);
        if (managed) _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Attribution_ConsecutiveCallsOnSameTransactionUseCurrentEvidence(bool scalar)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("same instance twice");
        var evidence = TrustedScalarRead with { Cause = ExecutionFailureCause.Timeout };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new(JournalFault(primary)) { FailureEvidence = evidence } };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        Task Execute() => scalar ? select.ExecuteScalarAsyncCore<int>() : PlanRows(select.ReadRowsAsyncCore());
        await Assert.That(await AsyncEnumerationFailureOf(Execute)).IsSameReferenceAs(primary);
        var previous = ExecutionFailureContexts.Get(primary)!;
        evidence = evidence with { Cause = ExecutionFailureCause.ProviderError };
        await Assert.That(await AsyncEnumerationFailureOf(Execute)).IsSameReferenceAs(primary);
        var current = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(previous.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(current.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(current.HasCleanupFailure).IsFalse();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Attribution_HelperKeepsOwnedSnapshotWhenExecutionContextFlowIsSuppressed(bool overwriteLookup)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.Scenario.AsyncCompletion = new();
        var transaction = fixture.Database.Transaction();
        var primary = new Exception("query on independently scheduled work");
        fixture.Scenario.AsyncSqlScalars = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new(JournalFault(primary)) { FailureEvidence = TrustedScalarRead with { Cause = ExecutionFailureCause.ProviderError } }
        };
        var failure = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore<int>(async _ =>
        {
            Task<int> pending;
            using (ExecutionContext.SuppressFlow())
                pending = Task.Run(() => transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore<int>());
            try { return await pending; }
            catch
            {
                if (overwriteLookup)
                    ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.Cleanup,
                        ExecutionCompletion.Unknown, ExecutionRecoveryActions.Dispose, 999, [], operation: ExecutionOperationKind.Save));
                throw;
            }
        }, new()));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.RolledBack);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Attribution_ProbeIgnoresEarlierCancellationWhenCurrentAvailabilityIsClassified(bool opening)
    {
        var primary = new ProbeConnectionFailure();
        ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Cancellation, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.NotApplicable, ExecutionRecoveryActions.None, null, []));
        var harness = new ExistenceProbeHarness { Classify = _ => true };
        if (opening) harness.Session.Opening = ProbeFault(primary);
        else { harness.Access = new(ProbeFault(primary)); harness.Session.Access = harness.Access; }
        using var provider = new ExistenceProbeProvider(new(), () => harness);
        await Assert.That(await provider.FileOrServerExistsAsyncCore()).IsFalse();
        await Assert.That(harness.Classifications).IsEqualTo(1);
        await Assert.That(harness.Session.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("insert")]
    [Arguments("update")]
    [Arguments("save-new")]
    [Arguments("save-existing")]
    [Arguments("delete")]
    [Arguments("batch")]
    public async Task Attribution_MutationCannotInheritEarlierReadCompletionOrCleanup(string kind)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var mutable = kind is "insert" or "save-new" or "batch" ? new Mutable<TransactionMutationGuardRow>() : fixture.CreateExistingMutable(1, "old");
        if (mutable.IsNew()) mutable["Id"] = 1;
        mutable["Value"] = "new value";
        var primary = new Exception("mutation reuses old exception");
        ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.Cleanup,
            ExecutionCompletion.Committed, ExecutionRecoveryActions.Dispose, transaction.TransactionID, [],
            operation: ExecutionOperationKind.Query, providerInstanceId: fixture.Provider.TelemetryInstanceId));
        EnableAsyncMutations(fixture, new(JournalFault(primary))
        {
            FailureEvidence = new(ExecutionFailureCause.ProviderError, ExecutionEffects.Mutation, TransactionIntegrity.Confirmed, true)
        });
        var failure = await AsyncEnumerationFailureOf(() => RunCorrelationMutation(kind, transaction, mutable));
        var current = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(current.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(current.Operation).IsEqualTo(ExpectedMutationKind(kind));
        await Assert.That(current.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(current.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
        await Assert.That(current.HasCleanupFailure).IsFalse();
        await Assert.That(current.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
        await Assert.That(transaction.IsPoisoned).IsTrue();
    }

    [Test]
    [Arguments("non-query")]
    [Arguments("scalar")]
    [Arguments("reader")]
    public async Task Attribution_SynchronousRawDispatchRejectsStaleFacts(string kind)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var factory = EnableSyncRaw(fixture);
        var primary = factory.ExecutionFailure = new Exception("reused sync provider failure");
        ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.Cleanup,
            ExecutionCompletion.Unknown, ExecutionRecoveryActions.Dispose, transaction.TransactionID, [],
            operation: ExecutionOperationKind.Save, providerInstanceId: fixture.Provider.TelemetryInstanceId));
        var failure = Capture<Exception>(() =>
        {
            if (kind == "non-query") transaction.DatabaseAccess.ExecuteNonQuerySyncCore("test");
            else if (kind == "scalar") transaction.DatabaseAccess.ExecuteScalarSyncCore("test");
            else transaction.DatabaseAccess.ExecuteReaderSyncCore("test");
        });
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RawCommand);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.CommandExecution);
        await Assert.That(context.HasCleanupFailure).IsFalse();
    }

    [Test]
    public async Task Attribution_MetadataUsesObservedCommandSnapshotAfterParserReplacesLookup()
    {
        var harness = new MetadataHarness();
        var primary = new Exception("metadata read");
        var cleanup = new Exception("actual command cleanup");
        harness.Queries["tables"].Reader.Advance = JournalFault(primary);
        harness.Queries["tables"].Command.Resource.Cleanup = JournalFault(cleanup);
        harness.Parser = async (context, _) =>
        {
            try { await context.ReadAsync(new Sql("tables"), reader => reader.GetInt32(0)); }
            catch
            {
                ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.Callback,
                    ExecutionCompletion.Committed, ExecutionRecoveryActions.Continue, 999, [], operation: ExecutionOperationKind.Save));
            }
            return harness.EmptyDefinition();
        };
        var failure = MetadataException(await ImportMetadata(new MetadataTestFactory(() => harness)));
        var observed = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(observed.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(observed.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(observed.Operation).IsEqualTo(ExecutionOperationKind.MetadataRead);
        await Assert.That(observed.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(observed.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(observed.SecondaryFailures[0].Exception).IsSameReferenceAs(cleanup);
    }
}
