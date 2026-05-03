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
    public async Task UnsupportedSelectorThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedSelectorThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Employees
                .Select(x => x.first_name + x.last_name)
                .ToList(),
            "Selector expression",
            "not supported");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedScalarResultOperatorThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedScalarResultOperatorThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Employees
                .Sum(x => x.emp_no!.Value),
            "Scalar result operator 'SumResultOperator'",
            "Query model:");
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
