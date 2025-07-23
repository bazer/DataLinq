using System.Collections.Generic;
using System.Linq;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests.LinqQueryTests;

public class NullableBooleanTests : BaseTests
{
    // This helper method sets up the database with a known state for each test.
    // emp_no 100 -> IsDeleted = true
    // emp_no 200 -> IsDeleted = false
    // emp_no 300 -> IsDeleted = null
    private List<int> SetupNullableBoolTestData(Database<EmployeesDb> employeesDb)
    {
        var empIds = new List<int> { 2001, 2002, 2003 };

        // Use a single transaction for setup
        employeesDb.Commit(transaction =>
        {
            // Ensure a clean slate by deleting any existing employees with these IDs
            foreach (var emp in transaction.Query().Employees.Where(x => empIds.Contains(x.emp_no!.Value)).ToList())
            {
                transaction.Delete(emp);
            }

            // Insert the test data
            transaction.Insert(new MutableEmployee { emp_no = 2001, first_name = "Test", last_name = "True", birth_date = new(1990, 1, 1), hire_date = new(2020, 1, 1), gender = Employee.Employeegender.M, IsDeleted = true });
            transaction.Insert(new MutableEmployee { emp_no = 2002, first_name = "Test", last_name = "False", birth_date = new(1990, 1, 1), hire_date = new(2020, 1, 1), gender = Employee.Employeegender.F, IsDeleted = false });
            transaction.Insert(new MutableEmployee { emp_no = 2003, first_name = "Test", last_name = "Null", birth_date = new(1990, 1, 1), hire_date = new(2020, 1, 1), gender = Employee.Employeegender.M, IsDeleted = null });
        });

        return empIds;
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_NullableBool_Equals_True(Database<EmployeesDb> employeesDb)
    {
        var empIds = SetupNullableBoolTestData(employeesDb);

        var expected = employeesDb.Query().Employees.ToList()
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted == true)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted == true)
            .ToList();

        Assert.Single(result);
        Assert.Equal(2001, result.Single().emp_no);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_NullableBool_NotEquals_True(Database<EmployeesDb> employeesDb)
    {
        var empIds = SetupNullableBoolTestData(employeesDb);

        var expected = employeesDb.Query().Employees.ToList()
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted != true)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted != true)
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.emp_no == 2002); // Should contain the 'false' case
        Assert.Contains(result, e => e.emp_no == 2003); // Should contain the 'null' case
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_NullableBool_Equals_False(Database<EmployeesDb> employeesDb)
    {
        var empIds = SetupNullableBoolTestData(employeesDb);

        var expected = employeesDb.Query().Employees.ToList()
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted == false)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted == false)
            .ToList();

        Assert.Single(result);
        Assert.Equal(2002, result.Single().emp_no);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_NullableBool_NotEquals_False(Database<EmployeesDb> employeesDb)
    {
        var empIds = SetupNullableBoolTestData(employeesDb);

        var expected = employeesDb.Query().Employees.ToList()
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted != false)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted != false)
            .ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.emp_no == 2001); // Should contain the 'true' case
        Assert.Contains(result, e => e.emp_no == 2003); // Should contain the 'null' case
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_NullableBool_Equals_Null(Database<EmployeesDb> employeesDb)
    {
        var empIds = SetupNullableBoolTestData(employeesDb);

        var expected = employeesDb.Query().Employees.ToList()
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted == null)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(x => empIds.Contains(x.emp_no.Value) && x.IsDeleted == null)
            .ToList();

        Assert.Single(result);
        Assert.Equal(2003, result.Single().emp_no);
        Assert.Equal(expected, result);
    }
}