using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Tests.Models.Employees; // Ensure this is correct
using Xunit;

namespace DataLinq.Tests.LinqQueryTests;

public class BooleanLogicTests : BaseTests
{
    // Helper to get consistent test data
    private (int empA_id, int empB_id, int empC_id, int empD_id, string fnameA, string fnameB, string lnameC, Employee.Employeegender genderD) GetTestEmployeeDetails(Database<EmployeesDb> db)
    {
        // Ensure a consistent small set of employees for these tests
        // Clear cache to ensure fresh reads if needed for specific test setup,
        // but for general details, cached is fine.
        var emps = db.Query().Employees.OrderBy(e => e.emp_no).Take(4).ToList();
        if (emps.Count < 4)
        {
            // Fallback: if DB is too small, create some test data or throw
            // For now, we'll assume the DB has at least 4 employees from fixture setup
            throw new InvalidOperationException("Database does not contain enough employees (at least 4 required) for these specific tests. Ensure fixture populates sufficiently.");
        }
        return (emps[0].emp_no.Value, emps[1].emp_no.Value, emps[2].emp_no.Value, emps[3].emp_no.Value,
                emps[0].first_name, emps[1].first_name, emps[2].last_name, emps[3].gender);
    }

    private List<Employee> GetOrderedFullEmployeeList(Database<EmployeesDb> db)
    {
        return db.Query().Employees.ToList().OrderBy(e => e.emp_no).ToList();
    }

    // --- Test Group 1: Nested Groups and Operator Precedence ---

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G1_1_GroupedAnd_Or_Simple(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => (e.emp_no == details.empA_id && e.first_name == details.fnameA) || e.last_name == details.lnameC)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => (e.emp_no == details.empA_id && e.first_name == details.fnameA) || e.last_name == details.lnameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G1_2_SimpleAnd_GroupedOr(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => e.emp_no == details.empA_id && (e.first_name == details.fnameB || e.last_name == details.lnameC))
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => e.emp_no == details.empA_id && (e.first_name == details.fnameB || e.last_name == details.lnameC))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    // --- Test Group 2: Negation of Groups ---

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G2_1_NegatedGroupedAnd(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => !(e.emp_no == details.empA_id && e.first_name == details.fnameA))
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => !(e.emp_no == details.empA_id && e.first_name == details.fnameA))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G2_2_NegatedGroupedOr(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => !(e.emp_no == details.empA_id || e.first_name == details.fnameB))
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => !(e.emp_no == details.empA_id || e.first_name == details.fnameB))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G2_3_SimpleAnd_NegatedGroupedOr(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => e.emp_no == details.empA_id && !(e.first_name == details.fnameB || e.last_name == details.lnameC))
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => e.emp_no == details.empA_id && !(e.first_name == details.fnameB || e.last_name == details.lnameC))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G2_4_Negated_GroupedAnd_Or_GroupedAnd(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => !((e.emp_no == details.empA_id && e.first_name == details.fnameA) || (e.last_name == details.lnameC && e.gender == details.genderD)))
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => !((e.emp_no == details.empA_id && e.first_name == details.fnameA) || (e.last_name == details.lnameC && e.gender == details.genderD)))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    // --- Test Group 3: Negation of Contains / Any with Empty Lists within Groups ---

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G3_1_GroupedAnd_With_NegatedEmptyContains_Or_Simple(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);
        var emptyList = new List<int>();

        // !emptyList.Contains(...) is true (1=1)
        // So: (e.emp_no == empA_id && true) || e.last_name == lastNameC
        // Which is: e.emp_no == empA_id || e.last_name == lastNameC
        var expected = allEmployees
            .Where(e => e.emp_no == details.empA_id || e.last_name == details.lnameC)
            .ToList();
        Assert.True(expected.Any() || allEmployees.Count(e => e.emp_no == details.empA_id) == 0 && allEmployees.Count(e => e.last_name == details.lnameC) == 0,
            "Test G3.1 ExpectedLinq should not be empty for valid setup, unless specific data makes it so.");

        var result = employeesDb.Query().Employees
            .Where(e => (e.emp_no == details.empA_id && !emptyList.Contains(e.emp_no.Value)) || e.last_name == details.lnameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G3_2_SimpleAnd_Negated_EmptyContains_Or_Simple(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);
        var emptyList = new List<int>();

        // emptyList.Contains(...) is false (1=0)
        // So: !(false || e.last_name == lastNameC)
        // Which is: !(e.last_name == lastNameC)
        // Final: e.emp_no == empA_id && !(e.last_name == lastNameC)
        var expected = allEmployees
            .Where(e => e.emp_no == details.empA_id && !(e.last_name == details.lnameC))
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => e.emp_no == details.empA_id && !(emptyList.Contains(e.emp_no.Value) || e.last_name == details.lnameC))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G3_3_GroupedOr_With_NegatedEmptyAny_And_Simple(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);
        var emptyList = new List<int>();

        // !emptyList.Any(...) is true (1=1)
        // So: (e.emp_no == empA_id || true) && e.last_name == lastNameC
        // Which is: true && e.last_name == lastNameC
        // Final: e.last_name == lastNameC
        var expected = allEmployees
            .Where(e => e.last_name == details.lnameC)
            .ToList();
        Assert.True(expected.Any() || allEmployees.Count(e => e.last_name == details.lnameC) == 0,
            "Test G3.3 ExpectedLinq should not be empty for valid setup, unless specific data makes it so.");

        var result = employeesDb.Query().Employees
            .Where(e => (e.emp_no == details.empA_id || !emptyList.Any(id => id == e.emp_no.Value)) && e.last_name == details.lnameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    // --- Test Group 4: Edge Cases with negations and ors consumption ---

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G4_1_NegatedSimple_Or_Simple(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => !(e.emp_no == details.empA_id) || e.first_name == details.fnameB)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => !(e.emp_no == details.empA_id) || e.first_name == details.fnameB)
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G4_2_Simple_Or_NegatedSimple(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        var expected = allEmployees
            .Where(e => e.emp_no == details.empA_id || !(e.first_name == details.fnameB))
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => e.emp_no == details.empA_id || !(e.first_name == details.fnameB))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G4_3_Negated_GroupedOr_With_InnerNegation_And_Simple(Database<EmployeesDb> employeesDb)
    {
        var details = GetTestEmployeeDetails(employeesDb);
        var allEmployees = GetOrderedFullEmployeeList(employeesDb);

        // !(A || !B) && C
        // equivalent to: (!A && B) && C
        var expected = allEmployees
            .Where(e => (!(e.emp_no == details.empA_id) && e.first_name == details.fnameB) && e.last_name == details.lnameC)
            .ToList();

        var result = employeesDb.Query().Employees
            .Where(e => !(e.emp_no == details.empA_id || !(e.first_name == details.fnameB)) && e.last_name == details.lnameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }
}