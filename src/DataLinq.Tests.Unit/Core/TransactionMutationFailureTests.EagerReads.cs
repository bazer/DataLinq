using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments("key")]
    [Arguments("legacy-key")]
    [Arguments("single")]
    [Arguments("batch")]
    [Arguments("index")]
    [Arguments("first")]
    [Arguments("scalar-column")]
    [Arguments("scalar")]
    [Arguments("scalar-object")]
    [Arguments("scalar-terminal")]
    [Arguments("key-terminal")]
    public async Task EagerReadFailure_IsObservedBeforeHelperOwnershipHandoff(string route)
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        await AssertEagerFailureObserved(transaction, fixture.Scenario, () => ExecuteEagerRead(fixture, transaction, route));
    }

    [Test]
    [Arguments("reference")]
    [Arguments("values")]
    [Arguments("dictionary")]
    public async Task EagerRelationFailure_IsObservedBeforeHelperOwnershipHandoff(string route)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new ScriptedMutationProvider<EagerReadRelationDb>(scenario);
        var transaction = provider.StartTransaction();
        var parent = provider.Metadata.GetTableModel(typeof(EagerReadParent));
        var child = provider.Metadata.GetTableModel(typeof(EagerReadChild));
        var reference = new ImmutableForeignKey<EagerReadParent, int>(401, transaction,
            child.Model.RelationProperties[nameof(EagerReadChild.Parent)]);
        var relation = new ImmutableRelation<EagerReadChild, int>(401, transaction,
            parent.Model.RelationProperties[nameof(EagerReadParent.Children)]);
        await AssertEagerFailureObserved(transaction, scenario, () =>
        {
            switch (route)
            {
                case "reference": _ = reference.Value; break;
                case "values": _ = relation.Values; break;
                case "dictionary": _ = relation.ToFrozenDictionary(); break;
            }
        });
    }

    [Test]
    [Arguments("key")]
    [Arguments("single")]
    [Arguments("batch")]
    [Arguments("index")]
    [Arguments("first")]
    [Arguments("scalar-column")]
    [Arguments("reader")]
    [Arguments("scalar")]
    [Arguments("scalar-object")]
    public async Task ReadAndCleanupFailures_PreserveOriginalAndAttemptEveryOwnedCleanup(string route)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var primary = new InjectedMutationException("original read failure");
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
        {
            OnRead = () => ThrowEagerReadFailure(primary), DisposeFailure = readerCleanup
        };
        fixture.Scenario.ReaderFactory = () => reader;
        fixture.Scenario.ScalarExecuting = () => ThrowEagerReadFailure(primary);
        fixture.Scenario.CommandDisposeFailure = commandCleanup;
        var error = Capture<InjectedMutationException>(() => ExecuteEagerRead(fixture, transaction, route));
        await Assert.That(error).IsSameReferenceAs(primary);
        await Assert.That(error.StackTrace!).Contains(nameof(ThrowEagerReadFailure));
        var context = ExecutionFailureContexts.Get(error)!;
        var scalar = route is "scalar" or "scalar-object";
        await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(scalar ? 1 : 2);
        if (!scalar) await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(readerCleanup);
        await Assert.That(context.SecondaryFailures[^1].Exception).IsSameReferenceAs(commandCleanup);
        await Assert.That(reader.Disposals).IsEqualTo(scalar ? 0 : 1);
        await Assert.That(fixture.Scenario.CommandDisposals).IsEqualTo(1);
        using var released = transaction.ExecutionGate.Enter("check ownership release only");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SuccessfulReadWithCleanupFailure_ReportsFirstCleanupAndStillDisposesCommand(bool transactionBound)
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var first = new Exception("reader cleanup");
        var second = new Exception("command cleanup");
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored")) { DisposeFailure = first };
        var command = new ScriptedDbCommand(() => throw second);
        var resources = new ReadCommandResources(transactionBound ? transaction.TransactionID : null);
        resources.OwnCommand(command);
        resources.OwnReader(reader);
        await Assert.That(Capture<Exception>(() => resources.Dispose())).IsSameReferenceAs(first);
        var context = ExecutionFailureContexts.Get(first)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Cleanup);
        await Assert.That(context.SecondaryFailures.Single().Exception).IsSameReferenceAs(second);
        await Assert.That(context.Completion).IsEqualTo(transactionBound ? ExecutionCompletion.NotAttempted : ExecutionCompletion.NotApplicable);
        resources.Dispose();
        await Assert.That(reader.Disposals).IsEqualTo(1);
    }

    [Test]
    public async Task MaterializationFailure_RemainsPrimaryWhenBothDisposalsFail()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var readerCleanup = new Exception("reader cleanup");
        var commandCleanup = new Exception("command cleanup");
        var reader = new OwnedReadProbe(new ScriptedRowData(fixture.RowTable, 401, "stored"))
        {
            ValueFailure = new FormatException("decode failed"), DisposeFailure = readerCleanup
        };
        fixture.Scenario.ReaderFactory = () => reader;
        fixture.Scenario.CommandDisposeFailure = commandCleanup;
        var failure = Capture<Exception>(() => ExecuteEagerRead(fixture, transaction, "single"));
        await Assert.That(ReferenceEquals(failure, readerCleanup)).IsFalse();
        await Assert.That(ReferenceEquals(failure, commandCleanup)).IsFalse();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(readerCleanup);
        await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(commandCleanup);
    }

    private static async Task AssertEagerFailureObserved(Transaction transaction, ScriptedMutationScenario scenario, Action read)
    {
        using var resume = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InjectedMutationException("pending eager read");
        void FailProvider()
        {
            entered.TrySetResult();
            if (!resume.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test did not release provider work.");
            ThrowEagerReadFailure(expected);
        }
        scenario.ReaderFactory = () => { FailProvider(); return EmptyReader.Instance; };
        scenario.ScalarExecuting = FailProvider;
        var helper = transaction.ExecutionGate.BeginHelperLifetime();
        // This intentionally blocks synchronous provider work. A dedicated test thread
        // leaves the shared pool free to run the continuation that releases the read.
        var pendingRead = Task.Factory.StartNew(() =>
        {
            // These ownership tests do not exercise background cache maintenance.
            // Stop it on this dedicated thread as well: fixture teardown otherwise
            // synchronously waits for its pool continuation and can starve peers.
            transaction.Provider.State.Cache.CleanupScheduler?.Stop();
            read();
        }, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var failures = new ExecutionFailures();
        Task<TransactionOperationGate.Lease>? drain = null;
        Exception? observed = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            drain = helper.CloseAndDrainAsync(failures);
            await Assert.That(drain.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.EnsureCanRead("start a competing read"));
            await Assert.That(scenario.CommandDisposals).IsEqualTo(0);
        }
        finally
        {
            resume.Set();
            // Even a failed entry/assertion must join the worker before its fixture or
            // release event is disposed. Do not leave a blocked orphan test operation.
            try { await pendingRead; }
            catch (Exception failure) { observed = failure; }
            using var completionOwner = await (drain ?? helper.CloseAndDrainAsync(failures)).WaitAsync(TimeSpan.FromSeconds(10));
            transaction.DatabaseAccess.Dispose(); // Scripted resource; public completion is borrowed.
        }
        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(failures.Primary).IsTypeOf<InvalidOperationException>();
        var context = failures.Snapshot(new(), ExecutionCompletion.NotAttempted, ExecutionRecoveryActions.Dispose, transaction.TransactionID);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(1);
        await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(expected);
        await Assert.That(scenario.CommandDisposals).IsEqualTo(1);
    }

    private static void ExecuteEagerRead(ScriptedFixture fixture, Transaction<TransactionMutationGuardDb> transaction, string route)
    {
        var key = DataLinqKey.FromValue(401);
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var loader = new DataSourceAccessSourceRowLoader(transaction);
        switch (route)
        {
            case "key": _ = transaction.Get<TransactionMutationGuardRow>(key); break;
            case "legacy-key": _ = transaction.Get<TransactionMutationGuardBinaryRow>(DataLinqKey.FromValue(new byte[] { 1, 2 })); break;
            case "single": _ = loader.LoadSingle(fixture.RowTable, key); break;
            case "batch": _ = loader.Load(new SourcePrimaryKeyRowRequest(fixture.RowTable, [key])); break;
            case "index":
                var index = fixture.RowTable.ColumnIndices.Single(x => x.Name == "idx_transaction_mutation_guard_value");
                _ = loader.Load(new SourceIndexRowRequest(fixture.RowTable, index, DataLinqKey.FromValue("stored")));
                break;
            case "first": _ = select.ReadFirstRow(); break;
            case "scalar-column": _ = new ScalarColumnRowsQuery(fixture.RowTable, transaction, fixture.RowTable.PrimaryKeyColumns[0], 401).ReadFirstRow(); break;
            case "reader": _ = select.ReadRows().ToArray(); break;
            case "scalar": _ = select.ExecuteScalar<int>(); break;
            case "scalar-object": _ = select.ExecuteScalar(); break;
            case "scalar-terminal": _ = transaction.Query().Rows.Count(); break;
            case "key-terminal": _ = transaction.Query().Rows.Single(row => row.Id == 401); break;
            default: throw new ArgumentOutOfRangeException(nameof(route));
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowEagerReadFailure(Exception failure) => throw failure;
}

[Database("eager_read_relations")]
public sealed partial class EagerReadRelationDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<EagerReadParent> Parents { get; } = new(source);
    public DbRead<EagerReadChild> Children { get; } = new(source);
}

[Table("eager_parents")]
public abstract partial class EagerReadParent(IRowData rowData, IDataSourceAccess source)
    : Immutable<EagerReadParent, EagerReadRelationDb>(rowData, source), ITableModel<EagerReadRelationDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [Relation("eager_children", "parent_id", "FK_eager_child")]
    public abstract IImmutableRelation<EagerReadChild> Children { get; }
}

[Table("eager_children")]
public abstract partial class EagerReadChild(IRowData rowData, IDataSourceAccess source)
    : Immutable<EagerReadChild, EagerReadRelationDb>(rowData, source), ITableModel<EagerReadRelationDb>
{
    [PrimaryKey, Column("id")]
    public abstract int Id { get; }
    [ForeignKey("eager_parents", "id", "FK_eager_child"), Column("parent_id")]
    public abstract int ParentId { get; }
    [Relation("eager_parents", "id", "FK_eager_child")]
    public abstract EagerReadParent Parent { get; }
}
