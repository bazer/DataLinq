using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryScalarProjectionTests
{
    [Test]
    public async Task DirectInt32Projection_ReadsCanonicalColumnsWithoutMaterializingEntities()
    {
        var database = CreateAdversarialDatabase();
        _ = database.Find<MemoryPrimitiveRow>(DataLinq.Instances.DataLinqKey.FromValue(17));
        var before = database.Diagnostics;

        var ids = database.Model.Rows.Select(static row => row.Id).ToArray();
        var groupIds = database.Model.Rows.Select(static row => row.GroupId).ToArray();

        await Assert.That(string.Join(",", ids)).IsEqualTo(
            $"17,{int.MinValue},{int.MaxValue},-11,0");
        await Assert.That(string.Join(",", groupIds)).IsEqualTo("7,3,7,7,3");
        await Assert.That(database.Diagnostics.ScanRowsVisited - before.ScanRowsVisited).IsEqualTo(10);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(before.CacheLookups);
        await Assert.That(database.Diagnostics.CacheHits).IsEqualTo(before.CacheHits);
        await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(before.CacheMisses);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(before.Materializations);
        await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(before.CacheInsertions);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
    }

    [Test]
    public async Task ScalarProjection_ComposesExactFiltersOrderingAndTakeBeforeReadingTheCell()
    {
        var database = CreateAdversarialDatabase();
        var groupId = 7;

        var ids = database.Model.Rows
            .Where(row => row.GroupId == groupId)
            .OrderBy(static row => row.Id)
            .Take(2)
            .Select(static row => row.Id)
            .ToArray();

        await Assert.That(string.Join(",", ids)).IsEqualTo("-11,17");
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(0);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    public async Task ScalarProjection_OrderedTakeZeroDoesNotScanOrMaterialize()
    {
        var database = CreateAdversarialDatabase();

        var ids = database.Model.Rows
            .OrderByDescending(static row => row.Id)
            .Take(0)
            .Select(static row => row.Id)
            .ToArray();

        await Assert.That(ids).IsEmpty();
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(0);
    }

    [Test]
    public async Task ScalarProjection_ObservesPreCancellationAndCancellationBetweenRows()
    {
        var preCancelledDatabase = CreateAdversarialDatabase();
        var preCancelledQuery = preCancelledDatabase.Model.Rows.Select(static row => row.Id);
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        var preCancelledException = Capture<OperationCanceledException>(() =>
            preCancelledDatabase.Execute(preCancelledQuery, preCancelled.Token).ToArray());

        await Assert.That(preCancelledException.CancellationToken).IsEqualTo(preCancelled.Token);
        await Assert.That(preCancelledDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(preCancelledDatabase.Diagnostics.Materializations).IsEqualTo(0);

        var midQueryDatabase = CreateAdversarialDatabase();
        var query = midQueryDatabase.Model.Rows.Select(static row => row.GroupId);
        using var cancellation = new CancellationTokenSource();
        using var values = midQueryDatabase.Execute(query, cancellation.Token).GetEnumerator();

        await Assert.That(values.MoveNext()).IsTrue();
        await Assert.That(values.Current).IsEqualTo(7);
        await Assert.That(midQueryDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(1);
        cancellation.Cancel();

        var midQueryException = Capture<OperationCanceledException>(() => values.MoveNext());

        await Assert.That(midQueryException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(midQueryDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(1);
        await Assert.That(midQueryDatabase.Diagnostics.Materializations).IsEqualTo(0);
    }

    [Test]
    public async Task UnsupportedScalarProjectionShapes_FailBeforeMemoryEnumeration()
    {
        var database = CreateAdversarialDatabase();
        var before = database.Diagnostics;

        var stringProjection = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Select(static row => row.Name).ToArray());
        var widenedProjection = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Select(static row => (long)row.Id).ToArray());
        var boxedProjection = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Select(static row => (object)row.Id).ToArray());
        var terminalProjection = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Select(static row => row.Id).First());

        foreach (var exception in new[] { stringProjection, widenedProjection, boxedProjection })
        {
            await Assert.That(exception.Message).Contains(
                "Backend 'memory' cannot execute query plan feature 'ScalarProjectionShape:Other'");
            await Assert.That(exception.Message).Contains("Location: projection.scalar.shape");
        }

        await Assert.That(terminalProjection.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'Result:First'");
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
