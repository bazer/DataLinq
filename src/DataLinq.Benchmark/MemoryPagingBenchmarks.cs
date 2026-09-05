using System;
using System.Linq;
using BenchmarkDotNet.Attributes;
using DataLinq.Memory;

namespace DataLinq.Benchmark;

/// <summary>Diagnostic scaling workloads; deliberately separate from release evidence lanes.</summary>
[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
[BenchmarkCategory("memory-paging-diagnostic")]
public class MemoryPagingBenchmarks
{
    private MemoryDatabase<MemoryBenchmarkDatabase> database1000 = null!;
    private MemoryDatabase<MemoryBenchmarkDatabase> database10000 = null!;
    private MemoryDatabase<MemoryBenchmarkDatabase> database100000 = null!;
    private IQueryable<int> take1000 = null!;
    private IQueryable<int> page1000 = null!;
    private IQueryable<int> take10000 = null!;
    private IQueryable<int> page10000 = null!;
    private IQueryable<int> take100000 = null!;
    private IQueryable<int> page100000 = null!;

    [Params("memory")]
    public string ProviderName { get; set; } = "memory";

    [GlobalSetup]
    public void Setup()
    {
        (database1000, take1000, page1000) = CreateQueries(1000);
        (database10000, take10000, page10000) = CreateQueries(10000);
        (database100000, take100000, page100000) = CreateQueries(100000);
    }

    private static (MemoryDatabase<MemoryBenchmarkDatabase> Database, IQueryable<int> Take, IQueryable<int> Page) CreateQueries(int count)
    {
        var database = new MemoryDatabase<MemoryBenchmarkDatabase>();
        database.Seed<MemoryBenchmarkRow>(Enumerable.Range(1, count).Reverse()
            .Select(id => new MutableMemoryBenchmarkRow { Id = id, GroupId = id % 16, Name = "row" }));
        var ordered = database.Query().Rows.OrderBy(row => row.Id);
        var take = ordered.Take(5).Select(row => row.Id);
        var page = ordered.Skip(100).Take(5).Select(row => row.Id);
        if (!take.ToArray().SequenceEqual(Enumerable.Range(1, 5)) || !page.ToArray().SequenceEqual(Enumerable.Range(101, 5)))
            throw new InvalidOperationException("Memory page benchmark returned an unexpected result.");
        return (database, take, page);
    }

    [Benchmark] public int[] Take5From1000() => take1000.ToArray();
    [Benchmark] public int[] Skip100Take5From1000() => page1000.ToArray();
    [Benchmark] public int[] Take5From10000() => take10000.ToArray();
    [Benchmark] public int[] Skip100Take5From10000() => page10000.ToArray();
    [Benchmark] public int[] Take5From100000() => take100000.ToArray();
    [Benchmark] public int[] Skip100Take5From100000() => page100000.ToArray();

    [GlobalCleanup(Target = nameof(Take5From1000))] public void Take1000Telemetry() => CaptureTelemetry(nameof(Take5From1000), database1000, take1000);
    [GlobalCleanup(Target = nameof(Skip100Take5From1000))] public void Page1000Telemetry() => CaptureTelemetry(nameof(Skip100Take5From1000), database1000, page1000);
    [GlobalCleanup(Target = nameof(Take5From10000))] public void Take10000Telemetry() => CaptureTelemetry(nameof(Take5From10000), database10000, take10000);
    [GlobalCleanup(Target = nameof(Skip100Take5From10000))] public void Page10000Telemetry() => CaptureTelemetry(nameof(Skip100Take5From10000), database10000, page10000);
    [GlobalCleanup(Target = nameof(Take5From100000))] public void Take100000Telemetry() => CaptureTelemetry(nameof(Take5From100000), database100000, take100000);
    [GlobalCleanup(Target = nameof(Skip100Take5From100000))] public void Page100000Telemetry() => CaptureTelemetry(nameof(Skip100Take5From100000), database100000, page100000);

    private void CaptureTelemetry(string method, MemoryDatabase<MemoryBenchmarkDatabase> database, IQueryable<int> query)
    {
        var before = database.Diagnostics;
        _ = query.ToArray();
        var after = database.Diagnostics;
        BenchmarkTelemetryDeltaWriter.TryWrite(new BenchmarkTelemetryDeltaArtifact(
            Method: method, ProviderName: ProviderName, OperationsPerInvoke: 1,
            EntityQueriesPerOperation: 0d, ScalarQueriesPerOperation: 0d,
            TransactionStartsPerOperation: 0d, TransactionCommitsPerOperation: 0d, TransactionRollbacksPerOperation: 0d,
            MutationInsertsPerOperation: 0d, MutationUpdatesPerOperation: 0d, MutationDeletesPerOperation: 0d, MutationAffectedRowsPerOperation: 0d,
            RowCacheHitsPerOperation: 0d, RowCacheMissesPerOperation: 0d, RowCacheStoresPerOperation: 0d,
            DatabaseRowsPerOperation: 0d, MaterializationsPerOperation: 0d, RelationHitsPerOperation: 0d, RelationLoadsPerOperation: 0d,
            CacheInvalidationOperationsPerOperation: 0d, CacheInvalidationRowsRemovedPerOperation: 0d, CacheInvalidationTablesClearedPerOperation: 0d,
            CacheInvalidationProviderKeysPerOperation: 0d, CacheInvalidationApproximateWorkPerOperation: 0d,
            CacheInvalidationPreciseOperationsPerOperation: 0d, CacheInvalidationConservativeFallbackOperationsPerOperation: 0d,
            MemoryScanRowsVisitedPerOperation: after.ScanRowsVisited - before.ScanRowsVisited,
            MemoryPredicateEvaluationsPerOperation: after.PredicateEvaluations - before.PredicateEvaluations,
            MemoryPredicateRejectionsPerOperation: after.PredicateRejections - before.PredicateRejections,
            MemoryCacheLookupsPerOperation: after.CacheLookups - before.CacheLookups,
            MemoryCacheHitsPerOperation: after.CacheHits - before.CacheHits,
            MemoryCacheMissesPerOperation: after.CacheMisses - before.CacheMisses,
            MemoryMaterializationsPerOperation: after.Materializations - before.Materializations,
            MemoryCacheInsertionsPerOperation: after.CacheInsertions - before.CacheInsertions));
    }
}
