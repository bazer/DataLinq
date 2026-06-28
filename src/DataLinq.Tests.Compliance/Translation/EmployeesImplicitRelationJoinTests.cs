using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesImplicitRelationJoinTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ImplicitSingularRelationPredicateAndOrdering_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ImplicitSingularRelationPredicateAndOrdering_MatchesInMemory),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Where(row => row.departments.Name.Contains("e"))
            .OrderBy(row => row.departments.Name, StringComparer.Ordinal)
            .ThenBy(row => row.emp_no)
            .Take(20)
            .Select(row => $"{row.dept_no}:{row.emp_no}:{row.departments.Name}")
            .ToArray();

        var query = employeesDatabase.Query().DepartmentEmployees
            .Where(row => row.departments.Name.Contains("e"))
            .OrderBy(row => row.departments.Name)
            .ThenBy(row => row.emp_no)
            .Take(20);

        var actual = query
            .ToList()
            .Select(row => $"{row.dept_no}:{row.emp_no}:{row.departments.Name}")
            .ToArray();

        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(employeesDatabase, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("JOIN");
        await Assert.That(normalized).Contains("dept_name");
        await Assert.That(normalized).Contains("t1.");
        await Assert.That(string.Join("|", actual)).IsEqualTo(string.Join("|", expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ImplicitSingularRelationPredicateAndOrdering_WorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ImplicitSingularRelationPredicateAndOrdering_WorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnlyRows = employeesDatabase.Query().DepartmentEmployees
            .Where(row => row.departments.Name.Contains("e"))
            .OrderBy(row => row.departments.Name)
            .ThenBy(row => row.emp_no)
            .Take(20)
            .ToList()
            .Select(row => $"{row.dept_no}:{row.emp_no}:{row.departments.Name}")
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .Where(row => row.departments.Name.Contains("e"))
            .OrderBy(row => row.departments.Name)
            .ThenBy(row => row.emp_no)
            .Take(20)
            .ToList()
            .Select(row => $"{row.dept_no}:{row.emp_no}:{row.departments.Name}")
            .ToArray();

        await Assert.That(string.Join("|", transactionRows)).IsEqualTo(string.Join("|", readOnlyRows));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedImplicitRelationProjectionThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedImplicitRelationProjectionThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .Select(row => row.departments.Name)
                .ToList(),
            "Relation property 'departments'",
            "LINQ Select projection");
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
