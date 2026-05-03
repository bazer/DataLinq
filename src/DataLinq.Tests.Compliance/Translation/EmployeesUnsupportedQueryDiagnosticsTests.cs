using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
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
            "SumResultOperator");
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
