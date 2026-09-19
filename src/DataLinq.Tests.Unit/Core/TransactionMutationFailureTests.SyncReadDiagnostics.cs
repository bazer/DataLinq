using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncReadDiagnostics_RowConversionRemainsPrimaryThroughBothDisposals(bool managed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
        { ValueFailure = new FormatException("row conversion"), DisposeFailure = readerCleanup };
        fixture.Scenario.CommandDisposeFailure = commandCleanup;
        var select = (managed ? transaction.From<TransactionMutationGuardRow>() : fixture.Database.From<TransactionMutationGuardRow>()).SelectQuery();
        var failure = Capture<Exception>(() => select.ReadRows().ToArray());
        await Assert.That(failure).IsNotSameReferenceAs(readerCleanup);
        await Assert.That(failure).IsNotSameReferenceAs(commandCleanup);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(2);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(readerCleanup);
        await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(commandCleanup);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task SyncReadDiagnostics_StringRelationKeepsOriginalAndBothCleanupFailures(bool managed, bool readFails)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new ScriptedMutationProvider<SyncReadStringRelationDb>(scenario);
        using var transaction = provider.StartTransaction();
        DataSourceAccess source = managed ? transaction : provider.ReadOnlyAccess;
        var parent = provider.Metadata.GetTableModel(typeof(SyncReadStringParent));
        var child = provider.Metadata.GetTableModel(typeof(SyncReadStringChild));
        var primary = new Exception("relation read");
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var reader = new OwnedReadProbe(new ScriptedRowData(child.Table, 401, "owner"))
        { OnRead = () => { if (readFails) throw primary; }, DisposeFailure = readerCleanup };
        scenario.ReaderFactory = () => reader;
        scenario.CommandDisposeFailure = commandCleanup;
        var failure = Capture<Exception>(() => provider.GetTableCache(child.Table).GetRows("owner",
            parent.Model.RelationProperties[nameof(SyncReadStringParent.Children)], source).ToArray());
        await Assert.That(failure).IsSameReferenceAs(readFails ? primary : readerCleanup);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Stage).IsEqualTo(readFails ? ExecutionFailureStage.RowLoading : ExecutionFailureStage.Cleanup);
        await Assert.That(context.Operation).IsEqualTo(readFails ? ExecutionOperationKind.RelationLoad : ExecutionOperationKind.Dispose);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(managed ? transaction.TransactionID : (uint?)null);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(readFails ? 2 : 1);
        if (readFails) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(readerCleanup);
        await Assert.That(context.SecondaryFailures[^1].Exception).IsSameReferenceAs(commandCleanup);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(scenario.CommandDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncReadDiagnostics_IteratorRestoresConsumerScopeAndAcceptsLaterMoveEvidence(bool managed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"));
        fixture.Scenario.ReaderFactory = () => reader;
        using var caller = ExecutionFailureScope.Begin();
        var callerScope = ExecutionFailureScope.Current;
        var select = (managed ? transaction.From<TransactionMutationGuardRow>() : fixture.Database.From<TransactionMutationGuardRow>()).SelectQuery();
        using var rows = select.ReadRows().GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(callerScope);
        var primary = new Exception("second move");
        reader.OnRead = () =>
        {
            ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.RowLoading,
                ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, managed ? transaction.TransactionID : null, []));
            throw primary;
        };
        await Assert.That(Capture<Exception>(() => rows.MoveNext())).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(primary)!;
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(callerScope);
        rows.Dispose();
        await Assert.That(reader.Disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("single", false)]
    [Arguments("single", true)]
    [Arguments("batch", false)]
    [Arguments("batch", true)]
    [Arguments("index", false)]
    [Arguments("index", true)]
    [Arguments("first", false)]
    [Arguments("first", true)]
    [Arguments("scalar-column", false)]
    [Arguments("scalar-column", true)]
    [Arguments("reader", false)]
    [Arguments("reader", true)]
    [Arguments("scalar", true)]
    [Arguments("scalar-object", true)]
    public async Task SyncReadDiagnostics_CurrentInvocationRejectsStaleFacts(string route, bool managed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        DataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        var primary = new Exception("reused synchronous read failure");
        var staleCleanup = new Exception("previous cleanup");
        var previous = new ExecutionFailureContext(ExecutionFailureCause.Timeout, ExecutionFailureStage.Cleanup,
            ExecutionCompletion.Committed, ExecutionRecoveryActions.Dispose, managed ? transaction.TransactionID : null,
            [new(ExecutionFailureCause.ProviderError, ExecutionFailureStage.Cleanup, staleCleanup)],
            operation: ExecutionOperationKind.Save, providerInstanceId: fixture.Provider.TelemetryInstanceId);
        ExecutionFailureContexts.Attach(primary, previous);
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
        { OnRead = () => throw primary };
        fixture.Scenario.ReaderFactory = () => reader;
        fixture.Scenario.ScalarExecuting = () => throw primary;
        var before = ExecutionFailureScope.Current;
        var failure = Capture<Exception>(() => ExecuteDiagnosticRead(fixture, transaction, source, route));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(ExecutionFailureScope.Current).IsSameReferenceAs(before);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(route.StartsWith("scalar", StringComparison.Ordinal) && route != "scalar-column"
            ? ExecutionFailureStage.CommandExecution : ExecutionFailureStage.RowLoading);
        await Assert.That(context.Operation).IsEqualTo(DiagnosticReadKind(route));
        await Assert.That(context.ProviderInstanceId).IsEqualTo(fixture.Provider.TelemetryInstanceId);
        await Assert.That(context.TransactionId).IsEqualTo(managed ? transaction.TransactionID : (uint?)null);
        await Assert.That(context.Completion).IsEqualTo(managed ? ExecutionCompletion.NotAttempted : ExecutionCompletion.NotApplicable);
        await Assert.That(context.HasCleanupFailure).IsFalse();
        await Assert.That(context.SecondaryFailures).IsEmpty();
        await Assert.That(previous.SecondaryFailures.Single().Exception).IsSameReferenceAs(staleCleanup);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncReadDiagnostics_FreshProviderReportSurvivesButLaterReuseDoesNot(bool managed)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        DataSourceAccess source = managed ? transaction : fixture.Provider.ReadOnlyAccess;
        var primary = new Exception("reported once, thrown twice");
        var fresh = true;
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
        {
            OnRead = () =>
            {
                if (fresh) ExecutionFailureContexts.Attach(primary, new(ExecutionFailureCause.Timeout,
                    ExecutionFailureStage.RowLoading, ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose,
                    managed ? transaction.TransactionID : null, []));
                throw primary;
            }
        };
        _ = Capture<Exception>(() => ExecuteDiagnosticRead(fixture, transaction, source, "single"));
        var previous = ExecutionFailureContexts.Get(primary)!;
        fresh = false;
        _ = Capture<Exception>(() => ExecuteDiagnosticRead(fixture, transaction, source, "single"));
        await Assert.That(previous.Cause).IsEqualTo(ExecutionFailureCause.Timeout);
        await Assert.That(ExecutionFailureContexts.Get(primary)!.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
    }

    [Test]
    [Arguments("matching")]
    [Arguments("foreign")]
    [Arguments("unrequested")]
    public async Task SyncReadDiagnostics_CancellationRequiresTheRequestedToken(string mode)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        using var request = new CancellationTokenSource();
        var primary = new OperationCanceledException(mode == "foreign" ? new CancellationToken(true) : request.Token);
        fixture.Scenario.ReaderFactory = () => new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
        {
            OnRead = () => { if (mode != "unrequested") request.Cancel(); throw primary; }
        };
        var loader = new DataSourceAccessSourceRowLoader(transaction);
        var failure = Capture<OperationCanceledException>(() => loader.LoadSingle(fixture.RowTable, DataLinqKey.FromValue(401), request.Token));
        await Assert.That(failure).IsSameReferenceAs(primary);
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Cause).IsEqualTo(mode == "matching" ? ExecutionFailureCause.Cancellation : ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.KeyLookup);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SyncReadDiagnostics_CleanupOccurrencesDoNotBorrowSiblingClassification(bool reportAtCleanup)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("read");
        var reused = new Exception("reader and command cleanup");
        var stale = new Exception("earlier phantom cleanup");
        ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.Timeout, ExecutionFailureStage.Callback,
            ExecutionCompletion.Committed, ExecutionRecoveryActions.Continue, transaction.TransactionID,
            [new(ExecutionFailureCause.Unknown, ExecutionFailureStage.Cleanup, stale)], operation: ExecutionOperationKind.Save));
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
        {
            OnRead = () => throw primary,
            OnDispose = () =>
            {
                if (reportAtCleanup) ExecutionFailureContexts.Attach(reused, new(ExecutionFailureCause.ProviderError,
                    ExecutionFailureStage.Cleanup, ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose,
                    transaction.TransactionID, [], operation: ExecutionOperationKind.Dispose));
            },
            DisposeFailure = reused
        };
        fixture.Scenario.ReaderFactory = () => reader;
        fixture.Scenario.CommandDisposeFailure = reused;
        var failure = Capture<Exception>(() => ExecuteDiagnosticRead(fixture, transaction, transaction, "single"));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(reused);
        await Assert.That(context.SecondaryFailures[0].Cause).IsEqualTo(reportAtCleanup ? ExecutionFailureCause.ProviderError : ExecutionFailureCause.Unknown);
        await Assert.That(context.SecondaryFailures[0].Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.SecondaryFailures[0].Operation).IsEqualTo(ExecutionOperationKind.Dispose);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
    }

    private static ExecutionOperationKind DiagnosticReadKind(string route) => route switch
    {
        "single" or "batch" or "scalar-column" => ExecutionOperationKind.KeyLookup,
        "index" => ExecutionOperationKind.RelationLoad,
        _ => ExecutionOperationKind.Query
    };

    private static void ExecuteDiagnosticRead(ScriptedFixture fixture, Transaction<TransactionMutationGuardDb> transaction,
        DataSourceAccess source, string route)
    {
        var key = DataLinqKey.FromValue(401);
        var select = (ReferenceEquals(source, transaction) ? transaction.From<TransactionMutationGuardRow>()
            : fixture.Database.From<TransactionMutationGuardRow>()).SelectQuery();
        var loader = new DataSourceAccessSourceRowLoader(source);
        switch (route)
        {
            case "single": _ = loader.LoadSingle(fixture.RowTable, key); break;
            case "batch": _ = loader.Load(new SourcePrimaryKeyRowRequest(fixture.RowTable, [key])); break;
            case "index":
                var index = fixture.RowTable.ColumnIndices.Single(x => x.Name == "idx_transaction_mutation_guard_value");
                _ = loader.Load(new SourceIndexRowRequest(fixture.RowTable, index, DataLinqKey.FromValue("stored"))); break;
            case "first": _ = select.ReadFirstRow(); break;
            case "scalar-column": _ = new ScalarColumnRowsQuery(fixture.RowTable, source, fixture.RowTable.PrimaryKeyColumns[0], 401).ReadFirstRow(); break;
            case "reader": _ = select.ReadRows().ToArray(); break;
            case "scalar": _ = select.ExecuteScalar<int>(); break;
            case "scalar-object": _ = select.ExecuteScalar(); break;
            default: throw new ArgumentOutOfRangeException(nameof(route));
        }
    }
}

