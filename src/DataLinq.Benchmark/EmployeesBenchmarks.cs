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
    private const string Phase3QueryHotPathCategory = "phase3-query-hotpath";
    private const string Phase10KeyFoundationCategory = "phase10-key-foundation";
    private const string Phase11CacheInvalidationCategory = "phase11-cache-invalidation";
    private const string Phase12CacheMemoryCategory = "phase12-cache-memory";
    private const string MacroReadWriteCategory = "macro-readwrite";
    private const string MacroBulkCategory = "macro-bulk";
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

    [IterationSetup(Target = nameof(CrudWorkflowSmall))]
    public void SetupCrudWorkflowSmall()
    {
        context!.CleanupCrudWorkflowEmployees();
        context.ResetState(clearCache: true);
    }

    [IterationCleanup(Target = nameof(CrudWorkflowSmall))]
    public void CleanupCrudWorkflowSmall()
    {
        context!.CleanupCrudWorkflowEmployees();
    }

    [BenchmarkCategory("experimental", MacroReadWriteCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.CrudWorkflowSmallOperationCount, Description = "CRUD workflow small")]
    public int CrudWorkflowSmall()
    {
        executedScenario = BenchmarkScenario.CrudWorkflowSmall;
        return context!.RunCrudWorkflowSmall();
    }

    [IterationSetup(Target = nameof(CrudWorkflowBatch))]
    public void SetupCrudWorkflowBatch()
    {
        context!.CleanupCrudWorkflowEmployees();
        context.ResetState(clearCache: true);
    }

    [IterationCleanup(Target = nameof(CrudWorkflowBatch))]
    public void CleanupCrudWorkflowBatch()
    {
        context!.CleanupCrudWorkflowEmployees();
    }

    [BenchmarkCategory("experimental", MacroBulkCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.CrudWorkflowOperationCount, Description = "CRUD workflow batch")]
    public int CrudWorkflowBatch()
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

    [IterationSetup(Target = nameof(WarmGeneratedStaticGet))]
    public void SetupWarmGeneratedStaticGet()
    {
        context!.ResetState(clearCache: true);
        _ = context.LoadEmployeesByGeneratedStaticGetBatch();
        DataLinqMetrics.Reset();
    }

    [BenchmarkCategory(Phase10KeyFoundationCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Warm generated static Get")]
    public int WarmGeneratedStaticGet()
    {
        executedScenario = BenchmarkScenario.WarmGeneratedStaticGet;
        return context!.LoadEmployeesByGeneratedStaticGetBatch();
    }

    [IterationSetup(Target = nameof(RepeatedNonPrimaryKeyEqualityFetch))]
    public void SetupRepeatedNonPrimaryKeyEqualityFetch()
    {
        context!.ResetState(clearCache: true);
    }

    [BenchmarkCategory(Phase3QueryHotPathCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Repeated non-PK equality fetch")]
    public int RepeatedNonPrimaryKeyEqualityFetch()
    {
        executedScenario = BenchmarkScenario.RepeatedNonPrimaryKeyEqualityFetch;
        return context!.LoadEmployeesByNonPrimaryKeyEqualityBatch();
    }

    [IterationSetup(Target = nameof(RepeatedInPredicateFetch))]
    public void SetupRepeatedInPredicateFetch()
    {
        context!.ResetState(clearCache: true);
    }

    [BenchmarkCategory(Phase3QueryHotPathCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Repeated IN predicate fetch")]
    public int RepeatedInPredicateFetch()
    {
        executedScenario = BenchmarkScenario.RepeatedInPredicateFetch;
        return context!.LoadEmployeesByInPredicateBatch();
    }

    [IterationSetup(Target = nameof(RepeatedScalarAny))]
    public void SetupRepeatedScalarAny()
    {
        context!.ResetState(clearCache: true);
    }

    [BenchmarkCategory(Phase3QueryHotPathCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Repeated scalar Any")]
    public int RepeatedScalarAny()
    {
        executedScenario = BenchmarkScenario.RepeatedScalarAny;
        return context!.ExecuteScalarAnyBatch();
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
        context.ClearWarmRelationTraversalCache();
        _ = context.TraverseWarmDepartmentNamesBatch();
        DataLinqMetrics.Reset();
    }

    [BenchmarkCategory(StableCategory, Phase10KeyFoundationCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Warm relation traversal")]
    public int WarmRelationTraversal()
    {
        executedScenario = BenchmarkScenario.WarmRelationTraversal;
        return context!.TraverseWarmDepartmentNamesBatch();
    }

    [IterationSetup(Target = nameof(ScalarRowCacheAddGetRemove))]
    public void SetupScalarRowCacheAddGetRemove()
    {
        context!.ResetScalarRowCacheProbe();
    }

    [BenchmarkCategory(Phase10KeyFoundationCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Scalar row-cache add/get/remove")]
    public int ScalarRowCacheAddGetRemove()
    {
        executedScenario = BenchmarkScenario.ScalarRowCacheAddGetRemove;
        return context!.AddGetRemoveScalarRowCacheEntries();
    }

    [IterationSetup(Target = nameof(WarmPrimaryKeyFetchWithCacheEstimate))]
    public void SetupWarmPrimaryKeyFetchWithCacheEstimate()
    {
        context!.ResetState(clearCache: true);
        _ = context.LoadEmployeesByPrimaryKeyBatch();
        DataLinqMetrics.Reset();
    }

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Warm PK with cache estimate")]
    public int WarmPrimaryKeyFetchWithCacheEstimate()
    {
        executedScenario = BenchmarkScenario.WarmPrimaryKeyFetchWithCacheEstimate;
        return context!.LoadEmployeesByPrimaryKeyBatchWithCacheEstimate();
    }

    [IterationSetup(Target = nameof(WarmRelationTraversalWithCacheEstimate))]
    public void SetupWarmRelationTraversalWithCacheEstimate()
    {
        context!.ResetState(clearCache: true);
        context.ClearWarmRelationTraversalCache();
        _ = context.TraverseWarmDepartmentNamesBatch();
        DataLinqMetrics.Reset();
    }

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Warm relation with cache estimate")]
    public int WarmRelationTraversalWithCacheEstimate()
    {
        executedScenario = BenchmarkScenario.WarmRelationTraversalWithCacheEstimate;
        return context!.TraverseWarmDepartmentNamesBatchWithCacheEstimate();
    }

    [IterationSetup(Target = nameof(LargeRelationIndexPreload))]
    public void SetupLargeRelationIndexPreload()
    {
        context!.ResetState(clearCache: true);
    }

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(Description = "Large relation index preload")]
    public int LargeRelationIndexPreload()
    {
        executedScenario = BenchmarkScenario.LargeRelationIndexPreload;
        return context!.PreloadLargeRelationIndex();
    }

    [BenchmarkCategory(Phase12CacheMemoryCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Composite dynamic key workload")]
    public int CompositeDynamicKeyWorkload()
    {
        executedScenario = BenchmarkScenario.CompositeDynamicKeyWorkload;
        return context!.CreateCompositeDynamicKeys();
    }

    [IterationSetup(Target = nameof(InvalidateOneEmployeeRow))]
    public void SetupInvalidateOneEmployeeRow()
    {
        context!.WarmEmployeeInvalidationCache();
    }

    [BenchmarkCategory(Phase11CacheInvalidationCategory)]
    [Benchmark(OperationsPerInvoke = BenchmarkContext.BatchOperationCount, Description = "Invalidate one employee row")]
    public int InvalidateOneEmployeeRow()
    {
        executedScenario = BenchmarkScenario.InvalidateOneEmployeeRow;
        return context!.InvalidateOneEmployeeRows();
    }

    [IterationSetup(Target = nameof(InvalidateManyEmployeeRows))]
    public void SetupInvalidateManyEmployeeRows()
    {
        context!.WarmEmployeeInvalidationCache();
    }

    [BenchmarkCategory(Phase11CacheInvalidationCategory)]
    [Benchmark(Description = "Invalidate many employee rows")]
    public int InvalidateManyEmployeeRows()
    {
        executedScenario = BenchmarkScenario.InvalidateManyEmployeeRows;
        return context!.InvalidateManyEmployeeRows();
    }

    [IterationSetup(Target = nameof(InvalidateEmployeeTable))]
    public void SetupInvalidateEmployeeTable()
    {
        context!.WarmEmployeeInvalidationCache();
    }

    [BenchmarkCategory(Phase11CacheInvalidationCategory)]
    [Benchmark(Description = "Invalidate employee table")]
    public int InvalidateEmployeeTable()
    {
        executedScenario = BenchmarkScenario.InvalidateEmployeeTable;
        return context!.InvalidateEmployeeTable();
    }

    [IterationSetup(Target = nameof(InvalidateDatabase))]
    public void SetupInvalidateDatabase()
    {
        context!.WarmDatabaseInvalidationCache();
    }

    [BenchmarkCategory(Phase11CacheInvalidationCategory)]
    [Benchmark(Description = "Invalidate database")]
    public int InvalidateDatabase()
    {
        executedScenario = BenchmarkScenario.InvalidateDatabase;
        return context!.InvalidateDatabase();
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
