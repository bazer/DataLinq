using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesGroupedAggregateTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedCountProjection_OverDirectKey_MatchesInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedCountProjection_OverDirectKey_MatchesInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Where(x => x.dept_no.StartsWith("d00", StringComparison.Ordinal))
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count()
            })
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Where(x => x.dept_no.StartsWith("d00"))
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count()
            })
            .ToList()
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(FormatGroups(actual)).IsEqualTo(FormatGroups(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedCountProjection_WorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedCountProjection_WorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnly = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count()
            })
            .ToList()
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count()
            })
            .ToList()
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(FormatGroups(transactionRows)).IsEqualTo(FormatGroups(readOnly));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedCountProjection_RendersGroupBySql(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedCountProjection_RendersGroupBySql),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Where(x => x.dept_no.StartsWith("d00"))
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count()
            });

        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("GROUP BY t0.");
        await Assert.That(normalized).Contains("COUNT(*)");
        await Assert.That(sql.Parameters.Select(x => x.Value).ToArray()).Contains("d00%");
    }

    private static string FormatGroups<T>(T[] groups)
    {
        return string.Join(
            "|",
            groups.Select(group =>
            {
                var type = group!.GetType();
                var deptNo = type.GetProperty("DeptNo")!.GetValue(group);
                var count = type.GetProperty("Count")!.GetValue(group);
                return $"{deptNo}:{count}";
            }));
    }
}
