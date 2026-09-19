using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RelationKey_KeylessUniqueViewReferencePreservesSynchronousTargetSupport(bool asynchronous, bool transactional)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationViewDb>(scenario);
        using var transaction = provider.StartTransaction();
        var source = transactional ? (DataSourceAccess)transaction : provider.ReadOnlyAccess;
        var table = provider.Metadata.GetTableModel(typeof(RelationUniqueView)).Table;
        var cache = provider.GetTableCache(table);
        provider.State.Cache.CleanupScheduler?.Stop();
        await Assert.That(table.PrimaryKeyColumns).IsEmpty();
        scenario.ReaderFactory = () => new OwnedReadProbe(new RelationTestRow(table, [7, "seven"]));
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([7, "seven"]), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var property = provider.Metadata.GetTableModel(typeof(RelationViewChild)).Model.RelationProperties[nameof(RelationViewChild.Parent)];
        var holder = new ImmutableForeignKey<RelationUniqueView, int>(7, source, property);
        var row = asynchronous ? await holder.GetRequiredValueAsyncCore() : holder.Value!;
        await Assert.That(row.Code).IsEqualTo(7);
        await Assert.That(row.Label).IsEqualTo("seven");
        await Assert.That(asynchronous ? await holder.GetValueAsyncCore() : holder.Value).IsSameReferenceAs(row);
        scenario.ReaderFactory = () => new OwnedReadProbe(new RelationTestRow(table, [8, "eight"]));
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([8, "eight"]), FailureEvidence = TrustedScalarRead };
        var other = new ImmutableForeignKey<RelationUniqueView, int>(8, source, property);
        var otherRow = asynchronous ? await other.GetRequiredValueAsyncCore() : other.Value!;
        await Assert.That(otherRow.Code).IsEqualTo(8);
        await Assert.That(otherRow.Label).IsEqualTo("eight");
        await Assert.That(cache.RowCount).IsEqualTo(0);
        await Assert.That(cache.GetTransactionRows(transaction).Count()).IsEqualTo(0);
        await Assert.That(cache.IndicesCount.Sum(index => index.count)).IsEqualTo(0);
        await Assert.That(scenario.ReaderExecutions).IsEqualTo(asynchronous ? 0 : 2);
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(asynchronous ? 2 : 0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task RelationKey_KeylessViewPreservesAbsenceAndCardinality(bool transactional, bool multiple)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationViewDb>(scenario);
        using var transaction = provider.StartTransaction();
        var source = transactional ? (DataSourceAccess)transaction : provider.ReadOnlyAccess;
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(RelationUniqueView)).Table);
        provider.State.Cache.CleanupScheduler?.Stop();
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(multiple ? [[7, "first"], [7, "second"]] : []), FailureEvidence = TrustedScalarRead }
        };
        scenario.AsyncSqlReaders = factory;
        var property = provider.Metadata.GetTableModel(typeof(RelationViewChild)).Model.RelationProperties[nameof(RelationViewChild.Parent)];
        var holder = new ImmutableForeignKey<RelationUniqueView, int>(7, source, property);
        if (multiple)
        {
            var failure = await AsyncEnumerationFailureOf(() => holder.GetValueAsyncCore());
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            if (transactional)
            {
                await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
                await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
            }
            factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([7, "valid"]), FailureEvidence = TrustedScalarRead };
            await Assert.That((await holder.GetRequiredValueAsyncCore()).Label).IsEqualTo("valid");
        }
        else
        {
            await Assert.That(await holder.GetValueAsyncCore()).IsNull();
            await Assert.That(await holder.GetValueAsyncCore()).IsNull();
            await Assert.That((await AsyncEnumerationFailureOf(() => holder.GetRequiredValueAsyncCore())).Message).Contains("RelationViewChild.Parent");
        }
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(multiple ? 2 : 1);
        await Assert.That(cache.RowCount).IsEqualTo(0);
        await Assert.That(cache.GetTransactionRows(transaction).Count()).IsEqualTo(0);
        await Assert.That(cache.IndicesCount.Sum(index => index.count)).IsEqualTo(0);
    }

    [Test]
    [Arguments("invalidate")]
    [Arguments("cancel")]
    [Arguments("cleanup")]
    [Arguments("constructor")]
    public async Task RelationKey_KeylessViewPublishesOnlyAfterSuccessfulCleanupAndStableGeneration(string outcome)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<RelationViewDb>(scenario);
        using var cancellation = new CancellationTokenSource();
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(RelationUniqueView)).Table);
        provider.State.Cache.CleanupScheduler?.Stop();
        var rows = new ControlledRowDataReader([7, "original"]) { Cleanup = new(paused: true) };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = rows, FailureEvidence = TrustedScalarRead } };
        scenario.AsyncSqlReaders = factory;
        var property = provider.Metadata.GetTableModel(typeof(RelationViewChild)).Model.RelationProperties[nameof(RelationViewChild.Parent)];
        var holder = new ImmutableForeignKey<RelationUniqueView, int>(7, provider.ReadOnlyAccess, property);
        var constructions = 0;
        RelationUniqueView.Creating.Value = () => { constructions++; if (outcome == "constructor") cache.ClearCache(); };
        try
        {
            var pending = holder.GetRequiredValueAsyncCore(cancellation.Token);
            await rows.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var expected = new InvalidOperationException("view reader cleanup");
            try
            {
                await Assert.That(constructions).IsEqualTo(0);
                await Assert.That(pending.IsCompleted).IsFalse();
                if (outcome == "invalidate") cache.ClearCache();
                if (outcome == "cancel") cancellation.Cancel();
                if (outcome == "cleanup") rows.Cleanup.Fail(expected);
            }
            finally { rows.Cleanup.Release(); }
            if (outcome == "cancel")
                await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<OperationCanceledException>();
            else if (outcome == "cleanup")
                await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
            else
                await Assert.That((await pending).Label).IsEqualTo("original");
        }
        finally { RelationUniqueView.Creating.Value = null; }
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([7, "fresh"]), FailureEvidence = TrustedScalarRead };
        await Assert.That((await holder.GetRequiredValueAsyncCore()).Label).IsEqualTo("fresh");
        await Assert.That(factory.Commands.Sum(command => command.Creates)).IsEqualTo(2);
        await Assert.That(cache.RowCount).IsEqualTo(0);
        await Assert.That(cache.IndicesCount.Sum(index => index.count)).IsEqualTo(0);
        await Assert.That(scenario.ReaderExecutions).IsEqualTo(0);
    }
}

