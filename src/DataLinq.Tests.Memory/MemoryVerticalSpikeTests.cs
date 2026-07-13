using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Memory;
using DataLinq.Mutation;

namespace DataLinq.Tests.Memory;

public sealed class MemoryVerticalSpikeTests
{
    [Test]
    public async Task CanonicalSeed_PrimaryKeyLookup_UsesGeneratedMaterializationAndSeparateIdentityCache()
    {
        var database = CreateSeededDatabase();

        var first = database.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(42));
        var second = database.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(42));
        var missing = database.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(999));

        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Id).IsEqualTo(42);
        await Assert.That(first.GroupId).IsEqualTo(7);
        await Assert.That(first.Name).IsEqualTo("forty-two");
        await Assert.That(first.GetReadSource()).IsSameReferenceAs(database.ReadSource);
        await Assert.That(second).IsSameReferenceAs(first);
        await Assert.That(missing).IsNull();
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);

        object source = database.ReadSource;
        await Assert.That(source is IDataSourceAccess).IsFalse();
        await Assert.That(source is IDatabaseProvider).IsFalse();
        await Assert.That(source is IDatabaseAccess).IsFalse();

        var beforeEviction = database.Diagnostics;
        await Assert.That(beforeEviction.PrimaryKeyRequests).IsEqualTo(3);
        await Assert.That(beforeEviction.PrimaryKeyProbes).IsEqualTo(3);
        await Assert.That(beforeEviction.CacheLookups).IsEqualTo(2);
        await Assert.That(beforeEviction.CacheHits).IsEqualTo(1);
        await Assert.That(beforeEviction.CacheMisses).IsEqualTo(1);
        await Assert.That(beforeEviction.Materializations).IsEqualTo(1);
        await Assert.That(beforeEviction.CacheInsertions).IsEqualTo(1);

        database.ClearMaterializedRowsForTest<MemoryPrimitiveRow>();
        var afterEviction = database.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(42));

        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
        await Assert.That(afterEviction).IsNotSameReferenceAs(first);
        await Assert.That(afterEviction!.Name).IsEqualTo("forty-two");
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(2);
        await Assert.That(database.Diagnostics.CacheInsertions).IsEqualTo(2);
    }

    [Test]
    public async Task GeneratedRoot_ExecutesPassThroughEntityPlanAndReusesMaterializedIdentity()
    {
        var database = CreateSeededDatabase();

        var rows = database.Model.Rows.ToArray();

        await Assert.That(rows.Length).IsEqualTo(2);
        await Assert.That(rows[0].Id).IsEqualTo(42);
        await Assert.That(rows[0].Name).IsEqualTo("forty-two");
        await Assert.That(rows[1].Id).IsEqualTo(7);
        await Assert.That(rows[1].Name).IsEqualTo("seven");
        await Assert.That(rows[0].GetReadSource()).IsSameReferenceAs(database.ReadSource);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(2);
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);

        var direct = database.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(42));
        await Assert.That(direct).IsSameReferenceAs(rows[0]);
        await Assert.That(database.Diagnostics.CacheHits).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(2);
    }

    [Test]
    public async Task EntityScan_ObservesPreCancellationAndCancellationBetweenRows()
    {
        var database = CreateSeededDatabase();
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        var preCancelledLookupException = Capture<OperationCanceledException>(() =>
            database.Find<MemoryPrimitiveRow>(
                DataLinqKey.FromValue(42),
                preCancelled.Token));
        var preCancelledException = Capture<OperationCanceledException>(() =>
            database.Scan<MemoryPrimitiveRow>(preCancelled.Token));

        await Assert.That(preCancelledLookupException.CancellationToken).IsEqualTo(preCancelled.Token);
        await Assert.That(preCancelledException.CancellationToken).IsEqualTo(preCancelled.Token);
        await Assert.That(database.Diagnostics.PrimaryKeyRequests).IsEqualTo(0);
        await Assert.That(database.Diagnostics.PrimaryKeyProbes).IsEqualTo(0);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);

        using var cancellation = new CancellationTokenSource();
        using var rows = database.Scan<MemoryPrimitiveRow>(cancellation.Token).GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current.Id).IsEqualTo(42);
        cancellation.Cancel();

        var midScanException = Capture<OperationCanceledException>(() => rows.MoveNext());

        await Assert.That(midScanException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(database.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(2);
    }

    [Test]
    public async Task CapturedInt32Equality_FiltersCanonicalRowsBeforeMaterializationAndReusesIdentity()
    {
        var database = CreateSeededDatabase();
        var direct = database.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(42));
        var groupId = 7;

        var rows = database.Model.Rows
            .Where(row => row.GroupId == groupId)
            .ToArray();

        await Assert.That(rows.Length).IsEqualTo(1);
        await Assert.That(rows[0]).IsSameReferenceAs(direct);
        await Assert.That(rows[0].Id).IsEqualTo(42);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(2);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(2);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(1);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(2);
        await Assert.That(database.Diagnostics.CacheHits).IsEqualTo(1);
        await Assert.That(database.Diagnostics.CacheMisses).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
    }

    [Test]
    public async Task NoMatchInt32Equality_CompletesWithoutMaterializationOrCacheWork()
    {
        var database = CreateSeededDatabase();
        var id = 999;

        var rows = database.Model.Rows
            .Where(row => row.Id == id)
            .ToArray();

        await Assert.That(rows).IsEmpty();
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(2);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(2);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    public async Task CapturedInt32Equality_RebindsChangedCaptureAndSupportsReversedOperands()
    {
        var database = CreateSeededDatabase();
        var groupId = 7;
        var query = database.Model.Rows.Where(row => row.GroupId == groupId);

        var first = query.ToArray();
        groupId = 3;
        var second = query.ToArray();
        var reversed = database.Model.Rows.Where(row => groupId == row.GroupId).ToArray();

        await Assert.That(first.Select(static row => row.Id)).IsEquivalentTo([42]);
        await Assert.That(second.Select(static row => row.Id)).IsEquivalentTo([7]);
        await Assert.That(reversed.Length).IsEqualTo(1);
        await Assert.That(reversed[0]).IsSameReferenceAs(second[0]);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(6);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(6);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(3);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(3);
        await Assert.That(database.Diagnostics.CacheHits).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(2);
    }

    [Test]
    public async Task RepeatedEqualityWhere_ShortCircuitsAndAcceptsLiteralScalarBindings()
    {
        var database = CreateSeededDatabase();
        var groupId = 7;

        var rows = database.Model.Rows
            .Where(row => row.GroupId == groupId)
            .Where(row => row.Id == 42)
            .ToArray();

        await Assert.That(rows.Length).IsEqualTo(1);
        await Assert.That(rows[0].Id).IsEqualTo(42);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(2);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(3);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(1);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
    }

    [Test]
    public async Task FilteredEntityScan_ObservesPreCancellationAndCancellationBetweenRows()
    {
        var database = CreateSeededDatabase();
        var groupId = 7;
        var query = database.Model.Rows.Where(row => row.GroupId == groupId);
        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();

        var preCancelledException = Capture<OperationCanceledException>(() =>
            database.Execute(query, preCancelled.Token));

        await Assert.That(preCancelledException.CancellationToken).IsEqualTo(preCancelled.Token);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(0);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);

        using var cancellation = new CancellationTokenSource();
        using var rows = database.Execute(query, cancellation.Token).GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current.Id).IsEqualTo(42);
        cancellation.Cancel();

        var midScanException = Capture<OperationCanceledException>(() => rows.MoveNext());

        await Assert.That(midScanException.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(1);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(1);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(1);
    }

    [Test]
    public async Task UnsupportedQueryShapes_FailBeforeMemoryEnumeration()
    {
        var database = CreateSeededDatabase();
        var before = database.Diagnostics;

        await Assert.That(database.SupportedCapabilityTokens).IsEquivalentTo(
        [
            "BindingKind:Scalar",
            "ComparisonOperator:Equal",
            "ComparisonShape:DirectNonNullableInt32ColumnAndScalar",
            "NullSemantics:Default",
            "Operation:OrderBy",
            "Operation:Take",
            "Operation:Where",
            "OrderingDirection:Ascending",
            "OrderingDirection:Descending",
            "OrderingShape:SingleDirectNonNullableInt32PrimaryKeyColumn",
            "PagingCountShape:NonNegativeInt32ScalarBinding",
            "Predicate:Compare",
            "Projection:Entity",
            "Projection:ScalarMember",
            "ProjectionDisposition:Direct",
            "Result:Sequence",
            "ScalarProjectionShape:DirectNonNullableInt32RootColumn",
            "ScalarNullness:NonNull",
            "SourceCardinality:Many",
            "SourceCount:Single",
            "SourceKind:RootTable",
            "SourceNullability:NonNullable",
            "SourceTopology:ExactlyOneRoot",
            "PagingCompositionShape:SingleTakeAfterSingleOrdering",
            "Value:Column@Ordering",
            "Value:Column@PredicateOperand",
            "Value:Column@ProjectionMember",
            "Value:ScalarBinding@PagingCount",
            "Value:ScalarBinding@PredicateOperand"
        ]);

        var notEqual = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Where(static row => row.GroupId != 7).ToArray());
        var bareTake = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Take(1).ToArray());
        var nonPrimaryKeyOrderBy = Capture<QueryTranslationException>(() =>
            database.Model.Rows.OrderBy(static row => row.GroupId).ToArray());
        var name = "seven";
        var stringEquality = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Where(row => row.Name == name).ToArray());
        var columnEquality = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Where(static row => row.Id == row.GroupId).ToArray());
        long promotedGroupId = 7;
        var promotedEquality = Capture<QueryTranslationException>(() =>
            database.Model.Rows.Where(row => row.GroupId == promotedGroupId).ToArray());

        await Assert.That(notEqual.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'ComparisonOperator:NotEqual'");
        await Assert.That(notEqual.Message).Contains("Location: operations[0].predicate.operator");
        await Assert.That(bareTake.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'PagingCompositionShape:Other'");
        await Assert.That(nonPrimaryKeyOrderBy.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'OrderingShape:Other'");
        await Assert.That(stringEquality.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'ComparisonShape:DefaultNullSemantics'");
        await Assert.That(stringEquality.Message).Contains("Location: operations[0].predicate.shape");
        await Assert.That(stringEquality.Message).DoesNotContain(name);
        await Assert.That(columnEquality.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'ComparisonShape:DefaultNullSemantics'");
        await Assert.That(columnEquality.Message).Contains("Location: operations[0].predicate.shape");
        await Assert.That(promotedEquality.Message).Contains(
            "Backend 'memory' cannot execute query plan feature 'ComparisonShape:DefaultNullSemantics'");
        await Assert.That(promotedEquality.Message).Contains("Location: operations[0].predicate.shape");
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(before.ScanRowsVisited);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(before.PredicateEvaluations);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(before.CacheLookups);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(before.Materializations);
    }

    [Test]
    public async Task SeedPublication_IsAtomicAndStoresRemainSourceLocal()
    {
        var rejected = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var duplicate = CreateCanonicalRow(rejected, id: 42, groupId: 7, name: "duplicate");

        var exception = Capture<MemorySeedException>(() =>
            rejected.SeedCanonical<MemoryPrimitiveRow>(duplicate, duplicate));

        await Assert.That(exception.Message).Contains("memory_primitive_rows");
        await Assert.That(exception.Message).Contains("duplicate primary key at row 1");
        await Assert.That(exception.Message).Contains("first row is 0");
        await Assert.That(rejected.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);

        rejected.SeedCanonical<MemoryPrimitiveRow>(
            CreateCanonicalRow(rejected, id: 7, groupId: 3, name: "recovered"));

        var independent = new MemoryDatabase<MemoryPrimitiveDatabase>();
        independent.SeedCanonical<MemoryPrimitiveRow>(
            CreateCanonicalRow(independent, id: 7, groupId: 9, name: "independent"));

        await Assert.That(independent.Metadata).IsSameReferenceAs(rejected.Metadata);
        await Assert.That(rejected.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
        await Assert.That(independent.GetStoredRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);

        var recovered = rejected.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(7));
        var separate = independent.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(7));
        await Assert.That(recovered!.Name).IsEqualTo("recovered");
        await Assert.That(separate!.Name).IsEqualTo("independent");
        await Assert.That(separate).IsNotSameReferenceAs(recovered);
        await Assert.That(separate.GetReadSource()).IsNotSameReferenceAs(recovered.GetReadSource());
    }

    [Test]
    [NotInParallel]
    public async Task ConcurrentColdStart_PublishesAndBindsOneMetadataGraph()
    {
        MemoryDatabase<MemoryPrimitiveDatabase>.ResetGeneratedMetadataForTest();
        using var start = new ManualResetEventSlim(initialState: false);
        var constructors = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return new MemoryDatabase<MemoryPrimitiveDatabase>();
            }))
            .ToArray();

        start.Set();
        var databases = await Task.WhenAll(constructors);
        var metadata = databases[0].Metadata;

        for (var index = 0; index < databases.Length; index++)
        {
            var database = databases[index];
            await Assert.That(database.Metadata).IsSameReferenceAs(metadata);

            database.SeedCanonical<MemoryPrimitiveRow>(
                CreateCanonicalRow(
                    database,
                    id: index + 1,
                    groupId: index % 3,
                    name: $"row-{index + 1}"));
            var row = database.Find<MemoryPrimitiveRow>(DataLinqKey.FromValue(index + 1));

            await Assert.That(row).IsNotNull();
            await Assert.That(row!.Name).IsEqualTo($"row-{index + 1}");
            await Assert.That(row.GetReadSource()).IsSameReferenceAs(database.ReadSource);
        }
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateSeededDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        return database.SeedCanonical<MemoryPrimitiveRow>(
            CreateCanonicalRow(database, id: 42, groupId: 7, name: "forty-two"),
            CreateCanonicalRow(database, id: 7, groupId: 3, name: "seven"));
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
