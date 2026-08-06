using BenchmarkDotNet.Attributes;
using DataLinq.Memory;

namespace DataLinq.Benchmark;

[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class MemoryReadBenchmarks
{
    private const string V09MemoryReadCategory = "v0.9-memory-read";
    private const int OperationCount = 1;
    private MemoryReadBenchmarkContext? context;
    private MemoryBenchmarkScenario? executedScenario;

    [Params("memory")]
    public string ProviderName { get; set; } = "memory";

    [GlobalSetup]
    public void GlobalSetup()
    {
        context = new MemoryReadBenchmarkContext();
        executedScenario = null;
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (context is not null && executedScenario.HasValue)
        {
            var delta = context.CaptureTelemetryDelta(executedScenario.Value, ProviderName);
            BenchmarkTelemetryDeltaWriter.TryWrite(delta);
        }

        context = null;
        executedScenario = null;
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory database construction")]
    public MemoryDatabase<MemoryBenchmarkDatabase> DatabaseConstruction()
    {
        executedScenario = MemoryBenchmarkScenario.DatabaseConstruction;
        return context!.ConstructDatabase();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory construct and seed")]
    public MemoryDatabase<MemoryBenchmarkDatabase> ConstructAndSeed()
    {
        executedScenario = MemoryBenchmarkScenario.ConstructAndSeed;
        return context!.ConstructAndSeed();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory primary-key hit")]
    public MemoryBenchmarkRow PrimaryKeyHit()
    {
        executedScenario = MemoryBenchmarkScenario.PrimaryKeyHit;
        return context!.PrimaryKeyHit();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory primary-key miss")]
    public MemoryBenchmarkRow? PrimaryKeyMiss()
    {
        executedScenario = MemoryBenchmarkScenario.PrimaryKeyMiss;
        return context!.PrimaryKeyMiss();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory scalar scan")]
    public int ScalarScan()
    {
        executedScenario = MemoryBenchmarkScenario.ScalarScan;
        return context!.ScalarScan();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory filter order page")]
    public int FilterOrderPage()
    {
        executedScenario = MemoryBenchmarkScenario.FilterOrderPage;
        return context!.FilterOrderPage();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory repeated entity identity")]
    public bool RepeatedEntityIdentity()
    {
        executedScenario = MemoryBenchmarkScenario.RepeatedEntityIdentity;
        return context!.RepeatedEntityIdentity();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory direct-Guid equality count")]
    public int DirectGuidEqualityCount()
    {
        executedScenario = MemoryBenchmarkScenario.DirectGuidEqualityCount;
        return context!.DirectGuidEqualityCount();
    }

    [BenchmarkCategory(V09MemoryReadCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Memory typed-ID equality count")]
    public int TypedIdEqualityCount()
    {
        executedScenario = MemoryBenchmarkScenario.TypedIdEqualityCount;
        return context!.TypedIdEqualityCount();
    }
}