[Database("sync_read_string_relations")]
public sealed partial class SyncReadStringRelationDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<SyncReadStringParent> Parents { get; } = new(source);
    public DbRead<SyncReadStringChild> Children { get; } = new(source);
}

[Table("sync_read_string_parents")]
public abstract partial class SyncReadStringParent(IRowData rowData, IDataSourceAccess source)
    : Immutable<SyncReadStringParent, SyncReadStringRelationDb>(rowData, source), ITableModel<SyncReadStringRelationDb>
{
    [PrimaryKey, Column("id")]
    public abstract string Id { get; }
    [Relation("sync_read_string_children", "value", "FK_sync_read_string_child")]
    public abstract IImmutableRelation<SyncReadStringChild> Children { get; }
}

[Table("sync_read_string_children")]
public abstract partial class SyncReadStringChild(IRowData rowData, IDataSourceAccess source)
    : Immutable<SyncReadStringChild, SyncReadStringRelationDb>(rowData, source), ITableModel<SyncReadStringRelationDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [ForeignKey("sync_read_string_parents", "id", "FK_sync_read_string_child"), Column("value")]
    public abstract string ParentId { get; }
    [Relation("sync_read_string_parents", "id", "FK_sync_read_string_child")]
    public abstract SyncReadStringParent Parent { get; }
}