[Database("relation_views"), UseCache, IndexCache(IndexCacheType.All)]
public sealed partial class RelationViewDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<RelationUniqueView> Parents { get; } = new(source);
    public DbRead<RelationViewChild> Children { get; } = new(source);
}

[View("relation_unique_view"), Definition("SELECT 7 AS code, 'seven' AS label"),
 Index("UX_relation_view_code", IndexCharacteristic.Unique, "code")]
public abstract partial class RelationUniqueView : Immutable<RelationUniqueView, RelationViewDb>, IViewModel<RelationViewDb>
{
    protected RelationUniqueView(IRowData row, IDataSourceAccess source) : base(row, source) => Creating.Value?.Invoke();
    internal static AsyncLocal<Action?> Creating { get; } = new();
    [Column("code")] public abstract int Code { get; }
    [Column("label")] public abstract string Label { get; }
    [Relation("relation_view_children", "parent_code", "FK_relation_view")]
    public abstract IImmutableRelation<RelationViewChild> Children { get; }
}

[Table("relation_view_children")]
public abstract partial class RelationViewChild(IRowData row, IDataSourceAccess source)
    : Immutable<RelationViewChild, RelationViewDb>(row, source), ITableModel<RelationViewDb>
{
    [PrimaryKey, Column("id")] public abstract int Id { get; }
    [ForeignKey("relation_unique_view", "code", "FK_relation_view"), Column("parent_code")] public abstract int ParentCode { get; }
    [Relation("relation_unique_view", "code", "FK_relation_view")] public abstract RelationUniqueView Parent { get; }
}
