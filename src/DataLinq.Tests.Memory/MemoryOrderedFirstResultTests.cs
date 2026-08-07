using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryOrderedFirstResultTests
{
    [Test]
    public async Task OrderedEntityFirst_ReturnsAscendingAndDescendingWithOnlySelectedMaterialization()
    {
        var ascendingDatabase = CreateDatabase();

        var ascending = ascendingDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .First();

        await Assert.That(ascending.Id).IsEqualTo(int.MinValue);
        await Assert.That(ascendingDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(ascendingDatabase.Diagnostics.CacheLookups).IsEqualTo(1);
        await Assert.That(ascendingDatabase.Diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(ascendingDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);

        var descendingDatabase = CreateDatabase();

        var descending = descendingDatabase.Model.Rows
            .OrderByDescending(static row => row.Id)
            .FirstOrDefault();

        await Assert.That(descending).IsNotNull();
        await Assert.That(descending!.Id).IsEqualTo(int.MaxValue);
        await Assert.That(descendingDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(descendingDatabase.Diagnostics.CacheLookups).IsEqualTo(1);
        await Assert.That(descendingDatabase.Diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(descendingDatabase.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
    }

    [Test]
    public async Task OrderedFirst_AdmitsPredicatesBeforeAndAfterOrderingAndPredicateOverload()
    {
        var beforeDatabase = CreateDatabase();
        var before = beforeDatabase.Model.Rows
            .Where(static row => row.GroupId == 7)
            .OrderBy(static row => row.Id)
            .First();

        var afterDatabase = CreateDatabase();
        var after = afterDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .Where(static row => row.GroupId == 7)
            .FirstOrDefault();

        var overloadDatabase = CreateDatabase();
        var overload = overloadDatabase.Model.Rows
            .OrderByDescending(static row => row.Id)
            .First(static row => row.GroupId == 3);

        await Assert.That(before.Id).IsEqualTo(-11);
        await Assert.That(after).IsNotNull();
        await Assert.That(after!.Id).IsEqualTo(-11);
        await Assert.That(overload.Id).IsEqualTo(0);
        foreach (var database in new[] { beforeDatabase, afterDatabase, overloadDatabase })
        {
            await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(5);
            await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
            await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
        }
    }

    [Test]
    public async Task OrderedScalarFirst_ConvertsOnlyTheSelectedCanonicalValueWithoutEntityWork()
    {
        var database = CreateDatabase();

        var groupId = database.Model.Rows
            .OrderBy(static row => row.Id)
            .Select(static row => row.GroupId)
            .First();

        await Assert.That(groupId).IsEqualTo(3);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(database);
    }

    [Test]
    public async Task OrderedFirst_UsesExactEmptyExceptionAndEntityAndScalarDefaultsWithoutMaterialization()
    {
        var expectedEmpty = Capture<InvalidOperationException>(() => Array.Empty<int>().First());
        var firstDatabase = CreateDatabase();

        var empty = Capture<InvalidOperationException>(() => firstDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .Where(static row => row.Id == 99)
            .First());

        await Assert.That(empty.Message).IsEqualTo(expectedEmpty.Message);
        await Assert.That(firstDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(firstDatabase);

        var entityDefaultDatabase = CreateDatabase();
        var entityDefault = entityDefaultDatabase.Model.Rows
            .Where(static row => row.Id == 99)
            .OrderBy(static row => row.Id)
            .FirstOrDefault();

        await Assert.That(entityDefault).IsNull();
        await Assert.That(entityDefaultDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(entityDefaultDatabase);

        var scalarDefaultDatabase = CreateDatabase();
        var scalarDefault = scalarDefaultDatabase.Model.Rows
            .OrderBy(static row => row.Id)
            .Where(static row => row.Id == 99)
            .Select(static row => row.GroupId)
            .FirstOrDefault();

        await Assert.That(scalarDefault).IsEqualTo(0);
        await Assert.That(scalarDefaultDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(scalarDefaultDatabase);
    }

    [Test]
    public async Task OrderedFirst_ObservesPreCancellationBeforeMemoryWork()
    {
        var database = CreateDatabase();
        var rows = database.Model.Rows;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Expression<Func<MemoryPrimitiveRow>> terminal = () =>
            rows.OrderBy(static row => row.Id).First();

        var cancelled = Capture<OperationCanceledException>(() =>
            database.Execute(terminal, cancellation.Token));

        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        AssertNoEntityWork(database);
    }

    [Test]
    public async Task OrderedEntityFirstCursor_SecondMoveNextCancellationDisposesWithoutRowTwoMaterialization()
    {
        var database = CreateDatabase();
        var ordered = database.Model.Rows.OrderBy(static row => row.Id);
        using var cancellation = new CancellationTokenSource();
        var terminal = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.First),
            [typeof(MemoryPrimitiveRow)],
            ordered.Expression);
        var invocation = ExpressionQueryPlanParser.Convert(
            database.Metadata,
            terminal,
            typeof(MemoryPrimitiveRow));
        var request = ValidatedQueryExecutionRequest.Prepare(new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(database.ReadSource, cancellation.Token)));
        using var cursor = request.Backend.OpenEntityCursor(request);

        await Assert.That(cursor.MoveNext()).IsTrue();
        await Assert.That(((MemoryPrimitiveRow)cursor.Current).Id).IsEqualTo(int.MinValue);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);

        cancellation.Cancel();
        var cancelled = Capture<OperationCanceledException>(() => cursor.MoveNext());

        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(cursor.MoveNext()).IsFalse();
        _ = Capture<InvalidOperationException>(() => _ = cursor.Current);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
    }

    [Test]
    public async Task OrderedScalarFirstCursor_SecondMoveNextCancellationDisposesWithoutRowTwoOrEntityWork()
    {
        var database = CreateDatabase();
        var orderedScalar = database.Model.Rows
            .OrderBy(static row => row.Id)
            .Select(static row => row.GroupId);
        using var cancellation = new CancellationTokenSource();
        var terminal = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.First),
            [typeof(int)],
            orderedScalar.Expression);
        var invocation = ExpressionQueryPlanParser.Convert(
            database.Metadata,
            terminal,
            typeof(int));
        var request = ValidatedQueryExecutionRequest.Prepare(new QueryExecutionRequest(
            invocation,
            new QueryExecutionContext(database.ReadSource, cancellation.Token)));
        using var cursor = request.Backend.OpenProjectionCursor<int>(request);

        await Assert.That(cursor.MoveNext()).IsTrue();
        await Assert.That(cursor.Current).IsEqualTo(3);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        AssertNoEntityWork(database);

        cancellation.Cancel();
        var cancelled = Capture<OperationCanceledException>(() => cursor.MoveNext());

        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(cursor.MoveNext()).IsFalse();
        _ = Capture<InvalidOperationException>(() => _ = cursor.Current);
        AssertNoEntityWork(database);
    }

    [Test]
    public async Task UnsupportedFirstCompositions_RejectBeforeMemoryWork()
    {
        var database = CreateDatabase();
        var rows = database.Model.Rows;
        var before = database.Diagnostics;

        var unordered = Capture<QueryBackendCapabilityException>(() => rows.First());
        var nonPrimaryOrdering = Capture<QueryBackendCapabilityException>(() =>
            rows.OrderBy(static row => row.GroupId).First());
        var thenBy = Capture<QueryBackendCapabilityException>(() =>
            rows.OrderBy(static row => row.Id).ThenBy(static row => row.GroupId).FirstOrDefault());
        var paging = Capture<QueryBackendCapabilityException>(() =>
            rows.OrderBy(static row => row.Id).Take(1).First());

        await Assert.That(unordered.Feature).IsEqualTo("ResultCompositionShape:Other");
        await Assert.That(unordered.Location).IsEqualTo("result.composition.shape");
        await Assert.That(nonPrimaryOrdering.Feature).IsEqualTo("OrderingShape:Other");
        await Assert.That(nonPrimaryOrdering.Location).IsEqualTo("operations.ordering.shape");
        await Assert.That(thenBy.Feature).IsEqualTo("OrderingShape:Other");
        await Assert.That(thenBy.Location).IsEqualTo("operations.ordering.shape");
        await Assert.That(paging.Feature).IsEqualTo("Operation:Pushdown");
        await Assert.That(paging.Location).IsEqualTo("operations[0]");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateDatabase()
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

    private static void AssertNoEntityWork(MemoryDatabase<MemoryPrimitiveDatabase> database)
    {
        if (database.Diagnostics.CacheLookups != 0 ||
            database.Diagnostics.Materializations != 0 ||
            database.GetMaterializedRowCount<MemoryPrimitiveRow>() != 0)
        {
            throw new InvalidOperationException("The ordered First result performed unexpected entity or cache work.");
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
