using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesLocalAnyPredicateTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task LocalScalarAnyPredicateTranslatesToMembership(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(LocalScalarAnyPredicateTranslatesToMembership),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var selectedIds = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(4)
            .Select(x => x.emp_no.Value)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => selectedIds.Any(id => id == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(selectedIds);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task LocalObjectMemberAnyPredicateTranslatesToMembership(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(LocalObjectMemberAnyPredicateTranslatesToMembership),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var selectedIds = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(4)
            .Select(x => x.emp_no.Value)
            .ToArray();
        var localIds = selectedIds.Select(id => new LocalEmployeeId(id)).ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => localIds.Any(id => id.Value == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(selectedIds);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task LocalObjectMemberAnyPredicateSupportsReversedEquality(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(LocalObjectMemberAnyPredicateSupportsReversedEquality),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var selectedIds = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(4)
            .Select(x => x.emp_no.Value)
            .ToArray();
        var localIds = selectedIds.Select(id => new LocalEmployeeId(id)).ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => localIds.Any(id => x.emp_no.Value == id.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(selectedIds);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task LocalObjectMemberAnyPredicateSupportsNullableWrappers(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(LocalObjectMemberAnyPredicateSupportsNullableWrappers),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var selectedIds = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(4)
            .Select(x => x.emp_no.Value)
            .ToArray();
        var localIds = selectedIds.Select(id => new NullableLocalEmployeeId(id)).ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => localIds.Any(id => id.Value.Value == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(selectedIds);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NegatedLocalObjectMemberAnyPredicateTranslatesToNotIn(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(NegatedLocalObjectMemberAnyPredicateTranslatesToNotIn),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var selectedIds = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.emp_no.Value)
            .ToArray();
        var excludedLocalIds = selectedIds.Take(2).Select(id => new LocalEmployeeId(id)).ToArray();
        var expected = selectedIds.Skip(2).ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => selectedIds.Contains(x.emp_no.Value) && !excludedLocalIds.Any(id => id.Value == x.emp_no.Value))
            .Select(x => x.emp_no.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
    }

    private sealed record LocalEmployeeId(int Value);

    private sealed record NullableLocalEmployeeId(int? Value);
}
