using System;
using BenchmarkDotNet.Attributes;
using DataLinq.Testing;

namespace DataLinq.Benchmark;

[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class Phase12StringDedupBenchmarks
{
    private const string Phase12CacheMemoryCategory = "phase12-cache-memory";
    private const int OperationCount = 4096;
    private const int LowCardinalityDistinctValues = 32;
    private const int MaxPoolEntries = 256;
    private const int MaxStringLength = 64;

    private string[] lowCardinalitySourceValues = [];
    private string[] highCardinalitySourceValues = [];
    private string?[] retainedValues = [];
    private BenchmarkStringPool? stringPool;

    [ParamsSource(nameof(GetProviderNames))]
    public string ProviderName { get; set; } = TestProviderMatrix.SQLiteInMemory.Name;

    public static IEnumerable<string> GetProviderNames() => EmployeesBenchmarks.GetProviderNames();

    [GlobalSetup]
    public void GlobalSetup()
    {
        lowCardinalitySourceValues = CreateSourceValues(LowCardinalityDistinctValues, "status");
        highCardinalitySourceValues = CreateSourceValues(OperationCount, "customer");
        retainedValues = new string?[OperationCount];
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Array.Clear(retainedValues);
        stringPool = new BenchmarkStringPool(MaxPoolEntries, MaxStringLength);
    }

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Low-cardinality strings baseline")]
    public int LowCardinalityStringsBaseline() => MaterializeStrings(lowCardinalitySourceValues, usePool: false);

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "Low-cardinality strings bounded pool")]
    public int LowCardinalityStringsBoundedPool() => MaterializeStrings(lowCardinalitySourceValues, usePool: true);

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "High-cardinality strings baseline")]
    public int HighCardinalityStringsBaseline() => MaterializeStrings(highCardinalitySourceValues, usePool: false);

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(OperationsPerInvoke = OperationCount, Description = "High-cardinality strings bounded pool")]
    public int HighCardinalityStringsBoundedPool() => MaterializeStrings(highCardinalitySourceValues, usePool: true);

    private int MaterializeStrings(string[] sourceValues, bool usePool)
    {
        var checksum = 0;
        var pool = usePool ? stringPool : null;

        for (var i = 0; i < OperationCount; i++)
        {
            var value = CreateFreshString(sourceValues[i % sourceValues.Length]);
            if (pool is not null)
                value = pool.Intern(value);

            retainedValues[i] = value;
            checksum = unchecked(checksum + value.Length + (ReferenceEquals(value, sourceValues[i % sourceValues.Length]) ? 1 : 0));
        }

        return checksum + (pool?.Count ?? 0);
    }

    private static string[] CreateSourceValues(int count, string prefix)
    {
        var values = new string[count];
        for (var i = 0; i < values.Length; i++)
            values[i] = $"{prefix}-{i:0000}";

        return values;
    }

    private static string CreateFreshString(string value) => new(value.AsSpan());

    private sealed class BenchmarkStringPool(int maxEntries, int maxStringLength)
    {
        private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

        public int Count => values.Count;

        public string Intern(string value)
        {
            if (value.Length > maxStringLength)
                return value;

            if (values.TryGetValue(value, out var existing))
                return existing;

            if (values.Count >= maxEntries)
                return value;

            values.Add(value, value);
            return value;
        }
    }
}
