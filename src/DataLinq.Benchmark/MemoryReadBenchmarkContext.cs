using System;
using System.Linq;
using DataLinq.Memory;

namespace DataLinq.Benchmark;

internal enum MemoryBenchmarkScenario
{
    DatabaseConstruction,
    ConstructAndSeed,
    PrimaryKeyHit,
    PrimaryKeyMiss,
    ScalarScan,
    FilterOrderPage,
    RepeatedEntityIdentity,
    DirectGuidEqualityCount,
    TypedIdEqualityCount
}

internal sealed class MemoryReadBenchmarkContext
{
    private const int PrimitiveRowCount = 1024;
    private const int GuidRowCount = 256;
    private const int PageGroupId = 7;
    private const int PageSkip = 8;
    private const int PageTake = 16;
    private const int PrimaryKeyHitId = PrimitiveRowCount / 2;
    private const int PrimaryKeyMissId = -1;

    private readonly MutableMemoryBenchmarkRow[] primitiveSeedRows;
    private readonly MutableMemoryBenchmarkGuidRow[] guidSeedRows;
    private readonly MemoryDatabase<MemoryBenchmarkDatabase> database;
    private readonly IQueryable<int> scalarScanQuery;
    private readonly IQueryable<int> filterOrderPageQuery;
    private readonly IQueryable<MemoryBenchmarkRow> repeatedIdentityQuery;
    private readonly IQueryable<MemoryBenchmarkGuidRow> directGuidEqualityQuery;
    private readonly IQueryable<MemoryBenchmarkGuidRow> typedIdEqualityQuery;
    private readonly MemoryBenchmarkRow identityAnchor;

    public MemoryReadBenchmarkContext()
    {
        // Bind and cache generated metadata before constructing generated mutable seed rows. This
        // leaves the construction benchmark on the public warm-metadata path.
        _ = new MemoryDatabase<MemoryBenchmarkDatabase>();

        primitiveSeedRows = CreatePrimitiveSeedRows();
        guidSeedRows = CreateGuidSeedRows();
        database = ConstructAndSeed();

        var query = database.Query();
        scalarScanQuery = query.Rows.Select(static row => row.Id);
        filterOrderPageQuery = query.Rows
            .Where(static row => row.GroupId == PageGroupId)
            .OrderBy(static row => row.Id)
            .Skip(PageSkip)
            .Take(PageTake)
            .Select(static row => row.Id);

        identityAnchor = database.Find<MemoryBenchmarkRow>(PrimaryKeyHitId)
            ?? throw new InvalidOperationException(
                $"Memory benchmark seed did not contain primary key {PrimaryKeyHitId}.");
        var identityId = PrimaryKeyHitId;
        repeatedIdentityQuery = query.Rows.Where(row => row.Id == identityId);

        var guidProbeRow = guidSeedRows[GuidRowCount / 2];
        var directGuidProbe = guidProbeRow.DirectGuid;
        var typedIdProbe = guidProbeRow.Id;
        directGuidEqualityQuery = query.GuidRows.Where(row => row.DirectGuid == directGuidProbe);
        typedIdEqualityQuery = query.GuidRows.Where(row => row.Id == typedIdProbe);

        ValidateSetup(query);
    }

    public MemoryDatabase<MemoryBenchmarkDatabase> ConstructDatabase() =>
        new();

    public MemoryDatabase<MemoryBenchmarkDatabase> ConstructAndSeed() =>
        new MemoryDatabase<MemoryBenchmarkDatabase>()
            .Seed<MemoryBenchmarkRow>(primitiveSeedRows)
            .Seed<MemoryBenchmarkGuidRow>(guidSeedRows);

    public MemoryBenchmarkRow PrimaryKeyHit() =>
        database.Find<MemoryBenchmarkRow>(PrimaryKeyHitId)
        ?? throw new InvalidOperationException(
            $"Memory benchmark primary-key hit {PrimaryKeyHitId} unexpectedly missed.");

    public MemoryBenchmarkRow? PrimaryKeyMiss() =>
        database.Find<MemoryBenchmarkRow>(PrimaryKeyMissId);

    public int ScalarScan()
    {
        var checksum = 0;
        foreach (var id in scalarScanQuery)
            checksum = unchecked(checksum + id);

        return checksum;
    }

    public int FilterOrderPage()
    {
        var checksum = 0;
        foreach (var id in filterOrderPageQuery)
            checksum = unchecked(checksum + id);

        return checksum;
    }

