using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Cache;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.TUnit;

public class EmployeesCacheTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_DoesNotAccumulateDuplicateDeptEmployeeRowsAcrossRepeatedQueries(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Cache_DoesNotAccumulateDuplicateDeptEmployeeRowsAcrossRepeatedQueries),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var setup = PrepareScenario(employeesDatabase);
        var cache = GetDeptEmployeeCache(employeesDatabase);

        for (var i = 0; i < 5; i++)
        {
            var employee = employeesDatabase.Query().Employees.Single(x => x.emp_no == setup.EmployeeNumber);
            var primaryDepartment = employeesDatabase.Query().Departments.Single(x => x.DeptNo == setup.PrimaryDepartmentNumber);
            var secondaryDepartment = employeesDatabase.Query().Departments.Single(x => x.DeptNo == setup.SecondaryDepartmentNumber);

            await Assert.That(employee.dept_emp.Count()).IsEqualTo(setup.EmployeeDepartmentCount);
            await Assert.That(primaryDepartment.DepartmentEmployees.Count()).IsEqualTo(setup.PrimaryDepartmentCount);
            await Assert.That(secondaryDepartment.DepartmentEmployees.Count()).IsEqualTo(setup.SecondaryDepartmentCount);
            await Assert.That(cache.RowCount).IsEqualTo(setup.PrimaryDepartmentCount + setup.SecondaryDepartmentCount);
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_RemoveRowsInsertedBeforeTick_EvictsRowsInLoadOrder(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Cache_RemoveRowsInsertedBeforeTick_EvictsRowsInLoadOrder),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var setup = PrepareScenario(employeesDatabase);
        var cache = GetDeptEmployeeCache(employeesDatabase);

        cache.ClearRows();

        var employee = employeesDatabase.Query().Employees.Single(x => x.emp_no == setup.EmployeeNumber);
        await Assert.That(employee.dept_emp.Count()).IsEqualTo(setup.EmployeeDepartmentCount);

        var tickAfterEmployee = DateTime.Now.Ticks;

        var primaryDepartment = employeesDatabase.Query().Departments.Single(x => x.DeptNo == setup.PrimaryDepartmentNumber);
        await Assert.That(primaryDepartment.DepartmentEmployees.Count()).IsEqualTo(setup.PrimaryDepartmentCount);

        var tickAfterPrimaryDepartment = DateTime.Now.Ticks;

        var secondaryDepartment = employeesDatabase.Query().Departments.Single(x => x.DeptNo == setup.SecondaryDepartmentNumber);
        await Assert.That(secondaryDepartment.DepartmentEmployees.Count()).IsEqualTo(setup.SecondaryDepartmentCount);
        await Assert.That(cache.RowCount).IsEqualTo(setup.PrimaryDepartmentCount + setup.SecondaryDepartmentCount);

        var firstRemoval = employeesDatabase.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(tickAfterEmployee)
            .ToDictionary(x => x.table.Table.DbName, x => x.numRows, StringComparer.Ordinal);

        await Assert.That(firstRemoval.Count).IsEqualTo(2);
        await Assert.That(firstRemoval["employees"]).IsEqualTo(1);
        await Assert.That(firstRemoval["dept-emp"]).IsEqualTo(1);
        await Assert.That(cache.RowCount).IsEqualTo(setup.PrimaryDepartmentCount + setup.SecondaryDepartmentCount - 1);

        var secondRemoval = employeesDatabase.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(tickAfterPrimaryDepartment)
            .ToDictionary(x => x.table.Table.DbName, x => x.numRows, StringComparer.Ordinal);

        await Assert.That(secondRemoval.Count).IsEqualTo(2);
        await Assert.That(secondRemoval["departments"]).IsEqualTo(1);
        await Assert.That(secondRemoval["dept-emp"]).IsEqualTo(setup.PrimaryDepartmentCount - 1);
        await Assert.That(cache.RowCount).IsEqualTo(setup.SecondaryDepartmentCount);

        var thirdRemoval = employeesDatabase.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
            .ToDictionary(x => x.table.Table.DbName, x => x.numRows, StringComparer.Ordinal);

        await Assert.That(thirdRemoval.Count).IsEqualTo(2);
        await Assert.That(thirdRemoval["departments"]).IsEqualTo(1);
        await Assert.That(thirdRemoval["dept-emp"]).IsEqualTo(setup.SecondaryDepartmentCount);
        await Assert.That(cache.RowCount).IsEqualTo(0);

        var fourthRemoval = employeesDatabase.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
            .ToList();

        await Assert.That(fourthRemoval.Count).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_RemoveRowsByRowLimit_KeepsConfiguredRowCount(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Cache_RemoveRowsByRowLimit_KeepsConfiguredRowCount),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var setup = PrepareScenario(employeesDatabase);
        var cache = GetDeptEmployeeCache(employeesDatabase);

        cache.ClearRows();

        var primaryDepartment = employeesDatabase.Query().Departments.Single(x => x.DeptNo == setup.PrimaryDepartmentNumber);
        await Assert.That(primaryDepartment.DepartmentEmployees.Count()).IsEqualTo(setup.PrimaryDepartmentCount);
        await Assert.That(cache.RowCount).IsEqualTo(setup.PrimaryDepartmentCount);

        var keepRows = Math.Max(1, setup.PrimaryDepartmentCount / 2);
        var removedTables = employeesDatabase.Provider.State.Cache
            .RemoveRowsByLimit(CacheLimitType.Rows, keepRows)
            .ToList();

        await Assert.That(removedTables.Count).IsEqualTo(1);
        await Assert.That(removedTables[0].table.Table.DbName).IsEqualTo("dept-emp");
        await Assert.That(removedTables[0].numRows).IsEqualTo(setup.PrimaryDepartmentCount - keepRows);
        await Assert.That(cache.RowCount).IsEqualTo(keepRows);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Cache_RemoveRowsByByteLimit_CapsTotalBytes(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Cache_RemoveRowsByByteLimit_CapsTotalBytes),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var setup = PrepareScenario(employeesDatabase);
        var cache = GetDeptEmployeeCache(employeesDatabase);

        cache.ClearRows();
        await Assert.That(cache.TotalBytes).IsEqualTo(0);

        var primaryDepartment = employeesDatabase.Query().Departments.Single(x => x.DeptNo == setup.PrimaryDepartmentNumber);
        await Assert.That(primaryDepartment.DepartmentEmployees.Count()).IsEqualTo(setup.PrimaryDepartmentCount);
        await Assert.That(cache.TotalBytes).IsGreaterThan(0);

        var targetBytes = Math.Max(1, cache.TotalBytes / 2);
        var removedTables = employeesDatabase.Provider.State.Cache
            .RemoveRowsByLimit(CacheLimitType.Bytes, targetBytes)
            .ToList();

        await Assert.That(removedTables.Count).IsEqualTo(1);
        await Assert.That(removedTables[0].table.Table.DbName).IsEqualTo("dept-emp");
        await Assert.That(removedTables[0].numRows).IsGreaterThan(0);
        await Assert.That(cache.TotalBytes).IsLessThanOrEqualTo(targetBytes);
    }

    private static CacheScenario PrepareScenario(Database<EmployeesDb> employeesDatabase)
    {
        employeesDatabase.Provider.State.ClearCache();

        var departmentCounts = employeesDatabase.Query().Departments
            .ToList()
            .Select(x => new { Department = x, Count = x.DepartmentEmployees.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Department.DeptNo, StringComparer.Ordinal)
            .ToList();

        var primaryDepartment = departmentCounts.First(x => x.Count > 1);
        var secondaryDepartment = departmentCounts.First(x => x.Department.DeptNo != primaryDepartment.Department.DeptNo && x.Count > 0);
        var employeeNumber = primaryDepartment.Department.DepartmentEmployees.First().emp_no;
        var employeeDepartmentCount = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber).dept_emp.Count();

        employeesDatabase.Provider.State.ClearCache();

        return new CacheScenario(
            employeeNumber,
            employeeDepartmentCount,
            primaryDepartment.Department.DeptNo,
            primaryDepartment.Count,
            secondaryDepartment.Department.DeptNo,
            secondaryDepartment.Count);
    }

    private static TableCache GetDeptEmployeeCache(Database<EmployeesDb> employeesDatabase)
    {
        var table = employeesDatabase.Provider.Metadata.TableModels
            .Single(x => x.Table.DbName == "dept-emp")
            .Table;

        return employeesDatabase.Provider.GetTableCache(table);
    }

    private sealed record CacheScenario(
        int EmployeeNumber,
        int EmployeeDepartmentCount,
        string PrimaryDepartmentNumber,
        int PrimaryDepartmentCount,
        string SecondaryDepartmentNumber,
        int SecondaryDepartmentCount);
}
