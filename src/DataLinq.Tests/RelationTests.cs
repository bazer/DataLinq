using System.Linq;
using DataLinq.Tests.Models;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests;

public class RelationTests : BaseTests
{
    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void LazyLoadSingleValue(Database<EmployeesDb> employeesDb)
    {
        var manager = employeesDb.Query().Managers.Single(x => x.dept_fk == "d005" && x.emp_no == 576);

        Assert.NotNull(manager.Department);
        Assert.Equal("d005", manager.Department.DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void LazyLoadList(Database<EmployeesDb> employeesDb)
    {
        var department = Department.Get("d005", employeesDb);

        Assert.NotNull(department);
        Assert.NotNull(department.Managers);
        Assert.NotEmpty(department.Managers);
        Assert.Equal(4, department.Managers.Count());
        Assert.Equal("d005", department.Managers.First().Department.DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void EmptyList(Database<EmployeesDb> employeesDb)
    {
        var employee = employeesDb.Query().Employees.Single(x => x.emp_no == 1000);

        Assert.NotNull(employee.dept_manager);
        Assert.Empty(employee.dept_manager);
    }
}