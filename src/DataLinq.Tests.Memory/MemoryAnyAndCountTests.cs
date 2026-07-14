using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryAnyAndCountTests
{
    [Test]
    public async Task Any_ExecutesRootAndEqualityWithShortCircuiting()
    {
        var empty = new MemoryDatabase<MemoryPrimitiveDatabase>();
        await Assert.That(empty.Model.Rows.Any()).IsFalse();
        await Assert.That(empty.Diagnostics.ScanRowsVisited).IsEqualTo(0);

        var root = CreateDatabase();
        await Assert.That(root.Model.Rows.Any()).IsTrue();
        await Assert.That(root.Diagnostics.ScanRowsVisited).IsEqualTo(1);
        AssertNoEntityWork(root);

        var matching = CreateDatabase();
        var groupId = 7;
        await Assert.That(matching.Model.Rows.Any(row => row.GroupId == groupId)).IsTrue();
        await Assert.That(matching.Diagnostics.ScanRowsVisited).IsEqualTo(3);
        await Assert.That(matching.Diagnostics.PredicateEvaluations).IsEqualTo(3);
        await Assert.That(matching.Diagnostics.PredicateRejections).IsEqualTo(2);
        AssertNoEntityWork(matching);

        var missing = CreateDatabase();
        var absentGroupId = 99;
        await Assert.That(missing.Model.Rows.Where(row => row.GroupId == absentGroupId).Any()).IsFalse();
        await Assert.That(missing.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(missing.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(missing.Diagnostics.PredicateRejections).IsEqualTo(5);
        AssertNoEntityWork(missing);
    }

    [Test]
    public async Task Count_ExecutesRootAndEqualityWithoutMaterializing()
    {
        var empty = new MemoryDatabase<MemoryPrimitiveDatabase>();
        await Assert.That(empty.Model.Rows.Count()).IsEqualTo(0);
        await Assert.That(empty.Diagnostics.ScanRowsVisited).IsEqualTo(0);

        var root = CreateDatabase();
        await Assert.That(root.Model.Rows.Count()).IsEqualTo(5);
        await Assert.That(root.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(root);

        var sevens = CreateDatabase();
        var groupId = 7;
        await Assert.That(sevens.Model.Rows.Count(row => row.GroupId == groupId)).IsEqualTo(3);
        await Assert.That(sevens.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(sevens.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(sevens.Diagnostics.PredicateRejections).IsEqualTo(2);
        AssertNoEntityWork(sevens);

        var threes = CreateDatabase();
        var otherGroupId = 3;
        await Assert.That(threes.Model.Rows.Where(row => row.GroupId == otherGroupId).Count()).IsEqualTo(2);
        await Assert.That(threes.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(threes.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(threes.Diagnostics.PredicateRejections).IsEqualTo(3);
        AssertNoEntityWork(threes);
    }

    [Test]
    public async Task ScalarProjection_AnyAndCountReduceRowsWithoutEntityWork()
    {
        var any = CreateDatabase();
        await Assert.That(any.Model.Rows.Select(static row => row.Id).Any()).IsTrue();
        await Assert.That(any.Diagnostics.ScanRowsVisited).IsEqualTo(1);
        AssertNoEntityWork(any);

        var count = CreateDatabase();
        await Assert.That(count.Model.Rows.Select(static row => row.GroupId).Count()).IsEqualTo(5);
        await Assert.That(count.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(count);

        var filtered = CreateDatabase();
        var groupId = 7;
        await Assert.That(filtered.Model.Rows
            .Where(row => row.GroupId == groupId)
            .Select(static row => row.Id)
            .Count()).IsEqualTo(3);
        await Assert.That(filtered.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(filtered.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        AssertNoEntityWork(filtered);
    }

    [Test]
    public async Task OrderedAnyAndCount_UseTheAdmittedBufferedRowIsland()
    {
        var any = CreateDatabase();
        await Assert.That(any.Model.Rows.OrderBy(static row => row.Id).Any()).IsTrue();
        await Assert.That(any.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(any);

        var count = CreateDatabase();
        var groupId = 7;
        await Assert.That(count.Model.Rows
            .Where(row => row.GroupId == groupId)
            .OrderByDescending(static row => row.Id)
            .Count()).IsEqualTo(3);
        await Assert.That(count.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(count.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(count.Diagnostics.PredicateRejections).IsEqualTo(2);
        AssertNoEntityWork(count);
    }

    [Test]
    public async Task PagedAndBroaderScalarReductions_RejectBeforeEnumeration()
    {
        var database = CreateDatabase();
        var before = database.Diagnostics;

        var any = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.Id).Take(2).Any());
        var count = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.Id).Take(2).Count());
        var projectedAny = Capture<QueryTranslationException>(() =>
            database.Model.Rows
                .OrderBy(static row => row.Id)
                .Take(2)
                .Select(static row => row.Id)
                .Any());
        var stringCount = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Select(static row => row.Name).Count());
        var sum = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Sum(static row => row.Id));

        foreach (var exception in new[] { any, count, projectedAny })
            await Assert.That(exception.Message).Contains("Operation:Pushdown");

        await Assert.That(stringCount.Message).Contains("ScalarProjectionShape:Other");
        await Assert.That(sum.Message).Contains("Result:Sum");
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(before.ScanRowsVisited);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(before.PredicateEvaluations);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(before.CacheLookups);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(before.Materializations);
    }

    [Test]
    public async Task ScalarReductions_ObservePreCancellation()
    {
        var anyDatabase = CreateDatabase();
        var anyRows = anyDatabase.Model.Rows;
        using var anyCancellation = new CancellationTokenSource();
        anyCancellation.Cancel();

        var any = Capture<OperationCanceledException>(() =>
            anyDatabase.Execute(() => anyRows.Any(), anyCancellation.Token));

        await Assert.That(any.CancellationToken).IsEqualTo(anyCancellation.Token);
        await Assert.That(anyDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        AssertNoEntityWork(anyDatabase);

        var countDatabase = CreateDatabase();
        var countRows = countDatabase.Model.Rows;
        var groupId = 7;
        using var countCancellation = new CancellationTokenSource();
        countCancellation.Cancel();

        var count = Capture<OperationCanceledException>(() =>
            countDatabase.Execute(
                () => countRows.Count(row => row.GroupId == groupId),
                countCancellation.Token));

        await Assert.That(count.CancellationToken).IsEqualTo(countCancellation.Token);
        await Assert.That(countDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        AssertNoEntityWork(countDatabase);
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        return database.SeedCanonical<MemoryPrimitiveRow>(
            CreateCanonicalRow(database, id: 17, groupId: 3, name: "seventeen"),
            CreateCanonicalRow(database, id: int.MinValue, groupId: 3, name: "minimum"),
            CreateCanonicalRow(database, id: int.MaxValue, groupId: 7, name: "maximum"),
            CreateCanonicalRow(database, id: -11, groupId: 7, name: "negative-eleven"),
            CreateCanonicalRow(database, id: 0, groupId: 7, name: "zero"));
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

    private static void AssertNoEntityWork(MemoryDatabase<MemoryPrimitiveDatabase> database)
    {
        if (database.Diagnostics.CacheLookups != 0 ||
            database.Diagnostics.CacheHits != 0 ||
            database.Diagnostics.CacheMisses != 0 ||
            database.Diagnostics.Materializations != 0 ||
            database.Diagnostics.CacheInsertions != 0 ||
            database.GetMaterializedRowCount<MemoryPrimitiveRow>() != 0)
        {
            throw new Exception("Scalar reduction unexpectedly touched entity materialization or RowCache.");
        }
    }

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
