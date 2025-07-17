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
        var emps = db.Query().Employees.Where(x => x.emp_no <= 1000).OrderBy(e => e.emp_no).Take(4).ToList();
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
        return db.Query().Employees.Where(x => x.emp_no <= 1000).ToList().OrderBy(e => e.emp_no).ToList();
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
            .Where(x => x.emp_no <= 1000)
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
            .Where(x => x.emp_no <= 1000)
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
            .Where(x => x.emp_no <= 1000)
            .Where(e => !(e.emp_no == details.empA_id && e.first_name == details.fnameA))
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G2_2_NegatedGroupedOr_Robust(Database<EmployeesDb> employeesDb)
    {
        // 1. Get specific employee details for our conditions
        // We need empA (for emp_no == empA_id) and an employee whose first_name will be fnameB_target.
        // And ideally, one that matches neither, and one that might match both (if empA.first_name == fnameB_target).

        var allEmpsForDetails = employeesDb.Query().Employees.OrderBy(e => e.emp_no).Take(10).ToList();
        if (allEmpsForDetails.Count < 3)
        {
            throw new InvalidOperationException("Test G2_2 requires at least 3 diverse employees for robust detail selection.");
        }

        var empA_for_id = allEmpsForDetails[0]; // Employee for condition A (emp_no match)
        int empA_id_target = empA_for_id.emp_no.Value;

        // Find an employee for condition B (first_name match), preferably different from empA_for_id
        var empB_for_name = allEmpsForDetails.FirstOrDefault(e => e.emp_no != empA_id_target);
        if (empB_for_name == null) empB_for_name = allEmpsForDetails[1]; // Fallback if all taken have same emp_no
        string fnameB_target = empB_for_name.first_name;

        // Find an employee that matches NEITHER empA_id_target NOR fnameB_target
        var empC_no_match = allEmpsForDetails.FirstOrDefault(e => e.emp_no != empA_id_target && e.first_name != fnameB_target);
        if (empC_no_match == null)
        {
            // This case is harder if data is very uniform. For now, try a different one.
            empC_no_match = allEmpsForDetails.Skip(2).FirstOrDefault() ?? allEmpsForDetails.Last();
            if (empC_no_match.emp_no == empA_id_target || empC_no_match.first_name == fnameB_target)
            {
                // Consider creating specific test data if this often fails to find a distinct empC
                // For now, we'll proceed, but the test might not cover the "matches neither" case well.
            }
        }


        // 2. Define a small superset of employee PKs for the test
        var supersetPks = new List<int> { empA_id_target, empB_for_name.emp_no.Value };
        if (empC_no_match != null) supersetPks.Add(empC_no_match.emp_no.Value);

        // Check if empA_for_id also has fnameB_target (to test the A AND B case within the OR)
        if (empA_for_id.first_name == fnameB_target && !supersetPks.Contains(empA_id_target))
        {
            // empA_for_id already covers "matches A" and "matches B". Ensure it's in the list.
            // This check is a bit redundant if empA_id_target is always added, but good for thought.
        }

        // Optionally add a few more PKs to make the superset slightly larger
        var additionalPks = allEmpsForDetails
            .Where(e => e.emp_no.HasValue && !supersetPks.Contains(e.emp_no.Value))
            .Take(2)
            .Select(e => e.emp_no.Value);
        supersetPks.AddRange(additionalPks);
        supersetPks = supersetPks.Distinct().ToList();
        if (supersetPks.Count == 0) throw new InvalidOperationException("Superset PKs is empty, cannot run test.");


        // 3. Get the actual employee objects for our superset
        var supersetEmployees = employeesDb.Query().Employees
                                    .Where(e => e.emp_no.HasValue && supersetPks.Contains(e.emp_no.Value))
                                    .ToList()
                                    .OrderBy(e => e.emp_no) // Consistent order
                                    .ToList();
        if (supersetEmployees.Count == 0 && supersetPks.Count > 0)
            throw new InvalidOperationException("Failed to fetch any employees for the superset PKs.");


        // 4. Apply the logic in-memory to supersetEmployees to get the expected result
        // Logic: !(A || B)  is equivalent to  !A && !B
        var expected = supersetEmployees
            .Where(e => !(e.emp_no == empA_id_target || e.first_name == fnameB_target))
            .OrderBy(e => e.emp_no) // Match DB query order
            .ToList();

        // 5. Query the database, restricting to supersetPks AND applying the test condition
        var result = employeesDb.Query().Employees
            .Where(e =>
                e.emp_no.HasValue && supersetPks.Contains(e.emp_no.Value) && // Restrict to our superset
                (!(e.emp_no == empA_id_target || e.first_name == fnameB_target)) // The actual logic under test
            )
            .OrderBy(e => e.emp_no) // Ensure same order
            .ToList();

        // 6. Assert
        Assert.Equal(expected.Count, result.Count);

        // More robust list content comparison
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].emp_no, result[i].emp_no);
            Assert.Equal(expected[i].first_name, result[i].first_name);
        }
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
            .Where(x => x.emp_no <= 1000)
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
            .Where(x => x.emp_no <= 1000)
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
            .Where(x => x.emp_no <= 1000)
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
            .Where(x => x.emp_no <= 1000)
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
            .Where(x => x.emp_no <= 1000)
            .Where(e => (e.emp_no == details.empA_id || !emptyList.Any(id => id == e.emp_no.Value)) && e.last_name == details.lnameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }

    // --- Test Group 4: Edge Cases with negations and ors consumption ---

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void G4_1_NegatedSimple_Or_Simple_Robust(Database<EmployeesDb> employeesDb)
    {
        // 1. Get specific employee details for our conditions
        var allEmpsForDetails = employeesDb.Query().Employees.OrderBy(e => e.emp_no).Take(10).ToList();
        if (allEmpsForDetails.Count < 3)
        {
            throw new InvalidOperationException("Test requires at least 3 employees for robust detail selection.");
        }

        var empA = allEmpsForDetails[0]; // This will be our empA_id target
        var empB = allEmpsForDetails.FirstOrDefault(e => e.first_name != empA.first_name && e.emp_no != empA.emp_no); // A different employee for fnameB
        var empC_NoMatch = allEmpsForDetails.FirstOrDefault(e => e.emp_no != empA.emp_no && e.first_name != empB?.first_name); // Should not match either

        if (empB == null || empC_NoMatch == null)
        {
            // If data is too homogenous, we might need to create specific test data
            // For now, let's assume distinct enough data from fixture or first 10.
            empB = allEmpsForDetails[1]; // Fallback
            empC_NoMatch = allEmpsForDetails[2]; // Fallback
            if (empB.emp_no == empA.emp_no || empC_NoMatch.emp_no == empA.emp_no || empC_NoMatch.emp_no == empB.emp_no)
                throw new InvalidOperationException("Fallback employees for empB or empC_NoMatch are not distinct enough. Adjust test data or selection.");
        }

        int empA_id = empA.emp_no.Value;
        string fnameB_target = empB.first_name;

        // 2. Define a small superset of employee PKs for the test
        // This superset MUST include empA, empB, and empC_NoMatch to cover all branches of the logic.
        var supersetPks = new List<int> { empA_id, empB.emp_no.Value, empC_NoMatch.emp_no.Value };
        // Optionally add a couple more random PKs if available to make the superset slightly larger than minimal
        var additionalPks = allEmpsForDetails
            .Where(e => !supersetPks.Contains(e.emp_no.Value))
            .Take(2)
            .Select(e => e.emp_no.Value);
        supersetPks.AddRange(additionalPks);
        supersetPks = supersetPks.Distinct().ToList();

        // 3. Get the actual employee objects for our superset (for in-memory filtering)
        // Ensure these are fresh if other tests modify them, or accept cached versions.
        // For this logic test, cached is usually fine as we're testing the WHERE clause translation.
        var supersetEmployees = employeesDb.Query().Employees
                                    .Where(e => supersetPks.Contains(e.emp_no.Value))
                                    .ToList()
                                    .OrderBy(e => e.emp_no) // Consistent order for comparison
                                    .ToList();

        // 4. Apply the logic in-memory to the supersetEmployees to get the expected result
        var expected = supersetEmployees
            .Where(e => !(e.emp_no == empA_id) || e.first_name == fnameB_target)
            // OrderBy here must match the DB query's OrderBy for Assert.Equal(list, list)
            .OrderBy(e => e.emp_no)
            .ToList();

        // 5. Query the database, restricting to the supersetPks AND applying the test condition
        var result = employeesDb.Query().Employees
            .Where(e =>
                supersetPks.Contains(e.emp_no.Value) && // Restrict to our superset
                (!(e.emp_no == empA_id) || e.first_name == fnameB_target) // The actual logic under test
            )
            .OrderBy(e => e.emp_no) // Ensure same order for comparison
            .ToList();

        // 6. Assert
        Assert.Equal(expected.Count, result.Count); // Compare counts first

        // For detailed list comparison, ensure properties compared by Employee.Equals are sufficient,
        // or compare specific properties if Employee.Equals is problematic.
        // Assert.Equal(expected, result); // This relies on Employee implementing Equals correctly based on PKs

        // More robust comparison if Employee.Equals is not yet PK-based or fully reliable:
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].emp_no, result[i].emp_no);
            Assert.Equal(expected[i].first_name, result[i].first_name);
            // Add other relevant properties if needed to confirm correctness
        }
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
            .Where(x => x.emp_no <= 1000)
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
            .Where(x => x.emp_no <= 1000)
            .Where(e => !(e.emp_no == details.empA_id || !(e.first_name == details.fnameB)) && e.last_name == details.lnameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        Assert.Equal(expected.Count, result.Count);
        Assert.Equal(expected, result);
    }
}