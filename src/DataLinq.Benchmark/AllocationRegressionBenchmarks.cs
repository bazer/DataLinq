using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Testing;

namespace DataLinq.Benchmark;

/// <summary>
/// Owns the strict final-0.8 allocation comparison workload. Keep this source compatible with
/// the frozen 0.8 benchmark project: current tooling injects the same file into historical replay
/// so baseline and candidate use identical scenario counts without modifying the target worktree.
/// </summary>
[Config(typeof(DataLinqBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: false)]
public class AllocationRegressionBenchmarks : IDisposable
{
    private const string AllocationRegressionCategory = "allocation-regression";

    internal const int ProviderInitializationOperations = 1;
    internal const int StartupPrimaryKeyOperations = 1;
    internal const int CrudWorkflowSmallOperations = 250;
    internal const int CrudWorkflowBatchOperations = 350;
    internal const int UpdateEmployeesOperations = 2_000;
    internal const int ColdPrimaryKeyOperations = 1_000;
    internal const int WarmPrimaryKeyOperations = 60_000;
    internal const int ColdRelationOperations = 1_000;
    internal const int WarmRelationOperations = 1_500_000;

    private const int CrudWorkflowSmallRepeats =
        CrudWorkflowSmallOperations / BenchmarkContext.CrudWorkflowSmallOperationCount;
    private const int UpdateEmployeesRepeats =
        UpdateEmployeesOperations / BenchmarkContext.MutationBatchOperationCount;
    private const int WarmPrimaryKeyRepeats =
        WarmPrimaryKeyOperations / BenchmarkContext.BatchOperationCount;
    private const int WarmRelationRepeats =
        WarmRelationOperations / BenchmarkContext.BatchOperationCount;

    private BenchmarkContext? context;
    private BenchmarkScenario? executedScenario;

    [ParamsSource(nameof(GetProviderNames))]
    public string ProviderName { get; set; } = TestProviderMatrix.SQLiteInMemory.Name;

    public static IEnumerable<string> GetProviderNames()
    {
        var configured = Environment.GetEnvironmentVariable("DATALINQ_BENCHMARK_PROVIDERS");
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
        {
            throw new InvalidOperationException(
                "Environment variable 'DATALINQ_BENCHMARK_PROVIDERS' did not contain any benchmark providers.");
        }

        var unsupportedProvider = selectedProviders.FirstOrDefault(
            provider => !supportedProviders.Contains(provider));
        if (unsupportedProvider is not null)
        {
            throw new InvalidOperationException(
                $"Environment variable 'DATALINQ_BENCHMARK_PROVIDERS' contains unsupported provider '{unsupportedProvider}'.");
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
            BenchmarkTelemetryDeltaWriter.TryWrite(
                CaptureTelemetryDelta(executedScenario.Value, ProviderName));
        }

        context?.Dispose();
        context = null;
        executedScenario = null;
    }

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(Description = "Provider initialization")]
    public int ProviderInitialization()
    {
        executedScenario = BenchmarkScenario.ProviderInitialization;
        return context!.InitializeProviderAndMetadataOnFreshScope();
    }

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(Description = "Startup primary-key fetch")]
    public int StartupPrimaryKeyFetch()
    {
        executedScenario = BenchmarkScenario.StartupPrimaryKeyFetch;
        return context!.LoadEmployeeByPrimaryKeyOnFreshScope();
    }

    [IterationSetup(Target = nameof(CrudWorkflowSmall))]
    public void SetupCrudWorkflowSmall() => PrepareScenario(BenchmarkScenario.CrudWorkflowSmall);

    [IterationCleanup(Target = nameof(CrudWorkflowSmall))]
    public void CleanupCrudWorkflowSmall() => context!.CleanupCrudWorkflowEmployees();

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(OperationsPerInvoke = CrudWorkflowSmallOperations, Description = "CRUD workflow small")]
    public int CrudWorkflowSmall()
    {
        executedScenario = BenchmarkScenario.CrudWorkflowSmall;
        return ExecuteCrudWorkflowSmall();
    }

    [IterationSetup(Target = nameof(CrudWorkflowBatch))]
    public void SetupCrudWorkflowBatch() => PrepareScenario(BenchmarkScenario.CrudWorkflowBatch);

    [IterationCleanup(Target = nameof(CrudWorkflowBatch))]
    public void CleanupCrudWorkflowBatch() => context!.CleanupCrudWorkflowEmployees();

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(OperationsPerInvoke = CrudWorkflowBatchOperations, Description = "CRUD workflow batch")]
    public int CrudWorkflowBatch()
    {
        executedScenario = BenchmarkScenario.CrudWorkflowBatch;
        return ExecuteCrudWorkflowBatch();
    }

    [IterationSetup(Target = nameof(UpdateEmployees))]
    public void SetupUpdateEmployees() => PrepareScenario(BenchmarkScenario.UpdateEmployeesBatch);

    [IterationCleanup(Target = nameof(UpdateEmployees))]
    public void CleanupUpdateEmployees() => context!.CleanupUpdatedEmployees();

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(OperationsPerInvoke = UpdateEmployeesOperations, Description = "Update employees")]
    public int UpdateEmployees()
    {
        executedScenario = BenchmarkScenario.UpdateEmployeesBatch;
        return ExecuteUpdateEmployees();
    }

    [IterationSetup(Target = nameof(ColdPrimaryKeyFetch))]
    public void SetupColdPrimaryKeyFetch() => PrepareScenario(BenchmarkScenario.ColdPrimaryKeyFetch);

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(OperationsPerInvoke = ColdPrimaryKeyOperations, Description = "Cold primary-key fetch")]
    public int ColdPrimaryKeyFetch()
    {
        executedScenario = BenchmarkScenario.ColdPrimaryKeyFetch;
        return context!.LoadEmployeesByPrimaryKeyBatch();
    }

    [IterationSetup(Target = nameof(WarmPrimaryKeyFetch))]
    public void SetupWarmPrimaryKeyFetch() => PrepareScenario(BenchmarkScenario.WarmPrimaryKeyFetch);

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(OperationsPerInvoke = WarmPrimaryKeyOperations, Description = "Warm primary-key fetch")]
    public int WarmPrimaryKeyFetch()
    {
        executedScenario = BenchmarkScenario.WarmPrimaryKeyFetch;
        return ExecuteWarmPrimaryKeyFetch();
    }

    [IterationSetup(Target = nameof(ColdRelationTraversal))]
    public void SetupColdRelationTraversal() => PrepareScenario(BenchmarkScenario.ColdRelationTraversal);

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(OperationsPerInvoke = ColdRelationOperations, Description = "Cold relation traversal")]
    public int ColdRelationTraversal()
    {
        executedScenario = BenchmarkScenario.ColdRelationTraversal;
        return context!.TraverseDepartmentNamesBatch();
    }

    [IterationSetup(Target = nameof(WarmRelationTraversal))]
    public void SetupWarmRelationTraversal() => PrepareScenario(BenchmarkScenario.WarmRelationTraversal);

    [BenchmarkCategory(AllocationRegressionCategory)]
    [Benchmark(OperationsPerInvoke = WarmRelationOperations, Description = "Warm relation traversal")]
    public int WarmRelationTraversal()
    {
        executedScenario = BenchmarkScenario.WarmRelationTraversal;
        return ExecuteWarmRelationTraversal();
    }

    public void Dispose() => GlobalCleanup();

    private void PrepareScenario(BenchmarkScenario scenario)
    {
        if (scenario == BenchmarkScenario.StartupPrimaryKeyFetch)
        {
            DataLinqMetrics.Reset();
            return;
        }

        switch (scenario)
        {
            case BenchmarkScenario.CrudWorkflowSmall:
            case BenchmarkScenario.CrudWorkflowBatch:
                context!.CleanupCrudWorkflowEmployees();
                break;
            case BenchmarkScenario.UpdateEmployeesBatch:
                context!.CleanupUpdatedEmployees();
                break;
        }

        context!.ResetState(clearCache: true);

        switch (scenario)
        {
            case BenchmarkScenario.WarmPrimaryKeyFetch:
                _ = context.LoadEmployeesByPrimaryKeyBatch();
                DataLinqMetrics.Reset();
                break;
            case BenchmarkScenario.WarmRelationTraversal:
                context.ClearWarmRelationTraversalCache();
                _ = context.TraverseWarmDepartmentNamesBatch();
                DataLinqMetrics.Reset();
                break;
        }
    }

    private int ExecuteScenario(BenchmarkScenario scenario)
        => scenario switch
        {
            BenchmarkScenario.ProviderInitialization =>
                context!.InitializeProviderAndMetadataOnFreshScope(),
            BenchmarkScenario.StartupPrimaryKeyFetch =>
                context!.LoadEmployeeByPrimaryKeyOnFreshScope(),
            BenchmarkScenario.CrudWorkflowSmall => ExecuteCrudWorkflowSmall(),
            BenchmarkScenario.CrudWorkflowBatch => ExecuteCrudWorkflowBatch(),
            BenchmarkScenario.UpdateEmployeesBatch => ExecuteUpdateEmployees(),
            BenchmarkScenario.ColdPrimaryKeyFetch => context!.LoadEmployeesByPrimaryKeyBatch(),
            BenchmarkScenario.WarmPrimaryKeyFetch => ExecuteWarmPrimaryKeyFetch(),
            BenchmarkScenario.ColdRelationTraversal => context!.TraverseDepartmentNamesBatch(),
            BenchmarkScenario.WarmRelationTraversal => ExecuteWarmRelationTraversal(),
            _ => throw new InvalidOperationException(
                $"Scenario '{scenario}' is not part of the allocation-regression lane.")
        };

    private int ExecuteCrudWorkflowSmall()
    {
        var checksum = 0;
        for (var repeat = 0; repeat < CrudWorkflowSmallRepeats; repeat++)
            checksum = unchecked(checksum + context!.RunCrudWorkflowSmall());
        return checksum;
    }

    private int ExecuteCrudWorkflowBatch() =>
        unchecked(context!.RunCrudWorkflowBatch() + context.RunCrudWorkflowSmall());

    private int ExecuteUpdateEmployees()
    {
        var checksum = 0;
        for (var repeat = 0; repeat < UpdateEmployeesRepeats; repeat++)
            checksum = unchecked(checksum + context!.UpdateEmployeesBatch());
        return checksum;
    }

    private int ExecuteWarmPrimaryKeyFetch()
    {
        var checksum = 0;
        for (var repeat = 0; repeat < WarmPrimaryKeyRepeats; repeat++)
            checksum = unchecked(checksum + context!.LoadEmployeesByPrimaryKeyBatch());
        return checksum;
    }

    private int ExecuteWarmRelationTraversal()
    {
        var checksum = 0;
        for (var repeat = 0; repeat < WarmRelationRepeats; repeat++)
            checksum = unchecked(checksum + context!.TraverseWarmDepartmentNamesBatch());
        return checksum;
    }

    private BenchmarkTelemetryDeltaArtifact CaptureTelemetryDelta(
        BenchmarkScenario scenario,
        string providerName)
    {
        PrepareScenario(scenario);
        var before = DataLinqMetrics.Snapshot();
        _ = ExecuteScenario(scenario);
        var after = DataLinqMetrics.Snapshot();
        var artifact = CreateDeltaArtifact(
            GetScenarioDisplayName(scenario),
            providerName,
            GetOperationsPerInvoke(scenario),
            before,
            after);

        switch (scenario)
        {
            case BenchmarkScenario.CrudWorkflowSmall:
            case BenchmarkScenario.CrudWorkflowBatch:
                context!.CleanupCrudWorkflowEmployees();
                break;
            case BenchmarkScenario.UpdateEmployeesBatch:
                context!.CleanupUpdatedEmployees();
                break;
        }

        return artifact;
    }

    private static BenchmarkTelemetryDeltaArtifact CreateDeltaArtifact(
        string method,
        string providerName,
        int operationsPerInvoke,
        DataLinqMetricsSnapshot before,
        DataLinqMetricsSnapshot after)
    {
        static double Normalize(long afterValue, long beforeValue, int operations) =>
            (afterValue - beforeValue) / (double)operations;

        var relationHits = Normalize(
            after.Relations.ReferenceCacheHits + after.Relations.CollectionCacheHits,
            before.Relations.ReferenceCacheHits + before.Relations.CollectionCacheHits,
            operationsPerInvoke);
        var relationLoads = Normalize(
            after.Relations.ReferenceLoads + after.Relations.CollectionLoads,
            before.Relations.ReferenceLoads + before.Relations.CollectionLoads,
            operationsPerInvoke);

        return new BenchmarkTelemetryDeltaArtifact(
            Method: method,
            ProviderName: providerName,
            OperationsPerInvoke: operationsPerInvoke,
            EntityQueriesPerOperation: Normalize(after.Queries.EntityExecutions, before.Queries.EntityExecutions, operationsPerInvoke),
            ScalarQueriesPerOperation: Normalize(after.Queries.ScalarExecutions, before.Queries.ScalarExecutions, operationsPerInvoke),
            TransactionStartsPerOperation: Normalize(after.Transactions.Starts, before.Transactions.Starts, operationsPerInvoke),
            TransactionCommitsPerOperation: Normalize(after.Transactions.Commits, before.Transactions.Commits, operationsPerInvoke),
            TransactionRollbacksPerOperation: Normalize(after.Transactions.Rollbacks, before.Transactions.Rollbacks, operationsPerInvoke),
            MutationInsertsPerOperation: Normalize(after.Mutations.Inserts, before.Mutations.Inserts, operationsPerInvoke),
            MutationUpdatesPerOperation: Normalize(after.Mutations.Updates, before.Mutations.Updates, operationsPerInvoke),
            MutationDeletesPerOperation: Normalize(after.Mutations.Deletes, before.Mutations.Deletes, operationsPerInvoke),
            MutationAffectedRowsPerOperation: Normalize(after.Mutations.AffectedRows, before.Mutations.AffectedRows, operationsPerInvoke),
            RowCacheHitsPerOperation: Normalize(after.RowCache.Hits, before.RowCache.Hits, operationsPerInvoke),
            RowCacheMissesPerOperation: Normalize(after.RowCache.Misses, before.RowCache.Misses, operationsPerInvoke),
            RowCacheStoresPerOperation: Normalize(after.RowCache.Stores, before.RowCache.Stores, operationsPerInvoke),
            DatabaseRowsPerOperation: Normalize(after.RowCache.DatabaseRowsLoaded, before.RowCache.DatabaseRowsLoaded, operationsPerInvoke),
            MaterializationsPerOperation: Normalize(after.RowCache.Materializations, before.RowCache.Materializations, operationsPerInvoke),
            RelationHitsPerOperation: relationHits,
            RelationLoadsPerOperation: relationLoads,
            CacheInvalidationOperationsPerOperation: Normalize(after.CacheInvalidations.Operations, before.CacheInvalidations.Operations, operationsPerInvoke),
            CacheInvalidationRowsRemovedPerOperation: Normalize(after.CacheInvalidations.RowsRemoved, before.CacheInvalidations.RowsRemoved, operationsPerInvoke),
            CacheInvalidationTablesClearedPerOperation: Normalize(after.CacheInvalidations.TablesCleared, before.CacheInvalidations.TablesCleared, operationsPerInvoke),
            CacheInvalidationProviderKeysPerOperation: Normalize(after.CacheInvalidations.ProviderKeys, before.CacheInvalidations.ProviderKeys, operationsPerInvoke),
            CacheInvalidationApproximateWorkPerOperation: Normalize(after.CacheInvalidations.ApproximateWork, before.CacheInvalidations.ApproximateWork, operationsPerInvoke),
            CacheInvalidationPreciseOperationsPerOperation: Normalize(after.CacheInvalidations.PreciseOperations, before.CacheInvalidations.PreciseOperations, operationsPerInvoke),
            CacheInvalidationConservativeFallbackOperationsPerOperation: Normalize(after.CacheInvalidations.ConservativeFallbackOperations, before.CacheInvalidations.ConservativeFallbackOperations, operationsPerInvoke));
    }

    private static int GetOperationsPerInvoke(BenchmarkScenario scenario)
        => scenario switch
        {
            BenchmarkScenario.ProviderInitialization => ProviderInitializationOperations,
            BenchmarkScenario.StartupPrimaryKeyFetch => StartupPrimaryKeyOperations,
            BenchmarkScenario.CrudWorkflowSmall => CrudWorkflowSmallOperations,
            BenchmarkScenario.CrudWorkflowBatch => CrudWorkflowBatchOperations,
            BenchmarkScenario.UpdateEmployeesBatch => UpdateEmployeesOperations,
            BenchmarkScenario.ColdPrimaryKeyFetch => ColdPrimaryKeyOperations,
            BenchmarkScenario.WarmPrimaryKeyFetch => WarmPrimaryKeyOperations,
            BenchmarkScenario.ColdRelationTraversal => ColdRelationOperations,
            BenchmarkScenario.WarmRelationTraversal => WarmRelationOperations,
            _ => throw new InvalidOperationException(
                $"Scenario '{scenario}' is not part of the allocation-regression lane.")
        };

    private static string GetScenarioDisplayName(BenchmarkScenario scenario)
        => scenario switch
        {
            BenchmarkScenario.ProviderInitialization => "Provider initialization",
            BenchmarkScenario.StartupPrimaryKeyFetch => "Startup primary-key fetch",
            BenchmarkScenario.CrudWorkflowSmall => "CRUD workflow small",
            BenchmarkScenario.CrudWorkflowBatch => "CRUD workflow batch",
            BenchmarkScenario.UpdateEmployeesBatch => "Update employees",
            BenchmarkScenario.ColdPrimaryKeyFetch => "Cold primary-key fetch",
            BenchmarkScenario.WarmPrimaryKeyFetch => "Warm primary-key fetch",
            BenchmarkScenario.ColdRelationTraversal => "Cold relation traversal",
            BenchmarkScenario.WarmRelationTraversal => "Warm relation traversal",
            _ => throw new InvalidOperationException(
                $"Scenario '{scenario}' is not part of the allocation-regression lane.")
        };

    private static TestProviderDescriptor ResolveProvider(string providerName)
        => providerName switch
        {
            var name when string.Equals(name, TestProviderMatrix.SQLiteFile.Name, StringComparison.OrdinalIgnoreCase) =>
                TestProviderMatrix.SQLiteFile,
            var name when string.Equals(name, TestProviderMatrix.SQLiteInMemory.Name, StringComparison.OrdinalIgnoreCase) =>
                TestProviderMatrix.SQLiteInMemory,
            _ => throw new InvalidOperationException($"Unknown benchmark provider '{providerName}'.")
        };
}
