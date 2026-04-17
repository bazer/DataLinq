using System;
using System.Linq;
using DataLinq.Diagnostics;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Benchmark;

internal sealed class BenchmarkContext : IDisposable
{
    private readonly EmployeesTestDatabase databaseScope;

    public BenchmarkContext(TestProviderDescriptor provider)
    {
        databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            "benchmark",
            EmployeesSeedMode.Bogus);

        Database = databaseScope.Database;
        SampleEmployeeNumber = Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no!.Value)
            .First();
        SampleEmployeeWithDepartmentNumber = Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .ToList()
            .First(x => x.dept_emp.Count > 0)
            .emp_no!.Value;
    }

    public Database<EmployeesDb> Database { get; }
    public int SampleEmployeeNumber { get; }
    public int SampleEmployeeWithDepartmentNumber { get; }

    public void ResetState(bool clearCache)
    {
        if (clearCache)
            Database.Provider.State.ClearCache();

        DataLinqMetrics.Reset();
    }

    public Employee LoadEmployeeByPrimaryKey()
        => Database.Query().Employees.Single(x => x.emp_no == SampleEmployeeNumber);

    public string TraverseDepartmentName()
    {
        var employee = Database.Query().Employees.Single(x => x.emp_no == SampleEmployeeWithDepartmentNumber);
        return employee.dept_emp.First().departments.Name;
    }

    public DataLinqMetricsSnapshot SnapshotMetrics() => DataLinqMetrics.Snapshot();

    public void Dispose()
    {
        databaseScope.Dispose();
    }
}
