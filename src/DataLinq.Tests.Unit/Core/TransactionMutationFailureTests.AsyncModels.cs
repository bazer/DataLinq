using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
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
    public async Task AsyncModels_CacheHitsMissingRowsAndDuplicatesKeepDatabaseOrderWithoutEditingProjection()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.PrimeCommittedRow(2, "cached");
        var keyReader = new ControlledRowDataReader([3], [1], [2], [3], [9]) { Cleanup = new(paused: true) };
        var rowReader = new ControlledRowDataReader([1, "one"], [3, "three"]) { Cleanup = new(paused: true) };
        var accesses = 0;
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = accesses++ == 0 ? keyReader : rowReader } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = fixture.Database.From<TransactionMutationGuardRow>().OrderBy("id", ascending: false).Limit(5).SelectQuery().What("value");
        var originalSql = select.ToSql().Text;
        var pending = select.ExecuteBufferedAsyncCore();
        await keyReader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(factory.Inputs.Count).IsEqualTo(1);
        await Assert.That(pending.IsCompleted).IsFalse();
        await Assert.That(select.ToSql().Text).IsEqualTo(originalSql);
        await Assert.That(factory.Inputs[0].Text).Contains("ORDER BY");
        await Assert.That(factory.Inputs[0].Text).Contains("LIMIT 5");
        keyReader.Cleanup.Release();
        await rowReader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(factory.Commands[0].Resource.AsyncDisposals).IsEqualTo(1);
            await Assert.That(factory.Inputs[1].ToSql().Parameters.Select(x => (int)x.Value!).ToArray()).IsEquivalentTo(new[] { 3, 1, 9 });
        }
        finally { rowReader.Cleanup.Release(); }
        var result = (await pending).Cast<TransactionMutationGuardRow>().ToArray();
        await Assert.That(string.Join(",", result.Select(x => x.Id))).IsEqualTo("3,1,2,3");
        await Assert.That(result[2].Value).IsEqualTo("cached");
        await Assert.That(result[0]).IsSameReferenceAs(result[3]);
        await Assert.That(select.ToSql().Text).IsEqualTo(originalSql);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncModels_CaptureIsColdAndPerInvocation_WithFactoryFixedAcrossStages(bool terminal)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var calls = 0;
        var firstAccess = new ControlledAsyncDatabaseAccess(new(paused: true)) { ReaderOverride = new ControlledRowDataReader([1]) };
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => calls++ % 2 == 0 ? firstAccess : new() { ReaderOverride = new ControlledRowDataReader([1, "value"]) } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var query = fixture.Database.From<TransactionMutationGuardRow>();
        query.Where("id").EqualTo(1);
        query.Where("value").EqualTo("value"); // Exercise both stages, not the direct-key shortcut.
        var select = query.SelectQuery().What("value");
        var sequence = select.ExecuteAsyncCore();
        await Assert.That(factory.Inputs).IsEmpty();
        IAsyncEnumerator<IImmutableInstance>? iterator = null;
        Task pending;
        if (terminal) pending = select.ExecuteBufferedAsyncCore();
        else
        {
            iterator = sequence.GetAsyncEnumerator();
            await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
            pending = iterator.MoveNextAsync().AsTask();
        }
        await firstAccess.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var replacement = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = replacement;
        factory.CreateAccess = _ => throw new Exception("later factory policy");
        factory.ConfigureCommand = _ => throw new Exception("later command policy");
        select.What("id");
        query.Where("id").EqualTo(99);
        firstAccess.Dispatch.Release();
        await pending;
        if (iterator is not null) await iterator.DisposeAsync();
        await Assert.That(factory.Inputs.Count).IsEqualTo(2);
        await Assert.That(factory.Inputs[0].ToSql().Parameters.Count).IsEqualTo(2);
        await Assert.That(replacement.Inputs).IsEmpty();
        replacement.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader() };
        await using var repeat = sequence.GetAsyncEnumerator();
        await Assert.That(replacement.Inputs[0].ToSql().Parameters.Count).IsEqualTo(3);
        await Assert.That(await repeat.MoveNextAsync()).IsFalse();
    }

    [Test]
    [Arguments("success")]
    [Arguments("cancel")]
    [Arguments("failure")]
    public async Task AsyncModels_FiniteBatchesAreBoundBeforeSuspension_AndNeverReturnAPartialCollection(string outcome)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var keyReader = new ControlledRowDataReader(Enumerable.Range(1, 1001).Select(x => new object?[] { x }).ToArray());
        var secondRows = new ControlledRowDataReader() { Advance = new(paused: true) };
        var created = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = sql =>
            {
                var index = created++;
                var rows = sql.ToSql().Parameters.Select(x => new object?[] { (int)x.Value!, "value" }).ToArray();
                if (index == 2) secondRows = new ControlledRowDataReader(rows) { Advance = new(paused: true) };
                return new() { ReaderOverride = index == 0 ? keyReader : index == 2 ? secondRows : new ControlledRowDataReader(rows), FailureEvidence = TrustedScalarRead };
            }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteBufferedAsyncCore(cancellation.Token);
        await secondRows.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(factory.Inputs.Count).IsEqualTo(4);
        await Assert.That(string.Join(",", factory.Inputs.Skip(1).Select(x => x.ToSql().Parameters.Count))).IsEqualTo("500,500,1");
        await Assert.That(factory.Commands[3].Creates).IsEqualTo(0);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        _ = Capture<InvalidOperationException>(() => fixture.RowCache.TryGetMaterializedRow(DataLinqKey.FromValue(1), transaction, out _));
        if (outcome == "cancel") cancellation.Cancel();
        else if (outcome == "failure") secondRows.Advance.Fail(new Exception("second batch"));
        else secondRows.Advance.Release();
        if (outcome == "success")
        {
            var result = await pending;
            await Assert.That(result.Count).IsEqualTo(1001);
            await Assert.That(((TransactionMutationGuardRow)result[500]).Id).IsEqualTo(501);
        }
        else
        {
            var error = await AsyncEnumerationFailureOf(() => pending);
            await Assert.That(outcome == "cancel" ? error is OperationCanceledException : error.Message == "second batch").IsTrue();
            await Assert.That(factory.Commands[3].Creates).IsEqualTo(0);
            await Assert.That(fixture.RowCache.TryGetMaterializedRow(DataLinqKey.FromValue(1), transaction, out _)).IsTrue();
            await Assert.That(fixture.RowCache.TryGetMaterializedRow(DataLinqKey.FromValue(501), transaction, out _)).IsFalse();
        }
        await Assert.That(fixture.RowCache.RowCount).IsEqualTo(0);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncModels_ChildFailureKeepsItsOwnRecoveryAssessment(bool childTrusted)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var expected = new Exception("child row advance");
        var child = new ControlledRowDataReader([1, "value"]) { Advance = new(paused: true) };
        child.Advance.Fail(expected);
        var parentAccess = new ControlledAsyncDatabaseAccess { ReaderOverride = new ControlledRowDataReader([1]), FailureEvidence = childTrusted ? new() : TrustedScalarRead };
        var childAccess = new ControlledAsyncDatabaseAccess { ReaderOverride = child, FailureEvidence = childTrusted ? TrustedScalarRead : new() };
        var calls = 0;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateAccess = _ => calls++ == 0 ? parentAccess : childAccess };
        var pending = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteBufferedAsyncCore();
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsEqualTo(childTrusted);
        await Assert.That(transaction.AsyncFailureContext.Stage).IsEqualTo(ExecutionFailureStage.RowLoading);
        await Assert.That(parentAccess.Calls.Contains("assess-failure")).IsFalse();
        await Assert.That(childAccess.Calls.Count(x => x == "assess-failure")).IsEqualTo(1);
        if (childTrusted) _ = transaction.Query();
        else _ = Capture<InvalidOperationException>(() => transaction.Query());
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncModels_BothTokensApplyThroughBufferedYieldAndAdmission(bool cancelMethod)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var method = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var call = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = call++ == 0 ? new ControlledRowDataReader([2], [1]) : new ControlledRowDataReader([1, "one"], [2, "two"]), FailureEvidence = TrustedScalarRead }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        await using var rows = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteAsyncCore(method.Token).GetAsyncEnumerator(enumeration.Token);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(((TransactionMutationGuardRow)rows.Current).Id).IsEqualTo(2);
        await Assert.That(factory.Commands.All(x => x.Resource.AsyncDisposals == 1)).IsTrue();
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        (cancelMethod ? method : enumeration).Cancel();
        await Assert.That(await AsyncEnumerationFailureOf(async () => { await rows.MoveNextAsync(); })).IsTypeOf<OperationCanceledException>();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncModels_PreCancellationAndUnsupportedCapabilityDoNotDispatch(bool unsupported)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory { ConfigureCommand = command => { if (unsupported) command.ValidationFailure = new NotSupportedException("models"); } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var error = await AsyncEnumerationFailureOf(() => transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteBufferedAsyncCore(new(true)));
        await Assert.That(unsupported ? error is NotSupportedException : error is OperationCanceledException).IsTrue();
        await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncModels_HelperDrainsAnEscapedEnumerationThroughChildCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var child = new ControlledRowDataReader([1, "value"]) { Cleanup = new(paused: true) };
        var call = 0;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = call++ == 0 ? new ControlledRowDataReader([1]) : child } };
        IAsyncEnumerator<IImmutableInstance>? escaped = null;
        Task<bool>? move = null;
        var helper = transaction.RunCallbackAsyncCore(_ =>
        {
            escaped = transaction.From<TransactionMutationGuardRow>().SelectQuery().ExecuteAsyncCore().GetAsyncEnumerator();
            move = escaped.MoveNextAsync().AsTask();
            return Task.FromResult(9);
        }, new());
        await child.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(helper.IsCompleted).IsFalse();
        child.Cleanup.Release();
        await move!;
        await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        await escaped!.DisposeAsync();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncModels_ProviderSensitiveKeysUseActualRowsAndOwnedBinaryValues()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var key = new byte[] { 1 };
        var payload = new byte[] { 7 };
        var calls = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? new ControlledRowDataReader([key]) : new ControlledRowDataReader([new byte[] { 1 }, payload]) },
            ConfigureCommand = command => command.Resource.Disposing = () => { key[0] = 9; if (calls > 1) payload[0] = 9; }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var select = fixture.Database.From<TransactionMutationGuardBinaryRow>().Where("id").EqualTo(new byte[] { 1 }).SelectQuery();
        var rows = await select.ExecuteBufferedAsyncCore();
        await Assert.That(factory.Inputs.Count).IsEqualTo(2);
        await Assert.That((byte[])rows[0].PrimaryKeys().GetValue(0)!).IsEquivalentTo(new byte[] { 1 });
        await Assert.That((byte[])rows[0]["Payload"]!).IsEquivalentTo(new byte[] { 7 });
    }

    [Test]
    [Arguments("string")]
    [Arguments("keyless")]
    [Arguments("composite")]
    public async Task AsyncModels_PreserveProviderIdentityCompositeOrderingAndKeylessProjection(string shape)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        var calls = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = shape switch
            {
                "string" => calls++ == 0 ? new ControlledRowDataReader(["STORED"]) : new ControlledRowDataReader(["STORED", "value"]),
                "keyless" => new ControlledRowDataReader([42, "value", 7]),
                _ => calls++ == 0 ? new ControlledRowDataReader([1, 2], [3, 4]) : new ControlledRowDataReader([3, 4, "second"], [1, 2, "first"])
            } }
        };
        scenario.AsyncSqlReaders = factory;
        if (shape == "string")
        {
            var query = new SqlQuery<AsyncModelStringRow>(provider.ReadOnlyAccess);
            query.Where("id").EqualTo("stored");
            var result = await query.SelectQuery().ExecuteBufferedAsyncCore();
            await Assert.That(((AsyncModelStringRow)result.Single()).Id).IsEqualTo("STORED");
            await Assert.That(factory.Inputs.Count).IsEqualTo(2);
        }
        else if (shape == "keyless")
        {
            var result = await new SqlQuery<AsyncModelKeylessRow>(provider.ReadOnlyAccess).SelectQuery().What("42 AS extra", "value", "number").ExecuteBufferedAsyncCore();
            await Assert.That(((AsyncModelKeylessRow)result.Single()).Value).IsEqualTo("value");
            await Assert.That(((AsyncModelKeylessRow)result.Single()).Number).IsEqualTo(7);
        }
        else
        {
            var result = await new SqlQuery<AsyncModelCompositeRow>(provider.ReadOnlyAccess).SelectQuery().ExecuteBufferedAsyncCore();
            await Assert.That(string.Join(",", result.Cast<AsyncModelCompositeRow>().Select(x => x.Value))).IsEqualTo("first,second");
            await Assert.That(factory.Inputs[1].ToSql().Parameters.Count).IsEqualTo(4);
        }
    }

    [Test]
    [Arguments("warm")]
    [Arguments("cold")]
    [Arguments("missing")]
    [Arguments("limit-zero")]
    [Arguments("offset")]
    public async Task AsyncModels_DirectKeyShortcutPreservesCacheAndPagingRules(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        fixture.PrimeCommittedRow(1, "cached");
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = mode == "cold" ? new ControlledRowDataReader([2, "loaded"]) : new ControlledRowDataReader() }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var query = fixture.Database.From<TransactionMutationGuardRow>();
        query.Where("id").EqualTo(mode is "cold" or "missing" ? 2 : 1);
        if (mode == "limit-zero") query.Limit(0);
        if (mode == "offset") query.Offset(1);
        var result = await query.SelectQuery().ExecuteBufferedAsyncCore();
        await Assert.That(result.Count).IsEqualTo(mode is "warm" or "cold" ? 1 : 0);
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(mode == "warm" ? 0 : 1);
        if (mode == "warm") await Assert.That(((TransactionMutationGuardRow)result[0]).Value).IsEqualTo("cached");
        if (mode == "cold") await Assert.That(((TransactionMutationGuardRow)result[0]).Value).IsEqualTo("loaded");
    }

    [Test]
    public async Task AsyncModels_DerivedProviderSensitiveQueryNeedsOnlyKeysFromItsDerivedSource()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        var calls = 0;
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? new ControlledRowDataReader(["stored"]) : new ControlledRowDataReader(["stored", "value"]) }
        };
        scenario.AsyncSqlReaders = factory;
        var query = new SqlQuery<AsyncModelStringRow>(provider.ReadOnlyAccess, "derived")
            .UseDerivedSource(new Sql("SELECT id FROM async_model_string LIMIT 2"));
        var select = query.SelectQuery().What("value");
        var result = await select.ExecuteBufferedAsyncCore();
        await Assert.That(((AsyncModelStringRow)result.Single()).Value).IsEqualTo("value");
        await Assert.That(factory.Inputs[0].Text.Contains("value", StringComparison.Ordinal)).IsFalse();
        await Assert.That(factory.Inputs[0].Text).Contains("SELECT id FROM async_model_string LIMIT 2");
        await Assert.That(factory.Inputs[1].Text.Contains("SELECT id FROM", StringComparison.Ordinal)).IsFalse();
        await Assert.That(select.ToSql().Text).Contains("value");
    }

    [Test]
    public async Task AsyncModels_ConvertedPrimaryKeyStaysInProviderDomainUntilModelMaterialization()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var calls = 0;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? new ControlledRowDataReader([42]) : new ControlledRowDataReader([42, "value"]) } };
        var select = fixture.Database.From<TransactionMutationGuardReferenceIdRow>().SelectQuery();
        var converter = (TransactionMutationGuardReferenceIdConverter)select.Query.Table.GetColumnByDbName("id").ScalarConverter!;
        converter.Reset();
        var result = await select.ExecuteBufferedAsyncCore();
        await Assert.That(((TransactionMutationGuardReferenceIdRow)result.Single()).Id.Value).IsEqualTo(42);
        await Assert.That(converter.ToProviderValues).IsEmpty();
    }

    [Test]
    public async Task AsyncModels_BatchInvalidationCannotOverwriteNewerCacheRows()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        var table = provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table;
        var cache = provider.GetTableCache(table);
        var rowReader = new ControlledRowDataReader([1, "old"], [2, "old"]) { Cleanup = new(paused: true) };
        var calls = 0;
        scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? new ControlledRowDataReader([1], [2]) : rowReader } };
        var pending = new SqlQuery<AsyncCanonicalCachedRow>(provider.ReadOnlyAccess).SelectQuery().ExecuteBufferedAsyncCore();
        await rowReader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            cache.ClearRows();
            scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
                { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, "new"]) } };
            _ = await cache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), provider.ReadOnlyAccess);
        }
        finally { rowReader.Cleanup.Release(); }
        var old = (await pending).Cast<AsyncCanonicalCachedRow>().ToArray();
        await Assert.That(old.Length).IsEqualTo(2);
        await Assert.That(old.All(x => x.Value == "old")).IsTrue();
        await Assert.That(cache.RowCount).IsEqualTo(1);
        var current = (AsyncCanonicalCachedRow)(await cache.GetCanonicalRowAsyncCore(DataLinqKey.FromValue(1), provider.ReadOnlyAccess))!;
        await Assert.That(current.Value).IsEqualTo("new");
    }

    [Test]
    public async Task AsyncModels_ModelConstructionFailureRetainsOwnerAndOnlyValidCachedPrefix()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncCanonicalCachedDb>(scenario);
        using var transaction = provider.StartTransaction();
        var table = provider.Metadata.GetTableModel(typeof(AsyncCanonicalCachedRow)).Table;
        var cache = provider.GetTableCache(table);
        var calls = 0;
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? new ControlledRowDataReader([1], [2]) : new ControlledRowDataReader([1, "one"], [2, "two"]), FailureEvidence = TrustedScalarRead } };
        scenario.AsyncSqlReaders = factory;
        var expected = new Exception("second constructor");
        var constructions = 0;
        AsyncCanonicalCachedRow.Creating.Value = () =>
        {
            _ = Capture<InvalidOperationException>(() => DataSourceAccess.EnsureReadAllowed(transaction, "reentrant read"));
            if (factory.Commands.Any(x => x.Resource.AsyncDisposals != 1)) throw new Exception("cleanup is unfinished");
            if (++constructions == 2) throw expected;
        };
        try
        {
            var pending = new SqlQuery<AsyncCanonicalCachedRow>(transaction).SelectQuery().ExecuteBufferedAsyncCore();
            await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
            await Assert.That(cache.TryGetMaterializedRow(DataLinqKey.FromValue(1), transaction, out _)).IsTrue();
            await Assert.That(cache.TryGetMaterializedRow(DataLinqKey.FromValue(2), transaction, out _)).IsFalse();
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        }
        finally { AsyncCanonicalCachedRow.Creating.Value = null; }
    }
}

