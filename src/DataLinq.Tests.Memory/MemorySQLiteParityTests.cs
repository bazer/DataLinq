using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Memory;
using DataLinq.Metadata;
using DataLinq.SQLite;

namespace DataLinq.Tests.Memory;

public sealed class MemorySQLiteParityTests
{
    private static readonly MemoryGuidId FirstGuidId = new(
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
    private static readonly MemoryGuidId SecondGuidId = new(
        Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"));
    private static readonly MemoryGuidId ThirdGuidId = new(
        Guid.Parse("20314253-6475-8697-a8b9-cadbecfd0e1f"));
    private static readonly Guid SharedDirectGuid =
        Guid.Parse("f1e2d3c4-b5a6-4789-90ab-cdef12345678");
    private static readonly Guid OtherDirectGuid =
        Guid.Parse("e2d3c4b5-a697-4809-a1bc-def234567890");
    private static readonly Guid MissingDirectGuid =
        Guid.Parse("d3c4b5a6-9780-491a-b2cd-ef3456789012");
    private static readonly MemoryGuidId SharedRelatedId = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"));
    private static readonly MemoryGuidId OtherRelatedId = new(
        Guid.Parse("76543210-fedc-ba98-7654-3210fedcba98"));
    private static readonly MemoryGuidId MissingRelatedId = new(
        Guid.Parse("01234567-89ab-cdef-1032-547698badcfe"));

    private static readonly ParitySeedRow[] SeedRows =
    [
        new(17, 3, "seventeen"),
        new(int.MinValue, 3, "minimum"),
        new(int.MaxValue, 7, "maximum"),
        new(-11, 7, "negative-eleven"),
        new(0, 7, "zero")
    ];

    private static readonly ParityObservation ExpectedObservation = new(
        RootEntities:
            "-2147483648:3:minimum|-11:7:negative-eleven|0:7:zero|" +
            "17:3:seventeen|2147483647:7:maximum",
        RepeatedEqualityEntities: "0:7:zero",
        AscendingEntityIds: "-2147483648,-11,0,17,2147483647",
        DescendingEntityIds: "2147483647,17,0,-11,-2147483648",
        TakeZeroEntityIds: string.Empty,
        OrderedTakenEntityIds: "2147483647,0",
        OverCardinalityEntityIds: "-2147483648,-11,0,17,2147483647",
        OrderedScalarIds: "-2147483648,-11,0,17,2147483647",
        OrderedScalarGroupIds: "3,7,7,3,7",
        ComposedScalarIds: "2147483647,0",
        EmptyScalarIds: string.Empty,
        RootAny: true,
        MatchingAny: true,
        MissingAny: false,
        RootCount: 5,
        MatchingCount: 3,
        MissingCount: 0,
        ProjectedRootAny: true,
        ProjectedMissingAny: false,
        ProjectedRootCount: 5,
        ProjectedMatchingCount: 3,
        ProjectedMissingCount: 0);

    private static readonly ConvertedParitySeedRow[] ConvertedSeedRows =
    [
        new(FirstGuidId, SharedDirectGuid, SharedRelatedId),
        new(SecondGuidId, OtherDirectGuid, SharedRelatedId),
        new(ThirdGuidId, SharedDirectGuid, OtherRelatedId)
    ];

    private static readonly CanonicalGuidParityObservation ExpectedCanonicalGuidObservation = new(
        DirectHitIds:
            "00112233-4455-6677-8899-aabbccddeeff," +
            "20314253-6475-8697-a8b9-cadbecfd0e1f",
        DirectMissIds: string.Empty,
        TypedRelatedHitIds:
            "00112233-4455-6677-8899-aabbccddeeff," +
            "10213243-5465-7687-98a9-bacbdcedfe0f",
        TypedRelatedMissIds: string.Empty,
        TypedPrimaryKeyHitIds: "10213243-5465-7687-98a9-bacbdcedfe0f",
        MixedEqualityIds: "00112233-4455-6677-8899-aabbccddeeff",
        DirectHitAny: true,
        TypedRelatedHitCount: 2);

    [Test]
    public async Task AdmittedPrimitiveQueryIsland_MatchesSQLiteForTheSameQueryShapes()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);
        await Assert.That(ReferenceEquals(memory.Metadata, sqlite.Provider.Metadata)).IsTrue();

        const int matchingGroupId = 7;
        const int missingGroupId = 99;
        const int matchingId = 0;
        var observations = Observe(
            memory,
            sqlite,
            matchingGroupId,
            missingGroupId,
            matchingId);

        await Assert.That(observations.Memory).IsEqualTo(ExpectedObservation);
        await Assert.That(observations.SQLite).IsEqualTo(ExpectedObservation);
        await Assert.That(memory.Diagnostics.ScanRowsVisited).IsGreaterThan(0);
        await Assert.That(memory.Diagnostics.PredicateEvaluations).IsGreaterThan(0);
    }

