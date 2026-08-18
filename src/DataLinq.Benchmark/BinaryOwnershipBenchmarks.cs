using BenchmarkDotNet.Attributes;

namespace DataLinq.Benchmark;

/// <summary>
/// Measures each binary ownership boundary independently. Configure server-backed providers with
/// DATALINQ_BINARY_BENCHMARK_PROVIDERS (for example: memory,sqlite-memory,mysql-8.4).
/// </summary>
[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class BinaryOwnershipBenchmarks : IDisposable
{
    private BinaryOwnershipBenchmarkContext? context;

    [ParamsSource(nameof(GetProviderNames))]
    public string ProviderName { get; set; } = BinaryOwnershipBenchmarkContext.MemoryProvider;

    [Params(32, 4096, 65536)]
    public int PayloadSize { get; set; }

    public static IEnumerable<string> GetProviderNames() =>
        BinaryOwnershipBenchmarkContext.GetConfiguredProviderNames();

    [GlobalSetup]
    public void GlobalSetup() =>
        context = new BinaryOwnershipBenchmarkContext(ProviderName, PayloadSize);

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        context?.Dispose();
        context = null;
    }

    [BenchmarkCategory("binary-ownership")]
    [Benchmark(Description = "Binary provider read")]
    public byte[] ProviderRead() => context!.ReadProviderBuffer();

    [BenchmarkCategory("binary-ownership")]
    [Benchmark(Description = "Binary canonical decode")]
    public int CanonicalDecode() => context!.DecodeCanonicalRow();

    [BenchmarkCategory("binary-ownership")]
    [Benchmark(Description = "Binary model materialization")]
    public int ModelMaterialization() => context!.MaterializeModelRow();

    [BenchmarkCategory("binary-ownership")]
    [Benchmark(Description = "Binary cache publication")]
    public int CachePublication() => context!.PublishCachedRow();

    [BenchmarkCategory("binary-ownership")]
    [Benchmark(Description = "Binary public detached access")]
    public int PublicDetachedAccess() => context!.ReadDetachedModelValue();

    public void Dispose() => GlobalCleanup();
}
