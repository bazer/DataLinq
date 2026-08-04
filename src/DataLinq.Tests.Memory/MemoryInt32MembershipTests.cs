using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryInt32MembershipTests
{
    private static readonly Guid FirstGuid =
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid SecondGuid =
        Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f");

    [Test]
    public async Task Contains_SupportsPositiveAndNegatedKeyAndNonKeyMembership()
    {
        var database = CreatePrimitiveDatabase();
        var rows = database.Query().Rows;
        int[] selectedIds = [int.MinValue, 17, 17, int.MaxValue];
        int[] selectedGroups = [3];

        var selected = rows
            .Where(row => selectedIds.Contains(row.Id))
            .ToArray();
        var excluded = rows
            .Where(row => !selectedIds.Contains(row.Id))
            .ToArray();
        var selectedByNonKey = rows
            .Where(row => selectedGroups.Contains(row.GroupId))
            .ToArray();
        var excludedByNonKey = rows
            .Where(row => !selectedGroups.Contains(row.GroupId))
            .ToArray();

        await Assert.That(SortedIds(selected))
            .IsEqualTo($"{int.MinValue},17,{int.MaxValue}");
        await Assert.That(SortedIds(excluded)).IsEqualTo("-11,0");
        await Assert.That(SortedIds(selectedByNonKey))
            .IsEqualTo($"{int.MinValue},0");
        await Assert.That(SortedIds(excludedByNonKey))
            .IsEqualTo($"-11,17,{int.MaxValue}");
    }

    [Test]
    public async Task Contains_EmptyAndReassignedSequencesUseTheCurrentInvocationSnapshot()
    {
        var database = CreatePrimitiveDatabase();
        var rows = database.Query().Rows;
        int[] selectedIds = [-11, 17];
        var reboundQuery = rows.Where(row => selectedIds.Contains(row.Id));

        var first = reboundQuery.ToArray();
        selectedIds = [int.MinValue, int.MaxValue];
        var rebound = reboundQuery.ToArray();
        selectedIds = [0, 0];
        var duplicateRebound = reboundQuery.ToArray();

        int[] empty = [];
        var emptyPositive = rows.Where(row => empty.Contains(row.Id)).ToArray();
        var emptyNegated = rows.Where(row => !empty.Contains(row.Id)).ToArray();
        int[]? nullSequence = null;
        var nullPositive = rows
            .Where(row => Enumerable.Contains(nullSequence!, row.Id))
            .ToArray();
        var nullNegated = rows
            .Where(row => !Enumerable.Contains(nullSequence!, row.Id))
            .ToArray();

        await Assert.That(SortedIds(first)).IsEqualTo("-11,17");
        await Assert.That(SortedIds(rebound))
            .IsEqualTo($"{int.MinValue},{int.MaxValue}");
        await Assert.That(SortedIds(duplicateRebound)).IsEqualTo("0");
        await Assert.That(emptyPositive).IsEmpty();
        await Assert.That(SortedIds(emptyNegated))
            .IsEqualTo($"{int.MinValue},-11,0,17,{int.MaxValue}");
        await Assert.That(nullPositive).IsEmpty();
        await Assert.That(SortedIds(nullNegated))
            .IsEqualTo(SortedIds(emptyNegated));
    }

    [Test]
    public async Task EqualityShapedLocalAny_UsesTheSamePositiveAndNegatedMembershipIsland()
    {
        var database = CreatePrimitiveDatabase();
        var rows = database.Query().Rows;
        int[] selectedIds = [-11, 17];

        var localFirst = rows
            .Where(row => selectedIds.Any(value => value == row.Id))
            .ToArray();
        var columnFirst = rows
            .Where(row => selectedIds.Any(value => row.Id == value))
            .ToArray();
        var negated = rows
            .Where(row => !selectedIds.Any(value => value == row.Id))
            .ToArray();
        int[] empty = [];
        var emptyPositive = rows
            .Where(row => empty.Any(value => value == row.Id))
            .ToArray();
        var emptyNegated = rows
            .Where(row => !empty.Any(value => row.Id == value))
            .ToArray();

        await Assert.That(SortedIds(localFirst)).IsEqualTo("-11,17");
        await Assert.That(SortedIds(columnFirst)).IsEqualTo(SortedIds(localFirst));
        await Assert.That(SortedIds(negated))
            .IsEqualTo($"{int.MinValue},0,{int.MaxValue}");
        await Assert.That(emptyPositive).IsEmpty();
        await Assert.That(SortedIds(emptyNegated))
            .IsEqualTo($"{int.MinValue},-11,0,17,{int.MaxValue}");
    }

    [Test]
    public async Task Membership_ComposesWithBooleanOrderTakeProjectionAnyAndCountWithoutEntityWork()
    {
        var projectionDatabase = CreatePrimitiveDatabase();
        int[] includedIds = [int.MinValue, -11, 0, 17, int.MaxValue];
        int[] excludedGroups = [3];

        var projectedGroups = projectionDatabase.Query().Rows
            .Where(row =>
                includedIds.Contains(row.Id) &&
                !(excludedGroups.Contains(row.GroupId) || row.Id < -11))
            .OrderBy(static row => row.Id)
            .Take(2)
            .Select(static row => row.GroupId)
            .ToArray();

        await Assert.That(string.Join(",", projectedGroups)).IsEqualTo("7,7");
        AssertNoPrimitiveEntityWork(projectionDatabase);

        var anyDatabase = CreatePrimitiveDatabase();
        int[] anyIds = [int.MaxValue];
        var any = anyDatabase.Query().Rows.Any(row => anyIds.Contains(row.Id));

        await Assert.That(any).IsTrue();
        AssertNoPrimitiveEntityWork(anyDatabase);

        var countDatabase = CreatePrimitiveDatabase();
        int[] countedOutGroups = [3];
        var count = countDatabase.Query().Rows.Count(row =>
            !countedOutGroups.Contains(row.GroupId));

        await Assert.That(count).IsEqualTo(3);
        AssertNoPrimitiveEntityWork(countDatabase);
    }

    [Test]
    public async Task NearbyNullableStringWidenedAndBoxedMembershipRejectsBeforeMemoryWork()
    {
        var database = CreatePrimitiveDatabase();
        var before = database.Diagnostics;
        int?[] nullableGroups = [3, null];
        string[] names = ["minimum"];
        long[] widenedIds = [17L];
        object[] boxedGroups = [3];

        var nullable = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => Enumerable.Contains(nullableGroups, (int?)row.GroupId))
                .ToArray());
        var text = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => names.Contains(row.Name))
                .ToArray());
        var widened = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => widenedIds.Contains(row.Id))
                .ToArray());
        var boxed = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => Enumerable.Contains(boxedGroups, (object)row.GroupId))
                .ToArray());

        foreach (var exception in new[] { nullable, text, widened, boxed })
        {
            await Assert.That(exception.Feature).IsEqualTo("MembershipShape:Other");
            await Assert.That(exception.Location).IsEqualTo("operations[0].predicate.shape");
        }

        await Assert.That(database.Diagnostics).IsEqualTo(before);
        await Assert.That(database.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task NullableConvertedAndCanonicalGuidMembershipRejectsBeforeConversionOrMemoryWork()
    {
        var ordered = CreateOrderedShapeDatabase();
        MemoryOrderedIntIdConverter.Reset();
        MemoryOrderedGuidIdConverter.Reset();
        var orderedBefore = ordered.Diagnostics;
        int?[] nullableScores = [7, null];
        MemoryOrderedIntId[] convertedScores = [new(7)];

        var nullable = Capture<QueryBackendCapabilityException>(() =>
            ordered.Query().Rows
                .Where(row => Enumerable.Contains(nullableScores, row.OptionalScore))
                .ToArray());
        var converted = Capture<QueryBackendCapabilityException>(() =>
            ordered.Query().Rows
                .Where(row => convertedScores.Contains(row.ConvertedScore))
                .ToArray());

        var guid = CreateGuidDatabase();
        MemoryGuidIdConverter.Reset();
        var guidBefore = guid.Diagnostics;
        Guid[] directGuids = [FirstGuid];
        MemoryGuidId[] typedIds = [new(FirstGuid)];

        var directGuid = Capture<QueryBackendCapabilityException>(() =>
            guid.Query().Rows
                .Where(row => directGuids.Contains(row.DirectGuid))
                .ToArray());
        var typedGuid = Capture<QueryBackendCapabilityException>(() =>
            guid.Query().Rows
                .Where(row => typedIds.Contains(row.Id))
                .ToArray());

        foreach (var exception in new[] { nullable, converted, directGuid, typedGuid })
        {
            await Assert.That(exception.Feature).IsEqualTo("MembershipShape:Other");
            await Assert.That(exception.Location).IsEqualTo("operations[0].predicate.shape");
        }

        await Assert.That(ordered.Diagnostics).IsEqualTo(orderedBefore);
        await Assert.That(guid.Diagnostics).IsEqualTo(guidBefore);
        await Assert.That(MemoryOrderedIntIdConverter.ToProviderCalls).IsEqualTo(0);
        await Assert.That(MemoryOrderedIntIdConverter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(MemoryOrderedGuidIdConverter.ToProviderCalls).IsEqualTo(0);
        await Assert.That(MemoryOrderedGuidIdConverter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns).IsEmpty();
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreatePrimitiveDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        return database.SeedCanonical<MemoryPrimitiveRow>(
            CreatePrimitiveCanonicalRow(database, id: 17, groupId: 7, name: "seventeen"),
            CreatePrimitiveCanonicalRow(database, id: int.MinValue, groupId: 3, name: "minimum"),
            CreatePrimitiveCanonicalRow(database, id: int.MaxValue, groupId: 7, name: "maximum"),
            CreatePrimitiveCanonicalRow(database, id: -11, groupId: 7, name: "negative-eleven"),
            CreatePrimitiveCanonicalRow(database, id: 0, groupId: 3, name: "zero"));
    }

    private static MemoryDatabase<MemoryOrderedComparisonDatabase> CreateOrderedShapeDatabase()
    {
        var database = new MemoryDatabase<MemoryOrderedComparisonDatabase>();
        var table = database.Metadata
            .GetTableModel(typeof(MemoryOrderedComparisonRow))
            .Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = 1;
        values[table.GetColumnByDbName("optional_score").Index] = 7;
        values[table.GetColumnByDbName("converted_score").Index] = 7;
        values[table.GetColumnByDbName("converted_guid").Index] = FirstGuid;
        return database.SeedCanonical<MemoryOrderedComparisonRow>(values);
    }

    private static MemoryDatabase<MemoryConvertedDatabase> CreateGuidDatabase()
    {
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        var table = database.Metadata.GetTableModel(typeof(MemoryConvertedRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = FirstGuid;
        values[table.GetColumnByDbName("direct_guid").Index] = SecondGuid;
        values[table.GetColumnByDbName("related_id").Index] = SecondGuid;
        values[table.GetColumnByDbName("optional_related_id").Index] = null;
        return database.SeedCanonical<MemoryConvertedRow>(values);
    }

    private static object?[] CreatePrimitiveCanonicalRow(
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

    private static string SortedIds(MemoryPrimitiveRow[] rows) =>
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
                "Int32 membership scalar execution unexpectedly touched entity materialization or RowCache.");
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
