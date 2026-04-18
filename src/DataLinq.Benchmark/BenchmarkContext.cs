using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Diagnostics;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

internal sealed class BenchmarkContext : IDisposable
{
    private const int SeedEmployeeCount = 1000;
    internal const int BatchOperationCount = 1000;
    internal const int MutationBatchOperationCount = 1000;

    private readonly TestProviderDescriptor provider;
    private readonly EmployeesTestDatabase databaseScope;
    private readonly int[] sampleEmployeeNumbers;
    private readonly int[] sampleEmployeeWithDepartmentNumbers;
    private readonly int[] sampleMutationEmployeeNumbers;
    private readonly InsertEmployeeTemplate[] insertEmployeeTemplates;
    private readonly Dictionary<int, string> originalMutationLastNames;
    private readonly int startupEmployeeNumber;
    private readonly List<Employee> insertedEmployees = [];

    public BenchmarkContext(TestProviderDescriptor provider)
    {
        this.provider = provider;
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
        sampleMutationEmployeeNumbers = Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no!.Value)
            .Take(MutationBatchOperationCount)
            .ToArray();
        insertEmployeeTemplates = Enumerable.Range(0, MutationBatchOperationCount)
            .Select(CreateInsertTemplate)
            .ToArray();
        originalMutationLastNames = Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(MutationBatchOperationCount)
            .ToDictionary(x => x.emp_no!.Value, x => x.last_name);

        if (sampleEmployeeNumbers.Length != BatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleEmployeeNumbers.Length} primary-key samples. Expected at least {BatchOperationCount}.");

        if (sampleEmployeeWithDepartmentNumbers.Length != BatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleEmployeeWithDepartmentNumbers.Length} relation-traversal samples. Expected at least {BatchOperationCount}.");

        if (sampleMutationEmployeeNumbers.Length != MutationBatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleMutationEmployeeNumbers.Length} mutation samples. Expected at least {MutationBatchOperationCount}.");