[Database("async_model_query")]
public sealed partial class AsyncModelQueryDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<AsyncModelStringRow> Strings { get; } = new(source);
    public DbRead<AsyncModelKeylessRow> Keyless { get; } = new(source);
    public DbRead<AsyncModelCompositeRow> Composite { get; } = new(source);
}

[Table("async_model_string")]
[UseCache]
public abstract partial class AsyncModelStringRow(IRowData rowData, IDataSourceAccess source) : Immutable<AsyncModelStringRow, AsyncModelQueryDb>(rowData, source), ITableModel<AsyncModelQueryDb>
{
    [PrimaryKey, Column("id"), Type(DatabaseType.SQLite, "text")]
    public abstract string Id { get; }
    [Column("value"), Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
}

[View("async_model_keyless")]
[Definition("SELECT 'value' AS value, 7 AS number")]
public abstract partial class AsyncModelKeylessRow(IRowData rowData, IDataSourceAccess source) : Immutable<AsyncModelKeylessRow, AsyncModelQueryDb>(rowData, source), IViewModel<AsyncModelQueryDb>
{
    [Column("value"), Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
    [Column("number"), Type(DatabaseType.SQLite, "integer")]
    public abstract int Number { get; }
}

[Table("async_model_composite")]
public abstract partial class AsyncModelCompositeRow(IRowData rowData, IDataSourceAccess source) : Immutable<AsyncModelCompositeRow, AsyncModelQueryDb>(rowData, source), ITableModel<AsyncModelQueryDb>
{
    [PrimaryKey, Column("first"), Type(DatabaseType.SQLite, "integer")]
    public abstract int First { get; }
    [PrimaryKey, Column("second"), Type(DatabaseType.SQLite, "integer")]
    public abstract int Second { get; }
    [Column("value"), Type(DatabaseType.SQLite, "text")]
    public abstract string Value { get; }
}
