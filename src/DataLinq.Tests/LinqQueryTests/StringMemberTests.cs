using System.Collections.Generic;
using System.Linq;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests.LinqQueryTests;

public class StringMemberTests : BaseTests
{
    readonly List<int> empIds = [2010, 2011, 2012];

    private (Employee employee, Department department) SetupStringTestData(Database<EmployeesDb> employeesDb)
    {
        employeesDb.Commit(transaction =>
        {
            // Ensure a clean slate by deleting any existing employees with these IDs
            foreach (var emp in transaction.Query().Employees.Where(x => empIds.Contains(x.emp_no!.Value)).ToList())
            {
                transaction.Delete(emp);
            }

            // Insert the test data
            transaction.Insert(new MutableEmployee { emp_no = 2010, first_name = " John ", last_name = " Doe ", birth_date = new(1990, 1, 1), hire_date = new(2020, 1, 1), gender = Employee.Employeegender.M, IsDeleted = true });
            transaction.Insert(new MutableEmployee { emp_no = 2011, first_name = "", last_name = "Devenshoe", birth_date = new(1990, 1, 1), hire_date = new(2020, 1, 1), gender = Employee.Employeegender.F, IsDeleted = false });
            transaction.Insert(new MutableEmployee { emp_no = 2012, first_name = "   ", last_name = "Noname", birth_date = new(1990, 1, 1), hire_date = new(2020, 1, 1), gender = Employee.Employeegender.M, IsDeleted = null });
        });

        // Use a known employee and department for predictable test data
        var employee = employeesDb.Query().Employees.First(x => x.emp_no == 2010);
        var department = employeesDb.Query().Departments.First(x => x.DeptNo == "d005");

        return (employee, department);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_ToUpper(Database<EmployeesDb> employeesDb)
    {
        var (_, department) = SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Departments.ToList().Where(d => d.Name.ToUpper() == department.Name.ToUpper()).ToList();
        var result = employeesDb.Query().Departments.Where(d => d.Name.ToUpper() == department.Name.ToUpper()).ToList();

        Assert.NotEmpty(result);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_ToLower(Database<EmployeesDb> employeesDb)
    {
        var (_, department) = SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Departments.ToList().Where(d => d.Name.ToLower() == department.Name.ToLower()).ToList();
        var result = employeesDb.Query().Departments.Where(d => d.Name.ToLower() == department.Name.ToLower()).ToList();

        Assert.NotEmpty(result);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_Trim(Database<EmployeesDb> employeesDb)
    {
        var (employee, _) = SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Single(e => empIds.Contains(e.emp_no!.Value) && e.first_name.Trim() == "John");
        var result = employeesDb.Query().Employees.Single(e => empIds.Contains(e.emp_no!.Value) && e.first_name.Trim() == "John");

        Assert.NotNull(result);
        Assert.Equal(expected, result);
        Assert.Equal(employee.emp_no, result.emp_no);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_Substring(Database<EmployeesDb> employeesDb)
    {
        SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(e => empIds.Contains(e.emp_no!.Value) && e.last_name.Substring(1, 4) == "even").ToList();
        var result = employeesDb.Query().Employees.Where(e => empIds.Contains(e.emp_no!.Value) && e.last_name.Substring(1, 4) == "even").ToList();

        Assert.NotEmpty(result);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_IsNullOrEmpty_False(Database<EmployeesDb> employeesDb)
    {
        SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(e => empIds.Contains(e.emp_no!.Value) && !string.IsNullOrEmpty(e.first_name)).ToList();
        var result = employeesDb.Query().Employees.Where(e => empIds.Contains(e.emp_no!.Value) && !string.IsNullOrEmpty(e.first_name)).ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.DoesNotContain(result, x => x.emp_no == 2011); // The empty string one
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_IsNullOrEmpty_True(Database<EmployeesDb> employeesDb)
    {
        SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(e => empIds.Contains(e.emp_no!.Value) && string.IsNullOrEmpty(e.first_name)).ToList();
        var result = employeesDb.Query().Employees.Where(e => empIds.Contains(e.emp_no!.Value) && string.IsNullOrEmpty(e.first_name)).ToList();

        Assert.Single(result);
        Assert.Equal(2011, result.Single().emp_no);
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_IsNullOrWhiteSpace_False(Database<EmployeesDb> employeesDb)
    {
        SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(e => empIds.Contains(e.emp_no!.Value) && !string.IsNullOrWhiteSpace(e.first_name)).ToList();
        var result = employeesDb.Query().Employees.Where(e => empIds.Contains(e.emp_no!.Value) && !string.IsNullOrWhiteSpace(e.first_name)).ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.DoesNotContain(result, x => x.emp_no == 2011); // The empty string one
        Assert.DoesNotContain(result, x => x.emp_no == 2012); // The whitespace one
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_IsNullOrWhiteSpace_True(Database<EmployeesDb> employeesDb)
    {
        SetupStringTestData(employeesDb);
        var expected = employeesDb.Query().Employees.ToList().Where(e => empIds.Contains(e.emp_no!.Value) && string.IsNullOrWhiteSpace(e.first_name)).ToList();
        var result = employeesDb.Query().Employees.Where(e => empIds.Contains(e.emp_no!.Value) && string.IsNullOrWhiteSpace(e.first_name)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.emp_no == 2011);
        Assert.Contains(result, x => x.emp_no == 2012);
        Assert.Equal(expected.Count, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_Length(Database<EmployeesDb> employeesDb)
    {
        SetupStringTestData(employeesDb);
        // We test against 'first_name', where one employee is " John " (length 6).

        // In-memory LINQ-to-Objects for comparison
        var expected = employeesDb.Query().Employees.ToList()
            .Where(e => empIds.Contains(e.emp_no!.Value) && e.first_name.Length == 6)
            .ToList();

        // DataLinq query
        var result = employeesDb.Query().Employees
            .Where(e => empIds.Contains(e.emp_no!.Value) && e.first_name.Length == 6)
            .ToList();

        Assert.Single(result);
        Assert.Equal(2010, result.Single().emp_no);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_String_Trim_Length(Database<EmployeesDb> employeesDb)
    {
        SetupStringTestData(employeesDb);
        // We test against 'first_name', where one employee is " John " which becomes "John" (length 4) after trimming.

        // In-memory LINQ-to-Objects for comparison
        var expected = employeesDb.Query().Employees.ToList()
            .Where(e => empIds.Contains(e.emp_no!.Value) && e.first_name.Trim().Length == 4)
            .ToList();

        // DataLinq query
        var result = employeesDb.Query().Employees
            .Where(e => empIds.Contains(e.emp_no!.Value) && e.first_name.Trim().Length == 4)
            .ToList();

        Assert.Single(result);
        Assert.Equal(2010, result.Single().emp_no);
        Assert.Equal(expected, result);
    }
}