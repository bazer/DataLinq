using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("rows", false)]
    [Arguments("rows", true)]
    [Arguments("keys", false)]
    [Arguments("keys", true)]
    [Arguments("scalar", false)]
    [Arguments("scalar", true)]
    [Arguments("models", false)]
    [Arguments("models", true)]
    [Arguments("direct", false)]
    [Arguments("direct", true)]
    [Arguments("projection", false)]
    [Arguments("projection", true)]
    [Arguments("prepared", false)]
    [Arguments("prepared", true)]
    [Arguments("raw", false)]
    [Arguments("raw", true)]
    [Arguments("lookup", false)]
    [Arguments("lookup", true)]
    public async Task ReadCorrelation_ExecutionFailureKeepsCapturedReadPurpose(string kind, bool managed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        DataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        var primary = new Exception("dispatch failure");
        var cleanup = new Exception("command disposal failure");
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new(JournalFault(primary)) { FailureEvidence = TrustedScalarRead },
            ConfigureCommand = command => command.Resource.Cleanup = JournalFault(cleanup)
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var query = managed ? transaction.From<TransactionMutationGuardRow>() : fixture.Database.From<TransactionMutationGuardRow>();
        if (kind == "direct") query.Where("id").EqualTo(1);
        var select = query.SelectQuery();
        var projection = managed ? transaction.Query().Rows.Select(row => row.Id) : fixture.Database.Query().Rows.Select(row => row.Id);
        var prepared = fixture.Database.PrepareSequenceQuery(1, id => fixture.Database.Query().Rows.Where(row => row.Id > id).Select(row => row.Id));
        var failure = await AsyncEnumerationFailureOf(() => kind switch
        {
            "rows" => PlanRows(select.ReadRowsAsyncCore()),
            "keys" => PlanRows(select.ReadKeysAsyncCore()),
            "scalar" => select.ExecuteScalarAsyncCore<int>(),
            "models" or "direct" => select.ExecuteBufferedAsyncCore(),
            "projection" => PlanRows(AsyncPlan(projection)),
            "prepared" => PlanRows(prepared.ExecuteAsyncCore(managed ? transaction : fixture.Database, 1)),
            "raw" => PlanRows(source.GetFromQueryAsyncCore<TransactionMutationGuardRow>("SELECT id, value FROM rows")),
            _ => AsyncModelLookup.GetByProviderKeyAsyncCore<TransactionMutationGuardRow>(DataLinqKey.FromValue(1), source)
        });
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(kind == "raw" ? ExecutionOperationKind.RawCommand
            : kind == "lookup" ? ExecutionOperationKind.KeyLookup : ExecutionOperationKind.Query);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(managed ? (uint?)transaction.TransactionID : null);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.SecondaryFailures.Single().Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(context.Recovery).IsEqualTo(managed ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.None);
        await Assert.That(factory.Commands.Sum(command => command.Resource.AsyncDisposals)).IsEqualTo(1);
        if (managed) await Assert.That(transaction.AsyncFailureContext).IsSameReferenceAs(context);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ReadCorrelation_RelationReadKeepsPurposeAndProviderRecovery(bool reference, bool managed)
    {
        using var fixture = new AsyncRelationFixture(reference);
        using var transaction = fixture.Provider.StartTransaction();
        var primary = new Exception("relation dispatch failure");
        fixture.Factory.CreateAccess = _ => new(JournalFault(primary)) { FailureEvidence = TrustedScalarRead };
        var failure = await AsyncEnumerationFailureOf(() => fixture.Holder(managed ? transaction : fixture.Provider.ReadOnlyAccess).Read());
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RelationLoad);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(managed ? (uint?)transaction.TransactionID : null);
        await Assert.That(context.Recovery).IsEqualTo(managed
            ? ExecutionRecoveryPolicy.ForReadFailure(TrustedScalarRead, true) : ExecutionRecoveryActions.None);
        if (managed) DataSourceAccess.EnsureReadAllowed(transaction, "read after failure");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadCorrelation_LocalKeyConversionFailureNeedsNoProviderAssessment(bool managed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        DataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        using var converter = new TransactionMutationGuardReferenceIdConverter.Observation();
        var primary = new Exception("key conversion failure");
        converter.Converting = () => throw primary;
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var failure = await AsyncEnumerationFailureOf(() => AsyncModelLookup.GetByModelKeyAsyncCore<TransactionMutationGuardReferenceIdRow>(
            [new TransactionMutationGuardReferenceId(42)], source));
        converter.Converting = null;
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.KeyLookup);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Recovery).IsEqualTo(managed
            ? ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.None);
        await Assert.That(factory.Commands).IsEmpty();
        if (managed) transaction.Rollback();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadCorrelation_RelationWaiterCancellationCannotRewriteOwnerFailure(bool reference)
    {
        using var fixture = new AsyncRelationFixture(reference);
        var access = fixture.SetRows(paused: true);
        var holder = fixture.Holder();
        var owner = holder.Read();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var waiter = holder.Read(cancellation.Token);
        cancellation.Cancel();
        ExecutionFailureContext snapshot;
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => waiter);
            snapshot = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(snapshot.Operation).IsEqualTo(ExecutionOperationKind.RelationLoad);
            await Assert.That(snapshot.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
            await Assert.That(snapshot.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
            await Assert.That(snapshot.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
            await Assert.That(owner.IsCompleted).IsFalse();
        }
        finally { access.Dispatch.Release(); await owner; }
        await Assert.That(await owner).IsEquivalentTo(new[] { 1 });
        await Assert.That(snapshot.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(fixture.Dispatches).IsEqualTo(1);
    }

    [Test]
    [Arguments("query")]
    [Arguments("lookup")]
    [Arguments("raw")]
    public async Task ReadCorrelation_OverlapReportsRequestedPurposeAndActiveQuery(string requested)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 7 };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var pending = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteScalarAsyncCore<int>();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => requested switch
            {
                "query" => PlanRows(AsyncPlan(transaction.Query().Rows)),
                "lookup" => AsyncModelLookup.GetByProviderKeyAsyncCore<TransactionMutationGuardRow>(DataLinqKey.FromValue(1), transaction),
                _ => PlanRows(transaction.GetFromQueryAsyncCore<TransactionMutationGuardRow>("rejected"))
            });
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Operation).IsEqualTo(requested == "query" ? ExecutionOperationKind.Query
                : requested == "lookup" ? ExecutionOperationKind.KeyLookup : ExecutionOperationKind.RawCommand);
            await Assert.That(context.ActiveOperation).IsEqualTo((ExecutionOperationKind?)ExecutionOperationKind.Query);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.FinishActiveOperation);
            await Assert.That(transaction.AsyncFailureContext).IsNull();
        }
        finally { access.Dispatch.Release(); await pending; }
        transaction.Commit();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadCorrelation_QueryContinuationKeepsChildCleanupRestrictions(bool managed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("row load after successful key read");
        var cleanup = new Exception("child cleanup");
        var dispatches = 0;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => dispatches++ == 0
                ? new() { ReaderOverride = new ControlledRowDataReader([1]) }
                : new(JournalFault(primary)) { FailureEvidence = TrustedScalarRead },
            ConfigureCommand = command => { if (dispatches == 2) command.Resource.Cleanup = JournalFault(cleanup); }
        };
        var select = (managed ? transaction.From<TransactionMutationGuardRow>() : fixture.Database.From<TransactionMutationGuardRow>()).SelectQuery();
        var failure = await AsyncEnumerationFailureOf(() => select.ExecuteBufferedAsyncCore());
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(context.Recovery).IsEqualTo(managed ? ExecutionRecoveryActions.Dispose : ExecutionRecoveryActions.None);
        await Assert.That(dispatches).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadCorrelation_PrivateReadRetainsExplicitOwnerKind(bool known)
    {
        var kind = known ? ExecutionOperationKind.Save : ExecutionOperationKind.Unknown;
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("nested read failure");
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new(JournalFault(primary)) { FailureEvidence = TrustedScalarRead } };
        fixture.Scenario.AsyncSqlReaders = factory;
        using var read = DataSourceAccess.BeginRead(transaction, "outer operation", operationKind: kind);
        var failure = await AsyncEnumerationFailureOf(() => fixture.RowCache.GetProviderRowAsyncCore(
            DataLinqKey.FromValue(1), transaction, owner: read!.Step));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(kind);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo((uint?)transaction.TransactionID);
    }

    [Test]
    public async Task ReadCorrelation_RequiredNullReferenceReportsLocalFailureWithoutDispatch()
    {
        using var fixture = new AsyncRelationFixture(reference: true);
        using var transaction = fixture.Provider.StartTransaction();
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationChild)).Model.RelationProperties[nameof(AsyncRelationChild.Parent)];
        var reference = new ImmutableForeignKey<AsyncRelationParent>(DataLinqKey.Null, transaction, property);
        var failure = await AsyncEnumerationFailureOf(() => reference.GetRequiredValueAsyncCore());
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.RelationLoad);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue | ExecutionRecoveryActions.Rollback)).IsTrue();
        await Assert.That(fixture.Dispatches).IsEqualTo(0);
        transaction.Rollback();
    }

    [Test]
    public async Task ReadCorrelation_PreCapturedEnumeratorRejectionPreservesAdmissionRecovery()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 7 };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        await using var rejected = select.ReadRowsAsyncCore().GetAsyncEnumerator();
        var pending = select.ExecuteScalarAsyncCore<int>();
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => rejected.MoveNextAsync().AsTask());
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
            await Assert.That(context.ActiveOperation).IsEqualTo((ExecutionOperationKind?)ExecutionOperationKind.Query);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.FinishActiveOperation);
            await Assert.That(transaction.AsyncFailureContext).IsNull();
            await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        }
        finally { access.Dispatch.Release(); await pending; }
        transaction.Commit();
    }
}
