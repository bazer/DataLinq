using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesProjectionTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ComputedAnonymousProjection_AppliesAfterSqlFilteringOrderingAndPaging(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ComputedAnonymousProjection_AppliesAfterSqlFilteringOrderingAndPaging),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.first_name, StringComparer.Ordinal)
            .ThenBy(x => x.emp_no)
            .Skip(2)
            .Take(5)
            .Select(x => new
            {
                x.emp_no,
                FullName = x.first_name + " " + x.last_name,
                Normalized = x.first_name.Trim().ToUpper(),
                NameLength = x.first_name.Trim().Length
            })
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.first_name)
            .ThenBy(x => x.emp_no)
            .Skip(2)
            .Take(5)
            .Select(x => new
            {
                x.emp_no,
                FullName = x.first_name + " " + x.last_name,
                Normalized = x.first_name.Trim().ToUpper(),
                NameLength = x.first_name.Trim().Length
            })
            .ToArray();

        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].FullName).IsEqualTo(expected[index].FullName);
            await Assert.That(actual[index].Normalized).IsEqualTo(expected[index].Normalized);
            await Assert.That(actual[index].NameLength).IsEqualTo(expected[index].NameLength);
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ComputedScalarProjection_MatchesPostMaterializationBehavior(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ComputedScalarProjection_MatchesPostMaterializationBehavior),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.first_name + ":" + x.emp_no!.Value)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.first_name + ":" + x.emp_no!.Value)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationProjection_ThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationProjection_ThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        QueryTranslationException? exception = null;

        try
        {
            _ = databaseScope.Database.Query().Departments
                .Select(x => new { x.DeptNo, ManagerCount = x.Managers.Count })
                .ToList();
        }
        catch (QueryTranslationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Relation property 'Managers'");
        await Assert.That(exception.Message).Contains("LINQ Select projection");
    }
}
