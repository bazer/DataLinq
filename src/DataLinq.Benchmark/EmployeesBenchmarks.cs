using System;
using BenchmarkDotNet.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

[MemoryDiagnoser]
public class EmployeesBenchmarks : IDisposable
{
    private BenchmarkContext? context;

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
        context?.Dispose();
        context = null;
    }

    [IterationSetup(Target = nameof(ColdPrimaryKeyFetch))]
    public void SetupColdPrimaryKeyFetch()
    {
        context!.ResetState(clearCache: true);
    }

    [Benchmark(Description = "Cold primary-key fetch")]
    public Employee ColdPrimaryKeyFetch()
    {
        return context!.LoadEmployeeByPrimaryKey();
    }

    [IterationSetup(Target = nameof(WarmPrimaryKeyFetch))]
    public void SetupWarmPrimaryKeyFetch()
    {
        context!.ResetState(clearCache: true);
        _ = context.LoadEmployeeByPrimaryKey();
        DataLinqRuntimeMetrics.Reset();
    }

    [Benchmark(Description = "Warm primary-key fetch")]
    public Employee WarmPrimaryKeyFetch()
    {
        return context!.LoadEmployeeByPrimaryKey();
    }

    [IterationSetup(Target = nameof(ColdRelationTraversal))]
    public void SetupColdRelationTraversal()
    {
        context!.ResetState(clearCache: true);
    }

    [Benchmark(Description = "Cold relation traversal")]
    public string ColdRelationTraversal()
    {
        return context!.TraverseDepartmentName();
    }

    [IterationSetup(Target = nameof(WarmRelationTraversal))]
    public void SetupWarmRelationTraversal()
    {
        context!.ResetState(clearCache: true);
        _ = context.TraverseDepartmentName();
        DataLinqRuntimeMetrics.Reset();
    }

    [Benchmark(Description = "Warm relation traversal")]
    public string WarmRelationTraversal()
    {
        return context!.TraverseDepartmentName();
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
