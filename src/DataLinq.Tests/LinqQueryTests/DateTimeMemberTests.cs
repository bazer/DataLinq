using System;
using System.Linq;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests.LinqQueryTests;

public class DateTimeMemberTests : BaseTests
{
    private (DateOnly date, TimeOnly time, DateTime dateTime) GetTestDateAndTime(Database<EmployeesDb> employeesDb)
    {
        // Use an employee that is guaranteed to exist and have values
        var firstEmployee = employeesDb.Query().Employees.OrderBy(x => x.emp_no).First();
        var firstDeptEmp = employeesDb.Query().DepartmentEmployees.OrderBy(x => x.from_date).First();

        return (firstDeptEmp.from_date, firstEmployee.last_login.Value, firstEmployee.created_at.Value);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateOnly_Year(Database<EmployeesDb> employeesDb)
    {
        var (testDate, _, _) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().DepartmentEmployees.ToList().Where(x => x.from_date.Year == testDate.Year).ToList();
        var result = employeesDb.Query().DepartmentEmployees.Where(x => x.from_date.Year == testDate.Year).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateOnly_Month(Database<EmployeesDb> employeesDb)
    {
        var (testDate, _, _) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().DepartmentEmployees.ToList().Where(x => x.from_date.Month == testDate.Month).ToList();
        var result = employeesDb.Query().DepartmentEmployees.Where(x => x.from_date.Month == testDate.Month).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateOnly_Day(Database<EmployeesDb> employeesDb)
    {
        var (testDate, _, _) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().DepartmentEmployees.ToList().Where(x => x.from_date.Day == testDate.Day).ToList();
        var result = employeesDb.Query().DepartmentEmployees.Where(x => x.from_date.Day == testDate.Day).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateOnly_DayOfYear(Database<EmployeesDb> employeesDb)
    {
        var (testDate, _, _) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().DepartmentEmployees.ToList().Where(x => x.from_date.DayOfYear == testDate.DayOfYear).ToList();
        var result = employeesDb.Query().DepartmentEmployees.Where(x => x.from_date.DayOfYear == testDate.DayOfYear).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateOnly_DayOfWeek(Database<EmployeesDb> employeesDb)
    {
        var (testDate, _, _) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().DepartmentEmployees.ToList().Where(x => x.from_date.DayOfWeek == testDate.DayOfWeek).ToList();
        var result = employeesDb.Query().DepartmentEmployees.Where(x => x.from_date.DayOfWeek == testDate.DayOfWeek).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_TimeOnly_Hour(Database<EmployeesDb> employeesDb)
    {
        var (_, testTime, _) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(x => x.last_login.HasValue && x.last_login.Value.Hour == testTime.Hour).ToList();
        var result = employeesDb.Query().Employees.Where(x => x.last_login.Value.Hour == testTime.Hour).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateTime_Minute(Database<EmployeesDb> employeesDb)
    {
        var (_, _, testDateTime) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(x => x.created_at.HasValue && x.created_at.Value.Minute == testDateTime.Minute).ToList();
        var result = employeesDb.Query().Employees.Where(x => x.created_at.Value.Minute == testDateTime.Minute).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateTime_Second(Database<EmployeesDb> employeesDb)
    {
        var (_, _, testDateTime) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(x => x.created_at.HasValue && x.created_at.Value.Second == testDateTime.Second).ToList();
        var result = employeesDb.Query().Employees.Where(x => x.created_at.Value.Second == testDateTime.Second).ToList();
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_DateTime_Millisecond(Database<EmployeesDb> employeesDb)
    {
        var (_, _, testDateTime) = GetTestDateAndTime(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(x => x.created_at.HasValue && x.created_at.Value.Millisecond == testDateTime.Millisecond).ToList();
        var result = employeesDb.Query().Employees.Where(x => x.created_at.Value.Millisecond == testDateTime.Millisecond).ToList();
        Assert.Equal(expected.Count, result.Count);
    }
}