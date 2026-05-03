using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesAggregateTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarAggregates_OverDirectNumericMembers_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ScalarAggregates_OverDirectNumericMembers_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var managers = employeesDatabase.Query().Managers.ToList();

        await Assert.That(employeesDatabase.Query().Managers.Sum(x => x.emp_no)).IsEqualTo(managers.Sum(x => x.emp_no));
        await Assert.That(employeesDatabase.Query().Managers.Min(x => x.emp_no)).IsEqualTo(managers.Min(x => x.emp_no));
        await Assert.That(employeesDatabase.Query().Managers.Max(x => x.emp_no)).IsEqualTo(managers.Max(x => x.emp_no));
        await Assert.That(NearlyEqual(employeesDatabase.Query().Managers.Average(x => x.emp_no), managers.Average(x => x.emp_no))).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarAggregates_OverFilteredNumericMembers_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ScalarAggregates_OverFilteredNumericMembers_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var managers = employeesDatabase.Query().Managers.ToList();
        var expected = managers.Where(x => x.dept_fk.StartsWith("d00", StringComparison.Ordinal)).ToList();

        await Assert.That(employeesDatabase.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Sum(x => x.emp_no)).IsEqualTo(expected.Sum(x => x.emp_no));
        await Assert.That(employeesDatabase.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Min(x => x.emp_no)).IsEqualTo(expected.Min(x => x.emp_no));
        await Assert.That(employeesDatabase.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Max(x => x.emp_no)).IsEqualTo(expected.Max(x => x.emp_no));
        await Assert.That(NearlyEqual(
            employeesDatabase.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Average(x => x.emp_no),
            expected.Average(x => x.emp_no))).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarAggregates_OverNullableNumericMembers_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ScalarAggregates_OverNullableNumericMembers_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employees = employeesDatabase.Query().Employees.ToList();

        await Assert.That(employeesDatabase.Query().Employees.Sum(x => x.emp_no)).IsEqualTo(employees.Sum(x => x.emp_no));
        await Assert.That(employeesDatabase.Query().Employees.Min(x => x.emp_no)).IsEqualTo(employees.Min(x => x.emp_no));
        await Assert.That(employeesDatabase.Query().Employees.Max(x => x.emp_no)).IsEqualTo(employees.Max(x => x.emp_no));
        await Assert.That(NearlyEqual(
            employeesDatabase.Query().Employees.Average(x => x.emp_no)!.Value,
            employees.Average(x => x.emp_no)!.Value)).IsTrue();

        await Assert.That(employeesDatabase.Query().Employees.Sum(x => x.emp_no!.Value)).IsEqualTo(employees.Sum(x => x.emp_no!.Value));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ScalarAggregates_EmptyFilteredSequences_FollowDocumentedNullAndSumBehavior(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ScalarAggregates_EmptyFilteredSequences_FollowDocumentedNullAndSumBehavior),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        await Assert.That(employeesDatabase.Query().Managers.Where(x => x.dept_fk == "missing").Sum(x => x.emp_no)).IsEqualTo(0);
        await Assert.That(employeesDatabase.Query().Employees.Where(x => x.first_name == "missing").Sum(x => x.emp_no)).IsEqualTo(0);
        await Assert.That(employeesDatabase.Query().Employees.Where(x => x.first_name == "missing").Min(x => x.emp_no)).IsNull();
        await Assert.That(employeesDatabase.Query().Employees.Where(x => x.first_name == "missing").Max(x => x.emp_no)).IsNull();
        await Assert.That(employeesDatabase.Query().Employees.Where(x => x.first_name == "missing").Average(x => x.emp_no)).IsNull();
    }

    private static bool NearlyEqual(double actual, double expected)
        => Math.Abs(actual - expected) < 0.0001;
}
