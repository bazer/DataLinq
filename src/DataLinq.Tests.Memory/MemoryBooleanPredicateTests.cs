using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Memory;
using TUnit.Assertions.Enums;

namespace DataLinq.Tests.Memory;

public sealed class MemoryBooleanPredicateTests
{
    private static readonly MemoryGuidId FirstId = new(
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
    private static readonly MemoryGuidId SecondId = new(
        Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"));
    private static readonly MemoryGuidId ThirdId = new(
        Guid.Parse("20314253-6475-8697-a8b9-cadbecfd0e1f"));
    private static readonly Guid SharedDirectGuid =
        Guid.Parse("f1e2d3c4-b5a6-4789-90ab-cdef12345678");
    private static readonly Guid OtherDirectGuid =
        Guid.Parse("e2d3c4b5-a697-4809-a1bc-def234567890");
    private static readonly MemoryGuidId SharedRelatedId = new(
        Guid.Parse("31425364-7586-97a8-b9ca-dbecfd0e1f20"));
    private static readonly MemoryGuidId OtherRelatedId = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"));

    [Test]
    public async Task CompositePredicateNodes_ShortCircuitAndOrAndEvaluateNotOnce()
    {
        var row = CreatePrimitiveDatabase()
            .GetCanonicalRowsForTest<MemoryPrimitiveRow>()[0];
        var andLeft = new SentinelPredicate(result: false);
        var skippedByAnd = new SentinelPredicate(
            result: true,
            failIfEvaluated: true);
        var orLeft = new SentinelPredicate(result: true);
        var skippedByOr = new SentinelPredicate(
            result: false,
            failIfEvaluated: true);
        var notChild = new SentinelPredicate(result: false);

        var andResult = new MemoryAndPredicate([andLeft, skippedByAnd]).Matches(row);
        var orResult = new MemoryOrPredicate([orLeft, skippedByOr]).Matches(row);
        var notResult = new MemoryNotPredicate(notChild).Matches(row);

        await Assert.That(andResult).IsFalse();
        await Assert.That(orResult).IsTrue();
        await Assert.That(notResult).IsTrue();
        await Assert.That(andLeft.CallCount).IsEqualTo(1);
        await Assert.That(skippedByAnd.CallCount).IsEqualTo(0);
        await Assert.That(orLeft.CallCount).IsEqualTo(1);
        await Assert.That(skippedByOr.CallCount).IsEqualTo(0);
        await Assert.That(notChild.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Int32BooleanPredicates_PreserveNestedTruthPrecedenceNegationAndLateRebinding()
    {
        var database = CreatePrimitiveDatabase();
        var rows = database.Query().Rows;
        var selectedGroupId = 7;
        var excludedId = int.MaxValue;
        var reboundQuery = rows.Where(row =>
            (row.GroupId == selectedGroupId && row.Id != excludedId) ||
            !(row.GroupId == selectedGroupId && row.Id == excludedId));

        var first = reboundQuery.ToArray();
        selectedGroupId = 3;
        excludedId = 0;
        var rebound = reboundQuery.ToArray();
        var precedence = rows.Where(row =>
            row.Id == 0 || row.GroupId == 7 && row.Id != 0).ToArray();

        await Assert.That(SortedPrimitiveIds(first))
            .IsEqualTo($"{int.MinValue},-11,0,17");
        await Assert.That(SortedPrimitiveIds(rebound))
            .IsEqualTo($"{int.MinValue},-11,17,{int.MaxValue}");
        await Assert.That(SortedPrimitiveIds(precedence))
            .IsEqualTo($"-11,0,17,{int.MaxValue}");

        // Diagnostics describe each complete Where predicate. A false left side inside a
        // successful Or is not itself a rejected row.
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(15);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(15);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(3);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(5);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(5);
    }

    [Test]
    public async Task Int32BooleanPredicates_ComposeWithOrderTakeProjectionAnyAndCount()
    {
        var projectionDatabase = CreatePrimitiveDatabase();
        var selectedGroupId = 7;
        var excludedId = int.MaxValue;

        var projected = projectionDatabase.Query().Rows
            .Where(row =>
                (row.GroupId == selectedGroupId && row.Id != excludedId) ||
                row.Id == 0)
            .OrderBy(static row => row.Id)
            .Take(2)
            .Select(static row => row.Id)
            .ToArray();

        await Assert.That(string.Join(",", projected)).IsEqualTo("-11,0");
        await Assert.That(projectionDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(projectionDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(projectionDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        AssertNoPrimitiveEntityWork(projectionDatabase);

        var anyDatabase = CreatePrimitiveDatabase();
        var any = anyDatabase.Query().Rows.Any(row =>
            (row.GroupId == selectedGroupId || row.Id == int.MinValue) &&
            !(row.Id == excludedId));

        await Assert.That(any).IsTrue();
        await Assert.That(anyDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(1);
        await Assert.That(anyDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(1);
        await Assert.That(anyDatabase.Diagnostics.PredicateRejections).IsEqualTo(0);
        AssertNoPrimitiveEntityWork(anyDatabase);

        var countDatabase = CreatePrimitiveDatabase();
        var count = countDatabase.Query().Rows.Count(row =>
            (row.GroupId == selectedGroupId || row.Id == int.MinValue) &&
            !(row.Id == excludedId));

        await Assert.That(count).IsEqualTo(3);
        await Assert.That(countDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(countDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(countDatabase.Diagnostics.PredicateRejections).IsEqualTo(2);
        AssertNoPrimitiveEntityWork(countDatabase);
    }

    [Test]
    [NotInParallel]
    public async Task CanonicalGuidBooleanPredicates_MixDirectAndTypedLeavesWithExactEagerRebinding()
    {
        var database = CreateConvertedDatabase();
        var rows = database.Query().Rows;
        var idProbe = FirstId;
        var directProbe = SharedDirectGuid;
        var relatedProbe = SharedRelatedId;
        var reboundQuery = rows.Where(row =>
            (row.Id == idProbe && row.DirectGuid == directProbe) ||
            !(row.RelatedId == relatedProbe));

        var firstCount = reboundQuery.Count();
        idProbe = SecondId;
        directProbe = OtherDirectGuid;
        relatedProbe = OtherRelatedId;
        var reboundCount = reboundQuery.Count();

        await Assert.That(firstCount).IsEqualTo(2);
        await Assert.That(reboundCount).IsEqualTo(2);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(
                ["id", "related_id", "id", "related_id"],
                CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(6);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(6);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(2);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
        await Assert.That(database.GetMaterializedRowCount<MemoryConvertedRow>()).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task BooleanPredicateCompilation_NormalizesAnOtherwiseShortCircuitedTypedLeafBeforeScanning()
    {
        const string sensitiveFailure = "boolean-eager-binding-secret-4281";
        var database = CreateConvertedDatabase();
        var directProbe = SharedDirectGuid;
        var relatedProbe = SharedRelatedId;
        MemoryGuidIdConverter.SetToProviderProbe(columnName =>
        {
            if (columnName == "related_id")
                throw new InvalidOperationException(sensitiveFailure);
        });
        var before = database.Diagnostics;

        QueryTranslationException exception;
        try
        {
            exception = Capture<QueryTranslationException>(() =>
                database.Query().Rows.Any(row =>
                    row.DirectGuid == directProbe || row.RelatedId == relatedProbe));
        }
        finally
        {
            MemoryGuidIdConverter.SetToProviderProbe(null);
        }

        // The first seeded row satisfies the direct-Guid left branch. The typed right binding is
        // nevertheless normalized while compiling the invocation-local predicate tree, before any
        // row can exercise Or's evaluation-time short circuit.
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["related_id"], CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(exception.Message).Contains("scalar binding");
        await Assert.That(exception.Message).Contains("memory_converted_rows.related_id");
        await Assert.That(exception.ToString()).DoesNotContain(sensitiveFailure);
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [NotInParallel]
    public async Task BooleanPredicateWithUnsupportedChild_RejectsBeforeStoreOrConversionWork()
    {
        var primitive = CreatePrimitiveDatabase();
        var primitiveBefore = primitive.Diagnostics;
        var sensitiveName = "unsupported-boolean-child-secret-7319";

        var stringChild = Capture<QueryBackendCapabilityException>(() =>
            primitive.Query().Rows
                .Where(row =>
                    row.GroupId == 7 &&
                    !(row.Id == 0 || row.Name == sensitiveName))
                .ToArray());

        await Assert.That(stringChild.Feature)
            .IsEqualTo("ComparisonShape:DefaultNullSemantics");
        await Assert.That(stringChild.Location)
            .IsEqualTo("operations[0].predicate.terms[1].predicate.terms[1].shape");
        await Assert.That(stringChild.ToString()).DoesNotContain(sensitiveName);
        await Assert.That(primitive.Diagnostics).IsEqualTo(primitiveBefore);

        var converted = CreateConvertedDatabase();
        var convertedBefore = converted.Diagnostics;
        MemoryGuidId[] selectedIds = [FirstId];

        var membershipChild = Capture<QueryBackendCapabilityException>(() =>
            converted.Query().Rows
                .Where(row => row.Id == FirstId && selectedIds.Contains(row.Id))
                .ToArray());

        await Assert.That(membershipChild.Feature).IsEqualTo("Predicate:In");
        await Assert.That(membershipChild.Location)
            .IsEqualTo("operations[0].predicate.terms[1]");
        await Assert.That(membershipChild.ToString())
            .DoesNotContain(FirstId.Value.ToString());
        await Assert.That(converted.Diagnostics).IsEqualTo(convertedBefore);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns).IsEmpty();
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreatePrimitiveDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        return database.SeedCanonical<MemoryPrimitiveRow>(
            CreateCanonicalRow(database, id: 17, groupId: 7, name: "seventeen"),
            CreateCanonicalRow(database, id: int.MinValue, groupId: 3, name: "minimum"),
            CreateCanonicalRow(database, id: int.MaxValue, groupId: 7, name: "maximum"),
            CreateCanonicalRow(database, id: -11, groupId: 7, name: "negative-eleven"),
            CreateCanonicalRow(database, id: 0, groupId: 3, name: "zero"));
    }

    private static MemoryDatabase<MemoryConvertedDatabase> CreateConvertedDatabase()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>().Seed<MemoryConvertedRow>(
        [
            new MutableMemoryConvertedRow
            {
                Id = FirstId,
                DirectGuid = SharedDirectGuid,
                RelatedId = SharedRelatedId,
                OptionalRelatedId = null
            },
            new MutableMemoryConvertedRow
            {
                Id = SecondId,
                DirectGuid = OtherDirectGuid,
                RelatedId = SharedRelatedId,
                OptionalRelatedId = SharedRelatedId
            },
            new MutableMemoryConvertedRow
            {
                Id = ThirdId,
                DirectGuid = SharedDirectGuid,
                RelatedId = OtherRelatedId,
                OptionalRelatedId = OtherRelatedId
            }
        ]);
        MemoryGuidIdConverter.Reset();
        return database;
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

    private static string SortedPrimitiveIds(MemoryPrimitiveRow[] rows) =>
        string.Join(",", rows.Select(static row => row.Id).Order());

    private static void AssertNoPrimitiveEntityWork(
        MemoryDatabase<MemoryPrimitiveDatabase> database)
    {
        if (database.Diagnostics.CacheLookups != 0 ||
            database.Diagnostics.CacheHits != 0 ||
            database.Diagnostics.CacheMisses != 0 ||
            database.Diagnostics.Materializations != 0 ||
            database.Diagnostics.CacheInsertions != 0 ||
            database.GetMaterializedRowCount<MemoryPrimitiveRow>() != 0)
        {
            throw new Exception(
                "Boolean scalar execution unexpectedly touched entity materialization or RowCache.");
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

    private sealed class SentinelPredicate(
        bool result,
        bool failIfEvaluated = false) : IMemoryRowPredicate
    {
        public int CallCount { get; private set; }

        public bool Matches(CanonicalProviderValueRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            CallCount++;
            if (failIfEvaluated)
                throw new InvalidOperationException("Short-circuited predicate was evaluated.");

            return result;
        }
    }
}
