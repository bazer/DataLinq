using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;
using TUnit.Assertions.Enums;

namespace DataLinq.Tests.Memory;

public sealed class MemoryNotEqualTests
{
    private static readonly MemoryGuidId FirstId = new(
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
    private static readonly MemoryGuidId SecondId = new(
        Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"));
    private static readonly MemoryGuidId ThirdId = new(
        Guid.Parse("20314253-6475-8697-a8b9-cadbecfd0e1f"));
    private static readonly MemoryGuidId MissingId = new(
        Guid.Parse("ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb"));
    private static readonly Guid SharedDirectGuid =
        Guid.Parse("f1e2d3c4-b5a6-4789-90ab-cdef12345678");
    private static readonly Guid OtherDirectGuid =
        Guid.Parse("e2d3c4b5-a697-4809-a1bc-def234567890");
    private static readonly Guid MissingDirectGuid =
        Guid.Parse("d3c4b5a6-9780-491a-b2cd-ef3456789012");
    private static readonly MemoryGuidId SharedRelatedId = new(
        Guid.Parse("31425364-7586-97a8-b9ca-dbecfd0e1f20"));
    private static readonly MemoryGuidId OtherRelatedId = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"));

    [Test]
    public async Task Int32NotEqual_EntitySequenceSupportsOperandReversalAndLateRebinding()
    {
        var database = CreatePrimitiveDatabase();
        var rows = database.Model.Rows;
        var groupId = 7;
        var reboundQuery = rows.Where(row => row.GroupId != groupId);

        var notSeven = reboundQuery.ToArray();
        groupId = 3;
        var notThree = reboundQuery.ToArray();
        var reversed = rows.Where(row => groupId != row.GroupId).ToArray();

        await Assert.That(PrimitiveIds(notSeven)).IsEqualTo($"{int.MinValue},0");
        await Assert.That(PrimitiveIds(notThree)).IsEqualTo($"17,{int.MaxValue},-11");
        await Assert.That(PrimitiveIds(reversed)).IsEqualTo(PrimitiveIds(notThree));
        await Assert.That(reversed[0]).IsSameReferenceAs(notThree[0]);
        await Assert.That(reversed[1]).IsSameReferenceAs(notThree[1]);
        await Assert.That(reversed[2]).IsSameReferenceAs(notThree[2]);
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(15);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(15);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(7);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(5);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(5);
    }

    [Test]
    public async Task Int32NotEqual_ComposesWithEqualityOrderingTakeAndScalarProjection()
    {
        var database = CreatePrimitiveDatabase();
        var groupId = 7;
        var excludedId = int.MaxValue;

        var ids = database.Model.Rows
            .Where(row => row.GroupId == groupId)
            .Where(row => row.Id != excludedId)
            .OrderBy(static row => row.Id)
            .Take(2)
            .Select(static row => row.Id)
            .ToArray();

        await Assert.That(string.Join(",", ids)).IsEqualTo("-11,17");
        await Assert.That(database.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(database.Diagnostics.PredicateEvaluations).IsEqualTo(8);
        await Assert.That(database.Diagnostics.PredicateRejections).IsEqualTo(3);
        AssertNoPrimitiveEntityWork(database);
    }

    [Test]
    public async Task Int32NotEqual_AnyShortCircuitsAndCountAvoidsEntityWork()
    {
        var anyDatabase = CreatePrimitiveDatabase();
        var excludedGroupId = 7;

        var any = anyDatabase.Model.Rows.Any(row => row.GroupId != excludedGroupId);

        await Assert.That(any).IsTrue();
        await Assert.That(anyDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(2);
        await Assert.That(anyDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(2);
        await Assert.That(anyDatabase.Diagnostics.PredicateRejections).IsEqualTo(1);
        AssertNoPrimitiveEntityWork(anyDatabase);

        var countDatabase = CreatePrimitiveDatabase();
        var count = countDatabase.Model.Rows.Count(row => row.GroupId != excludedGroupId);

        await Assert.That(count).IsEqualTo(2);
        await Assert.That(countDatabase.Diagnostics.ScanRowsVisited).IsEqualTo(5);
        await Assert.That(countDatabase.Diagnostics.PredicateEvaluations).IsEqualTo(5);
        await Assert.That(countDatabase.Diagnostics.PredicateRejections).IsEqualTo(3);
        AssertNoPrimitiveEntityWork(countDatabase);
    }

    [Test]
    [NotInParallel]
    public async Task GuidNotEqual_DirectAndTypedColumnsSupportHitMissReverseRebindingAndMixedPredicates()
    {
        var database = CreateConvertedDatabase();
        var rows = database.Query().Rows;
        var idProbe = FirstId;
        var reboundQuery = rows.Where(row => row.Id != idProbe);

        var notFirst = reboundQuery.ToArray();
        idProbe = SecondId;
        var notSecond = reboundQuery.ToArray();
        var reversed = rows.Where(row => idProbe != row.Id).ToArray();
        var directNotShared = rows
            .Where(row => row.DirectGuid != SharedDirectGuid)
            .ToArray();
        var directNotMissing = rows
            .Where(row => MissingDirectGuid != row.DirectGuid)
            .ToArray();
        var relatedNotShared = rows
            .Where(row => row.RelatedId != SharedRelatedId)
            .ToArray();
        var mixed = rows
            .Where(row => row.DirectGuid == SharedDirectGuid)
            .Where(row => row.RelatedId != SharedRelatedId)
            .ToArray();

        await Assert.That(GuidIds(notFirst)).IsEqualTo(GuidIds(SecondId, ThirdId));
        await Assert.That(GuidIds(notSecond)).IsEqualTo(GuidIds(FirstId, ThirdId));
        await Assert.That(GuidIds(reversed)).IsEqualTo(GuidIds(notSecond));
        await Assert.That(reversed[0]).IsSameReferenceAs(notSecond[0]);
        await Assert.That(reversed[1]).IsSameReferenceAs(notSecond[1]);
        await Assert.That(GuidIds(directNotShared)).IsEqualTo(GuidIds(SecondId));
        await Assert.That(GuidIds(directNotMissing)).IsEqualTo(GuidIds(FirstId, SecondId, ThirdId));
        await Assert.That(GuidIds(relatedNotShared)).IsEqualTo(GuidIds(ThirdId));
        await Assert.That(GuidIds(mixed)).IsEqualTo(GuidIds(ThirdId));
        await Assert.That(database.GetMaterializedRowCount<MemoryConvertedRow>()).IsEqualTo(3);
    }

    [Test]
    [NotInParallel]
    public async Task GuidNotEqual_AnyAndCountNormalizeEachTypedBindingOnceWithoutMaterializing()
    {
        var database = CreateConvertedDatabase();
        var rows = database.Query().Rows;
        var idProbe = FirstId;
        var relatedProbe = SharedRelatedId;
        var reboundQuery = rows
            .Where(row => row.Id != idProbe)
            .Where(row => row.RelatedId != relatedProbe);

        var firstCount = reboundQuery.Count();
        idProbe = SecondId;
        var secondCount = reboundQuery.Count();
        var directAny = rows.Any(row => row.DirectGuid != SharedDirectGuid);
        var typedAny = rows.Any(row => row.Id != MissingId);

        await Assert.That(firstCount).IsEqualTo(1);
        await Assert.That(secondCount).IsEqualTo(1);
        await Assert.That(directAny).IsTrue();
        await Assert.That(typedAny).IsTrue();
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(
                ["id", "related_id", "id", "related_id", "id"],
                CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(database.GetMaterializedRowCount<MemoryConvertedRow>()).IsEqualTo(0);
        await Assert.That(database.Diagnostics.CacheLookups).IsEqualTo(0);
        await Assert.That(database.Diagnostics.Materializations).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task TypedGuidNotEqualBindingFailure_RedactsOrdinaryConverterExceptions()
    {
        const string outerSecret = "not-equal-converter-outer-secret-4821";
        const string innerSecret = "not-equal-converter-inner-secret-7359";
        var database = CreateConvertedDatabase();
        MemoryGuidIdConverter.SetToProviderProbe(_ =>
            throw new InvalidOperationException(
                outerSecret,
                new Exception(innerSecret)));
        var before = database.Diagnostics;

        QueryTranslationException exception;
        try
        {
            exception = Capture<QueryTranslationException>(() =>
                database.Query().Rows.Any(row => row.Id != FirstId));
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }

        await Assert.That(exception.Message).Contains("scalar binding 'p0'");
        await Assert.That(exception.Message).Contains("memory_converted_rows.id");
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(exception.GetBaseException()).IsSameReferenceAs(exception);
        await Assert.That(exception.ToString()).DoesNotContain(outerSecret);
        await Assert.That(exception.ToString()).DoesNotContain(innerSecret);
        await Assert.That(exception.ToString()).DoesNotContain(FirstId.Value.ToString());
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [NotInParallel]
    public async Task TypedGuidNotEqualBindingFailure_PreservesCancellationAndFatalExceptionIdentity()
    {
        var database = CreateConvertedDatabase();
        var before = database.Diagnostics;
        Exception[] sentinels =
        [
            new OperationCanceledException("not-equal conversion cancelled"),
            new OutOfMemoryException("not-equal conversion exhausted memory"),
            new AccessViolationException("not-equal conversion accessed invalid memory")
        ];

        foreach (var sentinel in sentinels)
        {
            MemoryGuidIdConverter.Reset();
            MemoryGuidIdConverter.SetToProviderProbe(_ => throw sentinel);

            Exception actual;
            try
            {
                actual = Capture<Exception>(() =>
                    database.Query().Rows.Any(row => row.Id != FirstId));
            }
            finally
            {
                MemoryGuidIdConverter.Reset();
            }

            await Assert.That(actual).IsSameReferenceAs(sentinel);
            await Assert.That(database.Diagnostics).IsEqualTo(before);
        }
    }

    [Test]
    [NotInParallel]
    public async Task NearbyNotEqualShapes_RemainUnsupportedBeforeStoreWork()
    {
        var primitive = CreatePrimitiveDatabase();
        var primitiveBefore = primitive.Diagnostics;
        var name = "seventeen";
        long promotedGroupId = 7;

        var stringComparison = Capture<QueryBackendCapabilityException>(() =>
            primitive.Model.Rows.Where(row => row.Name != name).ToArray());
        var promotedComparison = Capture<QueryBackendCapabilityException>(() =>
            primitive.Model.Rows.Where(row => row.GroupId != promotedGroupId).ToArray());
        var columnComparison = Capture<QueryBackendCapabilityException>(() =>
            primitive.Model.Rows.Where(row => row.Id != row.GroupId).ToArray());

        await Assert.That(stringComparison.Feature)
            .IsEqualTo("ComparisonShape:DefaultNullSemantics");
        await Assert.That(promotedComparison.Feature)
            .IsEqualTo("ComparisonShape:DefaultNullSemantics");
        await Assert.That(columnComparison.Feature)
            .IsEqualTo("ComparisonShape:DefaultNullSemantics");
        await Assert.That(primitive.Diagnostics).IsEqualTo(primitiveBefore);

        var converted = CreateConvertedDatabase();
        var convertedBefore = converted.Diagnostics;
        MemoryGuidId? nullableProbe = SharedRelatedId;

        var nullableComparison = Capture<QueryBackendCapabilityException>(() =>
            converted.Query().Rows
                .Where(row => row.OptionalRelatedId != nullableProbe)
                .ToArray());

        await Assert.That(nullableComparison.Feature)
            .IsEqualTo("NullSemantics:CSharpNullableComparison");
        await Assert.That(nullableComparison.Location)
            .IsEqualTo("operations[0].predicate.nullSemantics");
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

    private static string PrimitiveIds(MemoryPrimitiveRow[] rows) =>
        string.Join(",", rows.Select(static row => row.Id));

    private static string GuidIds(MemoryConvertedRow[] rows) =>
        string.Join(",", rows.Select(static row => row.Id.Value));

    private static string GuidIds(params MemoryGuidId[] ids) =>
        string.Join(",", ids.Select(static id => id.Value));

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
                "NotEqual scalar execution unexpectedly touched entity materialization or RowCache.");
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
