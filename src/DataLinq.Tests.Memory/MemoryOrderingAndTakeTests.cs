using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Memory;
using DataLinq.Mutation;

namespace DataLinq.Tests.Memory;

public sealed class MemoryOrderingAndTakeTests
{
    private static readonly int[] AscendingIds =
    [
        int.MinValue,
        -11,
        0,
        17,
        int.MaxValue
    ];

    private static readonly int[] DescendingIds =
    [
        int.MaxValue,
        17,
        0,
        -11,
        int.MinValue
    ];

    [Test]
    public async Task PrimaryKeyOrdering_UsesCanonicalOrderAndFinalPrimaryOrderingResetsEarlierKeys()
    {
        var ascendingDatabase = CreateAdversarialDatabase();
        var cachedMinimum = ascendingDatabase.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(int.MinValue));

        var ascending = ascendingDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .ToArray();

        await Assert.That(Ids(ascending)).IsEqualTo(Ids(AscendingIds));
        await Assert.That(ascending[0]).IsSameReferenceAs(cachedMinimum);
        await Assert.That(ascendingDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(ascendingDatabase.Diagnostics.Materializations).IsEqualTo(5);
        await Assert.That(ascendingDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(5);

        var descendingDatabase = CreateAdversarialDatabase();
        var descending = descendingDatabase.Model.Rows
            .OrderByDescending(static row => row.Id)
            .ToArray();

        await Assert.That(Ids(descending)).IsEqualTo(Ids(DescendingIds));
        await Assert.That(descendingDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(descendingDatabase.Diagnostics.Materializations).IsEqualTo(5);

        var resetDatabase = CreateAdversarialDatabase();
        var groupId = 7;
        var reset = resetDatabase.Model.Rows
            .OrderBy(static row => row.GroupId)
            .Where(row => row.GroupId == groupId)
            .OrderByDescending(static row => row.Id)
            .ToArray();

        await Assert.That(Ids(reset)).IsEqualTo($"{int.MaxValue},17,-11");
        await Assert.That(resetDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(resetDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(resetDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(resetDatabase.Diagnostics.Materializations).IsEqualTo(3);
    }

    [Test]
    public async Task OrderedTake_AppliesWhereBeforeOrderingAndBetweenOrderingAndTake()
    {
        var beforeDatabase = CreateAdversarialDatabase();
        var cached = beforeDatabase.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(-11));
        var groupId = 7;

        var before = beforeDatabase.Model.Rows
            .Where(row => row.GroupId == groupId)
            .OrderBy(static row => row.Id)
            .Take(2)
            .ToArray();

        await Assert.That(Ids(before)).IsEqualTo("-11,17");
        await Assert.That(before[0]).IsSameReferenceAs(cached);
        await Assert.That(beforeDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(beforeDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(beforeDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(beforeDatabase.Diagnostics.Materializations).IsEqualTo(2);
        await Assert.That(beforeDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);

        var betweenDatabase = CreateAdversarialDatabase();
        var between = betweenDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .Where(row => row.GroupId == groupId)
            .Take(2)
            .ToArray();

        await Assert.That(Ids(between)).IsEqualTo("-11,17");
        await Assert.That(betweenDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(betweenDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(betweenDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(betweenDatabase.Diagnostics.Materializations).IsEqualTo(2);
    }

    [Test]
    public async Task OrderedTake_ZeroSkipsTheScanAndOverCardinalityReturnsEveryOrderedRow()
    {
        var zeroDatabase = CreateAdversarialDatabase();

        var zero = zeroDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .Take(0)
            .ToArray();

        await Assert.That(zero).IsEmpty();
        await Assert.That(zeroDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(zeroDatabase.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(zeroDatabase.Diagnostics.Materializations).IsEqualTo(0);

        var overCardinalityDatabase = CreateAdversarialDatabase();
        var overCardinality = overCardinalityDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .Take(99)
            .ToArray();

        await Assert.That(Ids(overCardinality)).IsEqualTo(Ids(AscendingIds));
        await Assert.That(overCardinalityDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(overCardinalityDatabase.Diagnostics.Materializations).IsEqualTo(5);
    }

    [Test]
    public async Task TakeCount_IsSnapshottedAtQueryConstructionAndRebuiltQueriesCaptureTheNewValue()
    {
        var database = CreateAdversarialDatabase();
        var count = 1;
        var originalQuery = database.Model.Rows
            .OrderBy(static row => row.Id)
            .Take(count);
        count = 3;

        var original = originalQuery.ToArray();
        var rebuilt = database.Model.Rows
            .OrderBy(static row => row.Id)
            .Take(count)
            .ToArray();

        await Assert.That(Ids(original)).IsEqualTo(int.MinValue.ToString());
        await Assert.That(Ids(rebuilt)).IsEqualTo($"{int.MinValue},-11,0");
        await Assert.That(rebuilt[0]).IsSameReferenceAs(original[0]);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(10);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(3);
    }

    [Test]
    public async Task OrderedQuery_ObservesPreCancellationAndCancellationBetweenMaterializations()
    {
        var preCancelledDatabase = CreateAdversarialDatabase();
        var preCancelledQuery = preCancelledDatabase.Model.Rows.OrderBy(static row => row.Id);
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        var preCancelledException = Capture<OperationCanceledException>(() =>
            preCancelledDatabase.Execute(preCancelledQuery, preCancelled.Token));

        await Assert.That(preCancelledException.CancellationToken).IsEqualTo(preCancelled.Token);
        await Assert.That(preCancelledDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(preCancelledDatabase.Diagnostics.Materializations).IsEqualTo(0);

        var midQueryDatabase = CreateAdversarialDatabase();
        var query = midQueryDatabase.Model.Rows.OrderBy(static row => row.Id);
        using var cancellation = new CancellationTokenSource();
        using var rows = midQueryDatabase.Execute(query, cancellation.Token).GetEnumerator();

        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current.Id).IsEqualTo(int.MinValue);
        await Assert.That(midQueryDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(midQueryDatabase.Diagnostics.Materializations).IsEqualTo(1);
        cancellation.Cancel();

        var midQueryException = Capture<OperationCanceledException>(() => rows.MoveNext());

        await Assert.That(midQueryException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(midQueryDatabase.Diagnostics.Materializations).IsEqualTo(1);
    }

    [Test]
    public async Task UnsupportedOrderingAndTakeShapes_FailBeforeMemoryEnumeration()
    {
        var database = CreateAdversarialDatabase();
        var before = database.Diagnostics;

        var bareTake = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Take(1).ToArray());
        var repeatedTake = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.Id).Take(2).Take(1).ToArray());
        var negativeTake = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.Id).Take(-1).ToArray());
        var nonPrimaryKeyOrdering = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.GroupId).ToArray());
        var stringOrdering = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.Name).ToArray());
        var convertedOrdering = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => (long)row.Id).ToArray());
        var thenBy = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.Id).ThenBy(static row => row.GroupId).ToArray());
        var rootThenBy = Capture<QueryTranslationException>(() =>
            database.Model.Rows.ThenBy(static row => row.Id).ToArray());

        await Assert.That(bareTake.Message).Contains("PagingCompositionShape:Other");
        await Assert.That(repeatedTake.Message).Contains("PagingCompositionShape:RepeatedTakeInScope");
        await Assert.That(negativeTake.Message).Contains("PagingCountShape:Negative");
        await Assert.That(nonPrimaryKeyOrdering.Message).Contains("OrderingShape:Other");
        await Assert.That(stringOrdering.Message).Contains("OrderingShape:Other");
        await Assert.That(convertedOrdering.Message).Contains("OrderingShape:Other");
        await Assert.That(thenBy.Message).Contains("OrderingShape:Other");
        await Assert.That(rootThenBy.Message).Contains("ThenBy");
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(before.ScanRowsVisited);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(before.PredicateEvaluations);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(before.CacheLookups);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(before.Materializations);
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

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }
}
