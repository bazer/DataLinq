using System;
using System.Threading.Tasks;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task PublicFluent_BorrowedRowsRejectAdvancementDisposalAndStaleGetters()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([1, "first"], [2, "second"]);
        var factory = RawFactory(() => new() { ReaderOverride = reader });
        fixture.Scenario.AsyncSqlReaders = factory;
        var sequence = transaction.From<TransactionMutationGuardRow>().SelectQuery().ReadReaderAsync();
        await Assert.That(factory.Accesses).IsEmpty();
        await using var rows = sequence.GetAsyncEnumerator();
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        IDataLinqAsyncDataReader first = rows.Current;
        await Assert.That(first.GetInt32(0)).IsEqualTo(1);
        _ = Capture<InvalidOperationException>(() => first.ReadNextRow());
        _ = Capture<InvalidOperationException>(() => first.ReadNextRowAsync());
        _ = Capture<InvalidOperationException>(first.Dispose);
        _ = Capture<InvalidOperationException>(() => first.DisposeAsync());
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        _ = Capture<InvalidOperationException>(() => first.GetInt32(0));
        var second = rows.Current;
        await Assert.That(second.GetInt32(0)).IsEqualTo(2);
        await rows.DisposeAsync();
        _ = Capture<InvalidOperationException>(() => second.GetString(1));
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    public async Task PublicFluent_ModelCastFailureSettlesOwnedResourcesInsideMaterializationBoundary()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([1, "stored"]);
        var factory = RawFactory(() => new() { ReaderOverride = reader });
        fixture.Scenario.AsyncSqlReaders = factory;
        await using var rows = transaction.From<TransactionMutationGuardRow>().Where("id").EqualTo(1)
            .SelectQuery().ExecuteAsAsync<string>().GetAsyncEnumerator();
        var failure = await AsyncEnumerationFailureOf(() => rows.MoveNextAsync().AsTask());
        await Assert.That(failure).IsTypeOf<InvalidCastException>();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(DataLinq.Diagnostics.DataLinqFailure.GetContext(failure)!.Stage)
            .IsEqualTo(DataLinq.Diagnostics.DataLinqFailureStage.RowLoading);
    }
}
