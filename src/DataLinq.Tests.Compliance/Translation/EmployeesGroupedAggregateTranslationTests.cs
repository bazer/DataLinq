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
    public async Task GroupedNumericAggregates_OverDirectNumericSelector_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedNumericAggregates_OverDirectNumericSelector_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Where(x => x.dept_no.StartsWith("d00", StringComparison.Ordinal))
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            })
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Where(x => x.dept_no.StartsWith("d00"))
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            })
            .ToList()
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < actual.Length; index++)
        {
            await Assert.That(actual[index].DeptNo).IsEqualTo(expected[index].DeptNo);
            await Assert.That(actual[index].Count).IsEqualTo(expected[index].Count);
            await Assert.That(actual[index].SumEmployeeNumbers).IsEqualTo(expected[index].SumEmployeeNumbers);
            await Assert.That(actual[index].MinEmployeeNumber).IsEqualTo(expected[index].MinEmployeeNumber);
            await Assert.That(actual[index].MaxEmployeeNumber).IsEqualTo(expected[index].MaxEmployeeNumber);
            await Assert.That(NearlyEqual(actual[index].AverageEmployeeNumber, expected[index].AverageEmployeeNumber)).IsTrue();
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedNumericAggregates_OverNullableValueSelector_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedNumericAggregates_OverNullableValueSelector_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .GroupBy(x => x.gender)
            .Select(group => new
            {
                Gender = group.Key,
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            })
            .OrderBy(x => x.Gender)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .GroupBy(x => x.gender)
            .Select(group => new
            {
                Gender = group.Key,
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            })
            .ToList()
            .OrderBy(x => x.Gender)
            .ToArray();

        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < actual.Length; index++)
        {
            await Assert.That(actual[index].Gender).IsEqualTo(expected[index].Gender);
            await Assert.That(actual[index].SumEmployeeNumbers).IsEqualTo(expected[index].SumEmployeeNumbers);
            await Assert.That(actual[index].MinEmployeeNumber).IsEqualTo(expected[index].MinEmployeeNumber);
            await Assert.That(actual[index].MaxEmployeeNumber).IsEqualTo(expected[index].MaxEmployeeNumber);
            await Assert.That(NearlyEqual(
                actual[index].AverageEmployeeNumber!.Value,
                expected[index].AverageEmployeeNumber!.Value)).IsTrue();
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedNumericAggregates_WorkFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedNumericAggregates_WorkFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnly = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            })
            .ToList()
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            })
            .ToList()
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(transactionRows.Length).IsEqualTo(readOnly.Length);

        for (var index = 0; index < transactionRows.Length; index++)
        {
            await Assert.That(transactionRows[index].DeptNo).IsEqualTo(readOnly[index].DeptNo);
            await Assert.That(transactionRows[index].SumEmployeeNumbers).IsEqualTo(readOnly[index].SumEmployeeNumbers);
            await Assert.That(transactionRows[index].MinEmployeeNumber).IsEqualTo(readOnly[index].MinEmployeeNumber);
            await Assert.That(transactionRows[index].MaxEmployeeNumber).IsEqualTo(readOnly[index].MaxEmployeeNumber);
            await Assert.That(NearlyEqual(
                transactionRows[index].AverageEmployeeNumber,
                readOnly[index].AverageEmployeeNumber)).IsTrue();
        }
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

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedNumericAggregates_RenderAggregateSql(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedNumericAggregates_RenderAggregateSql),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no),
                MinEmployeeNumber = group.Min(row => row.emp_no),
                MaxEmployeeNumber = group.Max(row => row.emp_no),
                AverageEmployeeNumber = group.Average(row => row.emp_no)
            });

        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("GROUP BY t0.");
        await Assert.That(normalized).Contains("COUNT(*)");
        await Assert.That(normalized).Contains("SUM(");
        await Assert.That(normalized).Contains("MIN(");
        await Assert.That(normalized).Contains("MAX(");
        await Assert.That(normalized).Contains("AVG(");
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

    private static bool NearlyEqual(double actual, double expected)
        => Math.Abs(actual - expected) < 0.0001;
}
