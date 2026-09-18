using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Mutation;
using DataLinq.Tests.Unit.Fixtures;

namespace DataLinq.Tests.Unit.Core;

public sealed partial class TransactionMutationFailureTests
{
    private static IAsyncEnumerable<T> AsyncPlan<T>(IQueryable<T> query, CancellationToken token = default) =>
        ((ExpressionQueryPlanProvider)query.Provider).ExecuteEnumerableAsyncCore<T>(query.Expression, token);

    private static Task<T> AsyncPlanTerminal<T>(IQueryable<T> query, string terminal, CancellationToken token = default) =>
        ((ExpressionQueryPlanProvider)query.Provider).ExecuteAsyncCore<T>(
            Expression.Call(typeof(System.Linq.Queryable), terminal, [typeof(T)], query.Expression), token);

    private static async Task<List<T>> PlanRows<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var row in source) result.Add(row);
        return result;
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryPlan_OrdinaryAndPreparedArgumentsHaveDistinctCaptureBoundaries(bool prepared)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var ids = new[] { 1, 2 };
        var query = fixture.Database.Query().Rows.Where(row => ids.Contains(row.Id)).Select(row => row.Id);
        var plan = fixture.Database.PrepareSequenceQuery(ids, values => fixture.Database.Query().Rows.Where(row => values.Contains(row.Id)).Select(row => row.Id));
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([7]) { ColumnNames = ["value"] } } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var sequence = prepared ? plan.ExecuteAsyncCore(fixture.Database, ids) : AsyncPlan(query);
        ids[0] = 3;
        await Assert.That(factory.Inputs).IsEmpty();
        await using (var first = sequence.GetAsyncEnumerator())
        {
            await Assert.That(factory.Commands[0].Creates).IsEqualTo(0);
            await Assert.That(factory.Accesses[0].Calls).IsEmpty();
            await Assert.That(string.Join(",", factory.Inputs[0].ToSql().Parameters.Select(x => x.Value))).IsEqualTo(prepared ? "1,2" : "3,2");
            ids[0] = 9;
            await Assert.That(await first.MoveNextAsync()).IsTrue();
            await Assert.That(first.Current).IsEqualTo(7);
            await Assert.That(string.Join(",", factory.Inputs[0].ToSql().Parameters.Select(x => x.Value))).IsEqualTo(prepared ? "1,2" : "3,2");
        }
        factory.CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([8]) { ColumnNames = ["value"] } };
        var repeated = await PlanRows(sequence);
        await Assert.That(repeated.Single()).IsEqualTo(8);
        await Assert.That(string.Join(",", factory.Inputs[1].ToSql().Parameters.Select(x => x.Value))).IsEqualTo(prepared ? "1,2" : "9,2");
        if (prepared)
        {
            _ = await PlanRows(plan.ExecuteAsyncCore(fixture.Database, ids));
            await Assert.That(string.Join(",", factory.Inputs[2].ToSql().Parameters.Select(x => x.Value))).IsEqualTo("9,2");
        }
    }

    [Test]
    public async Task AsyncQueryPlan_ListTerminalCapturesBeforeFirstSuspensionAndNeverReturnsPartialResults()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var id = 1;
        var query = transaction.Query().Rows.Where(row => row.Id > id).Select(row => row.Id);
        var reader = new ControlledRowDataReader([2], [3]) { ColumnNames = ["value"], Cleanup = new(paused: true) };
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ReaderOverride = reader, FailureEvidence = TrustedScalarRead };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = ((ExpressionQueryPlanProvider)query.Provider).ExecuteListAsyncCore<int>(query.Expression);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        id = 99;
        access.Dispatch.Release();
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(pending.IsCompleted).IsFalse();
        await Assert.That(factory.Inputs.Single().ToSql().Parameters.Single().Value).IsEqualTo(1);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        var expected = new Exception("list cleanup");
        reader.Cleanup.Fail(expected);
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
    }

    [Test]
    [Arguments("Single", "one")]
    [Arguments("Single", "empty")]
    [Arguments("Single", "two")]
    [Arguments("SingleOrDefault", "empty")]
    [Arguments("SingleOrDefault", "two")]
    [Arguments("First", "one")]
    [Arguments("First", "empty")]
    [Arguments("FirstOrDefault", "empty")]
    [Arguments("Last", "two")]
    [Arguments("Last", "empty")]
    [Arguments("LastOrDefault", "empty")]
    public async Task AsyncQueryPlan_EntityTerminalSemanticsFollowCleanupInsideTheOwner(string terminal, string shape)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var keys = new ControlledRowDataReader(shape == "empty" ? [] : shape == "two" ? [[1], [2]] : [[1]]);
        var reader = new ControlledRowDataReader(shape == "two" ? [[1, "one"], [2, "two"]] : [[1, "one"]]);
        var cleanup = new AsyncCheckpoint(paused: true);
        if (shape == "empty") keys.Cleanup = cleanup; else reader.Cleanup = cleanup;
        var calls = 0;
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? keys : reader, FailureEvidence = TrustedScalarRead } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = AsyncPlanTerminal(transaction.Query().Rows.Where(row => row.Id > 0), terminal);
        await cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await Assert.That(pending.IsCompleted).IsFalse();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
        }
        finally { cleanup.Release(); }
        var failure = shape == "empty" && !terminal.EndsWith("OrDefault", StringComparison.Ordinal) || shape == "two" && terminal.StartsWith("Single", StringComparison.Ordinal);
        if (failure)
        {
            await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<InvalidOperationException>();
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
            await Assert.That(transaction.AsyncFailureContext.Recovery.HasFlag(ExecutionRecoveryActions.Continue)).IsTrue();
        }
        else if (shape == "empty") await Assert.That(await pending).IsNull();
        else await Assert.That((await pending).Id).IsEqualTo(terminal == "Last" ? 2 : 1);
        await Assert.That(factory.Commands.All(x => x.Resource.AsyncDisposals == 1)).IsTrue();
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncQueryPlan_PreparedScalarCapturesArgumentsAndConvertsAfterOwnedCleanup()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var plan = fixture.Database.PrepareQuery(new[] { 1 }, ids => fixture.Database.Query().Rows.Count(row => ids.Contains(row.Id)));
        var access = new ControlledAsyncDatabaseAccess(new(paused: true)) { ScalarResult = 3L };
        var factory = new ControlledSqlReaderFactory
        {
            CreateAccess = _ => access,
            ConfigureCommand = command => command.Resource.Cleanup = new(paused: true)
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var ids = new[] { 7 };
        var pending = plan.ExecuteAsyncCore(transaction, ids);
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        ids[0] = 99;
        access.Dispatch.Release();
        await factory.Commands[0].Resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(pending.IsCompleted).IsFalse();
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        factory.Commands[0].Resource.Cleanup.Release();
        await Assert.That(await pending).IsEqualTo(3);
        await Assert.That(factory.Inputs.Single().ToSql().Parameters.Single().Value).IsEqualTo(7);
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryPlan_DirectProjectionSequencesHonorBothTokens(bool cancelMethod)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var method = new CancellationTokenSource();
        using var enumeration = new CancellationTokenSource();
        var reader = new ControlledRowDataReader([1], [2]) { ColumnNames = ["value"], Cleanup = new(paused: true) };
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead } };
        await using var rows = AsyncPlan(transaction.Query().Rows.Select(row => row.Id), method.Token).GetAsyncEnumerator(enumeration.Token);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        (cancelMethod ? method : enumeration).Cancel();
        var pending = rows.MoveNextAsync().AsTask();
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(pending.IsCompleted).IsFalse();
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        reader.Cleanup.Release();
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<OperationCanceledException>();
        _ = transaction.Query();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryPlan_NativeCapabilityValidationWinsOverPreCancellation(bool scalar)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory { ConfigureCommand = command => command.ValidationFailure = new NotSupportedException("native async path") };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        var query = transaction.Query().Rows.Select(row => row.Id);
        var error = scalar
            ? await AsyncEnumerationFailureOf(() => fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Count()).ExecuteAsyncCore(transaction, 0, new(true)))
            : await AsyncEnumerationFailureOf(async () => { await using var rows = AsyncPlan(query, new(true)).GetAsyncEnumerator(); await rows.MoveNextAsync(); });
        await Assert.That(error).IsTypeOf<NotSupportedException>();
        await Assert.That(factory.Commands.Sum(x => x.Creates)).IsEqualTo(0);
        await Assert.That(factory.Commands.Single().Validations.Count).IsEqualTo(1);
        await Assert.That(transaction.AsyncFailureContext).IsNull();
        _ = transaction.Query();
    }

    [Test]
    public async Task AsyncQueryPlan_DirectProjectionOwnsBinaryStorageBeforeYield()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var bytes = new byte[] { 7 };
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([bytes]) { ColumnNames = ["value"] } } };
        await using var rows = AsyncPlan(fixture.Database.Query().BinaryRows.Select(row => row.Payload)).GetAsyncEnumerator();
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var delivered = rows.Current;
        bytes[0] = 99;
        await rows.DisposeAsync();
        await Assert.That(delivered!).IsEquivalentTo(new byte[] { 7 });
    }

    [Test]
    public async Task AsyncQueryPlan_ConvertedProjectionUsesProviderValuesWithoutIdentityRoundtrip()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var observation = new TransactionMutationGuardReferenceIdConverter.Observation();
        var id = new TransactionMutationGuardReferenceId(42);
        var query = fixture.Database.Query().ReferenceIdRows.Where(row => row.Id == id).Select(row => row.Id);
        var access = new ControlledAsyncDatabaseAccess(new(paused: true))
            { ReaderOverride = new ControlledRowDataReader([42]) { ColumnNames = ["value"] } };
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => access };
        fixture.Scenario.AsyncSqlReaders = factory;
        var pending = PlanRows(AsyncPlan(query));
        await access.Dispatch.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        id.Value = 99;
        access.Dispatch.Release();
        await Assert.That((await pending).Single().Value).IsEqualTo(42);
        await Assert.That(factory.Inputs.Single().ToSql().Parameters.Single().Value).IsEqualTo(42);
        await Assert.That(observation.ToProviderValues.Count).IsEqualTo(1);
        await Assert.That(observation.FromProviderCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryPlan_LocalProjectionConstructionRemainsOwnedAfterHydration(bool fail)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, " value "]), FailureEvidence = TrustedScalarRead } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var query = transaction.Query().Rows.Where(row => row.Id == 1).Select(row => new AsyncPlanBox(row.Value.Trim()));
        var expected = new Exception("projection constructor");
        AsyncPlanBox.Creating.Value = () =>
        {
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            if (factory.Commands.Any(c => c.Creates != 0 && c.Resource.AsyncDisposals != 1)) throw new Exception("hydration reader still open");
            if (fail) throw expected;
        };
        try
        {
            var pending = PlanRows(AsyncPlan(query));
            if (fail)
            {
                var error = await AsyncEnumerationFailureOf(() => pending);
                await Assert.That(ReferenceEquals(error, expected) || ReferenceEquals(error.InnerException, expected)).IsTrue();
                await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
            }
            else await Assert.That((await pending).Single().Value).IsEqualTo("value");
            _ = transaction.Query();
        }
        finally { AsyncPlanBox.Creating.Value = null; }
    }

    [Test]
    public async Task AsyncQueryPlan_SqlRowAndGroupedProjectionUseCapturedColumnsAndConverters()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var reader = new ControlledRowDataReader(["row", 7]) { ColumnNames = ["Value", "Id"] };
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory { CreateAccess = _ => new() { ReaderOverride = reader } };
        var query = fixture.Database.Query().Rows.Select(row => new { row.Id, row.Value });
        await Assert.That(((ExpressionQueryPlanProvider)query.Provider).Parse(query.Expression, query.ElementType).Template.Projection.Kind).IsEqualTo(QueryPlanProjectionKind.SqlRow);
        var rows = await PlanRows(AsyncPlan(query));
        await Assert.That(rows.Single().Id).IsEqualTo(7);
        await Assert.That(rows.Single().Value).IsEqualTo("row");

        var grouped = fixture.Database.Query().Rows.GroupBy(row => row.Value).Select(group => new { Value = group.Key, Count = group.Count() });
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(["group", 3L]) { ColumnNames = ["Value", "Count"] } } };
        fixture.Scenario.AsyncSqlReaders = factory;
        await Assert.That(((ExpressionQueryPlanProvider)grouped.Provider).Parse(grouped.Expression, grouped.ElementType).Template.Projection.Kind).IsEqualTo(QueryPlanProjectionKind.GroupedAggregate);
        var groups = await PlanRows(AsyncPlan(grouped));
        await Assert.That(groups.Single().Value).IsEqualTo("group");
        await Assert.That(groups.Single().Count).IsEqualTo(3);
        await Assert.That(factory.Inputs.Single().Text).Contains("GROUP BY");
    }

    [Test]
    [Arguments("success")]
    [Arguments("missing")]
    [Arguments("failure")]
    [Arguments("cancel")]
    public async Task AsyncQueryPlan_JoinedLocalProjectionClosesKeysBeforeHydrationAndCapturesFactory(string outcome)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var keyReader = new ControlledRowDataReader([1, 1], [1, 1])
        {
            ColumnNames = [QueryPlanSqlBuilder.GetJoinedPrimaryKeyAlias(0, 0), QueryPlanSqlBuilder.GetJoinedPrimaryKeyAlias(1, 0)],
            Cleanup = new(paused: true)
        };
        var left = new ControlledRowDataReader([1, " left "]);
        var right = new ControlledRowDataReader(outcome == "missing" ? [] : [[1]]) { Advance = new(paused: true) };
        var calls = 0;
        var factory = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = calls++ switch { 0 => keyReader, 1 => left, _ => right }, FailureEvidence = TrustedScalarRead } };
        fixture.Scenario.AsyncSqlReaders = factory;
        var query = transaction.Query().Rows.Join(transaction.Query().OtherRows, a => a.Id, b => b.Id,
            (a, b) => new AsyncPlanBox(a.Value.Trim(), b.Id));
        await Assert.That(((ExpressionQueryPlanProvider)query.Provider).Parse(query.Expression, query.ElementType).Template.Projection.Kind).IsEqualTo(QueryPlanProjectionKind.JoinedRowLocal);
        AsyncPlanBox.Creating.Value = () => { _ = Capture<InvalidOperationException>(() => transaction.Query()); };
        try
        {
            var pending = PlanRows(AsyncPlan(query, cancellation.Token));
            await keyReader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(factory.Inputs.Count).IsEqualTo(1);
            var replacement = new ControlledSqlReaderFactory();
            fixture.Scenario.AsyncSqlReaders = replacement;
            factory.CreateAccess = _ => throw new Exception("uncaptured factory");
            keyReader.Cleanup.Release();
            await right.Advance.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(keyReader.Disposals).IsEqualTo(1);
            await Assert.That(left.Disposals).IsEqualTo(1);
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            var expected = new Exception("joined hydration");
            if (outcome == "failure") right.Advance.Fail(expected);
            else if (outcome == "cancel") cancellation.Cancel();
            else right.Advance.Release();
            if (outcome == "success")
            {
                var result = await pending;
                await Assert.That(result.Count).IsEqualTo(2);
                await Assert.That(result.All(row => row.Value == "left" && row.Other == 1)).IsTrue();
                await Assert.That(factory.Inputs.Count).IsEqualTo(3); // Duplicate tuples share captured hydration.
            }
            else
            {
                var error = await AsyncEnumerationFailureOf(() => pending);
                await Assert.That(outcome switch
                {
                    "failure" => ReferenceEquals(error, expected),
                    "cancel" => error is OperationCanceledException,
                    _ => error is InvalidOperationException
                }).IsTrue();
                await Assert.That(fixture.RowCache.TryGetMaterializedRow(DataLinqKey.FromValue(1), transaction, out _)).IsTrue();
            }
            await Assert.That(replacement.Inputs).IsEmpty();
            await Assert.That(factory.Commands.All(c => c.Resource.AsyncDisposals == 1)).IsTrue();
            _ = transaction.Query();
        }
        finally { keyReader.Cleanup.Release(); right.Advance.Release(); AsyncPlanBox.Creating.Value = null; }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryPlan_PreparedEntityReaderRetainsOwnerBetweenBufferedRowsAndHelperDrainsIt(bool escape)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        var keyReader = new ControlledRowDataReader([1], [2]);
        var rowReader = new ControlledRowDataReader([1, "one"], [2, "two"]) { Cleanup = new(paused: true) };
        var calls = 0;
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = calls++ == 0 ? keyReader : rowReader, FailureEvidence = TrustedScalarRead } };
        var prepared = fixture.Database.PrepareSequenceQuery(0, minimum => fixture.Database.Query().Rows.Where(row => row.Id > minimum));
        IAsyncEnumerator<TransactionMutationGuardRow>? rows = null;
        Task<bool>? move = null;
        if (escape)
        {
            var helper = transaction.RunCallbackAsyncCore(_ =>
            {
                rows = prepared.ExecuteAsyncCore(transaction, 0).GetAsyncEnumerator();
                move = rows.MoveNextAsync().AsTask();
                return Task.FromResult(42);
            }, new());
            await rowReader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(helper.IsCompleted).IsFalse();
            rowReader.Cleanup.Release();
            await move!;
            await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
            await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsFalse();
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            rows = prepared.ExecuteAsyncCore(transaction, 0).GetAsyncEnumerator(cancellation.Token);
            move = rows.MoveNextAsync().AsTask();
            await rowReader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            rowReader.Cleanup.Release();
            await Assert.That(await move).IsTrue();
            _ = Capture<InvalidOperationException>(() => transaction.Query());
            _ = Capture<InvalidOperationException>(transaction.Dispose);
            cancellation.Cancel();
            await Assert.That(await AsyncEnumerationFailureOf(async () => { await rows.MoveNextAsync(); })).IsTypeOf<OperationCanceledException>();
            _ = transaction.Query();
        }
        await rows!.DisposeAsync();
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments("any")]
    [Arguments("sum")]
    [Arguments("minimum-empty")]
    [Arguments("nullable-minimum")]
    [Arguments("average")]
    [Arguments("overflow")]
    public async Task AsyncQueryPlan_ScalarResultConversionPreservesEmptyAndNumericSemantics(string kind)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory { CreateAccess = _ => new()
        {
            ScalarResult = kind switch { "any" => 1L, "average" => 2.5m, "overflow" => long.MaxValue, _ => DBNull.Value },
            FailureEvidence = TrustedScalarRead
        } };
        fixture.Scenario.AsyncSqlScalars = factory;
        if (kind == "any") await Assert.That(await fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Any()).ExecuteAsyncCore(transaction, 0)).IsTrue();
        else if (kind == "sum") await Assert.That(await fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Sum(row => row.Id)).ExecuteAsyncCore(transaction, 0)).IsEqualTo(0);
        else if (kind == "nullable-minimum") await Assert.That(await fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Min(row => (int?)row.Id)).ExecuteAsyncCore(transaction, 0)).IsNull();
        else if (kind == "average") await Assert.That(await fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Average(row => row.Id)).ExecuteAsyncCore(transaction, 0)).IsEqualTo(2.5);
        else
        {
            var pending = kind == "overflow"
                ? fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Count()).ExecuteAsyncCore(transaction, 0)
                : fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Min(row => row.Id)).ExecuteAsyncCore(transaction, 0);
            var error = await AsyncEnumerationFailureOf(() => pending);
            await Assert.That(kind == "overflow" ? error is OverflowException : error is InvalidOperationException).IsTrue();
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        }
        await Assert.That(factory.Commands.Single().Resource.AsyncDisposals).IsEqualTo(1);
        _ = transaction.Query();
    }

    [Test]
    [Arguments("warm")]
    [Arguments("cancel")]
    [Arguments("unsupported")]
    [Arguments("busy")]
    public async Task AsyncQueryPlan_WarmExactTerminalPreservesIdentityAndValidation(string mode)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        fixture.PrimeCommittedRow(1, "committed");
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader([1, "transaction"]) } };
        var expected = await fixture.RowCache.GetProviderRowAsyncCore(DataLinqKey.FromValue(1), transaction);
        await Assert.That(expected).IsNotNull();
        var query = transaction.Query().Rows.Where(row => row.Id == 1);
        var factory = new ControlledSqlReaderFactory
            { ConfigureCommand = c => { if (mode == "unsupported") c.ValidationFailure = new NotSupportedException("query"); } };
        fixture.Scenario.AsyncSqlReaders = factory;
        using var scope = mode == "busy" ? transaction.ExecutionGate.Enter("other work") : null;
        if (mode == "warm") await Assert.That(await AsyncPlanTerminal(query, "Single")).IsSameReferenceAs(expected);
        else
        {
            var error = await AsyncEnumerationFailureOf(() => AsyncPlanTerminal(query, "Single", new(true)));
            await Assert.That(mode switch { "cancel" => error is OperationCanceledException, "unsupported" => error is NotSupportedException, _ => error is InvalidOperationException }).IsTrue();
        }
        await Assert.That(factory.Commands.Sum(c => c.Creates)).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryPlan_InitializationFailureIsTerminalBeforeScalarOrProjectionDispatch(bool scalar)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        var resource = new ControlledTransactionResource { Open = new(paused: true), Cleanup = new(paused: true) };
        var lazy = BindInitialization(fixture, transaction, resource);
        var factory = new ControlledSqlReaderFactory
        {
            WrapSource = source => new InitializingTransactionReaderSource<ControlledTransactionResource>(lazy, source),
            WrapScalar = source => new InitializingTransactionScalarSource<ControlledTransactionResource>(lazy, source)
        };
        fixture.Scenario.AsyncSqlReaders = factory;
        fixture.Scenario.AsyncSqlScalars = factory;
        Task pending = scalar
            ? fixture.Database.PrepareQuery(0, _ => fixture.Database.Query().Rows.Count()).ExecuteAsyncCore(transaction, 0)
            : PlanRows(AsyncPlan(transaction.Query().Rows.Select(row => row.Id)));
        await resource.Open.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        var expected = new Exception("query plan initialization");
        resource.Open.Fail(expected);
        await resource.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(pending.IsCompleted).IsFalse();
        await Assert.That(factory.Commands.Sum(c => c.Creates)).IsEqualTo(0);
        resource.Cleanup.Release();
        await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Initialization);
        await Assert.That(lazy.State).IsEqualTo(TransactionInitializationState.Failed);
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        await transaction.DisposeAsyncCore();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncQueryPlan_CaughtTerminalFailureDoesNotInventSafeHelperCompletion(bool trusted)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        var transaction = fixture.Database.Transaction();
        fixture.Scenario.AsyncCompletion = new();
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = new ControlledRowDataReader(), FailureEvidence = trusted ? TrustedScalarRead : new() } };
        var caught = false;
        var helper = transaction.RunCallbackAsyncCore(async callbackToken =>
        {
            try { _ = await AsyncPlanTerminal(transaction.Query().Rows.Select(row => row.Id), "Single"); }
            catch (InvalidOperationException) { caught = true; }
            return 42;
        }, new());
        if (trusted) await Assert.That(await helper).IsEqualTo(42);
        else await Assert.That(await AsyncEnumerationFailureOf(() => helper)).IsTypeOf<InvalidOperationException>();
        await Assert.That(caught).IsTrue();
        await Assert.That(fixture.Scenario.AsyncCompletion.Calls.Contains("commit")).IsEqualTo(trusted);
        await transaction.DisposeAsyncCore();
    }

    [Test]
    public async Task AsyncQueryPlan_UnusedReadersStayColdAndRejectLaterTransactionCompletion()
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var factory = new ControlledSqlReaderFactory();
        fixture.Scenario.AsyncSqlReaders = factory;
        var sequence = AsyncPlan(transaction.Query().Rows.Select(row => row.Id));
        var unused = sequence.GetAsyncEnumerator();
        await unused.DisposeAsync();
        await using var retained = sequence.GetAsyncEnumerator();
        transaction.Commit();
        await Assert.That(await AsyncEnumerationFailureOf(async () => { await retained.MoveNextAsync(); })).IsTypeOf<InvalidOperationException>();
        await Assert.That(factory.Commands.Sum(c => c.Creates)).IsEqualTo(0);
    }

    [Test]
    [Arguments("SingleOrDefault", false)]
    [Arguments("Single", true)]
    [Arguments("Last", true)]
    public async Task AsyncQueryPlan_DirectProjectionTerminalSemanticsRunAfterCleanup(string terminal, bool two)
    {
        using var fixture = new ScriptedFixture(captureSql: true);
        using var transaction = fixture.Database.Transaction();
        var reader = new ControlledRowDataReader(two ? [[2], [1]] : []) { ColumnNames = ["value"], Cleanup = new(paused: true) };
        fixture.Scenario.AsyncSqlReaders = new ControlledSqlReaderFactory
            { CreateAccess = _ => new() { ReaderOverride = reader, FailureEvidence = TrustedScalarRead } };
        var pending = AsyncPlanTerminal(transaction.Query().Rows.Select(row => row.Id), terminal);
        await reader.Cleanup.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(pending.IsCompleted).IsFalse();
        _ = Capture<InvalidOperationException>(() => transaction.Query());
        reader.Cleanup.Release();
        if (terminal == "Single")
        {
            await Assert.That(await AsyncEnumerationFailureOf(() => pending)).IsTypeOf<InvalidOperationException>();
            await Assert.That(transaction.AsyncFailureContext!.Stage).IsEqualTo(ExecutionFailureStage.Materialization);
        }
        else await Assert.That(await pending).IsEqualTo(two ? 1 : 0);
        _ = transaction.Query();
    }

    private sealed class AsyncPlanBox
    {
        internal static AsyncLocal<Action?> Creating { get; } = new();
        public string Value { get; }
        public int Other { get; }
        public AsyncPlanBox(string value) { Creating.Value?.Invoke(); Value = value; }
        public AsyncPlanBox(string value, int other) : this(value) { Other = other; }
    }
}
