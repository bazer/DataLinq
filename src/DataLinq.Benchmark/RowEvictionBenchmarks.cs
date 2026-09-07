using BenchmarkDotNet.Attributes;
using DataLinq.Cache;

namespace DataLinq.Benchmark;

[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class RowEvictionBenchmarks
{
    private RowStore<int> store = null!;
    private string executedMethod = "";

    [Params("memory")]
    public string ProviderName { get; set; } = "memory";

    private int rows;

    [IterationSetup(Targets = [nameof(RowLimit5000), nameof(PayloadLimit5000), nameof(SingleRowBatches5000)])]
    public void Setup5000() => FillCache(5_000);

    [IterationSetup(Targets = [nameof(RowLimit10000), nameof(PayloadLimit10000), nameof(SingleRowBatches10000)])]
    public void Setup10000() => FillCache(10_000);

    [IterationSetup(Targets = [nameof(RowLimit20000), nameof(PayloadLimit20000), nameof(SingleRowBatches20000)])]
    public void Setup20000() => FillCache(20_000);

    private void FillCache(int rowCount)
    {
        rows = rowCount;
        store = new RowStore<int>();
        for (var key = 0; key < rows; key++)
            // These workloads only inspect keys and sizes; entity materialization is intentionally excluded.
            store.TryAdd(key, 128, 0, null!);
    }

    [Benchmark]
    public int RowLimit5000() => RowLimit(nameof(RowLimit5000));
    [Benchmark]
    public int RowLimit10000() => RowLimit(nameof(RowLimit10000));
    [Benchmark]
    public int RowLimit20000() => RowLimit(nameof(RowLimit20000));
    [Benchmark]
    public int PayloadLimit5000() => PayloadLimit(nameof(PayloadLimit5000));
    [Benchmark]
    public int PayloadLimit10000() => PayloadLimit(nameof(PayloadLimit10000));
    [Benchmark]
    public int PayloadLimit20000() => PayloadLimit(nameof(PayloadLimit20000));
    [Benchmark]
    public int SingleRowBatches5000() => SingleRowBatches(nameof(SingleRowBatches5000));
    [Benchmark]
    public int SingleRowBatches10000() => SingleRowBatches(nameof(SingleRowBatches10000));
    [Benchmark]
    public int SingleRowBatches20000() => SingleRowBatches(nameof(SingleRowBatches20000));

    private int RowLimit(string method)
    {
        executedMethod = method;
        return store.RemoveRowsOverRowLimit(rows / 2);
    }

    private int PayloadLimit(string method)
    {
        executedMethod = method;
        return store.RemoveRowsOverSizeLimit(rows / 2 * 128L);
    }

    private int SingleRowBatches(string method)
    {
        executedMethod = method;
        var removed = 0;
        for (var i = 0; i < rows / 2; i++)
            removed += store.RemoveOldestRows(1).Count;
        return removed;
    }

    [GlobalCleanup]
    public void WriteTelemetry()
    {
        // RowStore is below query/provider instrumentation: these isolated operations emit no
        // application telemetry. The measured operation is one eviction of half the populated cache.
        BenchmarkTelemetryDeltaWriter.TryWrite(new BenchmarkTelemetryDeltaArtifact(
            executedMethod, ProviderName, OperationsPerInvoke: 1,
            EntityQueriesPerOperation: 0, ScalarQueriesPerOperation: 0,
            TransactionStartsPerOperation: 0, TransactionCommitsPerOperation: 0, TransactionRollbacksPerOperation: 0,
            MutationInsertsPerOperation: 0, MutationUpdatesPerOperation: 0, MutationDeletesPerOperation: 0, MutationAffectedRowsPerOperation: 0,
            RowCacheHitsPerOperation: 0, RowCacheMissesPerOperation: 0, RowCacheStoresPerOperation: 0,
            DatabaseRowsPerOperation: 0, MaterializationsPerOperation: 0, RelationHitsPerOperation: 0, RelationLoadsPerOperation: 0,
            CacheInvalidationOperationsPerOperation: 0, CacheInvalidationRowsRemovedPerOperation: 0, CacheInvalidationTablesClearedPerOperation: 0,
            CacheInvalidationProviderKeysPerOperation: 0, CacheInvalidationApproximateWorkPerOperation: 0,
            CacheInvalidationPreciseOperationsPerOperation: 0, CacheInvalidationConservativeFallbackOperationsPerOperation: 0));
    }
}