        using var startupScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            "benchmark-startup",
            EmployeesSeedMode.Bogus);

        startupEmployeeNumber = startupScope.Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no!.Value)
            .First();
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

    public int LoadEmployeeByPrimaryKeyOnFreshScope()
    {
        using var startupScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            "benchmark-startup",
            EmployeesSeedMode.Bogus);

        var employee = startupScope.Database.Query().Employees.Single(x => x.emp_no == startupEmployeeNumber);
        return employee.emp_no!.Value;
    }

    public int InsertEmployeesBatch()
    {
        insertedEmployees.Clear();

        using var transaction = Database.Transaction();
        var checksum = 0;

        for (var i = 0; i < MutationBatchOperationCount; i++)
        {
            var template = insertEmployeeTemplates[i];
            var employee = transaction.Insert(new MutableEmployee
            {
                birth_date = template.BirthDate,
                first_name = template.FirstName,
                last_name = template.LastName,
                gender = template.Gender,
                hire_date = template.HireDate
            });
            insertedEmployees.Add(employee);
            checksum += employee.emp_no!.Value;
        }

        transaction.Commit();
        return checksum;
    }

    public void CleanupInsertedEmployees()
    {
        if (insertedEmployees.Count == 0)
            return;

        using var transaction = Database.Transaction();
        foreach (var employee in insertedEmployees)
            transaction.Delete(employee);

        transaction.Commit();
        insertedEmployees.Clear();
    }

    public int UpdateEmployeesBatch()
    {
        using var transaction = Database.Transaction();
        var checksum = 0;

        foreach (var employeeNumber in sampleMutationEmployeeNumbers)
        {
            var employee = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);
            var updated = transaction.Update(employee, mutable => mutable.last_name = $"Bench-{employeeNumber}");
            checksum += updated.last_name.Length;
        }

        transaction.Commit();
        return checksum;
    }

    public void CleanupUpdatedEmployees()
    {
        using var transaction = Database.Transaction();

        foreach (var employeeNumber in sampleMutationEmployeeNumbers)
        {
            var employee = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);
            var originalLastName = originalMutationLastNames[employeeNumber];
            transaction.Update(employee, mutable => mutable.last_name = originalLastName);
        }

        transaction.Commit();
    }

    public BenchmarkTelemetryDeltaArtifact CaptureTelemetryDelta(BenchmarkScenario scenario, string providerName)
    {
        var method = GetScenarioDisplayName(scenario);
        if (scenario is not BenchmarkScenario.StartupPrimaryKeyFetch)
        {
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
                case BenchmarkScenario.InsertEmployeesBatch:
                    CleanupInsertedEmployees();
                    break;
                case BenchmarkScenario.UpdateEmployeesBatch:
                    CleanupUpdatedEmployees();
                    break;
            }
        }
        else
        {
            DataLinqMetrics.Reset();
        }

        var before = SnapshotMetrics();

        _ = scenario switch
        {
            BenchmarkScenario.StartupPrimaryKeyFetch => LoadEmployeeByPrimaryKeyOnFreshScope(),
            BenchmarkScenario.InsertEmployeesBatch => InsertEmployeesBatch(),
            BenchmarkScenario.UpdateEmployeesBatch => UpdateEmployeesBatch(),
            BenchmarkScenario.ColdPrimaryKeyFetch or BenchmarkScenario.WarmPrimaryKeyFetch => LoadEmployeesByPrimaryKeyBatch(),
            BenchmarkScenario.ColdRelationTraversal or BenchmarkScenario.WarmRelationTraversal => TraverseDepartmentNamesBatch(),
            _ => throw new InvalidOperationException($"Unsupported benchmark scenario '{scenario}'.")
        };

        var after = SnapshotMetrics();
        var delta = CreateDeltaArtifact(method, providerName, GetOperationsPerInvoke(scenario), before, after);

        switch (scenario)
        {
            case BenchmarkScenario.InsertEmployeesBatch:
                CleanupInsertedEmployees();
                break;
            case BenchmarkScenario.UpdateEmployeesBatch:
                CleanupUpdatedEmployees();
                break;
        }

        return delta;
    }

    public DataLinqMetricsSnapshot SnapshotMetrics() => DataLinqMetrics.Snapshot();

    public void Dispose()
    {
        databaseScope.Dispose();
    }

    private static BenchmarkTelemetryDeltaArtifact CreateDeltaArtifact(
        string method,
        string providerName,
        int operationsPerInvoke,
        DataLinqMetricsSnapshot before,
        DataLinqMetricsSnapshot after)
    {
        static double Normalize(long afterValue, long beforeValue, int operationsPerInvoke)
            => (afterValue - beforeValue) / (double)operationsPerInvoke;

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
            RelationLoadsPerOperation: relationLoads);
    }

    private static int GetOperationsPerInvoke(BenchmarkScenario scenario)
        => scenario switch
        {
            BenchmarkScenario.StartupPrimaryKeyFetch => 1,
            BenchmarkScenario.InsertEmployeesBatch => MutationBatchOperationCount,
            BenchmarkScenario.UpdateEmployeesBatch => MutationBatchOperationCount,
            BenchmarkScenario.ColdPrimaryKeyFetch => BatchOperationCount,
            BenchmarkScenario.WarmPrimaryKeyFetch => BatchOperationCount,
            BenchmarkScenario.ColdRelationTraversal => BatchOperationCount,
            BenchmarkScenario.WarmRelationTraversal => BatchOperationCount,
            _ => throw new InvalidOperationException($"Unsupported benchmark scenario '{scenario}'.")
        };

    private static InsertEmployeeTemplate CreateInsertTemplate(int index)
    {
        var month = (index % 12) + 1;
        var day = (index % 28) + 1;
        return new InsertEmployeeTemplate(
            new DateOnly(1970 + (index % 25), month, day),
            $"BenchFirst{index:0000}",
            $"BenchLast{index:0000}",
            index % 2 == 0 ? Employee.Employeegender.M : Employee.Employeegender.F,
            new DateOnly(2000 + (index % 20), month, day));
    }

    private static string GetScenarioDisplayName(BenchmarkScenario scenario)
        => scenario switch
        {
            BenchmarkScenario.StartupPrimaryKeyFetch => "Startup primary-key fetch",
            BenchmarkScenario.InsertEmployeesBatch => "Insert employees",
            BenchmarkScenario.UpdateEmployeesBatch => "Update employees",
            BenchmarkScenario.ColdPrimaryKeyFetch => "Cold primary-key fetch",
            BenchmarkScenario.WarmPrimaryKeyFetch => "Warm primary-key fetch",
            BenchmarkScenario.ColdRelationTraversal => "Cold relation traversal",
            BenchmarkScenario.WarmRelationTraversal => "Warm relation traversal",
            _ => throw new InvalidOperationException($"Unsupported benchmark scenario '{scenario}'.")
        };

    private readonly record struct InsertEmployeeTemplate(
        DateOnly BirthDate,
        string FirstName,
        string LastName,
        Employee.Employeegender Gender,
        DateOnly HireDate);
}
