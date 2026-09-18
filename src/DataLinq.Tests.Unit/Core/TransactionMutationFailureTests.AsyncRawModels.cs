using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private static IAsyncEnumerable<T> RawModels<T>(DataSourceAccess source, bool borrowed, ControlledCommand command,
        CancellationToken token = default) where T : IModel => borrowed
            ? source.GetFromCommandAsyncCore<T>(command, token)
            : source.GetFromQueryAsyncCore<T>("SELECT value, id FROM supplied_source", token);

    private static ControlledSqlReaderFactory RawFactory(Func<ControlledAsyncDatabaseAccess> create) =>
        new() { CreateAccess = _ => create(), CreateBorrowedAccess = _ => create() };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRaw_ColdPerEnumerationCaptureMapsReorderedColumnsAndBorrowsCommand(bool borrowed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var command = new ControlledCommand { CommandText = "caller SQL", CommandTimeout = 37, CommandType = CommandType.Text };
        var firstReader = new ControlledRowDataReader(["first", 1]) { ColumnNames = ["value", "id"] };
        var factory = RawFactory(() => new() { ReaderOverride = firstReader });
        fixture.Scenario.AsyncSqlReaders = factory;
        var sequence = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command);
        await Assert.That(factory.Accesses).IsEmpty();
        var first = sequence.GetAsyncEnumerator();
        await Assert.That(factory.Accesses.Count).IsEqualTo(1);
        await Assert.That(factory.Accesses[0].Calls).IsEmpty();
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(0);
        var nextReader = new ControlledRowDataReader([2, "second"]) { ColumnNames = ["id", "value"] };
        var next = RawFactory(() => new() { ReaderOverride = nextReader });
        fixture.Scenario.AsyncSqlReaders = next;
        try
        {
            await Assert.That(await first.MoveNextAsync()).IsTrue();
            await Assert.That(first.Current.Id).IsEqualTo(1);
            await Assert.That(first.Current.Value).IsEqualTo("first");
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { await first.DisposeAsync(); }
        await using var second = sequence.GetAsyncEnumerator();
        await Assert.That(await second.MoveNextAsync()).IsTrue();
        await Assert.That(second.Current.Id).IsEqualTo(2);
        await Assert.That(second.Current.Value).IsEqualTo("second");
        await second.DisposeAsync();
        await Assert.That(firstReader.Disposals).IsEqualTo(1);
        await Assert.That(nextReader.Disposals).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(command.SyncExecutionCalls).IsEqualTo(0);
        await Assert.That(command.CommandText).IsEqualTo("caller SQL");
        await Assert.That(command.CommandTimeout).IsEqualTo(37);
        await Assert.That(command.CommandType).IsEqualTo(CommandType.Text);
        if (borrowed)
        {
            await Assert.That(factory.BorrowedCommands.Single()).IsSameReferenceAs(command);
            await Assert.That(factory.Accesses[0].ObservedCommand).IsSameReferenceAs(command);
            await Assert.That(next.Accesses[0].ObservedCommand).IsSameReferenceAs(command);
            await Assert.That(factory.Commands).IsEmpty();
        }
        else await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRaw_ReorderedBinaryRowsOwnValuesAfterProviderBufferReuse(bool borrowed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var key = new byte[] { 1 };
        var payload = new byte[] { 7 };
        var reader = new ControlledRowDataReader([payload, key]) { ColumnNames = ["payload", "id"] };
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new() { ReaderOverride = reader });
        await using var rows = RawModels<TransactionMutationGuardBinaryRow>(fixture.Provider.ReadOnlyAccess, borrowed, new()).GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var row = rows.Current;
        key[0] = 99;
        payload[0] = 99;
        await rows.DisposeAsync();
        await Assert.That(row.Id).IsEquivalentTo(new byte[] { 1 });
        await Assert.That(row.Payload!).IsEquivalentTo(new byte[] { 7 });
    }

    [Test]
    public async Task AsyncRaw_ConvertedValuesMaterializeOnceWithoutProviderRoundtrip()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var converter = new TransactionMutationGuardReferenceIdConverter.Observation();
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new()
            { ReaderOverride = new ControlledRowDataReader(["value", 42]) { ColumnNames = ["value", "id"] } });
        await using var rows = fixture.Provider.ReadOnlyAccess.GetFromQueryAsyncCore<TransactionMutationGuardReferenceIdRow>("SELECT value, id").GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current.Id.Value).IsEqualTo(42);
        await Assert.That(converter.ToProviderValues).IsEmpty();
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRaw_ConstructorKeepsOwnerButNeverPublishesRawResultsInTheRowCache(bool fail)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        using var transaction = provider.StartTransaction();
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table);
        var reader = new ControlledRowDataReader([1, "value"]) { ColumnNames = ["id", "value"] };
        scenario.AsyncSqlReaders = RawFactory(() => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead });
        var expected = new Exception("raw constructor");
        var constructed = 0;
        AsyncCanonicalCachedRow.Creating.Value = () =>
        {
            constructed++;
            _ = Capture<InvalidOperationException>(() => DataSourceAccess.EnsureReadAllowed(transaction, "reentry"));
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            if (fail) throw expected;
        };
        try
        {
            await using var rows = transaction.GetFromQueryAsyncCore<AsyncCanonicalCachedRow>("SELECT id, value").GetAsyncEnumerator();
            if (fail) await Assert.That(await AsyncEnumerationFailureOf(async () => { await rows.MoveNextAsync(); })).IsSameReferenceAs(expected);
            else await Assert.That(await rows.MoveNextAsync()).IsTrue();
            await rows.DisposeAsync();
            await Assert.That(constructed).IsEqualTo(1);
            await Assert.That(cache.TryGetMaterializedRow(DataLinqKey.FromValue(1), transaction, out _)).IsFalse();
            await Assert.That(cache.RowCount).IsEqualTo(0);
            DataSourceAccess.EnsureReadAllowed(transaction, "next read");
        }
        finally { AsyncCanonicalCachedRow.Creating.Value = null; }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRaw_BothTokensCancelLaterRowsWithoutUndoingYieldAndCleanupRetainsOwner(bool borrowed, bool cancelMethod)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var method = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var reader = new ControlledRowDataReader([1, "first"], [2, "second"])
            { ColumnNames = ["id", "value"], Cleanup = new(paused: true) };
        var factory = RawFactory(() => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead });
        fixture.Scenario.AsyncSqlReaders = factory;
        var command = new ControlledCommand();
        await using var rows = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command, method.Token).GetAsyncEnumerator(enumeration.Token);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var delivered = rows.Current;
        (cancelMethod ? method : enumeration).Cancel();
        var pending = rows.MoveNextAsync().AsTask();
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(reader.Cleanup.ObservedToken.CanBeCanceled).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
        }
        finally { reader.Cleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<OperationCanceledException>();
        await Assert.That(delivered.Value).IsEqualTo("first");
        await Assert.That(reader.Moves).IsEqualTo(1);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(factory.Commands.All(x => x.Resource.AsyncDisposals == 1)).IsTrue();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRaw_ValidationPrecedesCancellationAndNeitherDispatches(bool borrowed, bool unsupported)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = RawFactory(() => new() { UnsupportedKind = unsupported ? AsyncCommandKind.Reader : null });
        factory.ConfigureCommand = c => { if (unsupported) c.ValidationFailure = new NotSupportedException("raw read"); };
        fixture.Scenario.AsyncSqlReaders = factory;
        var command = new ControlledCommand();
        await using var rows = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command, new(true)).GetAsyncEnumerator();
        var error = await AsyncEnumerationFailureOf(async () => { await rows.MoveNextAsync(); });
        await Assert.That(unsupported ? error is NotSupportedException : error is OperationCanceledException).IsTrue();
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(0);
        await Assert.That(factory.Accesses.All(x => x.ObservedCommand is null)).IsTrue();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRaw_MissingColumnAndCleanupFailurePreservePrimaryAndBorrowedCommand(bool borrowed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([1]) { ColumnNames = ["id"], Cleanup = new(paused: true) };
        var cleanup = new Exception("reader cleanup");
        var factory = RawFactory(() => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead });
        fixture.Scenario.AsyncSqlReaders = factory;
        var command = new ControlledCommand();
        await using var rows = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command).GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        reader.Cleanup.Fail(cleanup);
        var error = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(error).IsTypeOf<IndexOutOfRangeException>();
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        await Assert.That(transaction.AsyncFailureContext.SecondaryFailures.Single().Exception).IsSameReferenceAs(cleanup);
        await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsFalse();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands.All(x => x.Resource.AsyncDisposals == 1)).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncRaw_LazyInitializationKeepsOwnerAndFailureIsTerminal(bool borrowed, bool fail)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Open = new(paused: true), Cleanup = new(paused: true) };
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = RawFactory(() => new()
            { ReaderOverride = new ControlledRowDataReader([1, "value"]) { ColumnNames = ["id", "value"] }, FailureEvidence = TrustedScalarRead });
        factory.WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source);
        fixture.Scenario.AsyncSqlReaders = factory;
        var command = new ControlledCommand();
        await using var rows = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command).GetAsyncEnumerator();
        var pending = rows.MoveNextAsync().AsTask();
        await resource.Open.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        await Assert.That(factory.Accesses.All(x => x.ObservedCommand is null)).IsTrue();
        var expected = new Exception("raw initialization");
        if (fail)
        {
            resource.Open.Fail(expected);
            await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(pending.IsCompleted).IsFalse();
            resource.Cleanup.Release();
            await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        else
        {
            resource.Open.Release();
            await Assert.That(await pending).IsTrue();
            await rows.DisposeAsync();
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
            resource.Cleanup.Release();
        }
        await transaction.DisposeAsyncCore();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRaw_HelperDrainsEscapedReaderBeforeRejectingCallbackSuccess(bool borrowed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var reader = new ControlledRowDataReader([1, "value"]) { ColumnNames = ["id", "value"], Cleanup = new(paused: true) };
        fixture.Scenario.AsyncSqlReaders = RawFactory(() => new() { ReaderOverride = reader });
        var command = new ControlledCommand();
        IAsyncEnumerator<TransactionMutationGuardRow>? escaped = null;
        var helper = transaction.RunCallbackAsyncCore(async _ =>
        {
            escaped = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command).GetAsyncEnumerator();
            await escaped.MoveNextAsync();
            return 9;
        }, new());
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(helper.IsCompleted).IsFalse();
        reader.Cleanup.Release();
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        await escaped!.DisposeAsync();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncRaw_KeylessViewsMapByNameWithoutRequiringAPrimaryKey()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        scenario.AsyncSqlReaders = RawFactory(() => new()
            { ReaderOverride = new ControlledRowDataReader([7, 99, "value"]) { ColumnNames = ["number", "extra", "value"] } });
        await using var rows = provider.ReadOnlyAccess.GetFromQueryAsyncCore<AsyncModelKeylessRow>("SELECT number, extra, value").GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current.Number).IsEqualTo(7);
        await Assert.That(rows.Current.Value).IsEqualTo("value");
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncRaw_ReturningRowsDoesNotEstablishHarmlessFailureEvidence(bool borrowed)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([1, "first"]) { ColumnNames = ["id", "value"] };
        var factory = RawFactory(() => new() { ReaderOverride = reader }); // Deliberately unknown statement effects.
        fixture.Scenario.AsyncSqlReaders = factory;
        var command = new ControlledCommand();
        await using var rows = RawModels<TransactionMutationGuardRow>(transaction, borrowed, command).GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var expected = new Exception("raw advancement");
        reader.Advance = new(paused: true);
        reader.Advance.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(async () => { await rows.MoveNextAsync(); })).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }
}
