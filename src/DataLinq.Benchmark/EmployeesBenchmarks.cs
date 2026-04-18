using System;
using BenchmarkDotNet.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class EmployeesBenchmarks : IDisposable
{
    private BenchmarkContext? context;
    private BenchmarkScenario? executedScenario;

    [Params("sqlite-file", "sqlite-memory")]
    public string ProviderName { get; set; } = TestProviderMatrix.SQLiteFile.Name;

    [GlobalSetup]
    public void GlobalSetup()
    {
        context = new BenchmarkContext(ResolveProvider(ProviderName));
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        if (context is not null && executedScenario.HasValue)
        {
            var delta = context.CaptureTelemetryDelta(executedScenario.Value, ProviderName);
            BenchmarkTelemetryDeltaWriter.TryWrite(delta);
        }

        context?.Dispose();
        context = null;
        executedScenario = null;
    }

    [Benchmark(Description = "Startup primary-key fetch")]
    public int StartupPrimaryKeyFetch()
    {
        executedScenario = BenchmarkScenario.StartupPrimaryKeyFetch;
        return context!.LoadEmployeeByPrimaryKeyOnFreshScope();
    }

    [IterationSetup(Target = nameof(ColdPrimaryKeyFetch))]
    public void SetupColdPrimaryKeyFetch()
    {
        context!.ResetState(clearCache: true);
    }

    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Cold primary-key fetch")]
    public int ColdPrimaryKeyFetch()
    {
        executedScenario = BenchmarkScenario.ColdPrimaryKeyFetch;
        return context!.LoadEmployeesByPrimaryKeyBatch();
    }

    [IterationSetup(Target = nameof(WarmPrimaryKeyFetch))]
    public void SetupWarmPrimaryKeyFetch()
    {
        context!.ResetState(clearCache: true);
        _ = context.LoadEmployeesByPrimaryKeyBatch();
        DataLinqMetrics.Reset();
    }

    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Warm primary-key fetch")]
    public int WarmPrimaryKeyFetch()
    {
        executedScenario = BenchmarkScenario.WarmPrimaryKeyFetch;
        return context!.LoadEmployeesByPrimaryKeyBatch();
    }

    [IterationSetup(Target = nameof(ColdRelationTraversal))]
    public void SetupColdRelationTraversal()
    {
        context!.ResetState(clearCache: true);
    }

    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Cold relation traversal")]
    public int ColdRelationTraversal()
    {
        executedScenario = BenchmarkScenario.ColdRelationTraversal;
        return context!.TraverseDepartmentNamesBatch();
    }

    [IterationSetup(Target = nameof(WarmRelationTraversal))]
    public void SetupWarmRelationTraversal()
    {
        context!.ResetState(clearCache: true);
        _ = context.TraverseDepartmentNamesBatch();
        DataLinqMetrics.Reset();
    }

    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Warm relation traversal")]
    public int WarmRelationTraversal()
    {
        executedScenario = BenchmarkScenario.WarmRelationTraversal;
        return context!.TraverseDepartmentNamesBatch();
    }

    public void Dispose()
    {
        GlobalCleanup();
    }

    private static TestProviderDescriptor ResolveProvider(string providerName)
        => providerName switch
        {
            var name when string.Equals(name, TestProviderMatrix.SQLiteFile.Name, StringComparison.OrdinalIgnoreCase) => TestProviderMatrix.SQLiteFile,
            var name when string.Equals(name, TestProviderMatrix.SQLiteInMemory.Name, StringComparison.OrdinalIgnoreCase) => TestProviderMatrix.SQLiteInMemory,
            _ => throw new InvalidOperationException($"Unknown benchmark provider '{providerName}'.")
        };
}
