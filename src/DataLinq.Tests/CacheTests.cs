using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests;

public class SharedSetup
{
    public Employee TestEmployee { get; }
    public int TestEmployeeDeptCount { get; }
    public int Dept2Count { get; }
    public int Dept6Count { get; }
    public int Dept7Count { get; }


    public SharedSetup(Database<EmployeesDb> employeesDb)
    {
        TestEmployee = employeesDb.Query().Employees.Single(x => x.emp_no == 1010);
        TestEmployeeDeptCount = TestEmployee.dept_emp.Count();
        Dept2Count = employeesDb.Query().Departments.Single(x => x.DeptNo == "d002").DepartmentEmployees.Count();
        Dept6Count = employeesDb.Query().Departments.Single(x => x.DeptNo == "d006").DepartmentEmployees.Count();
        Dept7Count = employeesDb.Query().Departments.Single(x => x.DeptNo == "d007").DepartmentEmployees.Count();

        employeesDb.Provider.State.ClearCache();
    }
}

public class CacheTests
{
    public static DatabaseFixture fixture;

    static CacheTests()
    {
        fixture = new DatabaseFixture();
    }

    public static IEnumerable<object[]> GetEmployees()
    {
        foreach (var db in fixture.AllEmployeesDb)
            yield return new object[] { db };
    }

    public CacheTests()
    {
        foreach (var employeesDb in fixture.AllEmployeesDb)
        {
            employeesDb.Provider.State.ClearCache();
        }
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void CheckRowDuplicates(Database<EmployeesDb> employeesDb)
    {
        var setup = new SharedSetup(employeesDb);

        for (var i = 0; i < 10; i++)
        {
            var employee = employeesDb.Query().Employees.Single(x => x.emp_no == setup.TestEmployee.emp_no);

            Assert.NotNull(employee);
            Assert.NotEmpty(employee.dept_emp);
            Assert.Equal(setup.TestEmployeeDeptCount, employee.dept_emp.Count());

            var dept = employeesDb.Query().Departments.Single(x => x.DeptNo == "d002");
            Assert.NotNull(dept);
            Assert.NotEmpty(dept.DepartmentEmployees);
            Assert.True(dept.DepartmentEmployees.Count() > 0);
            Assert.Equal(setup.Dept2Count, dept.DepartmentEmployees.Count());

            var dept6 = employeesDb.Query().Departments.Single(x => x.DeptNo == "d006");
            Assert.NotNull(dept6);
            Assert.NotEmpty(dept6.DepartmentEmployees);
            Assert.Equal(setup.Dept6Count, dept6.DepartmentEmployees.Count());

            var table = employeesDb.Provider.Metadata
                .TableModels.Single(x => x.Table.DbName == "dept-emp").Table;

            Assert.Equal(setup.Dept2Count + setup.Dept6Count + 2 - 1, employeesDb.Provider.GetTableCache(table).RowCount);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TimeLimit(Database<EmployeesDb> employeesDb)
    {
        var setup = new SharedSetup(employeesDb);

        var table = employeesDb.Provider.Metadata
                .TableModels.Single(x => x.Table.DbName == "dept-emp").Table;

        var cache = employeesDb.Provider.GetTableCache(table);
        cache.ClearRows();

        var employee = employeesDb.Query().Employees.Single(x => x.emp_no == setup.TestEmployee.emp_no);
        Assert.Equal(setup.TestEmployeeDeptCount, employee.dept_emp.Count());

        var ticks = DateTime.Now.Ticks;

        var dept = employeesDb.Query().Departments.Single(x => x.DeptNo == "d002");
        Assert.Equal(setup.Dept2Count, dept.DepartmentEmployees.Count());

        var ticks2 = DateTime.Now.Ticks;

        var dept6 = employeesDb.Query().Departments.Single(x => x.DeptNo == "d006");
        Assert.Equal(setup.Dept6Count, dept6.DepartmentEmployees.Count());
        Assert.Equal(setup.Dept2Count + setup.Dept6Count + 2 - 1, cache.RowCount);

        var tables = employeesDb.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(ticks)
            .OrderBy(x => x.numRows)
            .ToList();

        Assert.Equal(2, tables.Count);
        Assert.Equal("employees", tables[1].table.Table.DbName);
        Assert.Equal(1, tables[1].numRows);
        Assert.Equal("dept-emp", tables[0].table.Table.DbName);
        Assert.Equal(1, tables[0].numRows);
        Assert.Equal(setup.Dept2Count + setup.Dept6Count, cache.RowCount);

        tables = employeesDb.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(ticks2)
            .OrderBy(x => x.numRows)
            .ToList();

        Assert.Equal(2, tables.Count);
        Assert.Equal("departments", tables[0].table.Table.DbName);
        Assert.Equal(1, tables[0].numRows);
        Assert.Equal("dept-emp", tables[1].table.Table.DbName);
        Assert.Equal(setup.Dept2Count, tables[1].numRows);
        Assert.Equal(setup.Dept6Count, cache.RowCount);

        tables = employeesDb.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
            .OrderBy(x => x.numRows)
            .ToList();

        Assert.Equal(2, tables.Count);
        Assert.Equal("departments", tables[0].table.Table.DbName);
        Assert.Equal(1, tables[0].numRows);
        Assert.Equal("dept-emp", tables[1].table.Table.DbName);
        Assert.Equal(setup.Dept6Count, tables[1].numRows);
        Assert.Equal(0, cache.RowCount);

        tables = employeesDb.Provider.State.Cache
            .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
            .OrderBy(x => x.numRows)
            .ToList();

        Assert.Empty(tables);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void RowLimit(Database<EmployeesDb> employeesDb)
    {
        var setup = new SharedSetup(employeesDb);

        var table = employeesDb.Provider.Metadata
                .TableModels.Single(x => x.Table.DbName == "dept-emp").Table;

        var cache = employeesDb.Provider.GetTableCache(table);
        cache.ClearRows();

        var dept = employeesDb.Query().Departments.Single(x => x.DeptNo == "d007");
        Assert.Equal(setup.Dept7Count, dept.DepartmentEmployees.Count());
        Assert.Equal(setup.Dept7Count, cache.RowCount);

        var tables = employeesDb.Provider.State.Cache
            .RemoveRowsByLimit(CacheLimitType.Rows, 100)
            .OrderBy(x => x.numRows)
            .ToList();

        Assert.Single(tables);
        Assert.Equal("dept-emp", tables[0].table.Table.DbName);
        Assert.Equal(setup.Dept7Count - 100, tables[0].numRows);
        Assert.Equal(100, cache.RowCount);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SizeLimit(Database<EmployeesDb> employeesDb)
    {
        var setup = new SharedSetup(employeesDb);

        var table = employeesDb.Provider.Metadata
                .TableModels.Single(x => x.Table.DbName == "dept-emp").Table;

        var cache = employeesDb.Provider.GetTableCache(table);
        cache.ClearRows();

        var dept = employeesDb.Query().Departments.Single(x => x.DeptNo == "d007");
        Assert.Equal(setup.Dept7Count, dept.DepartmentEmployees.Count());
        Assert.True(cache.TotalBytes > 0);

        var tables = employeesDb.Provider.State.Cache
            .RemoveRowsByLimit(CacheLimitType.Kilobytes, 10)
            .OrderBy(x => x.numRows)
            .ToList();

        Assert.Single(tables);
        Assert.Equal("dept-emp", tables[0].table.Table.DbName);
        Assert.True(tables[0].numRows > 0);
        Assert.True(cache.TotalBytes <= 1024 * 1024);
    }
}