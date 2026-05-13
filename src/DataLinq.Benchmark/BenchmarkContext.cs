using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Cache;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Metadata;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

internal sealed class BenchmarkContext : IDisposable
{
    private const int SeedEmployeeCount = 1000;
    internal const int BatchOperationCount = 1000;
    internal const int MutationBatchOperationCount = 1000;
    internal const int CrudWorkflowSmallOperationCount = 50;
    internal const int CrudWorkflowOperationCount = 300;

    private readonly EmployeesTestDatabase databaseScope;
    private readonly int[] sampleEmployeeNumbers;
    private readonly Employee[] sampleEmployees;
    private readonly int[] sampleEmployeeWithDepartmentNumbers;
    private readonly Employee[] sampleEmployeesWithDepartments;
    private readonly DataLinqKey[] sampleEmployeePrimaryKeys;
    private readonly RowData[] sampleEmployeeRowData;
    private readonly int[] sampleMutationEmployeeNumbers;
    private readonly int[] sampleCrudWorkflowEmployeeNumbers;
    private readonly string[] sampleEmployeeLastNames;
    private readonly Employee.Employeegender[] sampleEmployeeGenders;
    private readonly int[][] sampleInPredicateEmployeeNumbers;
    private readonly InsertEmployeeTemplate[] insertEmployeeTemplates;
    private readonly Dictionary<int, string> originalMutationLastNames;
    private readonly int startupEmployeeNumber;
    private readonly TestConnectionDefinition startupConnection;
    private readonly List<Employee> insertedEmployees = [];
    private RowCache scalarRowCacheProbe = new();

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
        sampleEmployees = Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(BatchOperationCount)
            .ToArray();
        sampleEmployeeLastNames = sampleEmployees
            .Select(x => x.last_name)
            .ToArray();
        sampleEmployeeGenders = sampleEmployees
            .Select(x => x.gender)
            .ToArray();
        sampleEmployeePrimaryKeys = sampleEmployees
            .Select(static x => KeyFactory.CreateKeyFromValue(x.emp_no!.Value))
            .ToArray();
        sampleEmployeeRowData = sampleEmployees
            .Select(static x => (RowData)x.GetRowData())
            .ToArray();
        sampleInPredicateEmployeeNumbers = Enumerable.Range(0, BatchOperationCount)
            .Select(index => new[]
            {
                sampleEmployeeNumbers[index],
                sampleEmployeeNumbers[(index + 1) % BatchOperationCount],
                sampleEmployeeNumbers[(index + 2) % BatchOperationCount]
            })
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
        sampleCrudWorkflowEmployeeNumbers = sampleMutationEmployeeNumbers
            .Take(CrudWorkflowOperationCount)
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

        if (sampleEmployeeLastNames.Length != BatchOperationCount || sampleEmployeeGenders.Length != BatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleEmployees.Length} query hot-path samples. Expected at least {BatchOperationCount}.");

        if (sampleEmployeePrimaryKeys.Length != BatchOperationCount || sampleEmployeeRowData.Length != BatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleEmployees.Length} row-cache samples. Expected at least {BatchOperationCount}.");

        if (sampleEmployeeWithDepartmentNumbers.Length != BatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleEmployeeWithDepartmentNumbers.Length} relation-traversal samples. Expected at least {BatchOperationCount}.");

        sampleEmployeesWithDepartments = sampleEmployeeWithDepartmentNumbers
            .Select(employeeNumber => Database.Query().Employees.Single(x => x.emp_no == employeeNumber))
            .ToArray();

        if (sampleMutationEmployeeNumbers.Length != MutationBatchOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleMutationEmployeeNumbers.Length} mutation samples. Expected at least {MutationBatchOperationCount}.");

        if (sampleCrudWorkflowEmployeeNumbers.Length != CrudWorkflowOperationCount)
            throw new InvalidOperationException(
                $"The deterministic employees benchmark dataset only yielded {sampleCrudWorkflowEmployeeNumbers.Length} CRUD workflow samples. Expected at least {CrudWorkflowOperationCount}.");

