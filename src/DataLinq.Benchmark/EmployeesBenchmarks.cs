using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class EmployeesBenchmarks : IDisposable
{
    private const string BenchmarkProvidersEnvironmentVariable = "DATALINQ_BENCHMARK_PROVIDERS";
    private const string StableCategory = "stable";
    private const string Phase2WatchCategory = "phase2-watch";
    private BenchmarkContext? context;
    private BenchmarkScenario? executedScenario;

    [ParamsSource(nameof(GetProviderNames))]
    public string ProviderName { get; set; } = TestProviderMatrix.SQLiteInMemory.Name;

    public static IEnumerable<string> GetProviderNames()
    {
        var configured = Environment.GetEnvironmentVariable(BenchmarkProvidersEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return [TestProviderMatrix.SQLiteFile.Name, TestProviderMatrix.SQLiteInMemory.Name];

        var supportedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            TestProviderMatrix.SQLiteFile.Name,
            TestProviderMatrix.SQLiteInMemory.Name
        };

        var selectedProviders = configured
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selectedProviders.Length == 0)
            throw new InvalidOperationException(
                $"Environment variable '{BenchmarkProvidersEnvironmentVariable}' did not contain any benchmark providers.");

        var unsupportedProvider = selectedProviders.FirstOrDefault(provider => !supportedProviders.Contains(provider));
        if (unsupportedProvider is not null)
        {
            throw new InvalidOperationException(
                $"Environment variable '{BenchmarkProvidersEnvironmentVariable}' contains unsupported provider '{unsupportedProvider}'.");
        }

        return selectedProviders;
    }

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

    [BenchmarkCategory(StableCategory, Phase2WatchCategory)]
    [Benchmark(Description = "Provider initialization")]
    public int ProviderInitialization()
    {
        executedScenario = BenchmarkScenario.ProviderInitialization;
        return context!.InitializeProviderAndMetadataOnFreshScope();
    }

    [BenchmarkCategory(StableCategory, Phase2WatchCategory)]
    [Benchmark(Description = "Startup primary-key fetch")]
    public int StartupPrimaryKeyFetch()
    {
        executedScenario = BenchmarkScenario.StartupPrimaryKeyFetch;
        return context!.LoadEmployeeByPrimaryKeyOnFreshScope();
    }

    [IterationSetup(Target = nameof(CrudWorkflow))]
    public void SetupCrudWorkflow()
    {
        context!.CleanupCrudWorkflowEmployees();
        context.ResetState(clearCache: true);
    }

    [IterationCleanup(Target = nameof(CrudWorkflow))]
    public void CleanupCrudWorkflow()
    {
        context!.CleanupCrudWorkflowEmployees();
    }

    [BenchmarkCategory("experimental", "macro")]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.CrudWorkflowOperationCount, Description = "CRUD workflow")]
    public int CrudWorkflow()
    {
        executedScenario = BenchmarkScenario.CrudWorkflowBatch;
        return context!.RunCrudWorkflowBatch();
    }

    [IterationSetup(Target = nameof(InsertEmployees))]
    public void SetupInsertEmployees()
    {
        context!.CleanupInsertedEmployees();
        context.ResetState(clearCache: true);
    }

    [IterationCleanup(Target = nameof(InsertEmployees))]
    public void CleanupInsertEmployees()
    {
        context!.CleanupInsertedEmployees();
    }

    [BenchmarkCategory("experimental")]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.MutationBatchOperationCount, Description = "Insert employees")]
    public int InsertEmployees()
    {
        executedScenario = BenchmarkScenario.InsertEmployeesBatch;
        return context!.InsertEmployeesBatch();
    }

    [IterationSetup(Target = nameof(UpdateEmployees))]
    public void SetupUpdateEmployees()
    {
        context!.CleanupUpdatedEmployees();
        context.ResetState(clearCache: true);
    }

    [IterationCleanup(Target = nameof(UpdateEmployees))]
    public void CleanupUpdateEmployees()
    {
        context!.CleanupUpdatedEmployees();
    }

    [BenchmarkCategory(StableCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.MutationBatchOperationCount, Description = "Update employees")]
    public int UpdateEmployees()
    {
        executedScenario = BenchmarkScenario.UpdateEmployeesBatch;
        return context!.UpdateEmployeesBatch();
    }

    [IterationSetup(Target = nameof(ColdPrimaryKeyFetch))]
    public void SetupColdPrimaryKeyFetch()
    {
        context!.ResetState(clearCache: true);
    }

    [BenchmarkCategory(StableCategory)]
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

    [BenchmarkCategory(StableCategory, Phase2WatchCategory)]
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

    [BenchmarkCategory(StableCategory)]
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

    [BenchmarkCategory(StableCategory)]
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
