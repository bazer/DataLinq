using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryOrderedSkipTests
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
    public async Task OrderedSkip_ReturnsAscendingAndDescendingSuffixesWithoutMaterializingSkippedRows()
    {
        var ascendingDatabase = CreateAdversarialDatabase();

        var ascending = ascendingDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(2)
            .ToArray();

        await Assert.That(Ids(ascending)).IsEqualTo($"0,17,{int.MaxValue}");
        await Assert.That(ascendingDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(ascendingDatabase.Diagnostics.Materializations).IsEqualTo(3);
        await Assert.That(ascendingDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(3);

        var descendingDatabase = CreateAdversarialDatabase();

        var descending = descendingDatabase.Query().Rows
            .OrderByDescending(static row => row.Id)
            .Skip(3)
            .ToArray();

        await Assert.That(Ids(descending)).IsEqualTo($"-11,{int.MinValue}");
        await Assert.That(descendingDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(descendingDatabase.Diagnostics.Materializations).IsEqualTo(2);
        await Assert.That(descendingDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);
    }

    [Test]
    public async Task OrderedSkip_AppliesWhereBeforeAndBetweenOrdering()
    {
        var beforeDatabase = CreateAdversarialDatabase();
        var groupId = 7;

        var before = beforeDatabase.Query().Rows
            .Where(row => row.GroupId == groupId)
            .OrderBy(static row => row.Id)
            .Skip(1)
            .ToArray();

        await Assert.That(Ids(before)).IsEqualTo($"17,{int.MaxValue}");
        await Assert.That(beforeDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(beforeDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(beforeDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(beforeDatabase.Diagnostics.Materializations).IsEqualTo(2);

        var betweenDatabase = CreateAdversarialDatabase();

        var between = betweenDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Where(row => row.GroupId == groupId)
            .Skip(1)
            .ToArray();

        await Assert.That(Ids(between)).IsEqualTo(Ids(before));
        await Assert.That(betweenDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(betweenDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(betweenDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(betweenDatabase.Diagnostics.Materializations).IsEqualTo(2);
    }

    [Test]
    public async Task OrderedSkip_ZeroExactAndOverCardinalityPreserveExactSelectionBoundaries()
    {
        var zeroDatabase = CreateAdversarialDatabase();

        var zero = zeroDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(0)
            .ToArray();

        await Assert.That(Ids(zero)).IsEqualTo(Ids(AscendingIds));
        await Assert.That(zeroDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(zeroDatabase.Diagnostics.Materializations).IsEqualTo(5);

        var exactDatabase = CreateAdversarialDatabase();

        var exact = exactDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(AscendingIds.Length)
            .ToArray();

        await Assert.That(exact).IsEmpty();
        await Assert.That(exactDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(exactDatabase.Diagnostics.Materializations).IsEqualTo(0);

        var overDatabase = CreateAdversarialDatabase();

        var over = overDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(99)
            .ToArray();

        await Assert.That(over).IsEmpty();
        await Assert.That(overDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(overDatabase.Diagnostics.Materializations).IsEqualTo(0);
    }

    [Test]
    public async Task SkipCount_IsSnapshottedAtQueryConstructionAndRebuiltQueriesCaptureTheNewValue()
    {
        var database = CreateAdversarialDatabase();
        var count = 1;
        var originalQuery = database.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(count);
        count = 3;

        var original = originalQuery.ToArray();
        var rebuilt = database.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(count)
            .ToArray();

        await Assert.That(Ids(original)).IsEqualTo($"-11,0,17,{int.MaxValue}");
        await Assert.That(Ids(rebuilt)).IsEqualTo($"17,{int.MaxValue}");
        await Assert.That(rebuilt[0]).IsSameReferenceAs(original[2]);
        await Assert.That(rebuilt[1]).IsSameReferenceAs(original[3]);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(10);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(4);
    }

    [Test]
    public async Task OrderedSkip_ComposesWithScalarProjectionAndObservesCancellation()
    {
        var projectionDatabase = CreateAdversarialDatabase();

        var projectedGroups = projectionDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(2)
            .Select(static row => row.GroupId)
            .ToArray();

        await Assert.That(string.Join(",", projectedGroups)).IsEqualTo("3,7,7");
        await Assert.That(projectionDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(projectionDatabase.Diagnostics.Materializations).IsEqualTo(0);
        await Assert.That(projectionDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);

        var preCancelledDatabase = CreateAdversarialDatabase();
        var preCancelledQuery = preCancelledDatabase.Query().Rows
            .OrderBy(static row => row.Id)
            .Skip(1);
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
            .Skip(1);
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
    public async Task UnsupportedSkipShapesRejectBeforeMemoryWork()
    {
        var database = CreateAdversarialDatabase();
        var before = database.Diagnostics;

        var bare = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.Skip(1).ToArray());
        var beforeOrdering = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.Skip(1).OrderBy(static row => row.Id).ToArray());
        var takeThenSkip = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id).Take(1).Skip(1).ToArray());
        var repeated = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id).Skip(1).Skip(1).ToArray());
        var negative = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id).Skip(-1).ToArray());
        var nonPrimaryKey = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.GroupId).Skip(1).ToArray());
        var postSkipFilter = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id).Skip(1)
                .Where(static row => row.GroupId == 7)
                .ToArray());
        var terminal = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows.OrderBy(static row => row.Id).Skip(1).First());

        await Assert.That(bare.Feature).IsEqualTo("PagingCompositionShape:Other");
        await Assert.That(beforeOrdering.Feature).IsEqualTo("Operation:Pushdown");
        await Assert.That(takeThenSkip.Feature).IsEqualTo("PagingCompositionShape:TakeBeforeSkipInScope");
        await Assert.That(repeated.Feature).IsEqualTo("PagingCompositionShape:RepeatedSkipInScope");
        await Assert.That(negative.Feature).IsEqualTo("PagingCountShape:Negative");
        await Assert.That(nonPrimaryKey.Feature).IsEqualTo("OrderingShape:Other");
        await Assert.That(postSkipFilter.Feature).IsEqualTo("Operation:Pushdown");
        await Assert.That(terminal.Feature).IsEqualTo("Operation:Pushdown");

        foreach (var exception in new[] { bare, takeThenSkip, repeated })
            await Assert.That(exception.Location).IsEqualTo("operations.pagingComposition.shape");
        await Assert.That(negative.Location).IsEqualTo("operations[1].count.shape");
        await Assert.That(nonPrimaryKey.Location).IsEqualTo("operations.ordering.shape");
        await Assert.That(beforeOrdering.Location).IsEqualTo("operations[0]");
        await Assert.That(postSkipFilter.Location).IsEqualTo("operations[0]");
        await Assert.That(terminal.Location).IsEqualTo("operations[0]");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
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

    private static string Ids(int[] ids) => string.Join(",", ids);

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
