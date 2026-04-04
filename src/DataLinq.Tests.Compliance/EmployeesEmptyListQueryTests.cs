using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesEmptyListQueryTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_ContainsReturnsEmpty(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_ContainsReturnsEmpty),
            EmployeesSeedMode.Bogus);

        var emptyList = new List<int>();
        var result = databaseScope.Database.Query().Employees
            .Where(x => emptyList.Contains(x.emp_no.Value))
            .ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_ContainsAndTrueConditionReturnsEmpty(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_ContainsAndTrueConditionReturnsEmpty),
            EmployeesSeedMode.Bogus);

        var emptyList = new List<int>();
        var result = databaseScope.Database.Query().Employees
            .Where(x => x.gender == Employee.Employeegender.M && emptyList.Contains(x.emp_no.Value))
            .ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_ContainsOrTrueConditionReturnsMatchingRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_ContainsOrTrueConditionReturnsMatchingRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var specificFirstName = employeesDatabase.Query().Employees.Select(x => x.first_name).First();
        var expected = employeesDatabase.Query().Employees
            .Where(x => x.first_name == specificFirstName)
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => x.first_name == specificFirstName || emptyList.Contains(x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_NegatedContainsReturnsAllSalaries(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_NegatedContainsReturnsAllSalaries),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var totalCount = employeesDatabase.Query().salaries.Count();

        var result = employeesDatabase.Query().salaries
            .Where(x => !emptyList.Contains(x.emp_no))
            .ToList();

        await Assert.That(result.Count).IsEqualTo(totalCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_ContainsOrNonEmptyContainsReturnsMatchingNonEmptyRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_ContainsOrNonEmptyContainsReturnsMatchingNonEmptyRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var employeeNumber = employeesDatabase.Query().Employees.Select(x => x.emp_no.Value).First();
        var nonEmptyList = new List<int> { employeeNumber };

        var expected = employeesDatabase.Query().Employees
            .Where(x => nonEmptyList.Contains(x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = employeesDatabase.Query().Employees
            .Where(x => nonEmptyList.Contains(x.emp_no.Value) || emptyList.Contains(x.gender == Employee.Employeegender.M ? 1 : 0))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_ContainsAndNonEmptyContainsReturnsEmpty(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_ContainsAndNonEmptyContainsReturnsEmpty),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var employeeNumber = employeesDatabase.Query().Employees.Select(x => x.emp_no.Value).First();
        var nonEmptyList = new List<int> { employeeNumber };

        var result = employeesDatabase.Query().Employees
            .Where(x => nonEmptyList.Contains(x.emp_no.Value) && emptyList.Contains(x.gender == Employee.Employeegender.M ? 1 : 0))
            .ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_AnyReturnsEmpty(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_AnyReturnsEmpty),
            EmployeesSeedMode.Bogus);

        var emptyList = new List<int>();
        var result = databaseScope.Database.Query().Employees
            .Where(x => emptyList.Any(id => id == x.emp_no.Value))
            .ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_AnyAndTrueConditionReturnsEmpty(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_AnyAndTrueConditionReturnsEmpty),
            EmployeesSeedMode.Bogus);

        var emptyList = new List<int>();
        var result = databaseScope.Database.Query().Employees
            .Where(x => x.gender == Employee.Employeegender.M && emptyList.Any(id => id == x.emp_no.Value))
            .ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_AnyOrTrueConditionReturnsMatchingRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_AnyOrTrueConditionReturnsMatchingRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var specificFirstName = employeesDatabase.Query().Employees.Select(x => x.first_name).First();
        var expected = employeesDatabase.Query().Employees
            .Where(x => x.first_name == specificFirstName)
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => x.first_name == specificFirstName || emptyList.Any(id => id == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_NegatedAnyKeepsKnownEmployees(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_NegatedAnyKeepsKnownEmployees),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var knownEmployeeNumbers = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.emp_no.Value)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => knownEmployeeNumbers.Contains(x.emp_no.Value) && !emptyList.Any(id => id == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(knownEmployeeNumbers);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_AnyOrNonEmptyAnyReturnsMatchingNonEmptyRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_AnyOrNonEmptyAnyReturnsMatchingNonEmptyRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var employeeNumber = employeesDatabase.Query().Employees.Select(x => x.emp_no.Value).First();
        var nonEmptyList = new List<int> { employeeNumber };

        var expected = employeesDatabase.Query().Employees
            .Where(x => nonEmptyList.Any(id => id == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => nonEmptyList.Any(id => id == x.emp_no.Value) || emptyList.Any(id => id == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_AnyAndNonEmptyAnyReturnsEmpty(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_AnyAndNonEmptyAnyReturnsEmpty),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var emptyList = new List<int>();
        var employeeNumber = employeesDatabase.Query().Employees.Select(x => x.emp_no.Value).First();
        var nonEmptyList = new List<int> { employeeNumber };

        var result = employeesDatabase.Query().Employees
            .Where(x => nonEmptyList.Any(id => id == x.emp_no.Value) && emptyList.Any(id => id == x.emp_no.Value))
            .ToList();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EmptyList_AnyWithComplexPredicateReturnsEmpty(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(EmptyList_AnyWithComplexPredicateReturnsEmpty),
            EmployeesSeedMode.Bogus);

        var emptyList = new List<int>();
        var result = databaseScope.Database.Query().Employees
            .Where(x => emptyList.Any(id => id > 1000 && id == x.emp_no.Value))
            .ToList();

        await Assert.That(result).IsEmpty();
    }
}
