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
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    public async Task AsyncCanonical_BatchCapturesBorrowedKeysAndFactoryBeforeSuspension()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([2, "second"], [1, "first"]) { Cleanup = new(paused: true) };
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ReaderOverride = reader };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        var keys = new[] { DataLinqKey.FromValue(1), DataLinqKey.FromValue(2) };
        var request = SourcePrimaryKeyRowRequest.Borrow(fixture.RowTable, keys, 0, keys.Length);
        var pending = new DataSourceAccessSourceRowLoader(transaction).LoadAsync(request);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var capturedSql = factory.Inputs[0].Text;
        keys[0] = DataLinqKey.FromValue(77);
        keys[1] = DataLinqKey.FromValue(88);
        var replacement = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = replacement;
        access.Dispatch.Release();
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            await Assert.That(access.ObservedCommand!.CommandText).IsEqualTo(capturedSql);
            await Assert.That(factory.Inputs[0].ToSql().Parameters.Select(x => x.Value).ToArray()).IsEquivalentTo(new object?[] { 1, 2 });
        }
        finally { reader.Cleanup.Release(); }
        var result = await pending;
        await Assert.That(result.Request).IsSameReferenceAs(request);
        await Assert.That(result.Rows.Select(x => (int)x.CanonicalProviderKey.GetValue(0)!).ToArray()).IsEquivalentTo(new[] { 2, 1 });
        await Assert.That(replacement.Inputs).IsEmpty();
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    [Arguments("single", "empty")]
    [Arguments("single", "valid")]
    [Arguments("single", "duplicate")]
    [Arguments("single", "wrong-key")]
    [Arguments("single", "bad-value")]
    [Arguments("batch", "duplicate")]
    [Arguments("batch", "wrong-key")]
    [Arguments("index", "empty")]
    [Arguments("index", "valid")]
    [Arguments("index", "duplicate")]
    public async Task AsyncCanonical_ValidatesRowsBeforeReturningOwnedResults(string shape, string outcome)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        object?[][] values = outcome switch
        {
            "empty" => [],
            "duplicate" => [[1, "value"], [1, "value"]],
            "wrong-key" => [[99, "value"]],
            "bad-value" => [[1, null]],
            _ => [[1, "value"]]
        };
        var reader = new ControlledRowDataReader(values);
        var access = new ControlledAsyncDatabaseAccess { ReaderOverride = reader, FailureEvidence = TrustedScalarRead };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        var loader = new DataSourceAccessSourceRowLoader(transaction);
        var key = DataLinqKey.FromValue(1);
        var index = fixture.RowTable.ColumnIndices.Single(x => x.Columns.Count == 1 && x.Columns[0].DbName == "value");
        Task pending = shape switch
        {
            "batch" => loader.LoadAsync(new SourcePrimaryKeyRowRequest(fixture.RowTable, [key])),
            "index" => loader.LoadAsync(new SourceIndexRowRequest(fixture.RowTable, index, DataLinqKey.FromValue("value"))),
            _ => loader.LoadSingleAsync(fixture.RowTable, key)
        };
        if (outcome is "empty" or "valid")
        {
            await pending;
            if (pending is Task<CanonicalProviderValueRow?> single)
                await Assert.That(await single is null).IsEqualTo(outcome == "empty");
            if (pending is Task<SourceIndexRowLoadResult> indexed)
                await Assert.That((await indexed).Rows.Length).IsEqualTo(outcome == "empty" ? 0 : 1);
        }
        else
        {
            _ = await AsyncEnumerationFailureOf(() => pending);
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
            await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        }
        await Assert.That(reader.Disposals).IsEqualTo(1);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    [Arguments("dispatch")]
    [Arguments("advance")]
    [Arguments("reader-cleanup")]
    [Arguments("command-cleanup")]
    public async Task AsyncCanonical_CancellationSettlesCleanupWithoutPublishing(string phase)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var reader = new ControlledRowDataReader([1, "value"])
        {
            Advance = new(paused: phase == "advance"),
            Cleanup = new(paused: true)
        };
        var access = new ControlledAsyncDatabaseAccess(new(paused: phase == "dispatch"))
            { ReaderOverride = reader, FailureEvidence = TrustedScalarRead };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            ConfigureCommand = command => command.Resource.Cleanup = new(paused: true)
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = fixture.RowCache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), transaction, cancellation.Token);
        var commandCleanup = factory.Commands[0].Resource.Cleanup;
        var checkpoint = phase switch
        {
            "dispatch" => access.Dispatch,
            "advance" => reader.Advance,
            "reader-cleanup" => reader.Cleanup,
            _ => commandCleanup
        };
        if (phase == "command-cleanup") reader.Cleanup.Release();
        await checkpoint.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        if (phase != "dispatch")
        {
            await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(reader.Cleanup.ObservedToken.CanBeCanceled).IsFalse();
            reader.Cleanup.Release();
        }
        await commandCleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await Assert.That(commandCleanup.ObservedToken.CanBeCanceled).IsFalse();
            await Assert.That(fixture.RowCache.RowCount).IsEqualTo(0);
        }
        finally { commandCleanup.Release(); }
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<OperationCanceledException>();
        await Assert.That(fixture.RowCache.RowCount).IsEqualTo(0);
        await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
        await Assert.That(reader.Disposals).IsEqualTo(phase == "dispatch" ? 0 : 1);
        await Assert.That(fixture.RowCache.TryGetMaterializedRow(DataLinqKey.FromValue(1), transaction, out _)).IsFalse();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCanonical_CacheGenerationPreventsStalePublicationAcrossCleanup(bool publishNewer)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table);
        var reader = new ControlledRowDataReader([1, "old"]) { Cleanup = new(paused: true) };
        var count = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = count++ == 0 ? reader : new ControlledRowDataReader([1, "new"]) }
        };
        scenario.AsyncSqlReaders = factory;
        var source = provider.ReadOnlyAccess;
        var pending = cache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), source);
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(cache.RowCount).IsEqualTo(0);
            cache.ClearRows();
            if (publishNewer)
            {
                var newer = (AsyncCanonicalCachedRow)(await cache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), source))!;
                await Assert.That(newer.Value).IsEqualTo("new");
            }
        }
        finally { reader.Cleanup.Release(); }
        var old = (AsyncCanonicalCachedRow)(await pending)!;
        await Assert.That(old.Value).IsEqualTo("old");
        await Assert.That(cache.RowCount).IsEqualTo(publishNewer ? 1 : 0);
        var current = (AsyncCanonicalCachedRow)(await cache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), source))!;
        await Assert.That(current.Value).IsEqualTo("new");
        await Assert.That(factory.Commands.Count(x => x.Creates != 0)).IsEqualTo(2);
    }

    [Test]
    public async Task AsyncCanonical_CacheHitStillRejectsTransactionOverlapAndCancellation()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, "value"]) } };
        var key = DataLinqKey.FromValue(1);
        var first = await fixture.RowCache.GetCanonicalRowAsyncCore(key, transaction);
        await Assert.That(await fixture.RowCache.GetCanonicalRowAsyncCore(key, transaction)).IsSameReferenceAs(first);
        await Assert.That(await AsyncEnumerationFailureOf(() => fixture.RowCache.GetCanonicalRowAsyncCore(key, transaction, new(true))))
            .IsTypeOf<OperationCanceledException>();
        using (var active = transaction.ExecutionGate.Enter("test overlap"))
            await Assert.That(await AsyncEnumerationFailureOf(() => fixture.RowCache.GetCanonicalRowAsyncCore(key, transaction, new(true))))
                .IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.RowCache.RowCount).IsEqualTo(0); // Transaction rows are never committed rows.
        await Assert.That(transaction.AsyncFailureContext).IsNull();
    }

    [Test]
    public async Task AsyncCanonical_PostCleanupMaterializationRemainsOwnedAndPreservesFailure()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader([1, "value"]);
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var expected = new Exception("local materialization");
        var read = new DataSourceAccessSourceRowLoader(transaction).CaptureSingleAsyncRead(fixture.RowTable, DataLinqKey.FromValue(1));
        var observedClosed = false;
        var pending = read.ExecuteAsync<int>((row, owner, token) =>
        {
            observedClosed = reader.Disposals == 1 && factory.Commands[0].Resource.AsyncDisposals == 1;
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            throw expected;
        }, default);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(observedClosed).IsTrue();
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCanonical_PreCancellationAndCapabilityValidationDoNotInitialize(bool unsupported)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource();
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledSqlReaderFactory
        {
            WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source),
            ConfigureCommand = command => { if (unsupported) command.ValidationFailure = new NotSupportedException("canonical read"); }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var error = await AsyncEnumerationFailureOf(() => fixture.RowCache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), transaction, new(true)));
        await Assert.That(unsupported ? error is NotSupportedException : error is OperationCanceledException).IsTrue();
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Unused);
        await Assert.That(resource.Calls).IsEmpty();
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCanonical_PrimaryAndOrderedCleanupFailuresRemainAttachedBeforeAdmissionRelease(bool sameFailure)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var primary = new Exception("advance");
        var readerCleanup = sameFailure ? primary : new Exception("reader cleanup");
        var commandCleanup = sameFailure ? primary : new Exception("command cleanup");
        var reader = new ControlledRowDataReader([1, "value"]) { Advance = new(paused: true), Cleanup = new(paused: true) };
        reader.Advance.Fail(primary);
        reader.Cleanup.Fail(readerCleanup);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = sql => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead,
                AssessingFailure = () => { _ = Capture<InvalidOperationException>(() => transaction.Query()); } },
            ConfigureCommand = command => { command.Resource.Cleanup = new(paused: true); command.Resource.Cleanup.Fail(commandCleanup); }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = fixture.RowCache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), transaction);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(primary);
        var context = transaction.AsyncFailureContext!;
        await Assert.That(context.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(context.SecondaryFailures.Count).IsEqualTo(sameFailure ? 0 : 2);
        if (!sameFailure)
        {
            await Assert.That(context.SecondaryFailures[0].Exception).IsSameReferenceAs(readerCleanup);
            await Assert.That(context.SecondaryFailures[1].Exception).IsSameReferenceAs(commandCleanup);
        }
        await Assert.That(context.HasCleanupFailure).IsTrue();
        await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        await Assert.That(fixture.RowCache.RowCount).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    [Test]
    public async Task AsyncCanonical_HelperWaitsForUnfinishedBufferedReadAndRejectsCallbackSuccess()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var reader = new ControlledRowDataReader([1, "value"]) { Cleanup = new(paused: true) };
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        Task<CanonicalProviderValueRow?>? loading = null;
        var helper = transaction.RunCallbackAsyncCore(_ =>
        {
            loading = new DataSourceAccessSourceRowLoader(transaction).LoadSingleAsync(fixture.RowTable, DataLinqKey.FromValue(1));
            return Task.FromResult(9);
        }, new());
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(helper.IsCompleted).IsFalse();
        reader.Cleanup.Release();
        await loading!;
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCanonical_ModelConstructorIsOwnedAndFailurePreventsPublication(bool fail)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        using var transaction = provider.StartTransaction();
        var table = provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table;
        var cache = provider.GetTableCache(table);
        var reader = new ControlledRowDataReader([1, "value"]) { Cleanup = new(paused: true) };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead } };
        scenario.AsyncSqlReaders = factory;
        var expected = new Exception("model constructor");
        var calls = 0;
        AsyncCanonicalCachedRow.Creating.Value = () =>
        {
            calls++;
            if (reader.Disposals != 1 || factory.Commands[0].Resource.AsyncDisposals != 1)
                throw new Exception("Native resources outlived canonical row buffering.");
            _ = Capture<InvalidOperationException>(() => DataSourceAccess.EnsureReadAllowed(transaction, "reentrant read"));
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            if (fail) throw expected;
        };
        try
        {
            var key = DataLinqKey.FromValue(1);
            var pending = cache.GetCanonicalRowAsyncCore(key, transaction);
            await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            reader.Cleanup.Release();
            if (fail)
            {
                await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
                await Assert.That(cache.TryGetMaterializedRow(key, transaction, out _)).IsFalse();
                await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
            }
            else
            {
                var result = await pending;
                await Assert.That(cache.TryGetMaterializedRow(key, transaction, out var cached)).IsTrue();
                await Assert.That(cached).IsSameReferenceAs(result);
                await Assert.That(cache.RowCount).IsEqualTo(0);
            }
            await Assert.That(calls).IsEqualTo(1);
            DataSourceAccess.EnsureReadAllowed(transaction, "next read");
        }
        finally { AsyncCanonicalCachedRow.Creating.Value = null; reader.Cleanup.Release(); }
    }

    [Test]
    public async Task AsyncCanonical_PrivateNestedOwnerCannotBeReusedByAnotherTransaction()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var other = fixture.Database.Transaction();
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, "value"]) } };
        using (var scope = DataSourceAccess.BeginRead(transaction, "outer read"))
        {
            var key = DataLinqKey.FromValue(1);
            var loaded = await new DataSourceAccessSourceRowLoader(transaction, scope!.Step).LoadSingleAsync(fixture.RowTable, key);
            await Assert.That(loaded).IsNotNull();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            await Assert.That(await AsyncEnumerationFailureOf(() => new DataSourceAccessSourceRowLoader(other, scope.Step)
                .LoadSingleAsync(fixture.RowTable, key))).IsTypeOf<InvalidOperationException>();
        }
        _ = transaction.Query();
        _ = other.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncCanonical_InitializationUsesTheSameOwnerAndFailureIsTerminal(bool fail)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Open = new(paused: true), Cleanup = new(paused: true) };
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledSqlReaderFactory
        {
            WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source),
            CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, "value"]), FailureEvidence = TrustedScalarRead }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = fixture.RowCache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), transaction);
        await resource.Open.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        var expected = new Exception("opening failed");
        if (fail)
        {
            resource.Open.Fail(expected);
            await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(pending.IsCompleted).IsFalse();
            resource.Cleanup.Release();
            await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
            await Assert.That(transaction.AsyncFailureContext.Recovery).IsEqualTo(ExecutionRecoveryActions.Dispose);
        }
        else
        {
            resource.Open.Release();
            await Assert.That(await pending).IsNotNull();
            await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Ready);
            await Assert.That(factory.Commands[0].Creates).IsEqualTo(1);
            resource.Cleanup.Release();
        }
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments("key-type")]
    [Arguments("missing-capability")]
    [Arguments("unsupported-key")]
    public async Task AsyncCanonical_InvalidInputsAndUnsupportedCapabilitiesPrecedeCancellation(string invalid)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory();
        if (invalid != "missing-capability") fixture.Scenario.AsyncSqlReaders = factory;
        // Even a warm row cannot hide a missing asynchronous capability.
        fixture.PrimeCommittedRow(1, "cached");
        var source = invalid == "missing-capability" ? fixture.Provider.ReadOnlyAccess : (DataSourceAccess)transaction;
        var key = invalid == "key-type" ? DataLinqKey.FromValue("bad") : DataLinqKey.FromValue(1);
        var cache = invalid == "unsupported-key" ? fixture.Provider.GetTableCache(fixture.BinaryTable) : fixture.RowCache;
        var error = await AsyncEnumerationFailureOf(() => cache.GetCanonicalRowAsyncCore(key, source, new(true)));
        await Assert.That(invalid == "key-type" ? error is ArgumentException : error is NotSupportedException).IsTrue();
        await Assert.That(factory.Inputs).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncCanonical_ModelConstructorInvalidationPreventsPublication()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        var table = provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table;
        var cache = provider.GetTableCache(table);
        var reader = new ControlledRowDataReader([1, "value"]) { Cleanup = new(paused: true) };
        scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        AsyncCanonicalCachedRow.Creating.Value = cache.ClearRows;
        try
        {
            var pending = cache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), provider.ReadOnlyAccess);
            await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            reader.Cleanup.Release();
            await Assert.That(await pending).IsNotNull();
            await Assert.That(cache.RowCount).IsEqualTo(0);
        }
        finally { AsyncCanonicalCachedRow.Creating.Value = null; reader.Cleanup.Release(); }
    }
}

[Database("async_canonical_cached")]
public sealed partial class AsyncCanonicalCachedDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<AsyncCanonicalCachedRow> Rows { get; } = new(source);
}

[Table("async_canonical_cached_rows")]
[UseCache]
public abstract partial class AsyncCanonicalCachedRow : Immutable<AsyncCanonicalCachedRow, AsyncCanonicalCachedDb>, ITableModel<AsyncCanonicalCachedDb>
{
    protected AsyncCanonicalCachedRow(IRowData rowData, IDataSourceAccess source) : base(rowData, source) => Creating.Value?.Invoke();
    internal static AsyncLocal<Action?> Creating { get; } = new();

    [PrimaryKey, Column("id"), Type(DatabaseType.SQLite, "integer")]
    public abstract int Id { get; }

    [Column("value"), Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
}
