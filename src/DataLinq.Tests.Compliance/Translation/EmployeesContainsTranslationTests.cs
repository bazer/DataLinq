using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesContainsTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Contains_EmptyArrayReturnsNoRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Contains_EmptyArrayReturnsNoRows),
            EmployeesSeedMode.Bogus);

        var results = databaseScope.Database.Query()
            .Employees
            .Where(x => Array.Empty<int>().Contains(x.emp_no.Value))
            .ToList();

        await Assert.That(results).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Contains_NegatedContainsFiltersRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Contains_NegatedContainsFiltersRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var ids = employeesDatabase.Query().Employees
            .Select(x => x.emp_no.Value)
            .Take(3)
            .ToArray();

        await Assert.That(ids.Length).IsGreaterThanOrEqualTo(2);

        var include = ids.Take(2).ToArray();
        var outside = employeesDatabase.Query().Employees
            .Where(x => !include.Contains(x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .ToList();

        await Assert.That(outside.Contains(include[0])).IsFalse();
        await Assert.That(outside.Contains(include[1])).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Contains_ReadOnlySpanFiltersRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Contains_ReadOnlySpanFiltersRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var ids = employeesDatabase.Query().Employees
            .Select(x => x.emp_no.Value)
            .Take(2)
            .ToArray();

        await Assert.That(ids.Length).IsEqualTo(2);

        var arr = ids.ToArray();
        var results = employeesDatabase.Query().Employees
            .Where(x => ((ReadOnlySpan<int>)arr).Contains(x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .ToArray();

        await Assert.That(results.Length).IsGreaterThanOrEqualTo(2);
        await Assert.That(results.Contains(ids[0])).IsTrue();
        await Assert.That(results.Contains(ids[1])).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Contains_ConstantTrueReturnsAllRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Contains_ConstantTrueReturnsAllRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var anyId = employeesDatabase.Query().Employees
            .Select(x => x.emp_no.Value)
            .First();

        var allCount = employeesDatabase.Query().Employees.Count();
        var count = employeesDatabase.Query().Employees
            .Where(x => new[] { anyId }.Contains(anyId))
            .Count();

        await Assert.That(count).IsEqualTo(allCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Contains_ConstantFalseReturnsNoRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Contains_ConstantFalseReturnsNoRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var ids = employeesDatabase.Query().Employees
            .Select(x => x.emp_no.Value)
            .Take(2)
            .ToArray();

        await Assert.That(ids.Length).IsEqualTo(2);

        var count = employeesDatabase.Query().Employees
            .Where(x => new[] { ids[0] }.Contains(ids[1]))
            .Count();

        await Assert.That(count).IsEqualTo(0);
    }
}
