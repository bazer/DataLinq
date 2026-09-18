using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Query;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncLookup_ConvertedKeysAreCapturedOnceWithoutProviderKeyReconversion(bool modelKey)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var converter = new TransactionMutationGuardReferenceIdConverter.Observation();
        converter.Converting = () => { _ = Capture<InvalidOperationException>(() => transaction.Query()); };
        var reader = new ControlledRowDataReader([42, "value"]);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ReaderOverride = reader };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        var wrapper = new TransactionMutationGuardReferenceId(42);
        object?[] values = [wrapper];
        var pending = modelKey
            ? AsyncModelLookup.GetByModelKeyAsyncCore<TransactionMutationGuardReferenceIdRow>(values, transaction)
            : AsyncModelLookup.GetByProviderKeyAsyncCore<TransactionMutationGuardReferenceIdRow>(DataLinqKey.FromValue(42), transaction);
        converter.Converting = null;
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        wrapper.Value = 99;
        values[0] = new TransactionMutationGuardReferenceId(100);
        access.Dispatch.Release();
        var result = await pending;
        await Assert.That(result!.Id.Value).IsEqualTo(42);
        await Assert.That(factory.Inputs[0].ToSql().Parameters.Single().Value).IsEqualTo(42);
        await Assert.That(converter.ToProviderValues.Count).IsEqualTo(modelKey ? 1 : 0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(1);
        await Assert.That(reader.Disposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncLookup_BinaryModelKeyOwnsItsInputBeforeDispatchSuspends()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var key = new byte[] { 1, 2 };
        var access = new ControlledAsyncDatabaseAccess(new(paused: true))
            { ReaderOverride = new ControlledRowDataReader([new byte[] { 1, 2 }, new byte[] { 7 }]) };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = AsyncModelLookup.GetByModelKeyAsyncCore<TransactionMutationGuardBinaryRow>([key], transaction);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        key[0] = 99;
        access.Dispatch.Release();
        var result = await pending;
        await Assert.That(result!.Id).IsEquivalentTo(new byte[] { 1, 2 });
        await Assert.That((byte[])factory.Inputs[0].ToSql().Parameters.Single().Value!).IsEquivalentTo(new byte[] { 1, 2 });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncLookup_ProviderMatchUsesActualIdentityAndFirstRowThenAwaitsCleanup(bool transactionSource)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        using var transaction = provider.StartTransaction();
        DataSourceAccess source = transactionSource ? transaction : provider.ReadOnlyAccess;
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(AsyncModelStringRow)).Table);
        var reader = new ControlledRowDataReader(["STORED", "first"], ["stored", "second"]) { Cleanup = new(paused: true) };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        scenario.AsyncSqlReaders = factory;
        var alias = DataLinqKey.FromValue("stored");
        var actual = DataLinqKey.FromValue("STORED");
        var pending = AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelStringRow>(alias, source);
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            await Assert.That(reader.Moves).IsEqualTo(1);
            if (transactionSource) _ = Capture<InvalidOperationException>(() => DataSourceAccess.EnsureReadAllowed(source, "overlap"));
        }
        finally { reader.Cleanup.Release(); }
        var row = await pending;
        await Assert.That(row!.Id).IsEqualTo("STORED");
        await Assert.That(row.Value).IsEqualTo("first");
        await Assert.That(cache.TryGetMaterializedRow(alias, source, out _)).IsFalse();
        await Assert.That(cache.TryGetMaterializedRow(actual, source, out var cached)).IsTrue();
        await Assert.That(cached).IsSameReferenceAs(row);
        await Assert.That(await AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelStringRow>(actual, source)).IsSameReferenceAs(row);
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(1);
        await Assert.That(cache.RowCount).IsEqualTo(transactionSource ? 0 : 1);
    }

    [Test]
    public async Task AsyncLookup_ProviderMatchInvalidationCannotOverwriteANewerActualIdentity()
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(AsyncModelStringRow)).Table);
        var reader = new ControlledRowDataReader(["STORED", "old"]) { Cleanup = new(paused: true) };
        scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        var pending = AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelStringRow>(DataLinqKey.FromValue("stored"), provider.ReadOnlyAccess);
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            cache.ClearRows();
            scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
                { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(["STORED", "new"]) } };
            _ = await AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelStringRow>(DataLinqKey.FromValue("STORED"), provider.ReadOnlyAccess);
        }
        finally { reader.Cleanup.Release(); }
        await Assert.That((await pending)!.Value).IsEqualTo("old");
        await Assert.That((await AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelStringRow>(DataLinqKey.FromValue("STORED"), provider.ReadOnlyAccess))!.Value).IsEqualTo("new");
        await Assert.That(cache.RowCount).IsEqualTo(1);
    }

    [Test]
    public async Task AsyncLookup_NullSentinelKeepsExistingNeutralAndProviderSensitiveSemantics()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory();
        _ = Capture<ArgumentException>(() => fixture.RowCache.GetRow(DataLinqKey.Null, transaction));
        await Assert.That(await AsyncEnumerationFailureOf(() => AsyncModelLookup.GetByProviderKeyAsyncCore<TransactionMutationGuardRow>(DataLinqKey.Null, transaction)))
            .IsTypeOf<ArgumentException>();

        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        using var other = provider.StartTransaction();
        var cache = provider.GetTableCache(provider.Metadata.GetTableModel(typeof(AsyncModelStringRow)).Table);
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader() } };
        scenario.AsyncSqlReaders = factory;
        await Assert.That(cache.GetRow(DataLinqKey.Null, other)).IsNull();
        await Assert.That(await AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelStringRow>(DataLinqKey.Null, other)).IsNull();
        await Assert.That(factory.Inputs.Single().Text).Contains("IS @w0");
        await Assert.That(factory.Inputs.Single().ToSql().Parameters.Single().Value).IsNull();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("cancel")]
    [Arguments("unsupported")]
    [Arguments("provider")]
    [Arguments("shape")]
    [Arguments("conversion")]
    public async Task AsyncLookup_OnlyAValidMissingRowBecomesNull(string outcome)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var access = new ControlledAsyncDatabaseAccess(new(paused: outcome == "provider"))
            { ReaderOverride = new ControlledRowDataReader(), FailureEvidence = TrustedScalarRead };
        var expected = new Exception("provider lookup failure");
        if (outcome == "provider") access.Dispatch.Fail(expected);
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            ConfigureCommand = c => { if (outcome == "unsupported") c.ValidationFailure = new NotSupportedException("lookup"); }
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        var token = new CancellationToken(outcome is "cancel" or "unsupported" or "shape" or "conversion");
        Task<TransactionMutationGuardRow?> pending = outcome == "conversion"
            ? AsyncModelLookup.GetByModelKeyAsyncCore<TransactionMutationGuardRow>(["bad integer"], transaction, token)
            : AsyncModelLookup.GetByProviderKeyAsyncCore<TransactionMutationGuardRow>(
                outcome == "shape" ? DataLinqKey.FromValues([1, 2]) : DataLinqKey.FromValue(1), transaction, token);
        if (outcome == "missing") await Assert.That(await pending).IsNull();
        else
        {
            var error = await AsyncEnumerationFailureOf(() => pending);
            if (outcome == "cancel") await Assert.That(error).IsTypeOf<OperationCanceledException>();
            else if (outcome == "unsupported") await Assert.That(error).IsTypeOf<NotSupportedException>();
            else if (outcome == "provider") await Assert.That(error).IsSameReferenceAs(expected);
            else await Assert.That(error is OperationCanceledException).IsFalse();
        }
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(outcome is "missing" or "provider" ? 1 : 0);
        _ = transaction.Query();
    }

    [Test]
    [Arguments("warm")]
    [Arguments("cold")]
    [Arguments("limit-zero")]
    [Arguments("offset")]
    public async Task AsyncLookup_ProviderSensitiveFluentShortcutPreservesPagingAndSkipsKeyDispatch(string mode)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(["STORED", "value"]) } };
        scenario.AsyncSqlReaders = factory;
        if (mode == "warm") _ = await AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelStringRow>(DataLinqKey.FromValue("STORED"), provider.ReadOnlyAccess);
        factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = mode == "cold" ? new ControlledRowDataReader(["STORED", "value"]) : new ControlledRowDataReader() } };
        scenario.AsyncSqlReaders = factory;
        var query = new SqlQuery<AsyncModelStringRow>(provider.ReadOnlyAccess);
        query.Where("id").EqualTo(mode == "warm" ? "STORED" : "stored");
        if (mode == "limit-zero") query.Limit(0);
        if (mode == "offset") query.Offset(1);
        var result = await query.SelectQuery().ExecuteBufferedAsyncCore();
        await Assert.That(result.Count).IsEqualTo(mode is "warm" or "cold" ? 1 : 0);
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(mode == "warm" ? 0 : 1);
        if (mode == "cold")
        {
            await Assert.That(((AsyncModelStringRow)result.Single()).Id).IsEqualTo("STORED");
            await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
            await Assert.That(factory.Commands[1].Creates).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncLookup_CompositeComponentsUseMetadataOrderAndAreCapturedBeforeAwait(bool modelKey)
    {
        var scenario = new ScriptedMutationScenario();
        using var provider = new CapturedReadProvider<AsyncModelQueryDb>(scenario);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ReaderOverride = new ControlledRowDataReader([3, 7, "value"]) };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        scenario.AsyncSqlReaders = factory;
        object?[] components = [3, 7];
        var pending = modelKey
            ? AsyncModelLookup.GetByModelKeyAsyncCore<AsyncModelCompositeRow>(components, provider.ReadOnlyAccess)
            : AsyncModelLookup.GetByProviderKeyAsyncCore<AsyncModelCompositeRow>(DataLinqKey.FromValues(components), provider.ReadOnlyAccess);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        components[0] = 99;
        components[1] = 88;
        access.Dispatch.Release();
        var result = await pending;
        await Assert.That(result!.First).IsEqualTo(3);
        await Assert.That(result.Second).IsEqualTo(7);
        await Assert.That(string.Join(",", factory.Inputs[0].ToSql().Parameters.Select(x => x.Value))).IsEqualTo("3,7");
    }

    [Test]
    [Arguments("cancel")]
    [Arguments("capability")]
    [Arguments("busy")]
    public async Task AsyncLookup_WarmCacheDoesNotBypassCancellationCapabilityOrOwnership(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        fixture.PrimeCommittedRow(1, "cached");
        var factory = new ControlledSqlReaderFactory
            { ConfigureCommand = c => { if (mode == "capability") c.ValidationFailure = new NotSupportedException("lookup"); } };
        fixture.Scenario.AsyncSqlReaders = factory;
        using var scope = mode == "busy" ? transaction.ExecutionGate.Enter("other work") : null;
        var error = await AsyncEnumerationFailureOf(() => AsyncModelLookup.GetByProviderKeyAsyncCore<TransactionMutationGuardRow>(
            DataLinqKey.FromValue(1), transaction, new(true)));
        await Assert.That(mode switch
        {
            "cancel" => error is OperationCanceledException,
            "capability" => error is NotSupportedException,
            _ => error is InvalidOperationException
        }).IsTrue();
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(0);
    }

    [Test]
    public async Task AsyncLookup_ConverterFailurePrecedesCancellationAndReleasesTheOwner()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var converter = new TransactionMutationGuardReferenceIdConverter.Observation();
        var expected = new Exception("model key conversion");
        converter.Converting = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            throw expected;
        };
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = AsyncModelLookup.GetByModelKeyAsyncCore<TransactionMutationGuardReferenceIdRow>(
            [new TransactionMutationGuardReferenceId(42)], transaction, new(true));
        converter.Converting = null;
        var error = await AsyncEnumerationFailureOf(() => pending);
        await Assert.That(error.InnerException).IsSameReferenceAs(expected);
        await Assert.That(factory.Inputs).IsEmpty();
        _ = transaction.Query();
    }
}
