using System;
using System.Linq;
using DataLinq.Diagnostics;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

internal sealed class BenchmarkContext : IDisposable
{
    internal const int BatchOperationCount = 300;

    private readonly EmployeesTestDatabase databaseScope;
    private readonly int[] sampleEmployeeNumbers;
    private readonly int[] sampleEmployeeWithDepartmentNumbers;

    public BenchmarkContext(TestProviderDescriptor provider)
    {
        databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            "benchmark",
            EmployeesSeedMode.Bogus);

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

    public DataLinqMetricsSnapshot SnapshotMetrics() => DataLinqMetrics.Snapshot();

    public void Dispose()
    {
        databaseScope.Dispose();
    }
}
