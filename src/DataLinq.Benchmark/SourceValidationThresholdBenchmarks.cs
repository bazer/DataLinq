using BenchmarkDotNet.Attributes;
using DataLinq.Instances;

namespace DataLinq.Benchmark;

/// <summary>
/// Experimental crossover probe for the result validator's linear-to-hash promotion threshold.
/// This is intentionally outside the canonical release-evidence matrix.
/// </summary>
[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class SourceValidationThresholdBenchmarks
{
    private const int OperationsPerInvoke = 1000;
    private DataLinqKey[] requestedKeys = [];
    private DataLinqKey[] returnedKeys = [];

    [Params("memory")]
    public string ProviderName { get; set; } = "memory";

    [Params(4, 8, 16, 32, 64, 128)]
    public int KeyCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        requestedKeys = Enumerable.Range(0, KeyCount)
            .Select(DataLinqKey.FromValue)
            .ToArray();
        returnedKeys = requestedKeys.ToArray();
    }

    [BenchmarkCategory("source-validation-threshold")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "Source validation linear scan")]
    public int LinearScan()
    {
        var checksum = 0;

        for (var iteration = 0; iteration < OperationsPerInvoke; iteration++)
        {
            for (var rowIndex = 0; rowIndex < returnedKeys.Length; rowIndex++)
            {
                var key = returnedKeys[rowIndex];
                var requested = false;
                for (var requestIndex = 0; requestIndex < requestedKeys.Length; requestIndex++)
                {
                    if (requestedKeys[requestIndex].Equals(key))
                    {
                        requested = true;
                        break;
                    }
                }

                if (!requested)
                    return -1;

                for (var previousIndex = 0; previousIndex < rowIndex; previousIndex++)
                {
                    if (returnedKeys[previousIndex].Equals(key))
                        return -2;
                }

                checksum = unchecked(checksum + key.GetHashCode());
            }
        }

        return checksum;
    }

    [BenchmarkCategory("source-validation-threshold")]
    [Benchmark(OperationsPerInvoke = OperationsPerInvoke, Description = "Source validation hash sets")]
    public int HashSets()
    {
        var checksum = 0;

        for (var iteration = 0; iteration < OperationsPerInvoke; iteration++)
        {
            var requested = new HashSet<DataLinqKey>(requestedKeys);
            var returned = new HashSet<DataLinqKey>();
            foreach (var key in returnedKeys)
            {
                if (!requested.Contains(key) || !returned.Add(key))
                    return -1;

                checksum = unchecked(checksum + key.GetHashCode());
            }
        }

        return checksum;
    }
}
