using System;
using System.Linq;
using DataLinq.Diagnostics;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

internal sealed class BenchmarkContext : IDisposable
{
    private const int SeedEmployeeCount = 1000;
    internal const int BatchOperationCount = 1000;

    private readonly EmployeesTestDatabase databaseScope;
    private readonly int[] sampleEmployeeNumbers;
    private readonly int[] sampleEmployeeWithDepartmentNumbers;

    public BenchmarkContext(TestProviderDescriptor provider)
    {
        databaseScope = EmployeesTestDatabase.CreateIsolatedBogus(
            provider,
            "benchmark",
            SeedEmployeeCount);

        Database = databaseScope.Database;
        sampleEmployeeNumbers = Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no!.Value)
            .Take(BatchOperationCount)
            .ToArray();
        sampleEmployeeWithDepartmentNumbers = Database.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no)
            .ToList()
            .Distinct()
            .Take(BatchOperationCount)
            .ToArray();

        if (sampleEmployeeNumbers.Length != BatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleEmployeeNumbers.Length} primary-key samples. Expected at least {BatchOperationCount}.");

        if (sampleEmployeeWithDepartmentNumbers.Length != BatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleEmployeeWithDepartmentNumbers.Length} relation-traversal samples. Expected at least {BatchOperationCount}.");
    }

    public Database<EmployeesDb> Database { get; }

    public void ResetState(bool clearCache)
    {
        if (clearCache)
            Database.Provider.State.ClearCache();

        DataLinqMetrics.Reset();
    }

    public int LoadEmployeesByPrimaryKeyBatch()
    {
        var checksum = 0;

        foreach (var employeeNumber in sampleEmployeeNumbers)
        {
            var employee = Database.Query().Employees.Single(x => x.emp_no == employeeNumber);
            checksum += employee.emp_no!.Value;
        }

        return checksum;
    }

    public int TraverseDepartmentNamesBatch()
    {
        var checksum = 0;

        foreach (var employeeNumber in sampleEmployeeWithDepartmentNumbers)
        {
            var employee = Database.Query().Employees.Single(x => x.emp_no == employeeNumber);
            checksum += employee.dept_emp.First().departments.Name.Length;
        }

        return checksum;
    }

    public BenchmarkTelemetryDeltaArtifact CaptureTelemetryDelta(BenchmarkScenario scenario, string providerName)
    {
        var method = GetScenarioDisplayName(scenario);
        ResetState(clearCache: true);

        switch (scenario)
        {
            case BenchmarkScenario.WarmPrimaryKeyFetch:
                _ = LoadEmployeesByPrimaryKeyBatch();
                DataLinqMetrics.Reset();
                break;
            case BenchmarkScenario.WarmRelationTraversal:
                _ = TraverseDepartmentNamesBatch();
                DataLinqMetrics.Reset();
                break;
        }

        var before = SnapshotMetrics();

        _ = scenario switch
        {
            BenchmarkScenario.ColdPrimaryKeyFetch or BenchmarkScenario.WarmPrimaryKeyFetch => LoadEmployeesByPrimaryKeyBatch(),
            BenchmarkScenario.ColdRelationTraversal or BenchmarkScenario.WarmRelationTraversal => TraverseDepartmentNamesBatch(),
            _ => throw new InvalidOperationException($"Unsupported benchmark scenario '{scenario}'.")
        };

        var after = SnapshotMetrics();
        return CreateDeltaArtifact(method, providerName, before, after);
    }

    public DataLinqMetricsSnapshot SnapshotMetrics() => DataLinqMetrics.Snapshot();

    public void Dispose()
    {
        databaseScope.Dispose();
    }

    private static BenchmarkTelemetryDeltaArtifact CreateDeltaArtifact(
        string method,
        string providerName,
        DataLinqMetricsSnapshot before,
        DataLinqMetricsSnapshot after)
    {
        static double Normalize(long afterValue, long beforeValue)
            => (afterValue - beforeValue) / (double)BatchOperationCount;

        var relationHits = Normalize(after.Relations.ReferenceCacheHits + after.Relations.CollectionCacheHits, before.Relations.ReferenceCacheHits + before.Relations.CollectionCacheHits);
        var relationLoads = Normalize(after.Relations.ReferenceLoads + after.Relations.CollectionLoads, before.Relations.ReferenceLoads + before.Relations.CollectionLoads);

        return new BenchmarkTelemetryDeltaArtifact(
            Method: method,
            ProviderName: providerName,
            OperationsPerInvoke: BatchOperationCount,
            EntityQueriesPerOperation: Normalize(after.Queries.EntityExecutions, before.Queries.EntityExecutions),
            ScalarQueriesPerOperation: Normalize(after.Queries.ScalarExecutions, before.Queries.ScalarExecutions),
            RowCacheHitsPerOperation: Normalize(after.RowCache.Hits, before.RowCache.Hits),
            RowCacheMissesPerOperation: Normalize(after.RowCache.Misses, before.RowCache.Misses),
            RowCacheStoresPerOperation: Normalize(after.RowCache.Stores, before.RowCache.Stores),
            DatabaseRowsPerOperation: Normalize(after.RowCache.DatabaseRowsLoaded, before.RowCache.DatabaseRowsLoaded),
            MaterializationsPerOperation: Normalize(after.RowCache.Materializations, before.RowCache.Materializations),
            RelationHitsPerOperation: relationHits,
            RelationLoadsPerOperation: relationLoads);
    }

    private static string GetScenarioDisplayName(BenchmarkScenario scenario)
        => scenario switch
        {
            BenchmarkScenario.ColdPrimaryKeyFetch => "Cold primary-key fetch",
            BenchmarkScenario.WarmPrimaryKeyFetch => "Warm primary-key fetch",
            BenchmarkScenario.ColdRelationTraversal => "Cold relation traversal",
            BenchmarkScenario.WarmRelationTraversal => "Warm relation traversal",
            _ => throw new InvalidOperationException($"Unsupported benchmark scenario '{scenario}'.")
        };
}
