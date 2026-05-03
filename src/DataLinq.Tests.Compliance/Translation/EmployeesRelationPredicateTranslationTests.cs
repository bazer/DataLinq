using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesRelationPredicateTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationAnyPredicate_TranslatesToExistsAndMatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationAnyPredicate_TranslatesToExistsAndMatchesInMemory),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var managerNumber = employeesDatabase.Query().Managers
            .OrderBy(x => x.emp_no)
            .First()
            .emp_no;

        var expected = employeesDatabase.Query().Departments
            .ToList()
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NegatedRelationAnyPredicate_TranslatesToNotExistsAndMatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(NegatedRelationAnyPredicate_TranslatesToNotExistsAndMatchesInMemory),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var managerNumber = employeesDatabase.Query().Managers
            .OrderBy(x => x.emp_no)
            .First()
            .emp_no;

        var expected = employeesDatabase.Query().Departments
            .ToList()
            .Where(department => !department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().Departments
            .Where(department => !department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCountGreaterThanZero_TranslatesToExistsAndMatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationCountGreaterThanZero_TranslatesToExistsAndMatchesInMemory),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(employee => employee.dept_manager.Count > 0)
            .Select(employee => employee.emp_no!.Value)
            .OrderBy(employeeNumber => employeeNumber)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .Where(employee => employee.dept_manager.Count() > 0)
            .Select(employee => employee.emp_no!.Value)
            .OrderBy(employeeNumber => employeeNumber)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCountEqualsZero_TranslatesToNotExistsAndMatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationCountEqualsZero_TranslatesToNotExistsAndMatchesInMemory),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(employee => employee.dept_manager.Count == 0)
            .Select(employee => employee.emp_no!.Value)
            .OrderBy(employeeNumber => employeeNumber)
            .Take(20)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .Where(employee => employee.dept_manager.Count() == 0)
            .Select(employee => employee.emp_no!.Value)
            .OrderBy(employeeNumber => employeeNumber)
            .Take(20)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationAnyCompoundPredicate_TranslatesGroupedExistsAndMatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationAnyCompoundPredicate_TranslatesGroupedExistsAndMatchesInMemory),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Departments
            .ToList()
            .Where(department => department.Managers.Any(manager =>
                manager.Type == ManagerType.Manager || manager.Type == ManagerType.AssistantManager))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().Departments
            .Where(department => department.Managers.Any(manager =>
                manager.Type == ManagerType.Manager || manager.Type == ManagerType.AssistantManager))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationPredicateTraversal_ThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationPredicateTraversal_ThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Departments
                .Where(department => department.Managers.Any(manager => manager.Department.DeptNo == "d001"))
                .ToList(),
            "Relation predicate",
            "Expected a direct related-row member");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationCountUnsupportedThreshold_ThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationCountUnsupportedThreshold_ThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Departments
                .Where(department => department.Managers.Count() > 1)
                .ToList(),
            "Relation Count() comparison",
            "not supported");
    }

    private static async Task AssertTranslationFailure(Action action, params string[] expectedFragments)
    {
        QueryTranslationException? exception = null;

        try
        {
            action();
        }
        catch (QueryTranslationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();

        foreach (var fragment in expectedFragments)
            await Assert.That(exception!.Message).Contains(fragment);
    }
}
