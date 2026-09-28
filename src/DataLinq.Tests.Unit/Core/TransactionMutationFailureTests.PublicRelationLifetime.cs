using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task PublicAsyncRelation_HoldsAdmissionThroughBufferedRows(bool cached, bool exhaust)
    {
        using var fixture = new AsyncRelationFixture();
        fixture.SetRows();
        using var transaction = fixture.Provider.StartTransaction();
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationParent)).Model.RelationProperties[nameof(AsyncRelationParent.Children)];
        var relation = new ImmutableRelation<AsyncRelationChild, int>(1, transaction, property);
        if (cached) await relation.ValuesAsync();
        await using var iterator = relation.AsAsyncEnumerable().GetAsyncEnumerator();
        // Capturing an unused iterator must not reserve the transaction.
        using (transaction.ExecutionGate.Enter("before first move")) { }
        await Assert.That(await iterator.MoveNextAsync()).IsTrue();
        await Assert.That(transaction.Commit).Throws<InvalidOperationException>();
        await Assert.That(() => transaction.CommitAsync()).Throws<InvalidOperationException>();
        await Assert.That(async () => { await relation.ValuesAsync(); }).Throws<InvalidOperationException>();
        if (exhaust) await Assert.That(await iterator.MoveNextAsync()).IsFalse();
        else await iterator.DisposeAsync();
        transaction.Commit();
        await Assert.That(fixture.Dispatches).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicAsyncRelation_BufferCancellationReleasesAdmission(bool enumeratorToken)
    {
        using var fixture = new AsyncRelationFixture();
        fixture.SetRows();
        using var transaction = fixture.Provider.StartTransaction();
        using var cancellation = new CancellationTokenSource();
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationParent)).Model.RelationProperties[nameof(AsyncRelationParent.Children)];
        var relation = new ImmutableRelation<AsyncRelationChild, int>(1, transaction, property);
        await using var iterator = relation.AsAsyncEnumerable(enumeratorToken ? default : cancellation.Token)
            .GetAsyncEnumerator(enumeratorToken ? cancellation.Token : default);
        await Assert.That(await iterator.MoveNextAsync()).IsTrue();
        cancellation.Cancel();
        await Assert.That(async () => { await iterator.MoveNextAsync(); }).Throws<OperationCanceledException>();
        // Purely local buffered cancellation must not invent uncertain native work.
        await relation.ValuesAsync();
        transaction.Rollback();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicAsyncRelation_HelperDrainsEscapedEnumerator(bool pendingLoad)
    {
        using var fixture = new AsyncRelationFixture();
        var access = fixture.SetRows(paused: pendingLoad);
        var transaction = fixture.Provider.StartTransaction();
        var property = fixture.Provider.Metadata.GetTableModel(typeof(AsyncRelationParent)).Model.RelationProperties[nameof(AsyncRelationParent.Children)];
        var relation = new ImmutableRelation<AsyncRelationChild, int>(1, transaction, property);
        var iterator = relation.AsAsyncEnumerable().GetAsyncEnumerator();
        var resource = new ControlledHelperTransaction { DisposingTransaction = transaction.DatabaseAccess.Dispose };
        Task<bool>? move = null;
        var helper = TransactionCallbackRunner.RunAsync(transaction.ExecutionGate, resource, new(), transaction.TransactionID,
            async token =>
            {
                move = iterator.MoveNextAsync().AsTask();
                if (pendingLoad) await access.Dispatch.Entered;
                else await Assert.That(await move).IsTrue();
                return 23;
            });
        try
        {
            if (pendingLoad)
            {
                await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.That(helper.IsCompleted).IsFalse();
                await Assert.That(resource.Calls).IsEmpty();
            }
        }
        finally { access.Dispatch.Release(); }
        await Assert.That(() => helper).Throws<InvalidOperationException>();
        await move!;
        await Assert.That(resource.Calls.Contains("commit")).IsFalse();
        await Assert.That(resource.Calls.Contains("rollback")).IsTrue();
        await iterator.DisposeAsync();
        await Assert.That(async () => { await iterator.MoveNextAsync(); }).Throws<InvalidOperationException>();
    }
}
