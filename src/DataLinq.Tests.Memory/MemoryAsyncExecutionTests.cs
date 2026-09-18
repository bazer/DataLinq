using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryAsyncExecutionTests
{
    private static readonly Guid First = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid Second = new("10213243-5465-7687-98a9-bacbdcedfe0f");

    private static MemoryDatabase<MemoryPrimitiveDatabase> Primitives() =>
        new MemoryDatabase<MemoryPrimitiveDatabase>().SeedCanonical<MemoryPrimitiveRow>(
            [3, 7, "three"], [1, 7, "one"], [2, 2, "two"], [4, 7, "four"]);

    private static MemoryDatabase<MemoryConvertedDatabase> Converted() =>
        new MemoryDatabase<MemoryConvertedDatabase>().SeedCanonical<MemoryConvertedRow>(
            [First, Second, Second, null], [Second, First, First, First]);

    private static IAsyncEnumerable<T> Rows<T>(IQueryable<T> query, CancellationToken token = default) =>
        ((ExpressionQueryPlanProvider)query.Provider).ExecuteEnumerableAsyncCore<T>(query.Expression, token);

    private static Task<TResult> Terminal<T, TResult>(IQueryable<T> query, string method, CancellationToken token = default) =>
        ((ExpressionQueryPlanProvider)query.Provider).ExecuteAsyncCore<TResult>(
            Expression.Call(typeof(System.Linq.Queryable), method, [typeof(T)], query.Expression), token);

    private static Task<List<T>> List<T>(IQueryable<T> query, CancellationToken token = default) =>
        ((ExpressionQueryPlanProvider)query.Provider).ExecuteListAsyncCore<T>(query.Expression, token);

    private static async Task<List<T>> Drain<T>(IAsyncEnumerable<T> source)
    {
        var values = new List<T>();
        await foreach (var row in source) values.Add(row);
        return values;
    }

    private static async Task<Exception> Failure(Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected a failure.");
    }

    private static Exception Failure(Action action)
    {
        try { action(); }
        catch (Exception exception) { return exception; }
        throw new InvalidOperationException("Expected a failure.");
    }

    [Test]
    public async Task AsyncMemory_OrdinaryCaptureIsColdPerEnumeratorAndMovesCompleteImmediately()
    {
        var database = Primitives();
        var ids = new[] { 3 };
        var sequence = Rows(database.Query().Rows.Where(row => ids.Contains(row.Id)).Select(row => row.Id));
        ids[0] = 1;
        await using var first = sequence.GetAsyncEnumerator();
        ids[0] = 2;
        await using var second = sequence.GetAsyncEnumerator();
        ids[0] = 4;
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        var move = first.MoveNextAsync();
        await Assert.That(move.IsCompletedSuccessfully).IsTrue();
        await Assert.That(await move).IsTrue();
        await Assert.That(first.Current).IsEqualTo(1);
        await Assert.That(await second.MoveNextAsync()).IsTrue();
        await Assert.That(second.Current).IsEqualTo(2);
        await Assert.That(string.Join(",", await Drain(sequence))).IsEqualTo("4");
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMemory_FilterOrderAndPagePreserveScalarAndEntityResults(bool scalar)
    {
        var database = Primitives();
        var query = database.Query().Rows.Where(row => row.GroupId == 7).OrderBy(row => row.Id).Skip(1).Take(2);
        if (scalar)
        {
            var projected = query.Select(row => row.Name);
            await Assert.That(string.Join(",", await List(projected))).IsEqualTo(string.Join(",", projected.ToArray()));
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        }
        else
        {
            var rows = await List(query);
            await Assert.That(string.Join(",", rows.Select(row => row.Id))).IsEqualTo("3,4");
            await Assert.That(rows[0]).IsSameReferenceAs(database.Find<MemoryPrimitiveRow>(3));
            await Assert.That(rows[1]).IsSameReferenceAs(database.Find<MemoryPrimitiveRow>(4));
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncMemory_AnyAndCountKeepShortCircuitingAndAvoidMaterialization(bool count, bool scalar)
    {
        var database = Primitives();
        var query = database.Query().Rows.Where(row => row.GroupId == 7);
        if (count)
        {
            var pending = scalar ? Terminal<int, int>(query.Select(row => row.Id), "Count") : Terminal<MemoryPrimitiveRow, int>(query, "Count");
            await Assert.That(pending.IsCompletedSuccessfully).IsTrue();
            await Assert.That(await pending).IsEqualTo(3);
        }
        else
        {
            var pending = scalar ? Terminal<int, bool>(query.Select(row => row.Id), "Any") : Terminal<MemoryPrimitiveRow, bool>(query, "Any");
            await Assert.That(pending.IsCompletedSuccessfully).IsTrue();
            await Assert.That(await pending).IsTrue();
        }
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(count ? 4L : 1L);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);
    }

    [Test]
    [Arguments("Single", 0, false)]
    [Arguments("Single", 1, false)]
    [Arguments("Single", 2, false)]
    [Arguments("SingleOrDefault", 0, false)]
    [Arguments("SingleOrDefault", 2, false)]
    [Arguments("First", 0, false)]
    [Arguments("First", 2, false)]
    [Arguments("FirstOrDefault", 0, false)]
    [Arguments("Single", 0, true)]
    [Arguments("Single", 1, true)]
    [Arguments("Single", 2, true)]
    [Arguments("SingleOrDefault", 0, true)]
    [Arguments("First", 2, true)]
    [Arguments("FirstOrDefault", 0, true)]
    public async Task AsyncMemory_ElementTerminalsMatchCardinalityAndDefaultSemantics(string method, int count, bool scalar)
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        database.SeedCanonical<MemoryPrimitiveRow>(Enumerable.Range(1, count).Reverse().Select(id => new object?[] { id, 7, "row" }).ToArray());
        IQueryable<MemoryPrimitiveRow> query = database.Query().Rows;
        if (method.StartsWith("First", StringComparison.Ordinal)) query = query.OrderBy(row => row.Id);
        var shouldFail = count == 0 && !method.EndsWith("OrDefault", StringComparison.Ordinal) || count > 1 && method.StartsWith("Single", StringComparison.Ordinal);
        if (scalar)
        {
            var pending = Terminal<int, int>(query.Select(row => row.Id), method);
            if (shouldFail) await Assert.That(await Failure(() => pending)).IsTypeOf<InvalidOperationException>();
            else await Assert.That(await pending).IsEqualTo(count == 0 ? 0 : 1);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        }
        else
        {
            var pending = Terminal<MemoryPrimitiveRow, MemoryPrimitiveRow?>(query, method);
            if (shouldFail)
            {
                await Assert.That(await Failure(() => pending)).IsTypeOf<InvalidOperationException>();
                await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
            }
            else if (count == 0) await Assert.That(await pending).IsNull();
            else await Assert.That((await pending)!.Id).IsEqualTo(1);
        }
    }

    [Test]
    [Arguments("last")]
    [Arguments("take")]
    [Arguments("order")]
    [Arguments("projection")]
    [Arguments("first")]
    [Arguments("paged-count")]
    [Arguments("sum")]
    [Arguments("join")]
    public async Task AsyncMemory_UnsupportedShapesStillRejectBeforeCancellationAndStoreWork(string shape)
    {
        var database = Primitives();
        var query = database.Query().Rows;
        var before = database.Diagnostics;
        Action sync;
        Func<Task> async;
        switch (shape)
        {
            case "last": sync = () => query.Last(); async = () => Terminal<MemoryPrimitiveRow, MemoryPrimitiveRow>(query, "Last", new(true)); break;
            case "first": sync = () => query.First(); async = () => Terminal<MemoryPrimitiveRow, MemoryPrimitiveRow>(query, "First", new(true)); break;
            case "take": sync = () => query.Take(1).ToArray(); async = () => List(query.Take(1), new(true)); break;
            case "order": sync = () => query.OrderBy(row => row.Name).ToArray(); async = () => List(query.OrderBy(row => row.Name), new(true)); break;
            case "projection": sync = () => query.Select(row => new { row.Id, row.Name }).ToArray(); async = () => List(query.Select(row => new { row.Id, row.Name }), new(true)); break;
            case "sum":
                sync = () => query.Select(row => row.Id).Sum();
                async = () => ((ExpressionQueryPlanProvider)query.Provider).ExecuteAsyncCore<int>(
                    Expression.Call(typeof(System.Linq.Queryable), "Sum", null, query.Select(row => row.Id).Expression), new(true));
                break;
            case "join":
                var joined = query.Join(query, left => left.Id, right => right.Id, (left, right) => left);
                sync = () => joined.ToArray(); async = () => List(joined, new(true)); break;
            default: sync = () => query.OrderBy(row => row.Id).Take(1).Count(); async = () => Terminal<MemoryPrimitiveRow, int>(query.OrderBy(row => row.Id).Take(1), "Count", new(true)); break;
        }
        var expected = Failure(sync);
        var actual = await Failure(async);
        await Assert.That(actual.GetType()).IsEqualTo(expected.GetType());
        await Assert.That(actual is QueryBackendCapabilityException or QueryTranslationException).IsTrue();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task AsyncMemory_PreCancellationCoversEmptyAndWarmSourcesWithoutExecuting(bool warm, bool method)
    {
        var database = warm ? Primitives() : new MemoryDatabase<MemoryPrimitiveDatabase>();
        if (warm) _ = database.Find<MemoryPrimitiveRow>(3);
        var before = database.Diagnostics;
        await using var rows = Rows(database.Query().Rows, method ? new(true) : default).GetAsyncEnumerator(method ? default : new(true));
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        var move = rows.MoveNextAsync();
        await Assert.That(move.IsCanceled).IsTrue();
        await Assert.That(await Failure(() => move.AsTask())).IsAssignableTo<OperationCanceledException>();
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMemory_BothTokensApplyAfterAPausedConsumerAndAlreadyDeliveredRowsRemainValid(bool method)
    {
        var database = Primitives();
        using var methodToken = new CancellationTokenSource();
        using var enumerationToken = new CancellationTokenSource();
        await using var rows = Rows(database.Query().Rows.OrderBy(row => row.Id), methodToken.Token).GetAsyncEnumerator(enumerationToken.Token);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        var delivered = rows.Current;
        var before = database.Diagnostics;
        // Consumer suspension is legitimate even though Memory's own move is immediate.
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = NextAfterConsumer();
        (method ? methodToken : enumerationToken).Cancel();
        await Assert.That(pending.IsCompleted).IsFalse();
        resume.SetResult();
        await Assert.That(await Failure(() => pending)).IsAssignableTo<OperationCanceledException>();
        await Assert.That(delivered.Id).IsEqualTo(1);
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(Failure(() => { _ = rows.Current; })).IsTypeOf<InvalidOperationException>();
        async Task NextAfterConsumer() { await resume.Task; _ = await rows.MoveNextAsync(); }
    }

    [Test]
    public async Task AsyncMemory_UnusedAndCompletedEnumeratorsDisposeLocallyAndStayCold()
    {
        var database = Primitives();
        var before = database.Diagnostics;
        var rows = Rows(database.Query().Rows).GetAsyncEnumerator();
        await Assert.That(Failure(() => { _ = rows.Current; })).IsTypeOf<InvalidOperationException>();
        await Assert.That(rows.DisposeAsync().IsCompletedSuccessfully).IsTrue();
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
        await rows.DisposeAsync();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That((await List(database.Query().Rows)).Count).IsEqualTo(4);
    }

    [Test]
    public async Task AsyncMemory_ConvertedQueryNormalizesBindingsOnceAndPreservesModelValues()
    {
        using var observation = new MemoryGuidIdConverter.Observation();
        var database = Converted();
        var id = new MemoryGuidId(First);
        var sequence = Rows(database.Query().Rows.Where(row => row.Id == id).Select(row => row.Id));
        await using var rows = sequence.GetAsyncEnumerator();
        id = new(Second);
        // Parsing captures the model-valued struct now; the existing local execution
        // plan normalizes that captured value when the first move compiles it.
        await Assert.That(observation.ToProviderColumns.Count).IsEqualTo(0);
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current).IsEqualTo(new MemoryGuidId(First));
        await Assert.That(observation.FromProviderColumns.Count).IsEqualTo(1);
        await Assert.That(observation.ToProviderColumns.Count).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMemory_ConverterCancellationCannotReturnAPartialMaterializedCollection(bool scalar)
    {
        using var observation = new MemoryGuidIdConverter.Observation();
        var database = Converted();
        using var cancellation = new CancellationTokenSource();
        var conversions = 0;
        observation.FromProvider = column => { if (column == "id" && ++conversions == 2) cancellation.Cancel(); };
        Task pending = scalar ? List(database.Query().Rows.Select(row => row.Id), cancellation.Token) : List(database.Query().Rows, cancellation.Token);
        await Assert.That(pending.IsCompleted).IsTrue();
        await Assert.That(await Failure(() => pending)).IsAssignableTo<OperationCanceledException>();
        await Assert.That(conversions).IsEqualTo(2);
        // Valid individual models may survive cancellation; no collection was returned.
        await Assert.That(database.GetMaterializedRowCount<MemoryConvertedRow>()).IsEqualTo(scalar ? 0 : 2);
        observation.FromProvider = null;
        await Assert.That((await List(database.Query().Rows)).Count).IsEqualTo(2);
    }

    [Test]
    public async Task AsyncMemory_ReentrantMoveCurrentAndDisposalCannotCorruptTheActiveMove()
    {
        using var observation = new MemoryGuidIdConverter.Observation();
        var database = Converted();
        await using var rows = Rows(database.Query().Rows.Select(row => row.Id)).GetAsyncEnumerator();
        var failures = new List<Exception>();
        observation.FromProvider = column =>
        {
            failures.Add(Failure(() => { _ = rows.MoveNextAsync(); }));
            failures.Add(Failure(() => { _ = rows.Current; }));
            failures.Add(Failure(() => { _ = rows.DisposeAsync(); }));
        };
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current).IsEqualTo(new MemoryGuidId(First));
        await Assert.That(failures.Count).IsEqualTo(3);
        await Assert.That(failures.All(failure => failure is InvalidOperationException)).IsTrue();
        observation.FromProvider = null;
        await Assert.That(await rows.MoveNextAsync()).IsTrue();
        await Assert.That(rows.Current).IsEqualTo(new MemoryGuidId(Second));
        await Assert.That(await rows.MoveNextAsync()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMemory_PredicateConversionFailureOrCancellationNeverVisitsTheStore(bool cancel)
    {
        using var observation = new MemoryGuidIdConverter.Observation();
        var database = Converted();
        using var cancellation = new CancellationTokenSource();
        var id = new MemoryGuidId(First);
        observation.ToProvider = column =>
        {
            if (cancel) cancellation.Cancel();
            else throw new Exception("private-predicate-converter");
        };
        var failure = await Failure(() => List(database.Query().Rows.Where(row => row.Id == id), cancellation.Token));
        await Assert.That(cancel ? failure is OperationCanceledException : failure is QueryTranslationException).IsTrue();
        await Assert.That(failure.ToString()).DoesNotContain("private-predicate-converter");
        await Assert.That(observation.ToProviderColumns.Count).IsEqualTo(1);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AsyncMemory_EmptyScalarsHonorCancellationAndSuccessfulResultsStaySettled(bool count)
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var before = database.Diagnostics;
        Task pending = count ? Terminal<MemoryPrimitiveRow, int>(database.Query().Rows, "Count", new(true))
            : Terminal<MemoryPrimitiveRow, bool>(database.Query().Rows, "Any", new(true));
        await Assert.That(pending.IsCanceled).IsTrue();
        await Assert.That(await Failure(() => pending)).IsAssignableTo<OperationCanceledException>();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        using var cancellation = new CancellationTokenSource();
        if (count)
        {
            var success = Terminal<MemoryPrimitiveRow, int>(database.Query().Rows, "Count", cancellation.Token);
            await Assert.That(success.IsCompletedSuccessfully).IsTrue();
            cancellation.Cancel();
            await Assert.That(await success).IsEqualTo(0);
        }
        else
        {
            var success = Terminal<MemoryPrimitiveRow, bool>(database.Query().Rows, "Any", cancellation.Token);
            await Assert.That(success.IsCompletedSuccessfully).IsTrue();
            cancellation.Cancel();
            await Assert.That(await success).IsFalse();
        }
    }

    [Test]
    public async Task AsyncMemory_SameTokenAndSimultaneousIndependentEnumeratorsKeepSeparatePositions()
    {
        var database = Primitives();
        using var cancellation = new CancellationTokenSource();
        var sequence = Rows(database.Query().Rows, cancellation.Token);
        await using var first = sequence.GetAsyncEnumerator(cancellation.Token);
        await using var second = sequence.GetAsyncEnumerator();
        await Assert.That(await first.MoveNextAsync()).IsTrue();
        await Assert.That(await first.MoveNextAsync()).IsTrue();
        await Assert.That(await second.MoveNextAsync()).IsTrue();
        await Assert.That(first.Current.Id).IsEqualTo(1);
        await Assert.That(second.Current.Id).IsEqualTo(3);
        cancellation.Cancel();
        var failure = await Failure(() => first.MoveNextAsync().AsTask());
        await Assert.That(((OperationCanceledException)failure).CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(await Failure(() => second.MoveNextAsync().AsTask())).IsAssignableTo<OperationCanceledException>();
        await Assert.That((await List(database.Query().Rows)).Count).IsEqualTo(4);
    }

    [Test]
    [Arguments(false, "cancel")]
    [Arguments(false, "requested-cancel")]
    [Arguments(false, "fatal")]
    [Arguments(true, "cancel")]
    [Arguments(true, "requested-cancel")]
    [Arguments(true, "fatal")]
    public async Task AsyncMemory_UserCancellationAndFatalFailuresPreserveExceptionIdentity(bool lookup, string kind)
    {
        using var observation = new MemoryGuidIdConverter.Observation();
        var database = Converted();
        using var cancellation = new CancellationTokenSource();
        if (kind == "requested-cancel") cancellation.Cancel();
        Exception expected = kind == "fatal" ? new OutOfMemoryException("local converter")
            : new OperationCanceledException("user cancellation", cancellation.Token);
        observation.FromProvider = column => throw expected;
        await using var rows = Rows(database.Query().Rows.Select(row => row.Id)).GetAsyncEnumerator();
        Task pending = lookup ? database.FindAsyncCore<MemoryConvertedRow>(new MemoryGuidId(First)).AsTask()
            : rows.MoveNextAsync().AsTask();
        await Assert.That(pending.IsCompleted).IsTrue();
        await Assert.That(pending.IsCanceled).IsEqualTo(kind != "fatal");
        await Assert.That(await Failure(() => pending)).IsSameReferenceAs(expected);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        observation.FromProvider = null;
        await Assert.That((await List(database.Query().Rows)).Count).IsEqualTo(2);
    }

    [Test]
    [Arguments("null")]
    [Arguments("type")]
    [Arguments("canonical")]
    [Arguments("composite")]
    public async Task AsyncMemory_FindValidationWinsOverCancellationWithoutStoreWork(string kind)
    {
        var database = Primitives();
        var composite = new MemoryDatabase<MemoryCompositeDatabase>();
        var before = database.Diagnostics;
        var beforeComposite = composite.Diagnostics;
        var failure = await Failure(async () =>
        {
            if (kind == "composite") _ = await composite.FindAsyncCore<MemoryCompositeRow>("private-key", new(true));
            else _ = await database.FindAsyncCore<MemoryPrimitiveRow>(kind switch { "null" => null!, "canonical" => DataLinqKey.FromValue(3), _ => "private-key" }, new(true));
        });
        await Assert.That(kind == "null" ? failure is ArgumentNullException : failure is MemoryLookupException).IsTrue();
        await Assert.That(failure.ToString()).DoesNotContain("private-key");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(composite.Diagnostics).IsEqualTo(beforeComposite);
    }

    [Test]
    public async Task AsyncMemory_FindHitMissAndWarmIdentityCompleteImmediatelyButHonorCancellation()
    {
        var database = Primitives();
        var cold = database.FindAsyncCore<MemoryPrimitiveRow>(3);
        await Assert.That(cold.IsCompletedSuccessfully).IsTrue();
        var row = await cold;
        await Assert.That(await database.FindAsyncCore<MemoryPrimitiveRow>(3)).IsSameReferenceAs(row);
        await Assert.That(database.Find<MemoryPrimitiveRow>(3)).IsSameReferenceAs(row);
        await Assert.That(await database.FindAsyncCore<MemoryPrimitiveRow>(99)).IsNull();
        var before = database.Diagnostics;
        await Assert.That(await Failure(async () => { _ = await database.FindAsyncCore<MemoryPrimitiveRow>(3, new(true)); })).IsAssignableTo<OperationCanceledException>();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [Arguments("success")]
    [Arguments("binding")]
    [Arguments("materialization")]
    [Arguments("binding-cancel")]
    [Arguments("materialization-cancel")]
    public async Task AsyncMemory_FindPreservesConversionRedactionCancellationAndRecovery(string mode)
    {
        using var observation = new MemoryGuidIdConverter.Observation();
        var database = Converted();
        using var cancellation = new CancellationTokenSource();
        var expected = new Exception("private-converter-detail");
        observation.ToProvider = _ => { if (mode == "binding") throw expected; if (mode == "binding-cancel") cancellation.Cancel(); };
        observation.FromProvider = _ => { if (mode == "materialization") throw expected; if (mode == "materialization-cancel") cancellation.Cancel(); };
        if (mode == "success")
        {
            var row = await database.FindAsyncCore<MemoryConvertedRow>(new MemoryGuidId(First));
            await Assert.That(row!.Id).IsEqualTo(new MemoryGuidId(First));
            await Assert.That(observation.ToProviderColumns.Count).IsEqualTo(1);
            await Assert.That(observation.FromProviderColumns.Count).IsEqualTo(2);
            await Assert.That(await database.FindAsyncCore<MemoryConvertedRow>(new MemoryGuidId(First))).IsSameReferenceAs(row);
            await Assert.That(observation.ToProviderColumns.Count).IsEqualTo(2);
            await Assert.That(observation.FromProviderColumns.Count).IsEqualTo(2);
        }
        else
        {
            var failure = await Failure(async () => { _ = await database.FindAsyncCore<MemoryConvertedRow>(new MemoryGuidId(First), cancellation.Token); });
            await Assert.That(mode.EndsWith("-cancel", StringComparison.Ordinal) ? failure is OperationCanceledException : failure is MemoryLookupException).IsTrue();
            await Assert.That(failure.ToString()).DoesNotContain("private-converter-detail");
            await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(mode.StartsWith("binding", StringComparison.Ordinal) ? 0L : 1L);
            await Assert.That(database.GetMaterializedRowCount<MemoryConvertedRow>()).IsEqualTo(mode == "materialization-cancel" ? 1 : 0);
            observation.ToProvider = null;
            observation.FromProvider = null;
            await Assert.That(await database.FindAsyncCore<MemoryConvertedRow>(new MemoryGuidId(First))).IsNotNull();
        }
    }
}
