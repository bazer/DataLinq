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
    public async Task GroupedHaving_OverAggregatePredicates_MatchesInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedHaving_OverAggregatePredicates_MatchesInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var minimumCount = 0;
        var minimumAverageEmployeeNumber = 0.0;

        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Where(x => x.dept_no.StartsWith("d00", StringComparison.Ordinal))
            .GroupBy(x => x.dept_no)
            .Where(group => group.Count() > minimumCount && group.Average(row => row.emp_no) > minimumAverageEmployeeNumber)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Where(x => x.dept_no.StartsWith("d00"))
            .GroupBy(x => x.dept_no)
            .Where(group => group.Count() > minimumCount && group.Average(row => row.emp_no) > minimumAverageEmployeeNumber)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .ToList()
            .OrderBy(x => x.DeptNo, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(FormatComposedGroups(actual)).IsEqualTo(FormatComposedGroups(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedProjectionWhereOrderingAndPaging_MatchesInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedProjectionWhereOrderingAndPaging_MatchesInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var minimumCount = 0;

        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .Where(row => row.Count > minimumCount && row.SumEmployeeNumbers > 0)
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.DeptNo, StringComparer.Ordinal)
            .Skip(1)
            .Take(3)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .Where(row => row.Count > minimumCount && row.SumEmployeeNumbers > 0)
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.DeptNo)
            .Skip(1)
            .Take(3)
            .ToList()
            .ToArray();

        await Assert.That(FormatComposedGroups(actual)).IsEqualTo(FormatComposedGroups(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedProjectionCountAndAny_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedProjectionCountAndAny_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var minimumCount = 0;

        var expectedRows = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .Where(row => row.Count > minimumCount && row.SumEmployeeNumbers > 0)
            .OrderBy(row => row.DeptNo, StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        var groupedRows = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .Where(row => row.Count > minimumCount && row.SumEmployeeNumbers > 0)
            .OrderBy(row => row.DeptNo)
            .Take(5);

        var actualCount = groupedRows.Count();
        var actualAny = groupedRows.Any();
        var actualAnyWithPredicate = groupedRows.Any(row => row.Count > minimumCount);

        await Assert.That(actualCount).IsEqualTo(expectedRows.Length);
        await Assert.That(actualAny).IsEqualTo(expectedRows.Any());
        await Assert.That(actualAnyWithPredicate).IsEqualTo(expectedRows.Any(row => row.Count > minimumCount));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedProjectionComposition_WorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedProjectionComposition_WorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnly = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .Where(row => row.Count > 0)
            .OrderByDescending(row => row.SumEmployeeNumbers)
            .ThenBy(row => row.DeptNo)
            .Take(4)
            .ToList()
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .Where(row => row.Count > 0)
            .OrderByDescending(row => row.SumEmployeeNumbers)
            .ThenBy(row => row.DeptNo)
            .Take(4)
            .ToList()
            .ToArray();

        await Assert.That(FormatComposedGroups(transactionRows)).IsEqualTo(FormatComposedGroups(readOnly));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedCompositeAndComputedKeys_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedCompositeAndComputedKeys_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .GroupBy(row => new
            {
                row.dept_no,
                FromYear = row.from_date.Year
            })
            .Select(group => new
            {
                DeptNo = group.Key.dept_no,
                group.Key.FromYear,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .OrderBy(row => row.DeptNo, StringComparer.Ordinal)
            .ThenBy(row => row.FromYear)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(row => new
            {
                row.dept_no,
                FromYear = row.from_date.Year
            })
            .Select(group => new
            {
                DeptNo = group.Key.dept_no,
                group.Key.FromYear,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .ToList()
            .OrderBy(row => row.DeptNo, StringComparer.Ordinal)
            .ThenBy(row => row.FromYear)
            .ToArray();

        await Assert.That(FormatCompositeGroups(actual)).IsEqualTo(FormatCompositeGroups(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedEnumNullableAndStringFunctionKeys_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedEnumNullableAndStringFunctionKeys_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .GroupBy(row => new
            {
                row.gender,
                row.emp_no,
                FirstNameLength = row.first_name.Length
            })
            .Select(group => new
            {
                Gender = group.Key.gender,
                EmployeeNumber = group.Key.emp_no,
                group.Key.FirstNameLength,
                Count = group.Count()
            })
            .OrderBy(row => row.Gender)
            .ThenBy(row => row.EmployeeNumber)
            .ThenBy(row => row.FirstNameLength)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .GroupBy(row => new
            {
                row.gender,
                row.emp_no,
                FirstNameLength = row.first_name.Length
            })
            .Select(group => new
            {
                Gender = group.Key.gender,
                EmployeeNumber = group.Key.emp_no,
                group.Key.FirstNameLength,
                Count = group.Count()
            })
            .ToList()
            .OrderBy(row => row.Gender)
            .ThenBy(row => row.EmployeeNumber)
            .ThenBy(row => row.FirstNameLength)
            .ToArray();

        await Assert.That(FormatMixedKeyGroups(actual)).IsEqualTo(FormatMixedKeyGroups(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedConstructorProjection_OverCompositeKey_MatchesInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedConstructorProjection_OverCompositeKey_MatchesInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .GroupBy(row => new
            {
                row.dept_no,
                FromYear = row.from_date.Year
            })
            .Select(group => new DepartmentYearGroup(
                group.Key.dept_no,
                group.Key.FromYear,
                group.Count()))
            .OrderBy(row => row.DeptNo, StringComparer.Ordinal)
            .ThenBy(row => row.FromYear)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(row => new
            {
                row.dept_no,
                FromYear = row.from_date.Year
            })
            .Select(group => new DepartmentYearGroup(
                group.Key.dept_no,
                group.Key.FromYear,
                group.Count()))
            .ToList()
            .OrderBy(row => row.DeptNo, StringComparer.Ordinal)
            .ThenBy(row => row.FromYear)
            .ToArray();

        await Assert.That(string.Join("|", actual)).IsEqualTo(string.Join("|", expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedExplicitJoinKeys_MatchInMemoryResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedExplicitJoinKeys_MatchInMemoryResults),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Join(
                employeesDatabase.Query().Departments.ToList(),
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    DepartmentName = department.Name
                })
            .GroupBy(row => row.DepartmentName)
            .Select(group => new
            {
                DepartmentName = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .OrderBy(row => row.DepartmentName, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    DepartmentName = department.Name
                })
            .GroupBy(row => row.DepartmentName)
            .Select(group => new
            {
                DepartmentName = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .ToList()
            .OrderBy(row => row.DepartmentName, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(FormatDepartmentNameGroups(actual)).IsEqualTo(FormatDepartmentNameGroups(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedImplicitRelationKey_WorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedImplicitRelationKey_WorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnly = employeesDatabase.Query().DepartmentEmployees
            .GroupBy(row => row.departments.Name)
            .Select(group => new
            {
                DepartmentName = group.Key,
                Count = group.Count()
            })
            .ToList()
            .OrderBy(row => row.DepartmentName, StringComparer.Ordinal)
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .GroupBy(row => row.departments.Name)
            .Select(group => new
            {
                DepartmentName = group.Key,
                Count = group.Count()
            })
            .ToList()
            .OrderBy(row => row.DepartmentName, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(FormatDepartmentNameGroups(transactionRows)).IsEqualTo(FormatDepartmentNameGroups(readOnly));
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

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedComposition_RendersHavingOrderingPagingAndDerivedCountSql(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedComposition_RendersHavingOrderingPagingAndDerivedCountSql),
            EmployeesSeedMode.Bogus);

        var minimumCount = 0;
        var query = databaseScope.Database.Query().DepartmentEmployees
            .GroupBy(x => x.dept_no)
            .Select(group => new
            {
                DeptNo = group.Key,
                Count = group.Count(),
                SumEmployeeNumbers = group.Sum(row => row.emp_no)
            })
            .Where(row => row.Count > minimumCount && row.SumEmployeeNumbers > 0)
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.DeptNo)
            .Take(3);

        var groupedSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(databaseScope.Database, query);
        var groupedNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(groupedSql.Text);

        await Assert.That(groupedNormalized).Contains("GROUP BY");
        await Assert.That(groupedNormalized).Contains("HAVING");
        await Assert.That(groupedNormalized).Contains("ORDER BY");
        await Assert.That(groupedNormalized).Contains("COUNT(*)");
        await Assert.That(groupedNormalized).Contains("SUM(");
        await Assert.That(groupedNormalized).Contains("DESC");

        var countSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            () => databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new
                {
                    DeptNo = group.Key,
                    Count = group.Count()
                })
                .Where(row => row.Count > minimumCount)
                .Count());
        var countNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(countSql.Text);

        await Assert.That(countNormalized).Contains("SELECT COUNT(*)");
        await Assert.That(countNormalized).Contains("FROM (SELECT");
        await Assert.That(countNormalized).Contains("GROUP BY");
        await Assert.That(countNormalized).Contains("HAVING");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupedAdvancedKeys_RenderSqlShape(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupedAdvancedKeys_RenderSqlShape),
            EmployeesSeedMode.Bogus);

        var compositeQuery = databaseScope.Database.Query().DepartmentEmployees
            .GroupBy(row => new
            {
                row.dept_no,
                FromYear = row.from_date.Year
            })
            .Select(group => new
            {
                DeptNo = group.Key.dept_no,
                group.Key.FromYear,
                Count = group.Count()
            });

        var compositeSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(databaseScope.Database, compositeQuery);
        var compositeNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(compositeSql.Text);

        await Assert.That(compositeNormalized).Contains("GROUP BY");
        await Assert.That(compositeNormalized).Contains("dept_no");
        await Assert.That(compositeNormalized).Contains("from_date");

        var joinedQuery = databaseScope.Database.Query().DepartmentEmployees
            .Join(
                databaseScope.Database.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    DepartmentName = department.Name
                })
            .GroupBy(row => row.DepartmentName)
            .Select(group => new
            {
                DepartmentName = group.Key,
                Count = group.Count()
            });

        var joinedSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(databaseScope.Database, joinedQuery);
        var joinedNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(joinedSql.Text);

        await Assert.That(joinedNormalized).Contains("JOIN");
        await Assert.That(joinedNormalized).Contains("GROUP BY");
        await Assert.That(joinedNormalized).Contains("dept_name");
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

    private static string FormatComposedGroups<T>(T[] groups)
    {
        return string.Join(
            "|",
            groups.Select(group =>
            {
                var type = group!.GetType();
                var deptNo = type.GetProperty("DeptNo")!.GetValue(group);
                var count = type.GetProperty("Count")!.GetValue(group);
                var sumEmployeeNumbers = type.GetProperty("SumEmployeeNumbers")!.GetValue(group);
                return $"{deptNo}:{count}:{sumEmployeeNumbers}";
            }));
    }

    private static string FormatCompositeGroups<T>(T[] groups)
    {
        return string.Join(
            "|",
            groups.Select(group =>
            {
                var type = group!.GetType();
                var deptNo = type.GetProperty("DeptNo")!.GetValue(group);
                var fromYear = type.GetProperty("FromYear")!.GetValue(group);
                var count = type.GetProperty("Count")!.GetValue(group);
                var sumEmployeeNumbers = type.GetProperty("SumEmployeeNumbers")!.GetValue(group);
                return $"{deptNo}:{fromYear}:{count}:{sumEmployeeNumbers}";
            }));
    }

    private static string FormatMixedKeyGroups<T>(T[] groups)
    {
        return string.Join(
            "|",
            groups.Select(group =>
            {
                var type = group!.GetType();
                var gender = type.GetProperty("Gender")!.GetValue(group);
                var employeeNumber = type.GetProperty("EmployeeNumber")!.GetValue(group);
                var firstNameLength = type.GetProperty("FirstNameLength")!.GetValue(group);
                var count = type.GetProperty("Count")!.GetValue(group);
                return $"{gender}:{employeeNumber}:{firstNameLength}:{count}";
            }));
    }

    private static string FormatDepartmentNameGroups<T>(T[] groups)
    {
        return string.Join(
            "|",
            groups.Select(group =>
            {
                var type = group!.GetType();
                var departmentName = type.GetProperty("DepartmentName")!.GetValue(group);
                var count = type.GetProperty("Count")!.GetValue(group);
                var sumEmployeeNumbers = type.GetProperty("SumEmployeeNumbers")?.GetValue(group);
                return sumEmployeeNumbers is null
                    ? $"{departmentName}:{count}"
                    : $"{departmentName}:{count}:{sumEmployeeNumbers}";
            }));
    }

    private static bool NearlyEqual(double actual, double expected)
        => Math.Abs(actual - expected) < 0.0001;

    private sealed record DepartmentYearGroup(string DeptNo, int FromYear, int Count);
}
