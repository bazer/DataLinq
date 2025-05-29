using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Tests.Models.Employees;
using Xunit;

namespace DataLinq.Tests.LinqQueryTests;
public class EmptyListTests : BaseTests
{

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Contains_EmptyList_ShouldReturnEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var result = employeesDb.Query().Employees.Where(e => emptyList.Contains(e.emp_no.Value)).ToList();
        Assert.Empty(result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Contains_EmptyList_And_TrueCondition_ShouldReturnEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var result = employeesDb.Query().Employees
            .Where(e => e.gender == Employee.Employeegender.M && emptyList.Contains(e.emp_no.Value))
            .ToList();
        Assert.Empty(result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Contains_EmptyList_Or_TrueCondition_ShouldReturnMatchingTrueCondition(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        // Find an actual first name to test against
        var firstEmployee = employeesDb.Query().Employees.First();
        var specificFirstName = firstEmployee.first_name;

        var expectedCount = employeesDb.Query().Employees.ToList().Count(e => e.first_name == specificFirstName);
        Assert.True(expectedCount > 0, "Test setup: No employees with the specific first name found.");

        var result = employeesDb.Query().Employees
            .Where(e => e.first_name == specificFirstName || emptyList.Contains(e.emp_no.Value))
            .ToList();

        Assert.Equal(expectedCount, result.Count);
        Assert.All(result, item => Assert.Equal(specificFirstName, item.first_name));
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Not_Contains_EmptyList_ShouldReturnAll(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var totalEmployees = employeesDb.Query().Employees.Count();

        var result = employeesDb.Query().Employees
            .Where(e => !emptyList.Contains(e.emp_no.Value))
            .ToList();

        Assert.Equal(totalEmployees, result.Count);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Contains_EmptyList_Or_OtherNonEmptyContains_ShouldReturnMatchingNonEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var nonEmptyList = new List<int> { employeesDb.Query().Employees.First().emp_no.Value }; // Get a valid emp_no

        var expected = employeesDb.Query().Employees.Where(e => nonEmptyList.Contains(e.emp_no.Value)).ToList();
        Assert.NotEmpty(expected); // Ensure test setup is valid

        var result = employeesDb.Query().Employees
            .Where(e => nonEmptyList.Contains(e.emp_no.Value) || emptyList.Contains(e.gender == Employee.Employeegender.M ? 1 : 0)) // example of different type list
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result); // Order might matter, consider ordering or comparing sets
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Contains_EmptyList_And_OtherNonEmptyContains_ShouldReturnEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var nonEmptyList = new List<int> { employeesDb.Query().Employees.First().emp_no.Value };

        var result = employeesDb.Query().Employees
            .Where(e => nonEmptyList.Contains(e.emp_no.Value) && emptyList.Contains(e.gender == Employee.Employeegender.M ? 1 : 0))
            .ToList();

        Assert.Empty(result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Any_EmptyList_ShouldReturnEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var result = employeesDb.Query().Employees
            .Where(e => emptyList.Any(id => id == e.emp_no.Value))
            .ToList();
        Assert.Empty(result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Any_EmptyList_And_TrueCondition_ShouldReturnEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var result = employeesDb.Query().Employees
            .Where(e => e.gender == Employee.Employeegender.M && emptyList.Any(id => id == e.emp_no.Value))
            .ToList();
        Assert.Empty(result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Any_EmptyList_Or_TrueCondition_ShouldReturnMatchingTrueCondition(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var firstEmployee = employeesDb.Query().Employees.First();
        var specificFirstName = firstEmployee.first_name;

        var expectedCount = employeesDb.Query().Employees.ToList().Count(e => e.first_name == specificFirstName);
        Assert.True(expectedCount > 0, "Test setup: No employees with the specific first name found.");

        var result = employeesDb.Query().Employees
            .Where(e => e.first_name == specificFirstName || emptyList.Any(id => id == e.emp_no.Value))
            .ToList();

        Assert.Equal(expectedCount, result.Count);
        Assert.All(result, item => Assert.Equal(specificFirstName, item.first_name));
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Not_Any_EmptyList_ShouldReturnAll(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var totalEmployees = employeesDb.Query().Employees.Count();

        var result = employeesDb.Query().Employees
            .Where(e => !emptyList.Any(id => id == e.emp_no.Value))
            .ToList();

        Assert.Equal(totalEmployees, result.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Any_EmptyList_Or_OtherNonEmptyAny_ShouldReturnMatchingNonEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var firstEmp = employeesDb.Query().Employees.First();
        var nonEmptyList = new List<int> { firstEmp.emp_no.Value };

        var expected = employeesDb.Query().Employees
            .Where(e => nonEmptyList.Any(id => id == e.emp_no.Value))
            .ToList();
        Assert.NotEmpty(expected);

        // Using a simple property for the second Any for test simplicity
        var result = employeesDb.Query().Employees
            .Where(e => nonEmptyList.Any(id => id == e.emp_no.Value) || emptyList.Any(id => id == e.emp_no.Value)) // second Any uses same prop for simplicity
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        // Consider ordering if direct list comparison is needed, or convert to HashSet for comparison
        // For now, count and ensuring all expected items are present (and no unexpected) is fine.
        foreach (var item in expected) Assert.Contains(item, result);
        foreach (var item in result) Assert.Contains(item, expected);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Any_EmptyList_And_OtherNonEmptyAny_ShouldReturnEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var firstEmp = employeesDb.Query().Employees.First();
        var nonEmptyList = new List<int> { firstEmp.emp_no.Value };

        var result = employeesDb.Query().Employees
            .Where(e => nonEmptyList.Any(id => id == e.emp_no.Value) && emptyList.Any(id => id == e.emp_no.Value))
            .ToList();

        Assert.Empty(result);
    }

    // Test for Any() on a nullable property (if applicable and makes sense for your schema)
    // e.g., if you had a nullable int property List<int?> nullableList.Any(x => x == e.NullableIntProp)

    // Test for Any() with a more complex inner predicate
    // emptyList.Any(y => y > 100 && y == e.emp_no.Value) - result should be same (false if emptyList)
    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Where_Any_EmptyList_WithComplexPredicate_ShouldReturnEmpty(Database<EmployeesDb> employeesDb)
    {
        var emptyList = new List<int>();
        var result = employeesDb.Query().Employees
            .Where(e => emptyList.Any(id => id > 1000 && id == e.emp_no.Value))
            .ToList();
        Assert.Empty(result);
    }
}
