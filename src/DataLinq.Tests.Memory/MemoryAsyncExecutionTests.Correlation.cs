using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed partial class MemoryAsyncExecutionTests
{
    [Test]
    [Arguments("lookup")]
    [Arguments("entity")]
    [Arguments("projection")]
    [Arguments("count")]
    [Arguments("first")]
    public async Task NontransactionalCorrelation_MemoryCancellationHasNoTransactionRecovery(string kind)
    {
        var database = Primitives();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var query = database.Query().Rows;
        var failure = await Failure(() => kind switch
        {
            "lookup" => database.FindAsyncCore<MemoryPrimitiveRow>(1, cancellation.Token).AsTask(),
            "entity" => Drain(Rows(query, cancellation.Token)),
            "projection" => Drain(Rows(query.Select(row => row.Id), cancellation.Token)),
            "count" => Terminal<MemoryPrimitiveRow, int>(query, "Count", cancellation.Token),
            _ => Terminal<MemoryPrimitiveRow, MemoryPrimitiveRow>(query.OrderBy(row => row.Id), "First", cancellation.Token)
        });
        await Assert.That(failure).IsAssignableTo<OperationCanceledException>();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(kind == "lookup" ? ExecutionOperationKind.KeyLookup : ExecutionOperationKind.Query);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.ProviderInstanceId).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task NontransactionalCorrelation_MemoryDoesNotImportPriorSqlIdentityOrCancellationCause(bool lookup, bool canceled)
    {
        using var observation = new MemoryGuidIdConverter.Observation();
        var database = Converted();
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        Exception primary = canceled ? new OperationCanceledException(unrelated.Token) : new OutOfMemoryException("local converter");
        var previous = new ExecutionFailureContext(ExecutionFailureCause.ProviderError, ExecutionFailureStage.CommandExecution,
            ExecutionCompletion.Unknown, ExecutionRecoveryActions.Rollback, 42, [], operation: ExecutionOperationKind.Save,
            providerInstanceId: "previous-provider", activeOperation: ExecutionOperationKind.Commit);
        ExecutionFailureContexts.Attach(primary, previous);
        observation.FromProvider = _ => throw primary;
        var failure = await Failure(() => lookup ? database.FindAsyncCore<MemoryConvertedRow>(new MemoryGuidId(First)).AsTask()
            : Drain(Rows(database.Query().Rows.Select(row => row.Id))));
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(failure).IsSameReferenceAs(primary);
        await Assert.That(context.Operation).IsEqualTo(lookup ? ExecutionOperationKind.KeyLookup : ExecutionOperationKind.Query);
        await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Unknown);
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.Unknown);
        await Assert.That(context.ProviderInstanceId).IsNull();
        await Assert.That(context.TransactionId).IsNull();
        await Assert.That(context.ActiveOperation).IsNull();
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
        await Assert.That(previous.Operation).IsEqualTo(ExecutionOperationKind.Save);
        await Assert.That(previous.ProviderInstanceId).IsEqualTo("previous-provider");
        observation.FromProvider = null;
        await Assert.That((await List(database.Query().Rows)).Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NontransactionalCorrelation_MemoryCardinalityRemainsQueryFailure(bool scalar)
    {
        var database = Primitives();
        var query = database.Query().Rows.Where(row => row.Id == 99);
        var failure = await Failure(() => scalar ? Terminal<int, int>(query.Select(row => row.Id), "Single")
            : Terminal<MemoryPrimitiveRow, MemoryPrimitiveRow>(query, "Single"));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        var context = ExecutionFailureContexts.Get(failure)!;
        await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
        await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotApplicable);
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.None);
    }
}