        using var startupScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            "benchmark-startup",
            EmployeesSeedMode.Bogus);

        startupConnection = startupScope.Connection;
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

    public int LoadEmployeesByGeneratedStaticGetBatch()
    {
        var checksum = 0;

        foreach (var employeeNumber in sampleEmployeeNumbers)
        {
            var employee = Employee.Get(employeeNumber, Database)
                ?? throw new InvalidOperationException($"Generated static Get returned no employee for primary key {employeeNumber}.");
            checksum += employee.emp_no!.Value;
        }

        return checksum;
    }

    public int LoadEmployeesByPrimaryKeyBatchWithCacheEstimate()
    {
        var checksum = LoadEmployeesByPrimaryKeyBatch();
        return AddEstimatedCacheBytesChecksum(checksum);
    }

    public int LoadEmployeesByNonPrimaryKeyEqualityBatch()
    {
        var checksum = 0;

        foreach (var lastName in sampleEmployeeLastNames)
        {
            var employee = Database.Query().Employees
                .Where(x => x.last_name == lastName)
                .OrderBy(x => x.emp_no)
                .First();
            checksum += employee.emp_no!.Value;
        }

        return checksum;
    }

    public int LoadEmployeesByInPredicateBatch()
    {
        var checksum = 0;

        foreach (var employeeNumbers in sampleInPredicateEmployeeNumbers)
        {
            var employees = Database.Query().Employees
                .Where(x => employeeNumbers.Contains(x.emp_no!.Value))
                .OrderBy(x => x.emp_no)
                .ToList();

            foreach (var employee in employees)
                checksum += employee.emp_no!.Value;
        }

        return checksum;
    }

    public int ExecuteScalarAnyBatch()
    {
        var checksum = 0;

        for (var i = 0; i < BatchOperationCount; i++)
        {
            var employeeNumber = sampleEmployeeNumbers[i];
            var gender = sampleEmployeeGenders[i];
            if (Database.Query().Employees.Any(x => x.emp_no == employeeNumber && x.gender == gender))
                checksum++;
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

    public int TraverseWarmDepartmentNamesBatch()
    {
        var checksum = 0;

        foreach (var employee in sampleEmployeesWithDepartments)
            checksum += employee.dept_emp.First().departments.Name.Length;

        return checksum;
    }

    public int TraverseWarmDepartmentNamesBatchWithCacheEstimate()
    {
        var checksum = TraverseWarmDepartmentNamesBatch();
        return AddEstimatedCacheBytesChecksum(checksum);
    }

    public int PreloadLargeRelationIndex()
    {
        var checksum = 0;
        var departments = Database.Query().Departments
            .OrderBy(x => x.DeptNo)
            .ToArray();

        foreach (var department in departments)
        {
            var employees = department.DepartmentEmployees.ToArray();
            checksum += employees.Length;
            if (employees.Length > 0)
                checksum += employees[0].emp_no;
        }

        return AddEstimatedCacheBytesChecksum(checksum);
    }

    public int CreateCompositeDynamicKeys()
    {
        var checksum = 0;

        for (var i = 0; i < sampleEmployeeNumbers.Length; i++)
        {
            var key = DataLinqKey.FromValues(
            [
                sampleEmployeeNumbers[i],
                sampleEmployeeLastNames[i],
                sampleEmployeeGenders[i]
            ]);

            checksum += key.GetHashCode();
        }

        return checksum;
    }

    public void ClearWarmRelationTraversalCache()
    {
        foreach (var employee in sampleEmployeesWithDepartments)
            employee.dept_emp.Clear();
    }

    public void ResetScalarRowCacheProbe()
    {
        scalarRowCacheProbe = new RowCache();
    }

    public int AddGetRemoveScalarRowCacheEntries()
    {
        var checksum = 0;

        for (var i = 0; i < sampleEmployeePrimaryKeys.Length; i++)
        {
            var key = sampleEmployeePrimaryKeys[i];
            var rowData = sampleEmployeeRowData[i];
            var employee = sampleEmployees[i];

            if (!scalarRowCacheProbe.TryAddRow(key, rowData, employee))
                throw new InvalidOperationException($"Scalar row-cache add failed for sample index {i}.");

            if (!scalarRowCacheProbe.TryGetValue(key, out var cached))
                throw new InvalidOperationException($"Scalar row-cache get failed for sample index {i}.");

            checksum += ((Employee)cached!).emp_no!.Value;

            if (!scalarRowCacheProbe.TryRemoveRow(key, out var rowsRemoved) || rowsRemoved != 1)
                throw new InvalidOperationException($"Scalar row-cache remove failed for sample index {i}.");
        }

        return checksum;
    }

    public void WarmEmployeeInvalidationCache()
    {
        ResetState(clearCache: true);
        _ = LoadEmployeesByPrimaryKeyBatch();
        DataLinqMetrics.Reset();
    }

    public void WarmDatabaseInvalidationCache()
    {
        ResetState(clearCache: true);
        _ = LoadEmployeesByPrimaryKeyBatch();
        _ = Database.Query().Departments.OrderBy(x => x.DeptNo).ToList();
        DataLinqMetrics.Reset();
    }

    public int InvalidateOneEmployeeRows()
    {
        var checksum = 0;

        foreach (var employeeNumber in sampleEmployeeNumbers)
        {
            var result = Database.Cache.Invalidate(CacheInvalidationEvent.Row(
                "employees",
                DataLinqKeyComponents.FromValue(employeeNumber),
                changedColumns: [nameof(Employee.first_name)],
                source: CacheInvalidationSources.Manual));

            checksum += result.RowsRemoved;
        }

        return checksum;
    }

    public int InvalidateManyEmployeeRows()
    {
        var keys = sampleEmployeeNumbers
            .Select(static employeeNumber => DataLinqKeyComponents.FromValue(employeeNumber))
            .ToArray();
        var result = Database.Cache.Invalidate(CacheInvalidationEvent.Rows(
            "employees",
            keys,
            changedColumns: [nameof(Employee.first_name)],
            source: CacheInvalidationSources.Manual));

        return result.RowsRemoved;
    }

    public int InvalidateEmployeeTable()
    {
        var result = Database.Cache.Invalidate(CacheInvalidationEvent.Table(
            "employees",
            source: CacheInvalidationSources.Manual));

        return result.RowsRemoved + result.TablesCleared;
    }

    public int InvalidateDatabase()
    {
        var result = Database.Cache.Invalidate(CacheInvalidationEvent.Database(
            source: CacheInvalidationSources.Manual));

        return result.RowsRemoved + result.TablesCleared;
    }

    public int LoadEmployeeByPrimaryKeyOnFreshScope()
    {
        using var startupDatabase = OpenStartupDatabase();

        var employee = startupDatabase.Query().Employees.Single(x => x.emp_no == startupEmployeeNumber);
        return employee.emp_no!.Value;
    }

    public int InitializeProviderAndMetadataOnFreshScope()
    {
        DatabaseDefinition.TryRemoveLoadedDatabase(typeof(EmployeesDb), out _);

        using var startupDatabase = OpenStartupDatabase();

        return startupDatabase.Provider.Metadata.TableModels.Length;
    }

    private Database<EmployeesDb> OpenStartupDatabase()
    {
        var creator = PluginHook.DatabaseProviders.Single(x => x.Key == startupConnection.DatabaseType).Value;
        return creator.GetDatabaseProvider<EmployeesDb>(
            startupConnection.ConnectionString,
            startupConnection.DataSourceName);
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

    public int RunCrudWorkflowSmall()
        => RunCrudWorkflow(CrudWorkflowSmallOperationCount);

    public int RunCrudWorkflowBatch()
        => RunCrudWorkflow(CrudWorkflowOperationCount);

    private int RunCrudWorkflow(int operationCount)
    {
        var checksum = 0;

        for (var i = 0; i < operationCount; i++)
        {
            var employeeNumber = sampleCrudWorkflowEmployeeNumbers[i];
            var template = insertEmployeeTemplates[i];

            using var transaction = Database.Transaction();

            var employee = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);
            checksum += employee.emp_no!.Value;
            checksum += employee.dept_emp.First().departments.Name.Length;

            var updated = transaction.Update(employee, mutable => mutable.last_name = $"Workflow-{employeeNumber}");
            checksum += updated.last_name.Length;

            var inserted = transaction.Insert(new MutableEmployee
            {
                birth_date = template.BirthDate,
                first_name = $"{template.FirstName}-Flow",
                last_name = $"{template.LastName}-Flow",
                gender = template.Gender,
                hire_date = template.HireDate
            });
            checksum += inserted.emp_no!.Value;

            var insertedReloaded = transaction.Query().Employees.Single(x => x.emp_no == inserted.emp_no);
            checksum += insertedReloaded.first_name.Length;

            transaction.Delete(insertedReloaded);
            transaction.Commit();
        }

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
        CleanupUpdatedEmployees(sampleMutationEmployeeNumbers);
    }

    public void CleanupCrudWorkflowEmployees()
    {
        CleanupUpdatedEmployees(sampleCrudWorkflowEmployeeNumbers);
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
                case BenchmarkScenario.WarmGeneratedStaticGet:
                    _ = LoadEmployeesByGeneratedStaticGetBatch();
                    DataLinqMetrics.Reset();
                    break;
                case BenchmarkScenario.WarmRelationTraversal:
                    ClearWarmRelationTraversalCache();
                    _ = TraverseWarmDepartmentNamesBatch();
                    DataLinqMetrics.Reset();
                    break;
                case BenchmarkScenario.WarmPrimaryKeyFetchWithCacheEstimate:
                    _ = LoadEmployeesByPrimaryKeyBatch();
                    DataLinqMetrics.Reset();
                    break;
                case BenchmarkScenario.WarmRelationTraversalWithCacheEstimate:
                    ClearWarmRelationTraversalCache();
                    _ = TraverseWarmDepartmentNamesBatch();
                    DataLinqMetrics.Reset();
                    break;
                case BenchmarkScenario.InvalidateOneEmployeeRow:
                case BenchmarkScenario.InvalidateManyEmployeeRows:
                case BenchmarkScenario.InvalidateEmployeeTable:
                    WarmEmployeeInvalidationCache();
                    break;
                case BenchmarkScenario.InvalidateDatabase:
                    WarmDatabaseInvalidationCache();
                    break;
                case BenchmarkScenario.CrudWorkflowSmall:
                case BenchmarkScenario.CrudWorkflowBatch:
                    CleanupCrudWorkflowEmployees();
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
            BenchmarkScenario.ProviderInitialization => InitializeProviderAndMetadataOnFreshScope(),
            BenchmarkScenario.StartupPrimaryKeyFetch => LoadEmployeeByPrimaryKeyOnFreshScope(),
            BenchmarkScenario.CrudWorkflowSmall => RunCrudWorkflowSmall(),
            BenchmarkScenario.CrudWorkflowBatch => RunCrudWorkflowBatch(),
            BenchmarkScenario.InsertEmployeesBatch => InsertEmployeesBatch(),
            BenchmarkScenario.UpdateEmployeesBatch => UpdateEmployeesBatch(),
            BenchmarkScenario.ColdPrimaryKeyFetch or BenchmarkScenario.WarmPrimaryKeyFetch => LoadEmployeesByPrimaryKeyBatch(),
            BenchmarkScenario.WarmGeneratedStaticGet => LoadEmployeesByGeneratedStaticGetBatch(),
            BenchmarkScenario.ColdRelationTraversal => TraverseDepartmentNamesBatch(),
            BenchmarkScenario.WarmRelationTraversal => TraverseWarmDepartmentNamesBatch(),
            BenchmarkScenario.ScalarRowCacheAddGetRemove => AddGetRemoveScalarRowCacheEntries(),
            BenchmarkScenario.InvalidateOneEmployeeRow => InvalidateOneEmployeeRows(),
            BenchmarkScenario.InvalidateManyEmployeeRows => InvalidateManyEmployeeRows(),
            BenchmarkScenario.InvalidateEmployeeTable => InvalidateEmployeeTable(),
            BenchmarkScenario.InvalidateDatabase => InvalidateDatabase(),
            BenchmarkScenario.RepeatedNonPrimaryKeyEqualityFetch => LoadEmployeesByNonPrimaryKeyEqualityBatch(),
            BenchmarkScenario.RepeatedInPredicateFetch => LoadEmployeesByInPredicateBatch(),
            BenchmarkScenario.RepeatedScalarAny => ExecuteScalarAnyBatch(),
            BenchmarkScenario.WarmPrimaryKeyFetchWithCacheEstimate => LoadEmployeesByPrimaryKeyBatchWithCacheEstimate(),
            BenchmarkScenario.WarmRelationTraversalWithCacheEstimate => TraverseWarmDepartmentNamesBatchWithCacheEstimate(),
            BenchmarkScenario.LargeRelationIndexPreload => PreloadLargeRelationIndex(),
            BenchmarkScenario.CompositeDynamicKeyWorkload => CreateCompositeDynamicKeys(),
            _ => throw new InvalidOperationException($"Unsupported benchmark scenario '{scenario}'.")
        };

        var after = SnapshotMetrics();
        var delta = CreateDeltaArtifact(method, providerName, GetOperationsPerInvoke(scenario), before, after);

        switch (scenario)
        {
            case BenchmarkScenario.CrudWorkflowSmall:
            case BenchmarkScenario.CrudWorkflowBatch:
                CleanupCrudWorkflowEmployees();
                break;
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
            BenchmarkScenario.ProviderInitialization => 1,
            BenchmarkScenario.StartupPrimaryKeyFetch => 1,
            BenchmarkScenario.CrudWorkflowSmall => CrudWorkflowSmallOperationCount,
            BenchmarkScenario.CrudWorkflowBatch => CrudWorkflowOperationCount,
            BenchmarkScenario.InsertEmployeesBatch => MutationBatchOperationCount,
            BenchmarkScenario.UpdateEmployeesBatch => MutationBatchOperationCount,
            BenchmarkScenario.ColdPrimaryKeyFetch => BatchOperationCount,
            BenchmarkScenario.WarmPrimaryKeyFetch => BatchOperationCount,
            BenchmarkScenario.WarmGeneratedStaticGet => BatchOperationCount,
            BenchmarkScenario.ColdRelationTraversal => BatchOperationCount,
            BenchmarkScenario.WarmRelationTraversal => BatchOperationCount,
            BenchmarkScenario.ScalarRowCacheAddGetRemove => BatchOperationCount,
            BenchmarkScenario.InvalidateOneEmployeeRow => BatchOperationCount,
            BenchmarkScenario.InvalidateManyEmployeeRows => 1,
            BenchmarkScenario.InvalidateEmployeeTable => 1,
            BenchmarkScenario.InvalidateDatabase => 1,
            BenchmarkScenario.RepeatedNonPrimaryKeyEqualityFetch => BatchOperationCount,
            BenchmarkScenario.RepeatedInPredicateFetch => BatchOperationCount,
            BenchmarkScenario.RepeatedScalarAny => BatchOperationCount,
            BenchmarkScenario.WarmPrimaryKeyFetchWithCacheEstimate => BatchOperationCount,
            BenchmarkScenario.WarmRelationTraversalWithCacheEstimate => BatchOperationCount,
            BenchmarkScenario.LargeRelationIndexPreload => 1,
            BenchmarkScenario.CompositeDynamicKeyWorkload => BatchOperationCount,
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

    private void CleanupUpdatedEmployees(IEnumerable<int> employeeNumbers)
    {
        using var transaction = Database.Transaction();

        foreach (var employeeNumber in employeeNumbers)
        {
            var employee = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);
            var originalLastName = originalMutationLastNames[employeeNumber];
            transaction.Update(employee, mutable => mutable.last_name = originalLastName);
        }

        transaction.Commit();
    }

    private static string GetScenarioDisplayName(BenchmarkScenario scenario)
        => scenario switch
        {
            BenchmarkScenario.ProviderInitialization => "Provider initialization",
            BenchmarkScenario.StartupPrimaryKeyFetch => "Startup primary-key fetch",
            BenchmarkScenario.CrudWorkflowSmall => "CRUD workflow small",
            BenchmarkScenario.CrudWorkflowBatch => "CRUD workflow batch",
            BenchmarkScenario.InsertEmployeesBatch => "Insert employees",
            BenchmarkScenario.UpdateEmployeesBatch => "Update employees",
            BenchmarkScenario.ColdPrimaryKeyFetch => "Cold primary-key fetch",
            BenchmarkScenario.WarmPrimaryKeyFetch => "Warm primary-key fetch",
            BenchmarkScenario.WarmGeneratedStaticGet => "Warm generated static Get",
            BenchmarkScenario.ColdRelationTraversal => "Cold relation traversal",
            BenchmarkScenario.WarmRelationTraversal => "Warm relation traversal",
            BenchmarkScenario.ScalarRowCacheAddGetRemove => "Scalar row-cache add/get/remove",
            BenchmarkScenario.InvalidateOneEmployeeRow => "Invalidate one employee row",
            BenchmarkScenario.InvalidateManyEmployeeRows => "Invalidate many employee rows",
            BenchmarkScenario.InvalidateEmployeeTable => "Invalidate employee table",
            BenchmarkScenario.InvalidateDatabase => "Invalidate database",
            BenchmarkScenario.RepeatedNonPrimaryKeyEqualityFetch => "Repeated non-PK equality fetch",
            BenchmarkScenario.RepeatedInPredicateFetch => "Repeated IN predicate fetch",
            BenchmarkScenario.RepeatedScalarAny => "Repeated scalar Any",
            BenchmarkScenario.WarmPrimaryKeyFetchWithCacheEstimate => "Warm PK with cache estimate",
            BenchmarkScenario.WarmRelationTraversalWithCacheEstimate => "Warm relation with cache estimate",
            BenchmarkScenario.LargeRelationIndexPreload => "Large relation index preload",
            BenchmarkScenario.CompositeDynamicKeyWorkload => "Composite dynamic key workload",
            _ => throw new InvalidOperationException($"Unsupported benchmark scenario '{scenario}'.")
        };

    private static int AddEstimatedCacheBytesChecksum(int checksum)
    {
        var estimatedBytes = DataLinqMetrics.Snapshot().Occupancy.EstimatedCacheBytes;
        return unchecked(checksum + (int)(estimatedBytes & 0x7fffffff));
    }

    private readonly record struct InsertEmployeeTemplate(
        DateOnly BirthDate,
        string FirstName,
        string LastName,
        Employee.Employeegender Gender,
        DateOnly HireDate);
}
