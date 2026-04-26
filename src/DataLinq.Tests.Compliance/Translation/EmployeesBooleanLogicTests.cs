using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesBooleanLogicTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_GroupedAndOr_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_GroupedAndOr_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => (e.emp_no == scenario.EmpAId && e.first_name == scenario.FirstNameA) || e.last_name == scenario.LastNameC)
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => (e.emp_no == scenario.EmpAId && e.first_name == scenario.FirstNameA) || e.last_name == scenario.LastNameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_SimpleAndGroupedOr_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_SimpleAndGroupedOr_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => e.emp_no == scenario.EmpAId && (e.first_name == scenario.FirstNameB || e.last_name == scenario.LastNameC))
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => e.emp_no == scenario.EmpAId && (e.first_name == scenario.FirstNameB || e.last_name == scenario.LastNameC))
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_NegatedGroupedAnd_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_NegatedGroupedAnd_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => !(e.emp_no == scenario.EmpAId && e.first_name == scenario.FirstNameA))
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => !(e.emp_no == scenario.EmpAId && e.first_name == scenario.FirstNameA))
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_NegatedGroupedOrWithinSuperset_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_NegatedGroupedOrWithinSuperset_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);
        var superset = GetSupersetEmployees(employees, scenario.EmpAId, scenario.FirstNameB);

        var expected = superset
            .Where(e => !(e.emp_no == scenario.EmpAId || e.first_name == scenario.FirstNameB))
            .OrderBy(e => e.emp_no)
            .ToList();

        var supersetIds = superset.Select(x => x.emp_no!.Value).ToHashSet();
        var result = databaseScope.Database.Query().Employees
            .Where(e => supersetIds.Contains(e.emp_no!.Value))
            .Where(e => !(e.emp_no == scenario.EmpAId || e.first_name == scenario.FirstNameB))
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_SimpleAndNegatedGroupedOr_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_SimpleAndNegatedGroupedOr_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => e.emp_no == scenario.EmpAId && !(e.first_name == scenario.FirstNameB || e.last_name == scenario.LastNameC))
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => e.emp_no == scenario.EmpAId && !(e.first_name == scenario.FirstNameB || e.last_name == scenario.LastNameC))
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_NegatedGroupedAndOrGroupedAnd_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_NegatedGroupedAndOrGroupedAnd_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => !((e.emp_no == scenario.EmpAId && e.first_name == scenario.FirstNameA) ||
                          (e.last_name == scenario.LastNameC && e.gender == scenario.GenderD)))
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => !((e.emp_no == scenario.EmpAId && e.first_name == scenario.FirstNameA) ||
                          (e.last_name == scenario.LastNameC && e.gender == scenario.GenderD)))
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_NegatedEmptyContainsInsideGroup_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_NegatedEmptyContainsInsideGroup_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => e.emp_no == scenario.EmpAId || e.last_name == scenario.LastNameC)
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => (e.emp_no == scenario.EmpAId && !Array.Empty<int>().Contains(e.emp_no!.Value)) || e.last_name == scenario.LastNameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_SimpleAndNegatedEmptyContainsOrSimple_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_SimpleAndNegatedEmptyContainsOrSimple_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => e.emp_no == scenario.EmpAId && e.last_name != scenario.LastNameC)
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => e.emp_no == scenario.EmpAId && !(Array.Empty<int>().Contains(e.emp_no!.Value) || e.last_name == scenario.LastNameC))
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_GroupedOrWithNegatedEmptyAny_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_GroupedOrWithNegatedEmptyAny_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => e.last_name == scenario.LastNameC)
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => (e.emp_no == scenario.EmpAId || !Array.Empty<int>().Any(id => id == e.emp_no!.Value)) && e.last_name == scenario.LastNameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_NegatedSimpleOrSimpleWithinSuperset_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_NegatedSimpleOrSimpleWithinSuperset_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);
        var superset = GetSupersetEmployees(employees, scenario.EmpAId, scenario.FirstNameB);
        var supersetIds = superset.Select(x => x.emp_no!.Value).ToHashSet();

        var expected = superset
            .Where(e => e.emp_no != scenario.EmpAId || e.first_name == scenario.FirstNameB)
            .OrderBy(e => e.emp_no)
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => supersetIds.Contains(e.emp_no!.Value))
            .Where(e => e.emp_no != scenario.EmpAId || e.first_name == scenario.FirstNameB)
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_SimpleOrNegatedSimple_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_SimpleOrNegatedSimple_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => e.emp_no == scenario.EmpAId || e.first_name != scenario.FirstNameB)
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => e.emp_no == scenario.EmpAId || e.first_name != scenario.FirstNameB)
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Boolean_NegatedGroupedOrWithInnerNegation_ReturnsExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Boolean_NegatedGroupedOrWithInnerNegation_ReturnsExpectedRows),
            EmployeesSeedMode.Bogus);

        var employees = GetOrderedEmployees(databaseScope.Database);
        var scenario = GetScenario(employees);

        var expected = employees
            .Where(e => e.emp_no != scenario.EmpAId && e.first_name == scenario.FirstNameB && e.last_name == scenario.LastNameC)
            .ToList();

        var result = databaseScope.Database.Query().Employees
            .Where(e => !(e.emp_no == scenario.EmpAId || e.first_name != scenario.FirstNameB) && e.last_name == scenario.LastNameC)
            .OrderBy(e => e.emp_no)
            .ToList();

        await AssertEmployeeIdentityEqual(expected, result);
    }

    private static List<Employee> GetOrderedEmployees(Database<EmployeesDb> database)
        => database.Query().Employees
            .OrderBy(e => e.emp_no)
            .ToList();

    private static BooleanScenario GetScenario(List<Employee> employees)
    {
        if (employees.Count < 10)
            throw new InvalidOperationException("Boolean compliance tests require at least 10 seeded employees.");

        var empA = employees[0];
        var empB = employees.First(e => e.emp_no != empA.emp_no && !string.Equals(e.first_name, empA.first_name, StringComparison.Ordinal));
        var empC = employees.First(e => e.emp_no != empA.emp_no && e.emp_no != empB.emp_no);
        var empD = employees.First(e => e.emp_no != empA.emp_no && e.emp_no != empB.emp_no && e.emp_no != empC.emp_no);

        return new BooleanScenario(
            empA.emp_no!.Value,
            empA.first_name,
            empB.first_name,
            empC.last_name,
            empD.gender);
    }

    private static List<Employee> GetSupersetEmployees(
        List<Employee> employees,
        int empAId,
        string firstNameB)
    {
        var superset = employees
            .Where(e => e.emp_no == empAId || e.first_name == firstNameB || (e.emp_no != empAId && e.first_name != firstNameB))
            .Take(5)
            .OrderBy(e => e.emp_no)
            .ToList();

        if (superset.Count < 3)
            throw new InvalidOperationException("Boolean compliance tests could not build a sufficiently diverse employee superset.");

        return superset;
    }

    private static async Task AssertEmployeeIdentityEqual(IReadOnlyList<Employee> expected, IReadOnlyList<Employee> actual)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].first_name).IsEqualTo(expected[index].first_name);
            await Assert.That(actual[index].last_name).IsEqualTo(expected[index].last_name);
        }
    }

    private sealed record BooleanScenario(
        int EmpAId,
        string FirstNameA,
        string FirstNameB,
        string LastNameC,
        Employee.Employeegender GenderD);
}
