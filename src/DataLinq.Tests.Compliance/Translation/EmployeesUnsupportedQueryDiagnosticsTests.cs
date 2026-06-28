using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesUnsupportedQueryDiagnosticsTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedPredicateMethodThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedPredicateMethodThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Employees
                .Where(x => HasKnownPrefix(x.first_name))
                .ToList(),
            "Method 'HasKnownPrefix'",
            "Expression:");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedLocalAnyPredicateThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedLocalAnyPredicateThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        var ids = new[] { 1001, 1002 };

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Employees
                .Where(x => ids.Any(id => id > 1000 && id == x.emp_no!.Value))
                .ToList(),
            "Any(predicate)",
            "Predicate:");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedRelationSelectorThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedRelationSelectorThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Departments
                .Select(x => x.Managers)
                .ToList(),
            "Relation property 'Managers'",
            "LINQ Select projection");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedAggregateSelectorThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedAggregateSelectorThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Employees
                .Sum(x => x.emp_no!.Value + 1),
            "Aggregate selector",
            "Sum");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedGroupByThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedGroupByThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Employees
                .GroupBy(x => x.gender)
                .ToList(),
            "GroupBy",
            "not supported");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedGroupedProjectionShapesThrowQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedGroupedProjectionShapesThrowQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no.Substring(0, 1))
                .Select(group => new { DeptNo = group.Key, Count = group.Count() })
                .ToList(),
            "GroupBy key selector",
            "direct mapped member");

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => new { x.dept_no, x.emp_no })
                .Select(group => new { group.Key, Count = group.Count() })
                .ToList(),
            "GroupBy key selector",
            "direct mapped member");

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new { group.Key, Rows = group.ToList() })
                .ToList(),
            "Grouped aggregate projection member",
            "group.Key and group.Count()");

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new { group.Key, Sum = group.Sum(row => row.emp_no) })
                .ToList(),
            "Grouped aggregate projection member",
            "group.Key and group.Count()");

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .OrderBy(x => x.emp_no)
                .GroupBy(x => x.dept_no)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToList(),
            "GroupBy is only supported after direct source queries or Where predicates",
            "OrderBy");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PostPagingOrderByUsesSubqueryPushdown(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PostPagingOrderByUsesSubqueryPushdown),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .OrderBy(x => x.birth_date)
            .ThenBy(x => x.emp_no)
            .Take(5)
            .OrderByDescending(x => x.hire_date)
            .ThenBy(x => x.emp_no)
            .Select(x => x.emp_no)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .OrderBy(x => x.birth_date)
            .ThenBy(x => x.emp_no)
            .Take(5)
            .OrderByDescending(x => x.hire_date)
            .ThenBy(x => x.emp_no)
            .Select(x => x.emp_no)
            .ToArray();

        await Assert.That(string.Join(",", actual)).IsEqualTo(string.Join(",", expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PostPagingWhereUsesSubqueryPushdown(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PostPagingWhereUsesSubqueryPushdown),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .OrderBy(x => x.emp_no)
            .Take(20)
            .Where(x => x.gender == Employee.Employeegender.M)
            .OrderByDescending(x => x.emp_no)
            .Select(x => x.emp_no)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(20)
            .Where(x => x.gender == Employee.Employeegender.M)
            .OrderByDescending(x => x.emp_no)
            .Select(x => x.emp_no)
            .ToArray();

        await Assert.That(string.Join(",", actual)).IsEqualTo(string.Join(",", expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PostPagingProjectionFilterThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PostPagingProjectionFilterThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Employees
                .Take(5)
                .Select(x => new { x.emp_no })
                .Where(x => x.emp_no > 10000)
                .ToList(),
            "after Select",
            "not supported");
    }

    private static bool HasKnownPrefix(string value)
        => value.StartsWith("A", StringComparison.Ordinal);

    private static async Task AssertTranslationFailure(Action action, params string[] expectedMessageFragments)
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

        foreach (var fragment in expectedMessageFragments)
        {
            await Assert.That(exception!.Message).Contains(fragment);
        }
    }
}
