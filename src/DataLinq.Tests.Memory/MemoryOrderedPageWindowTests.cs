using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryOrderedPageWindowTests
{
    private static readonly int[] AscendingIds =
    [
        int.MinValue,
        -11,
        0,
        17,
        int.MaxValue
    ];

    [Test]
    public async Task OrderedPageWindow_ReturnsAscendingAndDescendingEntitiesWithSelectedOnlyMaterialization()
    {
        var ascendingDatabase = CreateAdversarialDatabase();
        var cachedAscending = ascendingDatabase.FindCanonical<MemoryPrimitiveRow>(DataLinqKey.FromValue(-11));
        var beforeAscending = ascendingDatabase.Diagnostics;

        var ascending = ascendingDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(1)
            .Take(2)
            .ToArray();

        await Assert.That(Ids(ascending)).IsEqualTo("-11,0");
        await Assert.That(ascending[0]).IsSameReferenceAs(cachedAscending);
        await Assert.That(ascendingDatabase.Diagnostics.ScanRowsVisited)
            .IsEqualTo(beforeAscending.ScanRowsVisited + 5);
        await Assert.That(ascendingDatabase.Diagnostics.Materializations)
            .IsEqualTo(beforeAscending.Materializations + 1);
        await Assert.That(ascendingDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);

        var descendingDatabase = CreateAdversarialDatabase();
        var cachedDescending = descendingDatabase.FindCanonical<MemoryPrimitiveRow>(DataLinqKey.FromValue(17));
        var beforeDescending = descendingDatabase.Diagnostics;

        var descending = descendingDatabase.Query().Rows
            .OrderByDescending(static row => row.Id)
            .Skip(1)
            .Take(2)
            .ToArray();

        await Assert.That(Ids(descending)).IsEqualTo("17,0");
        await Assert.That(descending[0]).IsSameReferenceAs(cachedDescending);
        await Assert.That(descendingDatabase.Diagnostics.ScanRowsVisited)
            .IsEqualTo(beforeDescending.ScanRowsVisited + 5);
        await Assert.That(descendingDatabase.Diagnostics.Materializations)
            .IsEqualTo(beforeDescending.Materializations + 1);
        await Assert.That(descendingDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);
    }

    [Test]
    public async Task OrderedPageWindow_AdmitsWhereBeforeOrderingAndBetweenOrderingAndSkipWithScalarProjection()
    {
        var beforeDatabase = CreateAdversarialDatabase();
        var groupId = 7;

        var before = beforeDatabase.Query().Rows
            .Where(row => row.GroupId == groupId)
            .OrderBy(static row => row.Id)
            .Skip(1)
            .Take(1)
            .ToArray();

        await Assert.That(Ids(before)).IsEqualTo("17");
        await Assert.That(beforeDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(beforeDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(beforeDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(beforeDatabase.Diagnostics.Materializations).IsEqualTo(1);

        var betweenDatabase = CreateAdversarialDatabase();

        var between = betweenDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Where(row => row.GroupId == groupId)
            .Skip(1)
            .Take(1)
            .ToArray();

        await Assert.That(Ids(between)).IsEqualTo(Ids(before));
        await Assert.That(betweenDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(betweenDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(betweenDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(betweenDatabase.Diagnostics.Materializations).IsEqualTo(1);

        var projectionDatabase = CreateAdversarialDatabase();
        var projectedGroups = projectionDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(1)
            .Take(3)
            .Select(static row => row.GroupId)
            .ToArray();

        await Assert.That(string.Join(",", projectedGroups)).IsEqualTo("7,3,7");
        await Assert.That(projectionDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(projectionDatabase.Diagnostics.Materializations).IsEqualTo(0);
        await Assert.That(projectionDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    public async Task OrderedPageWindow_HandlesZeroExactAndOverBoundariesAndStillRejectsNegativeSkip()
    {
        var takeZeroDatabase = CreateAdversarialDatabase();
        var takeZero = takeZeroDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(2)
            .Take(0)
            .ToArray();

        await Assert.That(takeZero).IsEmpty();
        await Assert.That(takeZeroDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(takeZeroDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(0);
        await Assert.That(takeZeroDatabase.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(takeZeroDatabase.Diagnostics.Materializations).IsEqualTo(0);

        var skipZeroDatabase = CreateAdversarialDatabase();
        var skipZero = skipZeroDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(0)
            .Take(2)
            .ToArray();

        await Assert.That(Ids(skipZero)).IsEqualTo($"{int.MinValue},-11");
        await Assert.That(skipZeroDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(skipZeroDatabase.Diagnostics.Materializations).IsEqualTo(2);

        var exactWindowDatabase = CreateAdversarialDatabase();
        var exactWindow = exactWindowDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(2)
            .Take(3)
            .ToArray();

        await Assert.That(Ids(exactWindow)).IsEqualTo($"0,17,{int.MaxValue}");
        await Assert.That(exactWindowDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(exactWindowDatabase.Diagnostics.Materializations).IsEqualTo(3);

        var overTakeDatabase = CreateAdversarialDatabase();
        var overTake = overTakeDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(3)
            .Take(99)
            .ToArray();

        await Assert.That(Ids(overTake)).IsEqualTo($"17,{int.MaxValue}");
        await Assert.That(overTakeDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(overTakeDatabase.Diagnostics.Materializations).IsEqualTo(2);

        foreach (var skip in new[] { AscendingIds.Length, 99 })
        {
            var emptyDatabase = CreateAdversarialDatabase();
            var empty = emptyDatabase.Query().Rows
                .OrderBy(static row => row.Id)
                .Skip(skip)
                .Take(2)
                .ToArray();

            await Assert.That(empty).IsEmpty();
            await Assert.That(emptyDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
            await Assert.That(emptyDatabase.Diagnostics.Materializations).IsEqualTo(0);
        }

        var negativeDatabase = CreateAdversarialDatabase();
        var beforeNegative = negativeDatabase.Diagnostics;
        var negative = Capture<QueryBackendCapabilityException>(() =>
            negativeDatabase.Query().Rows
                .OrderBy(static row => row.Id)
                .Skip(-1)
                .Take(0)
                .ToArray());

        await Assert.That(negative.Feature).IsEqualTo("PagingCountShape:Negative");
        await Assert.That(negative.Location).IsEqualTo("operations[1].count.shape");
        await Assert.That(negativeDatabase.Diagnostics).IsEqualTo(beforeNegative);
        await Assert.That(negativeDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    public async Task PageCounts_AreIndependentlySnapshottedAndRebuiltQueriesCaptureNewValues()
    {
        var skipDatabase = CreateAdversarialDatabase();
        var skip = 1;
        var skipQuery = skipDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(skip)
            .Take(2);
        skip = 2;

        var originalSkip = skipQuery.ToArray();
        var rebuiltSkip = skipDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(skip)
            .Take(2)
            .ToArray();

        await Assert.That(Ids(originalSkip)).IsEqualTo("-11,0");
        await Assert.That(Ids(rebuiltSkip)).IsEqualTo("0,17");
        await Assert.That(rebuiltSkip[0]).IsSameReferenceAs(originalSkip[1]);
        await Assert.That(skipDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(10);
        await Assert.That(skipDatabase.Diagnostics.Materializations).IsEqualTo(3);

        var takeDatabase = CreateAdversarialDatabase();
        var take = 1;
        var takeQuery = takeDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(1)
            .Take(take);
        take = 3;

        var originalTake = takeQuery.ToArray();
        var rebuiltTake = takeDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(1)
            .Take(take)
            .ToArray();

        await Assert.That(Ids(originalTake)).IsEqualTo("-11");
        await Assert.That(Ids(rebuiltTake)).IsEqualTo("-11,0,17");
        await Assert.That(rebuiltTake[0]).IsSameReferenceAs(originalTake[0]);
        await Assert.That(takeDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(10);
        await Assert.That(takeDatabase.Diagnostics.Materializations).IsEqualTo(3);
    }

    [Test]
    public async Task OrderedPageWindow_ObservesPreCancellationAndCancellationBetweenSelectedMaterializations()
    {
        var preCancelledDatabase = CreateAdversarialDatabase();
        var preCancelledQuery = preCancelledDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(1)
            .Take(2);
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        var preCancelledException = Capture<OperationCanceledException>(() =>
            preCancelledDatabase.Execute(preCancelledQuery, preCancelled.Token));

        await Assert.That(preCancelledException.CancellationToken).IsEqualTo(preCancelled.Token);
        await Assert.That(preCancelledDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(preCancelledDatabase.Diagnostics.Materializations).IsEqualTo(0);

        var midQueryDatabase = CreateAdversarialDatabase();
        var midQuery = midQueryDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(1)
            .Take(2);
        using var cancellation = new CancellationTokenSource();
        using var rows = midQueryDatabase.Execute(midQuery, cancellation.Token).GetEnumerator();

        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current.Id).IsEqualTo(-11);
        await Assert.That(midQueryDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(midQueryDatabase.Diagnostics.Materializations).IsEqualTo(1);
        cancellation.Cancel();

        var midQueryException = Capture<OperationCanceledException>(() => rows.MoveNext());

        await Assert.That(midQueryException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(midQueryDatabase.Diagnostics.Materializations).IsEqualTo(1);
    }

    [Test]
    public async Task UnsupportedPageWindowShapesRejectWithRedactedDiagnosticsBeforeMemoryWork()
    {
        const int firstCount = 197531;
        const int secondCount = 864209;
        var database = CreateAdversarialDatabase();
        var before = database.Diagnostics;

        var unordered = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.Skip(firstCount).Take(secondCount).ToArray());
        var takeBeforeSkip = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id)
                .Take(firstCount)
                .Skip(secondCount)
                .ToArray());
        var repeatedSkip = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id)
                .Skip(firstCount)
                .Skip(secondCount)
                .Take(1)
                .ToArray());
        var repeatedTake = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id)
                .Skip(1)
                .Take(firstCount)
                .Take(secondCount)
                .ToArray());
        var negative = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id)
                .Skip(-firstCount)
                .Take(1)
                .ToArray());
        var nonExactSkip = Capture<QueryBackendCapabilityException>(() =>
            ExecuteManualPage(database, (long)firstCount, typeof(long), 1, typeof(int)));
        var nonExactTake = Capture<QueryBackendCapabilityException>(() =>
            ExecuteManualPage(database, 1, typeof(int), (long)secondCount, typeof(long)));
        var nonPrimaryKey = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.GroupId)
                .Skip(firstCount)
                .Take(secondCount)
                .ToArray());
        var filterBetweenSkipAndTake = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id)
                .Skip(firstCount)
                .Where(static row => row.GroupId == 7)
                .Take(secondCount)
                .ToArray());
        var postPagingFilter = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id)
                .Skip(firstCount)
                .Take(secondCount)
                .Where(static row => row.GroupId == 7)
                .ToArray());
        var terminal = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id)
                .Skip(firstCount)
                .Take(secondCount)
                .First());

        await Assert.That(unordered.Feature).IsEqualTo("PagingCompositionShape:Other");
        await Assert.That(takeBeforeSkip.Feature).IsEqualTo("PagingCompositionShape:TakeBeforeSkipInScope");
        await Assert.That(repeatedSkip.Feature).IsEqualTo("PagingCompositionShape:RepeatedSkipInScope");
        await Assert.That(repeatedTake.Feature).IsEqualTo("PagingCompositionShape:RepeatedTakeInScope");
        await Assert.That(negative.Feature).IsEqualTo("PagingCountShape:Negative");
        await Assert.That(nonExactSkip.Feature).IsEqualTo("PagingCountShape:NonNegative");
        await Assert.That(nonExactTake.Feature).IsEqualTo("PagingCountShape:NonNegative");
        await Assert.That(nonPrimaryKey.Feature).IsEqualTo("OrderingShape:Other");
        await Assert.That(filterBetweenSkipAndTake.Feature).IsEqualTo("PagingCompositionShape:Other");
        await Assert.That(postPagingFilter.Feature).IsEqualTo("Operation:Pushdown");
        await Assert.That(terminal.Feature).IsEqualTo("Operation:Pushdown");

        foreach (var exception in new[] { unordered, takeBeforeSkip, repeatedSkip, repeatedTake })
            await Assert.That(exception.Location).IsEqualTo("operations.pagingComposition.shape");
        await Assert.That(negative.Location).IsEqualTo("operations[1].count.shape");
        await Assert.That(nonExactSkip.Location).IsEqualTo("operations[1].count.shape");
        await Assert.That(nonExactTake.Location).IsEqualTo("operations[2].count.shape");
        await Assert.That(nonPrimaryKey.Location).IsEqualTo("operations.ordering.shape");
        await Assert.That(filterBetweenSkipAndTake.Location).IsEqualTo("operations.pagingComposition.shape");
        await Assert.That(postPagingFilter.Location).IsEqualTo("operations[0]");
        await Assert.That(terminal.Location).IsEqualTo("operations[0]");

        foreach (var exception in new[]
                 {
                     unordered,
                     takeBeforeSkip,
                     repeatedSkip,
                     repeatedTake,
                     negative,
                     nonExactSkip,
                     nonExactTake,
                     nonPrimaryKey,
                     filterBetweenSkipAndTake,
                     postPagingFilter,
                     terminal
                 })
        {
            await Assert.That(exception.Message).DoesNotContain(firstCount.ToString());
            await Assert.That(exception.Message).DoesNotContain(secondCount.ToString());
        }

        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    private static void ExecuteManualPage(
        MemoryDatabase<MemoryPrimitiveDatabase> database,
        object skipCount,
        Type skipType,
        object takeCount,
        Type takeType)
    {
        var table = database.Metadata.GetTableModel(typeof(MemoryPrimitiveRow)).Table;
        var source = new QueryPlanSourceSlot(
            "s0",
            "t0",
            table,
            typeof(MemoryPrimitiveRow),
            QueryPlanSourceKind.RootTable,
            QueryPlanSourceCardinality.Many,
            IsNullable: false);
        var capture = new QueryPlanBindingCapture();
        var skip = capture.CaptureScalar(skipCount, skipType);
        var take = capture.CaptureScalar(takeCount, takeType);
        var id = table.GetColumnByPropertyName(nameof(MemoryPrimitiveRow.Id));
        var template = new QueryPlanTemplate(
            [source],
            [
                new QueryPlanOperation.OrderBy([
                    new QueryPlanOrdering(
                        new QueryPlanColumnValue(source, id),
                        QueryPlanOrderingDirection.Ascending)
                ]),
                new QueryPlanOperation.Skip(skip),
                new QueryPlanOperation.Take(take)
            ],
            new QueryPlanProjection.Entity(source),
            QueryPlanResult.Sequence(typeof(MemoryPrimitiveRow)),
            capture.CreateDeclarations(),
            capture.CreateSpecialization());
        var invocation = QueryPlanInvocation.Bind(template, capture.InvocationValues);

        _ = ExpressionQueryPlanExecutor
            .ExecuteEnumerable<MemoryPrimitiveRow>(database.ReadSource, invocation)
            .ToArray();
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateAdversarialDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        return database.SeedCanonical<MemoryPrimitiveRow>(
            CreateCanonicalRow(database, id: 17, groupId: 7, name: "seventeen"),
            CreateCanonicalRow(database, id: int.MinValue, groupId: 3, name: "minimum"),
            CreateCanonicalRow(database, id: int.MaxValue, groupId: 7, name: "maximum"),
            CreateCanonicalRow(database, id: -11, groupId: 7, name: "negative-eleven"),
            CreateCanonicalRow(database, id: 0, groupId: 3, name: "zero"));
    }

    private static object?[] CreateCanonicalRow(
        MemoryDatabase<MemoryPrimitiveDatabase> database,
        int id,
        int groupId,
        string name)
    {
        var table = database.Metadata.GetTableModel(typeof(MemoryPrimitiveRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = id;
        values[table.GetColumnByDbName("group_id").Index] = groupId;
        values[table.GetColumnByDbName("name").Index] = name;
        return values;
    }

    private static string Ids(MemoryPrimitiveRow[] rows) =>
        string.Join(",", rows.Select(static row => row.Id));

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            $"Expected exception of type '{typeof(TException).Name}'.");
    }
}
