using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ModelFactoryOccurrences_PublicLegacyFactoryRejectsEarlierReport(bool freshReport)
    {
        using var scope = ExecutionFailureScope.Begin();
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        provider.State.Cache.CleanupScheduler?.Stop();
        var table = provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table;
        var row = ProviderRowMaterializer.Materialize(CanonicalProviderValueRow.Create(table, [1, "row"]), "factory-test");
        var occurrence = new SettledFailureOccurrence();
        var constructions = 0;
        AsyncCanonicalCachedRow.Creating.Value = () =>
        {
            if (++constructions == 1) { occurrence.RecordEarlier(); return; }
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        };
        try
        {
            _ = InstanceFactory.NewImmutableRow(row, provider.ReadOnlyAccess);
            var failure = Capture<Exception>(() => InstanceFactory.NewImmutableRow(row, provider.ReadOnlyAccess));
            await Assert.That(failure).IsSameReferenceAs(occurrence.Reused);
            var context = ExecutionFailureContexts.Get(failure);
            if (freshReport) await Assert.That(context!.Cause).IsEqualTo(ExecutionFailureCause.ProviderError);
            else await Assert.That(context).IsNull();
            await Assert.That(constructions).IsEqualTo(2);
            await occurrence.AssertEarlier();
        }
        finally { AsyncCanonicalCachedRow.Creating.Value = null; }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ModelFactoryOccurrences_BatchConstructorCannotBorrowEarlierModelReport(bool managed, bool freshReport)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        provider.State.Cache.CleanupScheduler?.Stop();
        using var transaction = provider.StartTransaction();
        IDataSourceAccess source = managed ? transaction : provider.ReadOnlyAccess;
        var table = provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table;
        var cache = provider.GetTableCache(table);
        var keys = new ControlledRowDataReader([1], [2]);
        var rows = new ControlledRowDataReader([1, "one"], [2, "two"]);
        var opens = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = opens++ == 0 ? keys : rows, FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var occurrence = new SettledFailureOccurrence();
        var constructions = 0;
        AsyncCanonicalCachedRow.Creating.Value = () =>
        {
            if (++constructions == 1) { occurrence.RecordEarlier(); return; }
            if (freshReport) occurrence.ReportCurrent();
            throw occurrence.Reused;
        };
        try
        {
            var failure = await AsyncEnumerationFailureOf(() => new SqlQuery<AsyncCanonicalCachedRow>(source).SelectQuery().ExecuteBufferedAsyncCore());
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(failure).IsSameReferenceAs(occurrence.Reused);
            await Assert.That(context.Cause).IsEqualTo(freshReport ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.MaterializationError);
            await Assert.That(context.Stage).IsEqualTo(freshReport ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.Materialization);
            await Assert.That(context.Operation).IsEqualTo(freshReport ? ExecutionOperationKind.Rollback : ExecutionOperationKind.Query);
            await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
            await Assert.That(context.TransactionId).IsEqualTo(managed ? (uint?)transaction.TransactionID : null);
            await Assert.That(context.SecondaryFailures).IsEmpty();
            await Assert.That(context.HasCleanupFailure).IsFalse();
            await Assert.That(context.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsEqualTo(managed);
            await Assert.That(constructions).IsEqualTo(2);
            await Assert.That(cache.TryGetMaterializedRow(DataLinqKey.FromValue(2), source, out _)).IsFalse();
            await Assert.That(keys.Disposals).IsEqualTo(1);
            await Assert.That(rows.Disposals).IsEqualTo(1);
            await Assert.That(factory.Commands.Count).IsEqualTo(2);
            await Assert.That(factory.Commands.All(command => command.Resource.AsyncDisposals == 1)).IsTrue();
            await occurrence.AssertEarlier();
            if (managed) DataSourceAccess.EnsureReadAllowed(transaction, "read after failed local constructor");
        }
        finally { AsyncCanonicalCachedRow.Creating.Value = null; }
    }
}
