using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesJoinTranslationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_ComposesAndProjectsSqlRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_ComposesAndProjectsSqlRows),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expected = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees.ToList()
                        join department in employeesDatabase.Query().Departments.ToList()
                            on departmentEmployee.dept_no equals department.DeptNo
                        where department.Name.Contains("e")
                        orderby department.Name, departmentEmployee.emp_no
                        select new
                        {
                            departmentEmployee.emp_no,
                            departmentEmployee.dept_no,
                            DepartmentName = department.Name
                        })
            .Take(20)
            .ToArray();

        var query =
            from departmentEmployee in employeesDatabase.Query().DepartmentEmployees
            join department in employeesDatabase.Query().Departments
                on departmentEmployee.dept_no equals department.DeptNo
            where department.Name.Contains("e")
            orderby department.Name, departmentEmployee.emp_no
            select new
            {
                departmentEmployee.emp_no,
                departmentEmployee.dept_no,
                DepartmentName = department.Name
            };

        var actual = query.Take(20).ToArray();
        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(employeesDatabase, query.Take(20));
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("JOIN");
        await Assert.That(normalized).Contains("dept_name");
        await Assert.That(normalized).Contains("DepartmentName");
        await Assert.That(FormatDepartmentRows(actual)).IsEqualTo(FormatDepartmentRows(expected));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_WorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_WorksFromTransactionRoot),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnlyRows = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees
                            join department in employeesDatabase.Query().Departments
                                on departmentEmployee.dept_no equals department.DeptNo
                            where department.Name.Contains("e")
                            orderby department.Name, departmentEmployee.emp_no
                            select new
                            {
                                departmentEmployee.emp_no,
                                departmentEmployee.dept_no,
                                DepartmentName = department.Name
                            })
            .Take(20)
            .ToArray();

        var transactionRows = (from departmentEmployee in transaction.Query().DepartmentEmployees
                               join department in transaction.Query().Departments
                                   on departmentEmployee.dept_no equals department.DeptNo
                               where department.Name.Contains("e")
                               orderby department.Name, departmentEmployee.emp_no
                               select new
                               {
                                   departmentEmployee.emp_no,
                                   departmentEmployee.dept_no,
                                   DepartmentName = department.Name
                               })
            .Take(20)
            .ToArray();

        await Assert.That(FormatDepartmentRows(transactionRows)).IsEqualTo(FormatDepartmentRows(readOnlyRows));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_CountAndAnyMatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_CountAndAnyMatchInMemory),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expectedCount = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees.ToList()
                             join department in employeesDatabase.Query().Departments.ToList()
                                 on departmentEmployee.dept_no equals department.DeptNo
                             where department.Name.Contains("e")
                             select departmentEmployee).Count();
        var expectedAny = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees.ToList()
                           join department in employeesDatabase.Query().Departments.ToList()
                               on departmentEmployee.dept_no equals department.DeptNo
                           where department.Name.Contains("e")
                           select departmentEmployee).Any();

        var actualCount = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees
                           join department in employeesDatabase.Query().Departments
                               on departmentEmployee.dept_no equals department.DeptNo
                           where department.Name.Contains("e")
                           select new { departmentEmployee.emp_no }).Count();
        var actualAny = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees
                         join department in employeesDatabase.Query().Departments
                             on departmentEmployee.dept_no equals department.DeptNo
                         where department.Name.Contains("e")
                         select new { departmentEmployee.emp_no }).Any();

        await Assert.That(actualCount).IsEqualTo(expectedCount);
        await Assert.That(actualAny).IsEqualTo(expectedAny);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_PostPagingWhereMatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_PostPagingWhereMatchesInMemory),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expected = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees.ToList()
                        join department in employeesDatabase.Query().Departments.ToList()
                            on departmentEmployee.dept_no equals department.DeptNo
                        orderby departmentEmployee.emp_no
                        select new
                        {
                            departmentEmployee.emp_no,
                            departmentEmployee.dept_no,
                            DepartmentName = department.Name
                        })
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e", StringComparison.Ordinal))
            .OrderBy(row => row.dept_no)
            .ThenBy(row => row.emp_no)
            .Take(10)
            .ToArray();

        var actual = (from departmentEmployee in employeesDatabase.Query().DepartmentEmployees
                      join department in employeesDatabase.Query().Departments
                          on departmentEmployee.dept_no equals department.DeptNo
                      orderby departmentEmployee.emp_no
                      select new
                      {
                          departmentEmployee.emp_no,
                          departmentEmployee.dept_no,
                          DepartmentName = department.Name
                      })
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderBy(row => row.dept_no)
            .ThenBy(row => row.emp_no)
            .Take(10)
            .ToArray();

        await Assert.That(FormatDepartmentRows(actual)).IsEqualTo(FormatDepartmentRows(expected));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_DirectMemberKeysProjectsBothSides_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_DirectMemberKeysProjectsBothSides_MatchesInMemory),
            EmployeesFixtureProfile.FullSeeded);

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
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no, StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .ToList()
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no, StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].dept_no).IsEqualTo(expected[index].dept_no);
            await Assert.That(actual[index].DepartmentName).IsEqualTo(expected[index].DepartmentName);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_NullableValueKeyProjectsBothSides_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_NullableValueKeyProjectsBothSides_MatchesInMemory),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Join(
                employeesDatabase.Query().Employees.ToList(),
                departmentEmployee => departmentEmployee.emp_no,
                employee => employee.emp_no!.Value,
                (departmentEmployee, employee) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    employee.first_name,
                    employee.last_name
                })
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no, StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Employees,
                departmentEmployee => departmentEmployee.emp_no,
                employee => employee.emp_no!.Value,
                (departmentEmployee, employee) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    employee.first_name,
                    employee.last_name
                })
            .ToList()
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no, StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].dept_no).IsEqualTo(expected[index].dept_no);
            await Assert.That(actual[index].first_name).IsEqualTo(expected[index].first_name);
            await Assert.That(actual[index].last_name).IsEqualTo(expected[index].last_name);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_CompositePrimaryKeysThrowQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_CompositePrimaryKeysThrowQueryTranslationException),
            EmployeesFixtureProfile.FullSeeded);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .Join(
                    databaseScope.Database.Query().Managers,
                    departmentEmployee => new { departmentEmployee.dept_no, departmentEmployee.emp_no },
                    manager => new { dept_no = manager.dept_fk, manager.emp_no },
                    (departmentEmployee, manager) => new { departmentEmployee.dept_no, manager.Type })
                .ToList(),
            "Join key selector",
            "Only direct member keys");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_ComposedWhereOrderingAndPaging_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_ComposedWhereOrderingAndPaging_MatchesInMemory),
            EmployeesFixtureProfile.FullSeeded);

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
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderBy(row => row.DepartmentName, StringComparer.Ordinal)
            .ThenByDescending(row => row.emp_no)
            .Skip(1)
            .Take(20)
            .ToArray();

        var query = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderBy(row => row.DepartmentName)
            .ThenByDescending(row => row.emp_no)
            .Skip(1)
            .Take(20);

        var actual = query.ToList().ToArray();
        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(employeesDatabase, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("JOIN");
        await Assert.That(normalized).Contains("WHERE");
        await Assert.That(normalized).Contains("ORDER BY");
        var quote = databaseScope.Database.Provider.Constants.EscapeCharacter;
        await Assert.That(normalized).Contains($"{quote}t0{quote}.");
        await Assert.That(normalized).Contains($"{quote}t1{quote}.");
        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].dept_no).IsEqualTo(expected[index].dept_no);
            await Assert.That(actual[index].DepartmentName).IsEqualTo(expected[index].DepartmentName);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_CountAndAnyOverJoinedProjection_MatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_CountAndAnyOverJoinedProjection_MatchInMemory),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expectedRows = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Join(
                employeesDatabase.Query().Departments.ToList(),
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                });

        var joinedRows = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                });

        await Assert.That(joinedRows.Count(row => row.DepartmentName.StartsWith("S")))
            .IsEqualTo(expectedRows.Count(row => row.DepartmentName.StartsWith("S", StringComparison.Ordinal)));
        await Assert.That(joinedRows.Any(row => row.DepartmentName.StartsWith("S")))
            .IsEqualTo(expectedRows.Any(row => row.DepartmentName.StartsWith("S", StringComparison.Ordinal)));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_ComposedJoinedProjectionWorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_ComposedJoinedProjectionWorksFromTransactionRoot),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnlyRows = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderBy(row => row.dept_no)
            .ThenBy(row => row.emp_no)
            .Take(15)
            .ToList()
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .Join(
                transaction.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderBy(row => row.dept_no)
            .ThenBy(row => row.emp_no)
            .Take(15)
            .ToList()
            .ToArray();

        await Assert.That(transactionRows.Length).IsEqualTo(readOnlyRows.Length);
        for (var index = 0; index < readOnlyRows.Length; index++)
        {
            await Assert.That(transactionRows[index].emp_no).IsEqualTo(readOnlyRows[index].emp_no);
            await Assert.That(transactionRows[index].dept_no).IsEqualTo(readOnlyRows[index].dept_no);
            await Assert.That(transactionRows[index].DepartmentName).IsEqualTo(readOnlyRows[index].DepartmentName);
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingWhereAndOrderingMatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingWhereAndOrderingMatchInMemory),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees.ToList()
            .Join(
                employeesDatabase.Query().Departments.ToList(),
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .OrderBy(row => row.emp_no)
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e", StringComparison.Ordinal))
            .OrderByDescending(row => row.DepartmentName)
            .ThenBy(row => row.emp_no)
            .Take(10)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .OrderBy(row => row.emp_no)
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderByDescending(row => row.DepartmentName)
            .ThenBy(row => row.emp_no)
            .Take(10)
            .ToArray();

        await Assert.That(FormatDepartmentRows(actual)).IsEqualTo(FormatDepartmentRows(expected));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_RowLocalFunctionProjection_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_RowLocalFunctionProjection_MatchesInMemory),
            EmployeesFixtureProfile.FullSeeded);

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
                    NormalizedDepartmentName = department.Name.Trim()
                })
            .OrderBy(row => row.emp_no)
            .ThenBy(row => row.NormalizedDepartmentName, StringComparer.Ordinal)
            .ToArray();

        var actual = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    NormalizedDepartmentName = department.Name.Trim()
                })
            .ToArray()
            .OrderBy(row => row.emp_no)
            .ThenBy(row => row.NormalizedDepartmentName, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_ScalarRecipeExecutesWithoutPlaceholderOrOriginalExpression(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_ScalarRecipeExecutesWithoutPlaceholderOrOriginalExpression),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Join(
                employeesDatabase.Query().Departments.ToList(),
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) =>
                    departmentEmployee.dept_no + ":" + department.Name.Trim())
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var query = employeesDatabase.Query().DepartmentEmployees.Join(
            employeesDatabase.Query().Departments,
            departmentEmployee => departmentEmployee.dept_no,
            department => department.DeptNo,
            (departmentEmployee, department) =>
                departmentEmployee.dept_no + ":" + department.Name.Trim());
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, query);
        var projection = invocation.Template.Projection as QueryPlanProjection.JoinedRowLocal;

        var actual = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(
                employeesDatabase.Provider.ReadOnlyAccess,
                invocation)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.Members).IsEmpty();
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingCountAndAnyMatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingCountAndAnyMatchInMemory),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        var expectedRows = employeesDatabase.Query().DepartmentEmployees.ToList()
            .Join(
                employeesDatabase.Query().Departments.ToList(),
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .OrderBy(row => row.emp_no)
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e", StringComparison.Ordinal))
            .ToArray();

        var pagedRows = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .OrderBy(row => row.emp_no)
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e"));

        await Assert.That(pagedRows.Count()).IsEqualTo(expectedRows.Length);
        await Assert.That(pagedRows.Any()).IsEqualTo(expectedRows.Any());
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingWorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingWorksFromTransactionRoot),
            EmployeesFixtureProfile.FullSeeded);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnlyRows = employeesDatabase.Query().DepartmentEmployees
            .Join(
                employeesDatabase.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .OrderBy(row => row.emp_no)
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderBy(row => row.dept_no)
            .ThenBy(row => row.emp_no)
            .Take(10)
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .Join(
                transaction.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    departmentEmployee.dept_no,
                    DepartmentName = department.Name
                })
            .OrderBy(row => row.emp_no)
            .Take(30)
            .Where(row => row.DepartmentName.Contains("e"))
            .OrderBy(row => row.dept_no)
            .ThenBy(row => row.emp_no)
            .Take(10)
            .ToArray();

        await Assert.That(FormatDepartmentRows(transactionRows)).IsEqualTo(FormatDepartmentRows(readOnlyRows));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingRowLocalProjectionThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingRowLocalProjectionThrowsQueryTranslationException),
            EmployeesFixtureProfile.FullSeeded);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .Join(
                    databaseScope.Database.Query().Departments,
                    departmentEmployee => departmentEmployee.dept_no,
                    department => department.DeptNo,
                    (departmentEmployee, department) => new
                    {
                        departmentEmployee.emp_no,
                        departmentEmployee.dept_no,
                        DepartmentName = department.Name,
                        Label = department.Name.ToUpper()
                    })
                .OrderBy(row => row.emp_no)
                .Take(10)
                .Where(row => row.dept_no == "d001")
                .ToList(),
            "SQL-backed joined projection rows",
            "row-local joined projections");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_RelationProjectionThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_RelationProjectionThrowsQueryTranslationException),
            EmployeesFixtureProfile.FullSeeded);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().DepartmentEmployees
                .Join(
                    databaseScope.Database.Query().Departments,
                    departmentEmployee => departmentEmployee.dept_no,
                    department => department.DeptNo,
                    (departmentEmployee, department) => new
                    {
                        departmentEmployee.emp_no,
                        ManagerCount = department.Managers.Count
                    })
                .ToList(),
            "collection relation 'Managers'",
            "not supported");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupJoin_ThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupJoin_ThrowsQueryTranslationException),
            EmployeesFixtureProfile.FullSeeded);

        await AssertTranslationFailure(
            () => databaseScope.Database.Query().Departments
                .GroupJoin(
                    databaseScope.Database.Query().Managers,
                    department => department.DeptNo,
                    manager => manager.dept_fk,
                    (department, managers) => new { department.DeptNo, ManagerCount = managers.Count() })
                .ToList(),
            "GroupJoin",
            "not supported");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_FilteredInputsPreserveParametersAndComposition(TestProviderDescriptor provider)
    {
        using var scope = EmployeesTestDatabase.OpenSharedSeeded(provider,
            nameof(ExplicitInnerJoin_FilteredInputsPreserveParametersAndComposition), EmployeesFixtureProfile.FullSeeded);
        var database = scope.Database;
        var employees = database.Query().DepartmentEmployees.ToArray();
        var departments = database.Query().Departments.ToArray();

        foreach (var (filterOuter, filterInner) in new[] { (true, false), (false, true), (true, true) })
        {
            var minimum = employees.Min(x => x.emp_no);
            var maximum = minimum + 20;
            var prefix = "d00";
            var outer = database.Query().DepartmentEmployees.AsQueryable();
            var inner = database.Query().Departments.AsQueryable();
            if (filterOuter)
                outer = outer.Where(x => x.emp_no >= minimum).Where(x => x.emp_no <= maximum);
            if (filterInner)
                inner = inner.Where(x => x.DeptNo.StartsWith(prefix)).Where(x => x.DeptNo != "missing");
            var query = outer.Join(inner, x => x.dept_no, x => x.DeptNo,
                    (employee, department) => new { employee.emp_no, employee.dept_no, DepartmentName = department.Name })
                .Where(x => x.emp_no > 0).OrderBy(x => x.emp_no).ThenBy(x => x.dept_no).Take(20);

            for (var invocation = 0; invocation < 3; invocation++)
            {
                var expected = employees.Where(x => !filterOuter || (x.emp_no >= minimum && x.emp_no <= maximum))
                    .Join(departments.Where(x => !filterInner || (x.DeptNo.StartsWith(prefix, StringComparison.Ordinal) && x.DeptNo != "missing")),
                        x => x.dept_no, x => x.DeptNo,
                        (employee, department) => new { employee.emp_no, employee.dept_no, DepartmentName = department.Name })
                    .OrderBy(x => x.emp_no).ThenBy(x => x.dept_no, StringComparer.Ordinal).Take(20).ToArray();
                await Assert.That(FormatDepartmentRows(query.ToArray())).IsEqualTo(FormatDepartmentRows(expected));
                var sql = CurrentQueryTranslationInspection.BuildSql(database, query);
                await Assert.That(sql.Text).Contains("JOIN");
                await Assert.That(sql.Text).Contains("WHERE");
                if (filterOuter)
                    await Assert.That(sql.Parameters.Any(x => Equals(x.Value, minimum))).IsTrue();
                minimum += 5;
                prefix = invocation == 0 ? "d001" : "no matching department";
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_FilteredSelfJoinPreservesSourceAliasesAndMultiplicity(TestProviderDescriptor provider)
    {
        using var scope = EmployeesTestDatabase.OpenSharedSeeded(provider,
            nameof(ExplicitInnerJoin_FilteredSelfJoinPreservesSourceAliasesAndMultiplicity), EmployeesFixtureProfile.FullSeeded);
        var rows = scope.Database.Query().DepartmentEmployees.ToArray();
        var group = rows.GroupBy(x => x.dept_no).First(x => x.Count() >= 3).ToArray();
        var leftIds = group.Take(2).Select(x => x.emp_no).ToArray();
        var rightIds = group.Skip(1).Take(3).Select(x => x.emp_no).ToArray();
        var expected = rows.Where(x => leftIds.Contains(x.emp_no))
            .Join(rows.Where(x => rightIds.Contains(x.emp_no)), x => x.dept_no, x => x.dept_no,
                (left, right) => new { Left = left.emp_no, Right = right.emp_no, left.dept_no })
            .Select(x => $"{x.Left}:{x.Right}:{x.dept_no}").OrderBy(x => x).ToArray();
        var query = scope.Database.Query().DepartmentEmployees.Where(x => leftIds.Contains(x.emp_no))
            .Join(scope.Database.Query().DepartmentEmployees.Where(x => rightIds.Contains(x.emp_no)),
                x => x.dept_no, x => x.dept_no,
                (left, right) => new { Left = left.emp_no, Right = right.emp_no, left.dept_no });
        var actual = query.ToArray().Select(x => $"{x.Left}:{x.Right}:{x.dept_no}").OrderBy(x => x).ToArray();
        await Assert.That(expected).IsNotEmpty();
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async Task ExplicitInnerJoin_UnsupportedInputOperatorsRemainRejected()
    {
        using var scope = EmployeesTestDatabase.OpenSharedSeeded(TestProviderMatrix.SQLiteInMemory,
            nameof(ExplicitInnerJoin_UnsupportedInputOperatorsRemainRejected), EmployeesFixtureProfile.FullSeeded);
        var database = scope.Database;
        await AssertTranslationFailure(() => database.Query().DepartmentEmployees.Take(1)
            .Join(database.Query().Departments, x => x.dept_no, x => x.DeptNo,
                (employee, department) => new { employee.emp_no, department.Name }).ToArray(),
            "Join outer sequence", "optional Where filters");
        await AssertTranslationFailure(() => database.Query().DepartmentEmployees
            .Join(database.Query().Departments.OrderBy(x => x.DeptNo), x => x.dept_no, x => x.DeptNo,
                (employee, department) => new { employee.emp_no, department.Name }).ToArray(),
            "Join inner sequence", "optional Where filters");
    }

    private static string FormatDepartmentRows<T>(T[] rows)
    {
        return string.Join(
            "|",
            rows.Select(row =>
            {
                var type = row!.GetType();
                var employeeNumber = type.GetProperty("emp_no")!.GetValue(row);
                var departmentNumber = type.GetProperty("dept_no")!.GetValue(row);
                var departmentName = type.GetProperty("DepartmentName")!.GetValue(row);
                return $"{employeeNumber}:{departmentNumber}:{departmentName}";
            }));
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
