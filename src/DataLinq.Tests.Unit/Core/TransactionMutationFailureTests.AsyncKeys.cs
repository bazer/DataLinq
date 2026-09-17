using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task AsyncKeys_CaptureActualOrdinals_AndDecodeProviderIdentityWithoutModelRoundTrip()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([99, 42, "ignored"]) }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = fixture.Database.From<TransactionMutationGuardReferenceIdRow>().SelectQuery().What("99 AS extra", "id", "value");
        var table = select.Query.Table;
        var converter = (TransactionMutationGuardReferenceIdConverter)table.GetColumnByDbName("id").ScalarConverter!;
        converter.Reset();
        var sequence = select.ReadKeysAsyncCore();
        await using var keys = sequence.GetAsyncEnumerator();
        select.What("id");
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        await Assert.That(await keys.MoveNextAsync()).IsTrue();
        await Assert.That(keys.Current).IsEqualTo(DataLinqKey.FromValue(42));
        await Assert.That(converter.ToProviderValues).IsEmpty();
        await keys.DisposeAsync();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncKeys_BinaryIdentitySurvivesBufferReuseAndAnotherEnumeration()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var bytes = new byte[] { 1 };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([bytes]) },
            ConfigureCommand = command => command.Resource.Disposing = () => bytes[0]++
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var sequence = fixture.Database.From<TransactionMutationGuardBinaryRow>().SelectQuery().What("id").ReadKeysAsyncCore();
        await using var first = sequence.GetAsyncEnumerator();
        await Assert.That(await first.MoveNextAsync()).IsTrue();
        var key = first.Current;
        await first.DisposeAsync();
        await using var second = sequence.GetAsyncEnumerator();
        await Assert.That(await second.MoveNextAsync()).IsTrue();
        await Assert.That(second.Current).IsEqualTo(DataLinqKey.FromValue(new byte[] { 2 }));
        await second.DisposeAsync();
        await Assert.That(key).IsEqualTo(DataLinqKey.FromValue(new byte[] { 1 }));
        await Assert.That(factory.Commands.Count).IsEqualTo(2);
    }

    [Test]
    public async Task AsyncKeys_MissingSelectedKeyOrForeignTableIndexFailsBeforeCancellationAndDispatch()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = fixture.Database.From<TransactionMutationGuardRow>().SelectQuery().What("value");
        _ = Capture<InvalidOperationException>(() => select.ReadKeysAsyncCore(new(true)).GetAsyncEnumerator());
        select.What("id");
        var wrong = new ColumnIndex("wrong", IndexCharacteristic.Simple, IndexType.BTREE, [fixture.BinaryTable.Columns[0]]);
        _ = Capture<ArgumentException>(() => select.ReadPrimaryAndForeignKeysAsyncCore(wrong, new(true)).GetAsyncEnumerator());
        await Assert.That(factory.Inputs).IsEmpty();
    }

    [Test]
    public async Task AsyncKeyGroups_ReadAndCleanEverythingBeforeYield_WhileRetainingAdmission()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var reader = new ControlledRowDataReader([1, "a"], [2, "b"], [3, "a"]);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = reader },
            ConfigureCommand = command => command.Resource.Cleanup = new(paused: true)
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = transaction.From<TransactionMutationGuardRow>().SelectQuery();
        var index = new ColumnIndex("group", IndexCharacteristic.Simple, IndexType.BTREE, [fixture.RowTable.GetColumnByDbName("value")]);
        await using var groups = select.ReadPrimaryAndForeignKeysAsyncCore(index).GetAsyncEnumerator(cancellation.Token);
        select.What("value"); // Must not change the captured default projection.
        var pending = groups.MoveNextAsync().AsTask();
        var cleanup = factory.Commands[0].Resource.Cleanup;
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(reader.Moves).IsEqualTo(4);
            await Assert.That(reader.Disposals).IsEqualTo(1);
            _ = Capture<InvalidOperationException>(() => _ = groups.Current);
            _ = Capture<InvalidOperationException>(() => groups.MoveNextAsync());
            _ = Capture<InvalidOperationException>(() => groups.DisposeAsync());
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { cleanup.Release(); await pending; }
        await Assert.That(groups.Current.fk).IsEqualTo(DataLinqKey.FromValue("a"));
        await Assert.That(groups.Current.pks.SequenceEqual(new[] { DataLinqKey.FromValue(1), DataLinqKey.FromValue(3) })).IsTrue();
        await Assert.That(await groups.MoveNextAsync()).IsTrue();
        await Assert.That(groups.Current.fk).IsEqualTo(DataLinqKey.FromValue("b"));
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        cancellation.Cancel();
        await Assert.That(await AsyncEnumerationFailureOf(() => groups.MoveNextAsync().AsTask())).IsTypeOf<OperationCanceledException>();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncKeyGroups_CompositeAndNullableBinaryForeignKeysHaveStableIdentity(bool composite)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var id = new byte[] { 1 };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([id, DBNull.Value], [id, DBNull.Value]) },
            ConfigureCommand = command => command.Resource.Disposing = () => id[0] = 9
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var columns = composite ? fixture.BinaryTable.Columns.ToList() : [fixture.BinaryTable.GetColumnByDbName("payload")];
        var index = new ColumnIndex("group", IndexCharacteristic.Simple, IndexType.BTREE, columns);
        await using var groups = fixture.Database.From<TransactionMutationGuardBinaryRow>().SelectQuery()
            .ReadPrimaryAndForeignKeysAsyncCore(index).GetAsyncEnumerator();
        await Assert.That(await groups.MoveNextAsync()).IsTrue();
        await Assert.That(groups.Current.fk).IsEqualTo(composite ? DataLinqKey.FromValues([new byte[] { 1 }, null]) : DataLinqKey.Null);
        await Assert.That(groups.Current.pks.Length).IsEqualTo(2);
        await Assert.That(groups.Current.pks[0]).IsEqualTo(DataLinqKey.FromValue(new byte[] { 1 }));
        await Assert.That(await groups.MoveNextAsync()).IsFalse();
    }

    [Test]
    [Arguments("read")]
    [Arguments("cancel")]
    [Arguments("cleanup")]
    [Arguments("materialize")]
    public async Task AsyncKeyGroups_FailureNeverExposesAnIncompleteGroup(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var expected = new Exception(phase);
        var reader = new ControlledRowDataReader([1, "a"], [phase == "materialize" ? DBNull.Value : 2, "a"])
        {
            Advancing = call => { if (call == 2) { if (phase == "cancel") cancellation.Cancel(); if (phase == "read") throw expected; } }
        };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = reader },
            ConfigureCommand = command => { if (phase == "cleanup") command.Resource.Disposing = () => throw expected; }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var index = new ColumnIndex("group", IndexCharacteristic.Simple, IndexType.BTREE, [fixture.RowTable.GetColumnByDbName("value")]);
        await using var groups = transaction.From<TransactionMutationGuardRow>().SelectQuery()
            .ReadPrimaryAndForeignKeysAsyncCore(index, cancellation.Token).GetAsyncEnumerator();
        var failure = await AsyncEnumerationFailureOf(() => groups.MoveNextAsync().AsTask());
        if (phase is "read" or "cleanup") await Assert.That(failure).IsSameReferenceAs(expected);
        if (phase == "cancel") await Assert.That(failure).IsTypeOf<OperationCanceledException>();
        if (phase == "materialize") await Assert.That(ExecutionFailureContexts.Get(failure)!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        _ = Capture<InvalidOperationException>(() => _ = groups.Current);
        await Assert.That(await groups.MoveNextAsync()).IsFalse();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncKeyGroups_SharedColumnsTransferEachOwnedBinaryFieldOnce()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var reader = new OnceOwnedBinaryReader([new byte[] { 1 }, new byte[] { 2 }]);
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var index = new ColumnIndex("group", IndexCharacteristic.Simple, IndexType.BTREE, fixture.BinaryTable.Columns.ToList());
        await using var groups = fixture.Database.From<TransactionMutationGuardBinaryRow>().SelectQuery()
            .ReadPrimaryAndForeignKeysAsyncCore(index).GetAsyncEnumerator();
        await Assert.That(await groups.MoveNextAsync()).IsTrue();
        await Assert.That(reader.OwnedReads).IsEqualTo(2);
        await Assert.That(groups.Current.fk).IsEqualTo(DataLinqKey.FromValues([new byte[] { 1 }, new byte[] { 2 }]));
        await Assert.That(groups.Current.pks[0]).IsEqualTo(DataLinqKey.FromValue(new byte[] { 1 }));
    }

    [Test]
    [Arguments("unused")]
    [Arguments("canceled")]
    [Arguments("empty")]
    public async Task AsyncKeyGroups_EmptyUnusedAndPreCanceledLifetimes(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader();
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var index = new ColumnIndex("group", IndexCharacteristic.Simple, IndexType.BTREE, [fixture.RowTable.GetColumnByDbName("value")]);
        var sequence = transaction.From<TransactionMutationGuardRow>().SelectQuery().ReadPrimaryAndForeignKeysAsyncCore(index, new(mode == "canceled"));
        await Assert.That(factory.Inputs).IsEmpty();
        await using var groups = sequence.GetAsyncEnumerator();
        if (mode == "empty") await Assert.That(await groups.MoveNextAsync()).IsFalse();
        if (mode == "canceled") await Assert.That(await AsyncEnumerationFailureOf(() => groups.MoveNextAsync().AsTask())).IsTypeOf<OperationCanceledException>();
        await groups.DisposeAsync();
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(mode == "empty" ? 1 : 0);
        await Assert.That(reader.Disposals).IsEqualTo(mode == "empty" ? 1 : 0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncKeyGroups_HelperDrainsAnUnfinishedBufferedEnumerationWithoutCommitting()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, "a"]) } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var index = new ColumnIndex("group", IndexCharacteristic.Simple, IndexType.BTREE, [fixture.RowTable.GetColumnByDbName("value")]);
        await using var groups = transaction.From<TransactionMutationGuardRow>().SelectQuery().ReadPrimaryAndForeignKeysAsyncCore(index).GetAsyncEnumerator();
        var failure = await AsyncEnumerationFailureOf(() => transaction.RunCallbackAsyncCore(async _ =>
        {
            await groups.MoveNextAsync();
            return 7;
        }, new()));
        await Assert.That(failure).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await transaction.DisposeAsyncCore();
    }

    private sealed class OnceOwnedBinaryReader(params object?[][] rows) : ControlledRowDataReader(rows), IDataLinqOwnedBinaryBufferReader
    {
        private readonly HashSet<(int Row, int Ordinal)> transferred = [];
        internal int OwnedReads { get; private set; }
        public byte[]? TakeOwnedBytes(int ordinal)
        {
            if (!transferred.Add((Moves, ordinal))) throw new InvalidOperationException("A binary field was transferred twice.");
            OwnedReads++;
            return GetBytes(ordinal)?.ToArray();
        }
    }
}