    public bool RepeatedEntityIdentity() =>
        ReferenceEquals(identityAnchor, repeatedIdentityQuery.Single());

    public int DirectGuidEqualityCount() =>
        directGuidEqualityQuery.Count();

    public int TypedIdEqualityCount() =>
        typedIdEqualityQuery.Count();

    public BenchmarkTelemetryDeltaArtifact CaptureTelemetryDelta(
        MemoryBenchmarkScenario scenario,
        string providerName)
    {
        var before = database.Diagnostics;
        var databasesConstructed = 0d;
        var rowsSeeded = 0d;

        switch (scenario)
        {
            case MemoryBenchmarkScenario.DatabaseConstruction:
                _ = ConstructDatabase();
                databasesConstructed = 1d;
                break;
            case MemoryBenchmarkScenario.ConstructAndSeed:
                _ = ConstructAndSeed();
                databasesConstructed = 1d;
                rowsSeeded = PrimitiveRowCount + GuidRowCount;
                break;
            case MemoryBenchmarkScenario.PrimaryKeyHit:
                _ = PrimaryKeyHit();
                break;
            case MemoryBenchmarkScenario.PrimaryKeyMiss:
                _ = PrimaryKeyMiss();
                break;
            case MemoryBenchmarkScenario.ScalarScan:
                _ = ScalarScan();
                break;
            case MemoryBenchmarkScenario.FilterOrderPage:
                _ = FilterOrderPage();
                break;
            case MemoryBenchmarkScenario.RepeatedEntityIdentity:
                _ = RepeatedEntityIdentity();
                break;
            case MemoryBenchmarkScenario.DirectGuidEqualityCount:
                _ = DirectGuidEqualityCount();
                break;
            case MemoryBenchmarkScenario.TypedIdEqualityCount:
                _ = TypedIdEqualityCount();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }

        var after = database.Diagnostics;
        return new BenchmarkTelemetryDeltaArtifact(
            Method: GetDescription(scenario),
            ProviderName: providerName,
            OperationsPerInvoke: 1,
            EntityQueriesPerOperation: 0d,
            ScalarQueriesPerOperation: 0d,
            TransactionStartsPerOperation: 0d,
            TransactionCommitsPerOperation: 0d,
            TransactionRollbacksPerOperation: 0d,
            MutationInsertsPerOperation: 0d,
            MutationUpdatesPerOperation: 0d,
            MutationDeletesPerOperation: 0d,
            MutationAffectedRowsPerOperation: 0d,
            RowCacheHitsPerOperation: 0d,
            RowCacheMissesPerOperation: 0d,
            RowCacheStoresPerOperation: 0d,
            DatabaseRowsPerOperation: 0d,
            MaterializationsPerOperation: 0d,
            RelationHitsPerOperation: 0d,
            RelationLoadsPerOperation: 0d,
            CacheInvalidationOperationsPerOperation: 0d,
            CacheInvalidationRowsRemovedPerOperation: 0d,
            CacheInvalidationTablesClearedPerOperation: 0d,
            CacheInvalidationProviderKeysPerOperation: 0d,
            CacheInvalidationApproximateWorkPerOperation: 0d,
            CacheInvalidationPreciseOperationsPerOperation: 0d,
            CacheInvalidationConservativeFallbackOperationsPerOperation: 0d,
            MemoryDatabasesConstructedPerOperation: databasesConstructed,
            MemoryRowsSeededPerOperation: rowsSeeded,
            MemoryPrimaryKeyRequestsPerOperation:
                after.PrimaryKeyRequests - before.PrimaryKeyRequests,
            MemoryPrimaryKeyProbesPerOperation:
                after.PrimaryKeyProbes - before.PrimaryKeyProbes,
            MemoryScanRowsVisitedPerOperation:
                after.ScanRowsVisited - before.ScanRowsVisited,
            MemoryPredicateEvaluationsPerOperation:
                after.PredicateEvaluations - before.PredicateEvaluations,
            MemoryPredicateRejectionsPerOperation:
                after.PredicateRejections - before.PredicateRejections,
            MemoryCacheLookupsPerOperation:
                after.CacheLookups - before.CacheLookups,
            MemoryCacheHitsPerOperation:
                after.CacheHits - before.CacheHits,
            MemoryCacheMissesPerOperation:
                after.CacheMisses - before.CacheMisses,
            MemoryMaterializationsPerOperation:
                after.Materializations - before.Materializations,
            MemoryCacheInsertionsPerOperation:
                after.CacheInsertions - before.CacheInsertions);
    }