    [Test]
    public async Task PagedTerminal_RemainsADeliberateSQLiteOnlyDifference()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        Expression<Func<bool>> query = () => rows
            .OrderBy(static row => row.Id)
            .Take(2)
            .Any();
        var invocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            query.Body,
            typeof(bool));
        var before = memory.Diagnostics;

        var sqliteResult = ExpressionQueryPlanExecutor.Execute<bool>(
            sqlite.Provider.ReadOnlyAccess,
            invocation);
        var memoryFailure = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanExecutor.Execute<bool>(memory.ReadSource, invocation));

        await Assert.That(sqliteResult).IsTrue();
        await Assert.That(memoryFailure.Message).Contains("Operation:Pushdown");
        await Assert.That(memory.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [NotInParallel]
    public async Task AdmittedCanonicalGuidEqualityIsland_MatchesSQLiteForTheSameInvocations()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var memory = CreateConvertedMemoryDatabase();
            using var sqlite = new SQLiteDatabase<MemoryConvertedDatabase>("Data Source=:memory:");
            await InitializeConvertedSQLite(sqlite);
            await Assert.That(ReferenceEquals(memory.Metadata, sqlite.Provider.Metadata)).IsTrue();

            var rows = memory.Query().Rows;
            var directHit = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => row.DirectGuid == SharedDirectGuid));
            var directMiss = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => MissingDirectGuid == row.DirectGuid));
            var typedRelatedHit = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => row.RelatedId == SharedRelatedId));
            var typedRelatedMiss = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => MissingRelatedId == row.RelatedId));
            var typedPrimaryKeyHit = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => SecondGuidId == row.Id));
            var mixedEquality = ExecuteSequence(
                memory,
                sqlite,
                rows
                    .Where(row => row.DirectGuid == SharedDirectGuid)
                    .Where(row => row.RelatedId == SharedRelatedId));
            var directHitAny = ExecuteScalar(
                memory,
                sqlite,
                () => rows.Any(row => row.DirectGuid == SharedDirectGuid));
            var typedRelatedHitCount = ExecuteScalar(
                memory,
                sqlite,
                () => rows.Count(row => row.RelatedId == SharedRelatedId));

            var observations = new ProviderPair<CanonicalGuidParityObservation>(
                CreateCanonicalGuidObservation(
                    directHit.Memory,
                    directMiss.Memory,
                    typedRelatedHit.Memory,
                    typedRelatedMiss.Memory,
                    typedPrimaryKeyHit.Memory,
                    mixedEquality.Memory,
                    directHitAny.Memory,
                    typedRelatedHitCount.Memory),
                CreateCanonicalGuidObservation(
                    directHit.SQLite,
                    directMiss.SQLite,
                    typedRelatedHit.SQLite,
                    typedRelatedMiss.SQLite,
                    typedPrimaryKeyHit.SQLite,
                    mixedEquality.SQLite,
                    directHitAny.SQLite,
                    typedRelatedHitCount.SQLite));

            await Assert.That(observations.Memory).IsEqualTo(ExpectedCanonicalGuidObservation);
            await Assert.That(observations.SQLite).IsEqualTo(ExpectedCanonicalGuidObservation);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    public async Task AdmittedPrimitiveInequalityIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        var excludedGroupId = 7;
        var reboundQuery = rows
            .Where(row => row.GroupId != excludedGroupId)
            .OrderBy(static row => row.Id)
            .Select(static row => row.Id);

        var firstBinding = ExecuteSequence(memory, sqlite, reboundQuery);
        excludedGroupId = 3;
        var reboundBinding = ExecuteSequence(memory, sqlite, reboundQuery);
        var reversed = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => excludedGroupId != row.GroupId)
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));
        var mixed = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId != excludedGroupId)
                .Where(static row => row.Id != int.MaxValue)
                .OrderBy(static row => row.Id)
                .Take(2)
                .Select(static row => row.Id));
        var any = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Any(static row => row.Id != int.MinValue));
        var count = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Count(static row => row.GroupId != 7));

        await Assert.That(string.Join(",", firstBinding.Memory))
            .IsEqualTo("-2147483648,17");
        await Assert.That(string.Join(",", firstBinding.SQLite))
            .IsEqualTo(string.Join(",", firstBinding.Memory));
        await Assert.That(string.Join(",", reboundBinding.Memory))
            .IsEqualTo("-11,0,2147483647");
        await Assert.That(string.Join(",", reboundBinding.SQLite))
            .IsEqualTo(string.Join(",", reboundBinding.Memory));
        await Assert.That(string.Join(",", reversed.Memory))
            .IsEqualTo(string.Join(",", reboundBinding.Memory));
        await Assert.That(string.Join(",", reversed.SQLite))
            .IsEqualTo(string.Join(",", reversed.Memory));
        await Assert.That(string.Join(",", mixed.Memory)).IsEqualTo("-11,0");
        await Assert.That(string.Join(",", mixed.SQLite))
            .IsEqualTo(string.Join(",", mixed.Memory));
        await Assert.That(any.Memory).IsTrue();
        await Assert.That(any.SQLite).IsEqualTo(any.Memory);
        await Assert.That(count.Memory).IsEqualTo(2);
        await Assert.That(count.SQLite).IsEqualTo(count.Memory);
    }

    [Test]
    [NotInParallel]
    public async Task AdmittedCanonicalGuidInequalityIsland_MatchesSQLiteForTheSameInvocations()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var memory = CreateConvertedMemoryDatabase();
            using var sqlite = new SQLiteDatabase<MemoryConvertedDatabase>("Data Source=:memory:");
            await InitializeConvertedSQLite(sqlite);

            var rows = memory.Query().Rows;
            var excludedId = FirstGuidId;
            var reboundQuery = rows.Where(row => row.Id != excludedId);

            var firstBinding = ExecuteSequence(memory, sqlite, reboundQuery);
            excludedId = SecondGuidId;
            var reboundBinding = ExecuteSequence(memory, sqlite, reboundQuery);
            var reversed = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => excludedId != row.Id));
            var direct = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => row.DirectGuid != SharedDirectGuid));
            var typedRelated = ExecuteSequence(
                memory,
                sqlite,
                rows.Where(row => row.RelatedId != SharedRelatedId));
            var mixed = ExecuteSequence(
                memory,
                sqlite,
                rows
                    .Where(row => row.DirectGuid == SharedDirectGuid)
                    .Where(row => row.Id != FirstGuidId));
            var any = ExecuteScalar(
                memory,
                sqlite,
                () => rows.Any(row => row.DirectGuid != MissingDirectGuid));
            var count = ExecuteScalar(
                memory,
                sqlite,
                () => rows.Count(row => row.RelatedId != SharedRelatedId));

            await Assert.That(SnapshotConvertedIds(firstBinding.Memory)).IsEqualTo(
                "10213243-5465-7687-98a9-bacbdcedfe0f," +
                "20314253-6475-8697-a8b9-cadbecfd0e1f");
            await Assert.That(SnapshotConvertedIds(firstBinding.SQLite))
                .IsEqualTo(SnapshotConvertedIds(firstBinding.Memory));
            await Assert.That(SnapshotConvertedIds(reboundBinding.Memory)).IsEqualTo(
                "00112233-4455-6677-8899-aabbccddeeff," +
                "20314253-6475-8697-a8b9-cadbecfd0e1f");
            await Assert.That(SnapshotConvertedIds(reboundBinding.SQLite))
                .IsEqualTo(SnapshotConvertedIds(reboundBinding.Memory));
            await Assert.That(SnapshotConvertedIds(reversed.Memory))
                .IsEqualTo(SnapshotConvertedIds(reboundBinding.Memory));
            await Assert.That(SnapshotConvertedIds(reversed.SQLite))
                .IsEqualTo(SnapshotConvertedIds(reversed.Memory));
            await Assert.That(SnapshotConvertedIds(direct.Memory))
                .IsEqualTo("10213243-5465-7687-98a9-bacbdcedfe0f");
            await Assert.That(SnapshotConvertedIds(direct.SQLite))
                .IsEqualTo(SnapshotConvertedIds(direct.Memory));
            await Assert.That(SnapshotConvertedIds(typedRelated.Memory))
                .IsEqualTo("20314253-6475-8697-a8b9-cadbecfd0e1f");
            await Assert.That(SnapshotConvertedIds(typedRelated.SQLite))
                .IsEqualTo(SnapshotConvertedIds(typedRelated.Memory));
            await Assert.That(SnapshotConvertedIds(mixed.Memory))
                .IsEqualTo("20314253-6475-8697-a8b9-cadbecfd0e1f");
            await Assert.That(SnapshotConvertedIds(mixed.SQLite))
                .IsEqualTo(SnapshotConvertedIds(mixed.Memory));
            await Assert.That(any.Memory).IsTrue();
            await Assert.That(any.SQLite).IsEqualTo(any.Memory);
            await Assert.That(count.Memory).IsEqualTo(1);
            await Assert.That(count.SQLite).IsEqualTo(count.Memory);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    public async Task AdmittedPrimitiveBooleanCompositionIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        var includedGroupId = 7;
        var excludedId = 0;
        var reboundQuery = rows
            .Where(row =>
                (row.GroupId == includedGroupId && row.Id != excludedId) ||
                !(row.GroupId == includedGroupId))
            .OrderBy(static row => row.Id)
            .Select(static row => row.Id);

        var firstBinding = ExecuteSequence(memory, sqlite, reboundQuery);
        includedGroupId = 3;
        excludedId = 17;
        var reboundBinding = ExecuteSequence(memory, sqlite, reboundQuery);
        var any = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Any(row =>
                (row.GroupId == includedGroupId && row.Id != excludedId) ||
                !(row.GroupId == includedGroupId)));
        var count = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Count(row =>
                (row.GroupId == includedGroupId && row.Id != excludedId) ||
                !(row.GroupId == includedGroupId)));

        await Assert.That(string.Join(",", firstBinding.Memory))
            .IsEqualTo("-2147483648,-11,17,2147483647");
        await Assert.That(string.Join(",", firstBinding.SQLite))
            .IsEqualTo(string.Join(",", firstBinding.Memory));
        await Assert.That(string.Join(",", reboundBinding.Memory))
            .IsEqualTo("-2147483648,-11,0,2147483647");
        await Assert.That(string.Join(",", reboundBinding.SQLite))
            .IsEqualTo(string.Join(",", reboundBinding.Memory));
        await Assert.That(any.Memory).IsTrue();
        await Assert.That(any.SQLite).IsEqualTo(any.Memory);
        await Assert.That(count.Memory).IsEqualTo(4);
        await Assert.That(count.SQLite).IsEqualTo(count.Memory);
    }

    [Test]
    [NotInParallel]
    public async Task AdmittedCanonicalGuidBooleanCompositionIsland_MatchesSQLiteForTheSameInvocations()
    {
        MemoryGuidIdConverter.Reset();
        try
        {
            var memory = CreateConvertedMemoryDatabase();
            using var sqlite = new SQLiteDatabase<MemoryConvertedDatabase>("Data Source=:memory:");
            await InitializeConvertedSQLite(sqlite);

            var rows = memory.Query().Rows;
            var directProbe = SharedDirectGuid;
            var excludedRelatedId = SharedRelatedId;
            var excludedId = FirstGuidId;
            var reboundQuery = rows.Where(row =>
                (row.DirectGuid == directProbe && row.RelatedId != excludedRelatedId) ||
                !(row.Id == excludedId));

            var firstBinding = ExecuteSequence(memory, sqlite, reboundQuery);
            directProbe = OtherDirectGuid;
            excludedRelatedId = OtherRelatedId;
            excludedId = ThirdGuidId;
            var reboundBinding = ExecuteSequence(memory, sqlite, reboundQuery);
            var any = ExecuteScalar(
                memory,
                sqlite,
                () => rows.Any(row =>
                    (row.DirectGuid == directProbe && row.RelatedId != excludedRelatedId) ||
                    !(row.Id == excludedId)));
            var count = ExecuteScalar(
                memory,
                sqlite,
                () => rows.Count(row =>
                    (row.DirectGuid == directProbe && row.RelatedId != excludedRelatedId) ||
                    !(row.Id == excludedId)));

            await Assert.That(SnapshotConvertedIds(firstBinding.Memory)).IsEqualTo(
                "10213243-5465-7687-98a9-bacbdcedfe0f," +
                "20314253-6475-8697-a8b9-cadbecfd0e1f");
            await Assert.That(SnapshotConvertedIds(firstBinding.SQLite))
                .IsEqualTo(SnapshotConvertedIds(firstBinding.Memory));
            await Assert.That(SnapshotConvertedIds(reboundBinding.Memory)).IsEqualTo(
                "00112233-4455-6677-8899-aabbccddeeff," +
                "10213243-5465-7687-98a9-bacbdcedfe0f");
            await Assert.That(SnapshotConvertedIds(reboundBinding.SQLite))
                .IsEqualTo(SnapshotConvertedIds(reboundBinding.Memory));
            await Assert.That(any.Memory).IsTrue();
            await Assert.That(any.SQLite).IsEqualTo(any.Memory);
            await Assert.That(count.Memory).IsEqualTo(2);
            await Assert.That(count.SQLite).IsEqualTo(count.Memory);
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }
    }

    [Test]
    public async Task AdmittedPrimitiveRelationalRangeIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        var lowerExclusive = -11;
        var upperInclusive = 17;
        var columnFirstQuery = rows
            .Where(row => row.Id > lowerExclusive && row.Id <= upperInclusive)
            .OrderBy(static row => row.Id)
            .Select(static row => row.Id);

        var firstBinding = ExecuteSequence(memory, sqlite, columnFirstQuery);
        lowerExclusive = int.MinValue;
        upperInclusive = 0;
        var reboundBinding = ExecuteSequence(memory, sqlite, columnFirstQuery);
        var scalarFirstQuery = rows
            .Where(row => lowerExclusive < row.Id && upperInclusive >= row.Id)
            .OrderBy(static row => row.Id)
            .Select(static row => row.Id);
        var reversedOperands = ExecuteSequence(memory, sqlite, scalarFirstQuery);

        await Assert.That(string.Join(",", firstBinding.Memory)).IsEqualTo("0,17");
        await Assert.That(string.Join(",", firstBinding.SQLite))
            .IsEqualTo(string.Join(",", firstBinding.Memory));
        await Assert.That(string.Join(",", reboundBinding.Memory)).IsEqualTo("-11,0");
        await Assert.That(string.Join(",", reboundBinding.SQLite))
            .IsEqualTo(string.Join(",", reboundBinding.Memory));
        await Assert.That(string.Join(",", reversedOperands.Memory))
            .IsEqualTo(string.Join(",", reboundBinding.Memory));
        await Assert.That(string.Join(",", reversedOperands.SQLite))
            .IsEqualTo(string.Join(",", reversedOperands.Memory));
    }

    [Test]
    public async Task AdmittedPrimitiveInt32MembershipIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        int[] selectedIds = [int.MinValue, 17, int.MaxValue, 17];
        var membershipQuery = rows
            .Where(row => selectedIds.Contains(row.Id))
            .OrderBy(static row => row.Id)
            .Select(static row => row.Id);

        var firstBinding = ExecuteSequence(memory, sqlite, membershipQuery);
        selectedIds = [-11, 0];
        var reboundBinding = ExecuteSequence(memory, sqlite, membershipQuery);
        var negated = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => !Enumerable.Contains(selectedIds, row.Id))
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));

        selectedIds = [];
        var emptyPositive = ExecuteSequence(memory, sqlite, membershipQuery);
        var emptyNegated = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => !selectedIds.Contains(row.Id))
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));

        selectedIds = null!;
        var nullPositive = ExecuteSequence(memory, sqlite, membershipQuery);
        var nullNegated = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => !Enumerable.Contains(selectedIds, row.Id))
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));

        selectedIds = [int.MinValue, 0, int.MaxValue];
        var composed = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => selectedIds.Contains(row.Id) && row.GroupId >= 7)
                .OrderBy(static row => row.Id)
                .Take(2)
                .Select(static row => row.GroupId));

        await Assert.That(string.Join(",", firstBinding.Memory))
            .IsEqualTo("-2147483648,17,2147483647");
        await Assert.That(string.Join(",", firstBinding.SQLite))
            .IsEqualTo(string.Join(",", firstBinding.Memory));
        await Assert.That(string.Join(",", reboundBinding.Memory)).IsEqualTo("-11,0");
        await Assert.That(string.Join(",", reboundBinding.SQLite))
            .IsEqualTo(string.Join(",", reboundBinding.Memory));
        await Assert.That(string.Join(",", negated.Memory))
            .IsEqualTo("-2147483648,17,2147483647");
        await Assert.That(string.Join(",", negated.SQLite))
            .IsEqualTo(string.Join(",", negated.Memory));
        await Assert.That(emptyPositive.Memory).IsEmpty();
        await Assert.That(emptyPositive.SQLite).IsEmpty();
        await Assert.That(string.Join(",", emptyNegated.Memory))
            .IsEqualTo("-2147483648,-11,0,17,2147483647");
        await Assert.That(string.Join(",", emptyNegated.SQLite))
            .IsEqualTo(string.Join(",", emptyNegated.Memory));
        await Assert.That(nullPositive.Memory).IsEmpty();
        await Assert.That(nullPositive.SQLite).IsEmpty();
        await Assert.That(string.Join(",", nullNegated.Memory))
            .IsEqualTo(string.Join(",", emptyNegated.Memory));
        await Assert.That(string.Join(",", nullNegated.SQLite))
            .IsEqualTo(string.Join(",", nullNegated.Memory));
        await Assert.That(string.Join(",", composed.Memory)).IsEqualTo("7,7");
        await Assert.That(string.Join(",", composed.SQLite))
            .IsEqualTo(string.Join(",", composed.Memory));
    }

    [Test]
    public async Task AdmittedSingleResultIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        var matchingId = 0;
        var missingId = 99;
        var matchingGroupId = 7;
        var entity = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Single(row => row.Id == matchingId));
        var missingEntity = ExecuteScalar(
            memory,
            sqlite,
            () => rows.SingleOrDefault(row => row.Id == missingId)!);
        var scalar = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.Id == matchingId)
                .Select(static row => row.GroupId)
                .Single());
        var missingScalar = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.Id == missingId)
                .Select(static row => row.GroupId)
                .SingleOrDefault());

        await Assert.That(entity.Memory.Id).IsEqualTo(matchingId);
        await Assert.That(entity.SQLite.Id).IsEqualTo(entity.Memory.Id);
        await Assert.That(missingEntity.Memory).IsNull();
        await Assert.That(missingEntity.SQLite).IsNull();
        await Assert.That(scalar.Memory).IsEqualTo(matchingGroupId);
        await Assert.That(scalar.SQLite).IsEqualTo(scalar.Memory);
        await Assert.That(missingScalar.Memory).IsEqualTo(0);
        await Assert.That(missingScalar.SQLite).IsEqualTo(missingScalar.Memory);

        Expression<Func<MemoryPrimitiveRow>> emptySingle = () =>
            rows.Single(row => row.Id == missingId);
        var emptyInvocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            emptySingle.Body,
            typeof(MemoryPrimitiveRow));
        var expectedEmpty = Capture<InvalidOperationException>(() => Array.Empty<int>().Single());
        var memoryEmpty = Capture<InvalidOperationException>(() =>
            ExpressionQueryPlanExecutor.Execute<MemoryPrimitiveRow>(memory.ReadSource, emptyInvocation));
        var sqliteEmpty = Capture<InvalidOperationException>(() =>
            ExpressionQueryPlanExecutor.Execute<MemoryPrimitiveRow>(sqlite.Provider.ReadOnlyAccess, emptyInvocation));

        await Assert.That(memoryEmpty.Message).IsEqualTo(expectedEmpty.Message);
        await Assert.That(sqliteEmpty.Message).IsEqualTo(memoryEmpty.Message);

        Expression<Func<MemoryPrimitiveRow>> multipleSingle = () =>
            rows.SingleOrDefault(row => row.GroupId == matchingGroupId)!;
        var multipleInvocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            multipleSingle.Body,
            typeof(MemoryPrimitiveRow));
        var expectedMultiple = Capture<InvalidOperationException>(() => new[] { 1, 2 }.Single());
        var memoryMultiple = Capture<InvalidOperationException>(() =>
            ExpressionQueryPlanExecutor.Execute<MemoryPrimitiveRow>(memory.ReadSource, multipleInvocation));
        var sqliteMultiple = Capture<InvalidOperationException>(() =>
            ExpressionQueryPlanExecutor.Execute<MemoryPrimitiveRow>(sqlite.Provider.ReadOnlyAccess, multipleInvocation));

        await Assert.That(memoryMultiple.Message).IsEqualTo(expectedMultiple.Message);
        await Assert.That(sqliteMultiple.Message).IsEqualTo(memoryMultiple.Message);
        await Assert.That(memory.GetMaterializedRowCount<MemoryPrimitiveRow>()).IsEqualTo(1);
    }

    [Test]
    public async Task AdmittedOrderedFirstResultIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        var matchingGroupId = 7;
        var missingId = 99;
        var ascendingEntity = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .OrderBy(static row => row.Id)
                .First());
        var descendingEntity = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .OrderByDescending(static row => row.Id)
                .FirstOrDefault()!);
        var predicateEntity = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .OrderBy(static row => row.Id)
                .First(row => row.GroupId == matchingGroupId));
        var missingEntity = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.Id == missingId)
                .OrderBy(static row => row.Id)
                .FirstOrDefault()!);
        var ascendingScalar = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .OrderBy(static row => row.Id)
                .Select(static row => row.GroupId)
                .First());
        var descendingPredicateScalar = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.GroupId == matchingGroupId)
                .OrderByDescending(static row => row.Id)
                .Select(static row => row.Id)
                .FirstOrDefault());
        var missingScalar = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.Id == missingId)
                .OrderBy(static row => row.Id)
                .Select(static row => row.GroupId)
                .FirstOrDefault());

        await Assert.That(ascendingEntity.Memory.Id).IsEqualTo(int.MinValue);
        await Assert.That(ascendingEntity.SQLite.Id).IsEqualTo(ascendingEntity.Memory.Id);
        await Assert.That(descendingEntity.Memory.Id).IsEqualTo(int.MaxValue);
        await Assert.That(descendingEntity.SQLite.Id).IsEqualTo(descendingEntity.Memory.Id);
        await Assert.That(predicateEntity.Memory.Id).IsEqualTo(-11);
        await Assert.That(predicateEntity.SQLite.Id).IsEqualTo(predicateEntity.Memory.Id);
        await Assert.That(missingEntity.Memory).IsNull();
        await Assert.That(missingEntity.SQLite).IsNull();
        await Assert.That(ascendingScalar.Memory).IsEqualTo(3);
        await Assert.That(ascendingScalar.SQLite).IsEqualTo(ascendingScalar.Memory);
        await Assert.That(descendingPredicateScalar.Memory).IsEqualTo(int.MaxValue);
        await Assert.That(descendingPredicateScalar.SQLite)
            .IsEqualTo(descendingPredicateScalar.Memory);
        await Assert.That(missingScalar.Memory).IsEqualTo(0);
        await Assert.That(missingScalar.SQLite).IsEqualTo(missingScalar.Memory);

        Expression<Func<MemoryPrimitiveRow>> emptyFirst = () =>
            rows
                .OrderBy(static row => row.Id)
                .First(row => row.Id == missingId);
        var emptyInvocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            emptyFirst.Body,
            typeof(MemoryPrimitiveRow));
        var expectedEmpty = Capture<InvalidOperationException>(() => Array.Empty<int>().First());
        var memoryEmpty = Capture<InvalidOperationException>(() =>
            ExpressionQueryPlanExecutor.Execute<MemoryPrimitiveRow>(memory.ReadSource, emptyInvocation));
        var sqliteEmpty = Capture<InvalidOperationException>(() =>
            ExpressionQueryPlanExecutor.Execute<MemoryPrimitiveRow>(sqlite.Provider.ReadOnlyAccess, emptyInvocation));

        await Assert.That(memoryEmpty.Message).IsEqualTo(expectedEmpty.Message);
        await Assert.That(sqliteEmpty.Message).IsEqualTo(memoryEmpty.Message);
    }

    [Test]
    public async Task AdmittedOrderedSkipIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        var ascending = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(2)
                .Select(static row => row.Id));
        var descendingFiltered = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(static row => row.GroupId == 7)
                .OrderByDescending(static row => row.Id)
                .Skip(1)
                .Select(static row => row.Id));
        var zero = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(0)
                .Select(static row => row.Id));
        var exact = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(SeedRows.Length)
                .Select(static row => row.Id));
        var over = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(99)
                .Select(static row => row.Id));
        var projectedGroups = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(2)
                .Select(static row => row.GroupId));

        var count = 1;
        var snapshottedQuery = rows
            .OrderBy(static row => row.Id)
            .Skip(count)
            .Select(static row => row.Id);
        count = 3;
        var snapshotted = ExecuteSequence(memory, sqlite, snapshottedQuery);
        var rebuilt = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(count)
                .Select(static row => row.Id));

        await AssertPair(ascending, "0,17,2147483647");
        await AssertPair(descendingFiltered, "0,-11");
        await AssertPair(zero, "-2147483648,-11,0,17,2147483647");
        await Assert.That(exact.Memory).IsEmpty();
        await Assert.That(exact.SQLite).IsEmpty();
        await Assert.That(over.Memory).IsEmpty();
        await Assert.That(over.SQLite).IsEmpty();
        await AssertPair(projectedGroups, "7,3,7");
        await AssertPair(snapshotted, "-11,0,17,2147483647");
        await AssertPair(rebuilt, "17,2147483647");

        static async Task AssertPair(ProviderPair<int[]> pair, string expected)
        {
            await Assert.That(string.Join(",", pair.Memory)).IsEqualTo(expected);
            await Assert.That(string.Join(",", pair.SQLite))
                .IsEqualTo(string.Join(",", pair.Memory));
        }
    }

    [Test]
    public async Task AdmittedOrderedPageWindowIsland_MatchesSQLiteForTheSameInvocations()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        var ascending = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(1)
                .Take(2)
                .Select(static row => row.Id));
        var descendingFiltered = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(static row => row.GroupId == 7)
                .OrderByDescending(static row => row.Id)
                .Skip(1)
                .Take(1)
                .Select(static row => row.Id));
        var betweenOrderingAndPaging = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Where(static row => row.GroupId == 7)
                .Skip(1)
                .Take(2)
                .Select(static row => row.Id));
        var zeroTake = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(1)
                .Take(0)
                .Select(static row => row.Id));
        var exact = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(SeedRows.Length)
                .Take(2)
                .Select(static row => row.Id));
        var over = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(99)
                .Take(2)
                .Select(static row => row.Id));
        var projectedGroups = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(2)
                .Take(2)
                .Select(static row => row.GroupId));

        var skip = 1;
        var take = 2;
        var snapshottedQuery = rows
            .OrderBy(static row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(static row => row.Id);
        skip = 2;
        take = 1;
        var snapshotted = ExecuteSequence(memory, sqlite, snapshottedQuery);
        var rebuilt = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Skip(skip)
                .Take(take)
                .Select(static row => row.Id));

        await AssertPair(ascending, "-11,0");
        await AssertPair(descendingFiltered, "0");
        await AssertPair(betweenOrderingAndPaging, "0,2147483647");
        await Assert.That(zeroTake.Memory).IsEmpty();
        await Assert.That(zeroTake.SQLite).IsEmpty();
        await Assert.That(exact.Memory).IsEmpty();
        await Assert.That(exact.SQLite).IsEmpty();
        await Assert.That(over.Memory).IsEmpty();
        await Assert.That(over.SQLite).IsEmpty();
        await AssertPair(projectedGroups, "7,3");
        await AssertPair(snapshotted, "-11,0");
        await AssertPair(rebuilt, "0");

        static async Task AssertPair(ProviderPair<int[]> pair, string expected)
        {
            await Assert.That(string.Join(",", pair.Memory)).IsEqualTo(expected);
            await Assert.That(string.Join(",", pair.SQLite))
                .IsEqualTo(string.Join(",", pair.Memory));
        }
    }

    private static ProviderPair<ParityObservation> Observe(
        MemoryDatabase<MemoryPrimitiveDatabase> memory,
        SQLiteDatabase<MemoryPrimitiveDatabase> sqlite,
        int matchingGroupId,
        int missingGroupId,
        int matchingId)
    {
        var rows = memory.Model.Rows;
        var rootEntities = ExecuteSequence(
            memory,
            sqlite,
            rows);
        var repeatedEqualityEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == matchingGroupId)
                .Where(row => row.Id == matchingId));
        var ascendingEntities = ExecuteSequence(
            memory,
            sqlite,
            rows.OrderBy(static row => row.Id));
        var descendingEntities = ExecuteSequence(
            memory,
            sqlite,
            rows.OrderByDescending(static row => row.Id));
        var takeZeroEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Take(0));
        var orderedTakenEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == matchingGroupId)
                .OrderByDescending(static row => row.Id)
                .Take(2));
        var overCardinalityEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Take(99));
        var orderedScalarIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));
        var orderedScalarGroupIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Select(static row => row.GroupId));
        var composedScalarIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == matchingGroupId)
                .OrderByDescending(static row => row.Id)
                .Take(2)
                .Select(static row => row.Id));
        var emptyScalarIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == missingGroupId)
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));
        var rootAny = ExecuteScalar(memory, sqlite, () => rows.Any());
        var matchingAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Any(row => row.GroupId == matchingGroupId));
        var missingAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Any(row => row.GroupId == missingGroupId));
        var rootCount = ExecuteScalar(memory, sqlite, () => rows.Count());
        var matchingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Count(row => row.GroupId == matchingGroupId));
        var missingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Count(row => row.GroupId == missingGroupId));
        var projectedRootAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Select(static row => row.Id).Any());
        var projectedMissingAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.GroupId == missingGroupId)
                .Select(static row => row.Id)
                .Any());
        var projectedRootCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Select(static row => row.GroupId).Count());
        var projectedMatchingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.GroupId == matchingGroupId)
                .Select(static row => row.GroupId)
                .Count());
        var projectedMissingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.GroupId == missingGroupId)
                .Select(static row => row.GroupId)
                .Count());

        return new ProviderPair<ParityObservation>(
            CreateObservation(
                rootEntities.Memory,
                repeatedEqualityEntities.Memory,
                ascendingEntities.Memory,
                descendingEntities.Memory,
                takeZeroEntities.Memory,
                orderedTakenEntities.Memory,
                overCardinalityEntities.Memory,
                orderedScalarIds.Memory,
                orderedScalarGroupIds.Memory,
                composedScalarIds.Memory,
                emptyScalarIds.Memory,
                rootAny.Memory,
                matchingAny.Memory,
                missingAny.Memory,
                rootCount.Memory,
                matchingCount.Memory,
                missingCount.Memory,
                projectedRootAny.Memory,
                projectedMissingAny.Memory,
                projectedRootCount.Memory,
                projectedMatchingCount.Memory,
                projectedMissingCount.Memory),
            CreateObservation(
                rootEntities.SQLite,
                repeatedEqualityEntities.SQLite,
                ascendingEntities.SQLite,
                descendingEntities.SQLite,
                takeZeroEntities.SQLite,
                orderedTakenEntities.SQLite,
                overCardinalityEntities.SQLite,
                orderedScalarIds.SQLite,
                orderedScalarGroupIds.SQLite,
                composedScalarIds.SQLite,
                emptyScalarIds.SQLite,
                rootAny.SQLite,
                matchingAny.SQLite,
                missingAny.SQLite,
                rootCount.SQLite,
                matchingCount.SQLite,
                missingCount.SQLite,
                projectedRootAny.SQLite,
                projectedMissingAny.SQLite,
                projectedRootCount.SQLite,
                projectedMatchingCount.SQLite,
                projectedMissingCount.SQLite));
    }

    private static ParityObservation CreateObservation(
        MemoryPrimitiveRow[] rootEntities,
        MemoryPrimitiveRow[] repeatedEqualityEntities,
        MemoryPrimitiveRow[] ascendingEntities,
        MemoryPrimitiveRow[] descendingEntities,
        MemoryPrimitiveRow[] takeZeroEntities,
        MemoryPrimitiveRow[] orderedTakenEntities,
        MemoryPrimitiveRow[] overCardinalityEntities,
        int[] orderedScalarIds,
        int[] orderedScalarGroupIds,
        int[] composedScalarIds,
        int[] emptyScalarIds,
        bool rootAny,
        bool matchingAny,
        bool missingAny,
        int rootCount,
        int matchingCount,
        int missingCount,
        bool projectedRootAny,
        bool projectedMissingAny,
        int projectedRootCount,
        int projectedMatchingCount,
        int projectedMissingCount) =>
        new(
            RootEntities: SnapshotUnordered(rootEntities),
            RepeatedEqualityEntities: SnapshotUnordered(repeatedEqualityEntities),
            AscendingEntityIds: SnapshotIds(ascendingEntities),
            DescendingEntityIds: SnapshotIds(descendingEntities),
            TakeZeroEntityIds: SnapshotIds(takeZeroEntities),
            OrderedTakenEntityIds: SnapshotIds(orderedTakenEntities),
            OverCardinalityEntityIds: SnapshotIds(overCardinalityEntities),
            OrderedScalarIds: string.Join(",", orderedScalarIds),
            OrderedScalarGroupIds: string.Join(",", orderedScalarGroupIds),
            ComposedScalarIds: string.Join(",", composedScalarIds),
            EmptyScalarIds: string.Join(",", emptyScalarIds),
            RootAny: rootAny,
            MatchingAny: matchingAny,
            MissingAny: missingAny,
            RootCount: rootCount,
            MatchingCount: matchingCount,
            MissingCount: missingCount,
            ProjectedRootAny: projectedRootAny,
            ProjectedMissingAny: projectedMissingAny,
            ProjectedRootCount: projectedRootCount,
            ProjectedMatchingCount: projectedMatchingCount,
            ProjectedMissingCount: projectedMissingCount);

    private static string SnapshotUnordered(MemoryPrimitiveRow[] rows) =>
        string.Join(
            "|",
            rows
                .OrderBy(static row => row.Id)
                .Select(static row => $"{row.Id}:{row.GroupId}:{row.Name}"));

    private static string SnapshotIds(MemoryPrimitiveRow[] rows) =>
        string.Join(",", rows.Select(static row => row.Id));

    private static CanonicalGuidParityObservation CreateCanonicalGuidObservation(
        MemoryConvertedRow[] directHit,
        MemoryConvertedRow[] directMiss,
        MemoryConvertedRow[] typedRelatedHit,
        MemoryConvertedRow[] typedRelatedMiss,
        MemoryConvertedRow[] typedPrimaryKeyHit,
        MemoryConvertedRow[] mixedEquality,
        bool directHitAny,
        int typedRelatedHitCount) =>
        new(
            DirectHitIds: SnapshotConvertedIds(directHit),
            DirectMissIds: SnapshotConvertedIds(directMiss),
            TypedRelatedHitIds: SnapshotConvertedIds(typedRelatedHit),
            TypedRelatedMissIds: SnapshotConvertedIds(typedRelatedMiss),
            TypedPrimaryKeyHitIds: SnapshotConvertedIds(typedPrimaryKeyHit),
            MixedEqualityIds: SnapshotConvertedIds(mixedEquality),
            DirectHitAny: directHitAny,
            TypedRelatedHitCount: typedRelatedHitCount);

    private static string SnapshotConvertedIds(MemoryConvertedRow[] rows) =>
        string.Join(",", rows
            .Select(static row => row.Id.Value.ToString("D"))
            .OrderBy(static value => value, StringComparer.Ordinal));

    private static ProviderPair<T[]> ExecuteSequence<TDatabase, T>(
        MemoryDatabase<TDatabase> memory,
        SQLiteDatabase<TDatabase> sqlite,
        IQueryable<T> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var invocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            query.Expression,
            typeof(T));

        return new ProviderPair<T[]>(
            ExpressionQueryPlanExecutor
                .ExecuteEnumerable<T>(memory.ReadSource, invocation)
                .ToArray(),
            ExpressionQueryPlanExecutor
                .ExecuteEnumerable<T>(sqlite.Provider.ReadOnlyAccess, invocation)
                .ToArray());
    }

    private static ProviderPair<T> ExecuteScalar<TDatabase, T>(
        MemoryDatabase<TDatabase> memory,
        SQLiteDatabase<TDatabase> sqlite,
        Expression<Func<T>> query)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var invocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            query.Body,
            typeof(T));

        return new ProviderPair<T>(
            ExpressionQueryPlanExecutor.Execute<T>(memory.ReadSource, invocation),
            ExpressionQueryPlanExecutor.Execute<T>(sqlite.Provider.ReadOnlyAccess, invocation));
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateMemoryDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var rows = SeedRows
            .Select(row => CreateCanonicalRow(database, row))
            .ToArray();
        return database.SeedCanonical<MemoryPrimitiveRow>(rows);
    }

    private static MemoryDatabase<MemoryConvertedDatabase> CreateConvertedMemoryDatabase()
    {
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        return database.Seed<MemoryConvertedRow>(
            ConvertedSeedRows.Select(static row => new MutableMemoryConvertedRow
            {
                Id = row.Id,
                DirectGuid = row.DirectGuid,
                RelatedId = row.RelatedId,
                OptionalRelatedId = null
            }));
    }

    private static object?[] CreateCanonicalRow(
        MemoryDatabase<MemoryPrimitiveDatabase> database,
        ParitySeedRow row)
    {
        var table = database.Metadata.GetTableModel(typeof(MemoryPrimitiveRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = row.Id;
        values[table.GetColumnByDbName("group_id").Index] = row.GroupId;
        values[table.GetColumnByDbName("name").Index] = row.Name;
        return values;
    }

    private static async Task SeedSQLite(
        SQLiteDatabase<MemoryPrimitiveDatabase> database)
    {
        foreach (var row in SeedRows)
        {
            var escapedName = row.Name.Replace("'", "''", StringComparison.Ordinal);
            var affected = database.Provider.DatabaseAccess.ExecuteNonQuery(
                $"INSERT INTO memory_primitive_rows (id, group_id, name) " +
                $"VALUES ({row.Id}, {row.GroupId}, '{escapedName}')");

            await Assert.That(affected).IsEqualTo(1);
        }
    }

    private static async Task SeedConvertedSQLite(
        SQLiteDatabase<MemoryConvertedDatabase> database)
    {
        foreach (var row in ConvertedSeedRows)
        {
            var affected = database.Provider.DatabaseAccess.ExecuteNonQuery(
                "INSERT INTO memory_converted_rows " +
                "(id, direct_guid, related_id, optional_related_id) VALUES (" +
                $"{GuidBlobLiteral(row.Id.Value, bigEndian: false)}, " +
                $"'{row.DirectGuid:D}', " +
                $"{GuidBlobLiteral(row.RelatedId.Value, bigEndian: true)}, NULL)");

            await Assert.That(affected).IsEqualTo(1);
        }

        database.Provider.State.ClearCache();
    }

    private static string GuidBlobLiteral(Guid value, bool bigEndian) =>
        $"X'{Convert.ToHexString(value.ToByteArray(bigEndian))}'";

    private static async Task InitializeSQLite(
        SQLiteDatabase<MemoryPrimitiveDatabase> database)
    {
        await CreateSQLiteSchema(database);
        await SeedSQLite(database);
    }

    private static async Task InitializeConvertedSQLite(
        SQLiteDatabase<MemoryConvertedDatabase> database)
    {
        await CreateSQLiteSchema(database);
        await SeedConvertedSQLite(database);
    }

    private static async Task CreateSQLiteSchema<TDatabase>(
        SQLiteDatabase<TDatabase> database)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var creation = DatabaseType.SQLite.CreateDatabaseFromMetadata(
            database.Provider.Metadata,
            database.Provider.DatabaseName,
            database.Provider.ConnectionString,
            foreignKeyRestrict: true);

        await Assert.That(creation.HasFailed).IsFalse();
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

    private sealed record ParitySeedRow(int Id, int GroupId, string Name);

    private sealed record ConvertedParitySeedRow(
        MemoryGuidId Id,
        Guid DirectGuid,
        MemoryGuidId RelatedId);

    private sealed record ProviderPair<T>(T Memory, T SQLite);

    private sealed record CanonicalGuidParityObservation(
        string DirectHitIds,
        string DirectMissIds,
        string TypedRelatedHitIds,
        string TypedRelatedMissIds,
        string TypedPrimaryKeyHitIds,
        string MixedEqualityIds,
        bool DirectHitAny,
        int TypedRelatedHitCount);

    private sealed record ParityObservation(
        string RootEntities,
        string RepeatedEqualityEntities,
        string AscendingEntityIds,
        string DescendingEntityIds,
        string TakeZeroEntityIds,
        string OrderedTakenEntityIds,
        string OverCardinalityEntityIds,
        string OrderedScalarIds,
        string OrderedScalarGroupIds,
        string ComposedScalarIds,
        string EmptyScalarIds,
        bool RootAny,
        bool MatchingAny,
        bool MissingAny,
        int RootCount,
        int MatchingCount,
        int MissingCount,
        bool ProjectedRootAny,
        bool ProjectedMissingAny,
        int ProjectedRootCount,
        int ProjectedMatchingCount,
        int ProjectedMissingCount);
}
