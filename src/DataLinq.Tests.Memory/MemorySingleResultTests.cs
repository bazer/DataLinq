using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemorySingleResultTests
{
    [Test]
    public async Task EntitySingleResults_ProveCardinalityBeforeOneCanonicalMaterialization()
    {
        var database = CreateDatabase();
        var rows = database.Model.Rows;
        var id = 17;

        var predicateResult = rows.Single(row => row.Id == id);
        var whereResult = rows.Where(row => row.Id == id).SingleOrDefault();

        await Assert.That(predicateResult.Id).IsEqualTo(17);
        await Assert.That(whereResult).IsSameReferenceAs(predicateResult);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(10);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(10);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(8);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
    }

    [Test]
    public async Task SingleOrDefault_EmptyEntityAndScalarReturnTheirExactDefaultsWithoutMaterialization()
    {
        var entityDatabase = CreateDatabase();
        var missingId = 99;

        var entity = entityDatabase.Model.Rows.SingleOrDefault(row => row.Id == missingId);

        await Assert.That(entity).IsNull();
        await Assert.That(entityDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(entityDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        AssertNoEntityWork(entityDatabase);

        var scalarDatabase = CreateDatabase();
        var scalar = scalarDatabase.Model.Rows
            .Where(row => row.Id == missingId)
            .Select(static row => row.GroupId)
            .SingleOrDefault();

        await Assert.That(scalar).IsEqualTo(0);
        await Assert.That(scalarDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(scalarDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        AssertNoEntityWork(scalarDatabase);
    }

    [Test]
    public async Task EntitySingleResults_UseExactEmptyAndMultiplicityErrorsWithoutPartialMaterialization()
    {
        var expectedEmpty = Capture<InvalidOperationException>(() => Array.Empty<int>().Single());
        var expectedMultiple = Capture<InvalidOperationException>(() => new[] { 1, 2 }.Single());

        var emptyDatabase = CreateDatabase();
        var empty = Capture<InvalidOperationException>(() =>
            emptyDatabase.Model.Rows.Single(row => row.Id == 99));

        await Assert.That(empty.Message).IsEqualTo(expectedEmpty.Message);
        await Assert.That(emptyDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(emptyDatabase);

        var singleDatabase = CreateDatabase();
        var singleMultiple = Capture<InvalidOperationException>(() =>
            singleDatabase.Model.Rows.Single(row => row.GroupId == 7));

        await Assert.That(singleMultiple.Message).IsEqualTo(expectedMultiple.Message);
        await Assert.That(singleDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(4);
        await Assert.That(singleDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(4);
        await Assert.That(singleDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        AssertNoEntityWork(singleDatabase);

        var defaultDatabase = CreateDatabase();
        var defaultMultiple = Capture<InvalidOperationException>(() =>
            defaultDatabase.Model.Rows.SingleOrDefault(row => row.GroupId == 7));

        await Assert.That(defaultMultiple.Message).IsEqualTo(expectedMultiple.Message);
        await Assert.That(defaultDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(4);
        AssertNoEntityWork(defaultDatabase);
    }

    [Test]
    public async Task DirectInt32SingleResults_UseRowCardinalityWithoutEntityOrCacheWork()
    {
        var database = CreateDatabase();
        var rows = database.Model.Rows;

        var group = rows
            .Where(static row => row.Id == 17)
            .Select(static row => row.GroupId)
            .Single();
        var expectedEmpty = Capture<InvalidOperationException>(() => Array.Empty<int>().Single());
        var empty = Capture<InvalidOperationException>(() => rows
            .Where(static row => row.Id == 99)
            .Select(static row => row.GroupId)
            .Single());
        var expectedMultiple = Capture<InvalidOperationException>(() => new[] { 7, 7 }.Single());
        var repeatedValueRows = Capture<InvalidOperationException>(() => rows
            .Where(static row => row.GroupId == 7)
            .Select(static row => row.GroupId)
            .SingleOrDefault());

        await Assert.That(group).IsEqualTo(3);
        await Assert.That(empty.Message).IsEqualTo(expectedEmpty.Message);
        await Assert.That(repeatedValueRows.Message).IsEqualTo(expectedMultiple.Message);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(14);
        AssertNoEntityWork(database);
    }

    [Test]
    public async Task SingleResults_ComposeWithTheAdmittedIslandAndPreserveInvocationCancellation()
    {
        var composedDatabase = CreateDatabase();
        int[] candidateIds = [-11, 0];

        var composed = composedDatabase.Model.Rows
            .Where(row => row.Id >= -11 && candidateIds.Contains(row.Id))
            .OrderBy(static row => row.Id)
            .Where(static row => row.Id != 0)
            .Single();

        await Assert.That(composed.Id).IsEqualTo(-11);
        await Assert.That(composedDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(composedDatabase.Diagnostics.Materializations).IsEqualTo(1);

        var reboundDatabase = CreateDatabase();
        var rows = reboundDatabase.Model.Rows;
        var id = 17;
        var filtered = rows.Where(row => row.Id == id);

        var first = filtered.Single();
        id = -11;
        var rebound = filtered.Single();
        id = 99;
        var missing = filtered.SingleOrDefault();

        await Assert.That(first.Id).IsEqualTo(17);
        await Assert.That(rebound.Id).IsEqualTo(-11);
        await Assert.That(missing).IsNull();
        await Assert.That(reboundDatabase.Diagnostics.Materializations).IsEqualTo(2);

        var cancelledDatabase = CreateDatabase();
        var cancelledRows = cancelledDatabase.Model.Rows;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Expression<Func<MemoryPrimitiveRow>> terminal = () =>
            cancelledRows.Single(static row => row.Id == 17);

        var cancelled = Capture<OperationCanceledException>(() =>
            cancelledDatabase.Execute(terminal, cancellation.Token));

        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(cancelledDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        AssertNoEntityWork(cancelledDatabase);
    }

    [Test]
    public async Task UnsupportedSingleNeighbors_RejectBeforeMemoryWork()
    {
        var database = CreateDatabase();
        var rows = database.Model.Rows;
        var before = database.Diagnostics;

        var first = Capture<QueryBackendCapabilityException>(() => rows.First());
        var firstOrDefault = Capture<QueryBackendCapabilityException>(() => rows.FirstOrDefault());
        var last = Capture<QueryBackendCapabilityException>(() => rows.Last());
        var afterTake = Capture<QueryBackendCapabilityException>(() =>
            rows.OrderBy(static row => row.Id).Take(1).Single());
        var afterSkip = Capture<QueryBackendCapabilityException>(() =>
            rows.OrderBy(static row => row.Id).Skip(1).SingleOrDefault());
        var stringProjection = Capture<QueryBackendCapabilityException>(() =>
            rows.Select(static row => row.Name).Single());
        var nonPrimaryOrdering = Capture<QueryBackendCapabilityException>(() =>
            rows.OrderBy(static row => row.GroupId).Single());

        await Assert.That(first.Feature).IsEqualTo("Result:First");
        await Assert.That(firstOrDefault.Feature).IsEqualTo("Result:FirstOrDefault");
        await Assert.That(last.Feature).IsEqualTo("Result:Last");
        await Assert.That(afterTake.Feature).IsEqualTo("Operation:Pushdown");
        await Assert.That(afterSkip.Feature).IsEqualTo("Operation:Pushdown");
        await Assert.That(stringProjection.Feature).IsEqualTo("ScalarProjectionShape:Other");
        await Assert.That(nonPrimaryOrdering.Feature).IsEqualTo("OrderingShape:Other");

        foreach (var result in new[] { first, firstOrDefault, last })
            await Assert.That(result.Location).IsEqualTo("result");
        await Assert.That(afterTake.Location).IsEqualTo("operations[0]");
        await Assert.That(afterSkip.Location).IsEqualTo("operations[0]");
        await Assert.That(stringProjection.Location).IsEqualTo("projection.scalar.shape");
        await Assert.That(nonPrimaryOrdering.Location).IsEqualTo("operations.ordering.shape");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
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
            database.Diagnostics.Materializations != 0 ||
            database.GetMaterializedRowCount<MemoryPrimitiveRow>() != 0)
        {
            throw new InvalidOperationException("The Single result performed unexpected entity or cache work.");
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

        throw new InvalidOperationException(
            $"Expected exception of type '{typeof(TException).Name}'.");
    }
}