    private static string GetDescription(MemoryBenchmarkScenario scenario) =>
        scenario switch
        {
            MemoryBenchmarkScenario.DatabaseConstruction => "Memory database construction",
            MemoryBenchmarkScenario.ConstructAndSeed => "Memory construct and seed",
            MemoryBenchmarkScenario.PrimaryKeyHit => "Memory primary-key hit",
            MemoryBenchmarkScenario.PrimaryKeyMiss => "Memory primary-key miss",
            MemoryBenchmarkScenario.ScalarScan => "Memory scalar scan",
            MemoryBenchmarkScenario.FilterOrderPage => "Memory filter order page",
            MemoryBenchmarkScenario.RepeatedEntityIdentity => "Memory repeated entity identity",
            MemoryBenchmarkScenario.DirectGuidEqualityCount => "Memory direct-Guid equality count",
            MemoryBenchmarkScenario.TypedIdEqualityCount => "Memory typed-ID equality count",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

    private void ValidateSetup(MemoryBenchmarkDatabase query)
    {
        if (query.Rows.Count() != PrimitiveRowCount)
        {
            throw new InvalidOperationException(
                $"Memory benchmark expected {PrimitiveRowCount} primitive rows.");
        }

        if (query.GuidRows.Count() != GuidRowCount)
        {
            throw new InvalidOperationException(
                $"Memory benchmark expected {GuidRowCount} Guid rows.");
        }

        if (PrimaryKeyHit().Id != PrimaryKeyHitId || PrimaryKeyMiss() is not null)
            throw new InvalidOperationException("Memory benchmark primary-key validation failed.");

        var expectedScalarChecksum = PrimitiveRowCount * (PrimitiveRowCount + 1) / 2;
        if (ScalarScan() != expectedScalarChecksum)
            throw new InvalidOperationException("Memory benchmark scalar-scan validation failed.");

        var expectedPageIds = Enumerable
            .Range(1, PrimitiveRowCount)
            .Where(static id => id % 16 == PageGroupId)
            .Skip(PageSkip)
            .Take(PageTake)
            .ToArray();
        var actualPageIds = filterOrderPageQuery.ToArray();
        if (!actualPageIds.SequenceEqual(expectedPageIds) ||
            FilterOrderPage() != expectedPageIds.Sum())
        {
            throw new InvalidOperationException("Memory benchmark filter/order/page validation failed.");
        }

        if (!RepeatedEntityIdentity())
            throw new InvalidOperationException("Memory benchmark repeated-identity validation failed.");

        if (DirectGuidEqualityCount() != 1 || TypedIdEqualityCount() != 1)
            throw new InvalidOperationException("Memory benchmark Guid equality validation failed.");
    }

    private static MutableMemoryBenchmarkRow[] CreatePrimitiveSeedRows()
    {
        var rows = new MutableMemoryBenchmarkRow[PrimitiveRowCount];
        for (var index = 0; index < rows.Length; index++)
        {
            var id = PrimitiveRowCount - index;
            rows[index] = new MutableMemoryBenchmarkRow
            {
                Id = id,
                GroupId = id % 16,
                Name = $"row-{id:0000}"
            };
        }

        return rows;
    }

    private static MutableMemoryBenchmarkGuidRow[] CreateGuidSeedRows()
    {
        var rows = new MutableMemoryBenchmarkGuidRow[GuidRowCount];
        for (var index = 0; index < rows.Length; index++)
        {
            var ordinal = index + 1;
            rows[index] = new MutableMemoryBenchmarkGuidRow
            {
                Id = new MemoryBenchmarkGuidId(CreateDeterministicGuid(ordinal, 0x11)),
                DirectGuid = CreateDeterministicGuid(ordinal, 0x22),
                Name = $"guid-row-{ordinal:000}"
            };
        }

        return rows;
    }

    private static Guid CreateDeterministicGuid(int ordinal, byte marker) =>
        new(
            ordinal,
            unchecked((short)0x4d42),
            unchecked((short)0x9a71),
            marker,
            0x00,
            0x00,
            0x00,
            (byte)(ordinal >> 24),
            (byte)(ordinal >> 16),
            (byte)(ordinal >> 8),
            (byte)ordinal);
}
