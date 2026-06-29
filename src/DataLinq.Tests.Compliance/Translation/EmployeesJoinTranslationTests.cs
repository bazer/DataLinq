using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesJoinTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_ComposesAndProjectsSqlRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_ComposesAndProjectsSqlRows),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_WorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_WorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_CountAndAnyMatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_CountAndAnyMatchInMemory),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QuerySyntaxInnerJoin_PostPagingWhereMatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(QuerySyntaxInnerJoin_PostPagingWhereMatchesInMemory),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_DirectMemberKeysProjectsBothSides_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_DirectMemberKeysProjectsBothSides_MatchesInMemory),
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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_NullableValueKeyProjectsBothSides_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_NullableValueKeyProjectsBothSides_MatchesInMemory),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_CompositePrimaryKeysThrowQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_CompositePrimaryKeysThrowQueryTranslationException),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_ComposedWhereOrderingAndPaging_MatchesInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_ComposedWhereOrderingAndPaging_MatchesInMemory),
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
        await Assert.That(normalized).Contains("t0.");
        await Assert.That(normalized).Contains("t1.");
        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].dept_no).IsEqualTo(expected[index].dept_no);
            await Assert.That(actual[index].DepartmentName).IsEqualTo(expected[index].DepartmentName);
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_CountAndAnyOverJoinedProjection_MatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_CountAndAnyOverJoinedProjection_MatchInMemory),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_ComposedJoinedProjectionWorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_ComposedJoinedProjectionWorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingWhereAndOrderingMatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingWhereAndOrderingMatchInMemory),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingCountAndAnyMatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingCountAndAnyMatchInMemory),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingWorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingWorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_PostPagingRowLocalProjectionThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_PostPagingRowLocalProjectionThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitInnerJoin_RelationProjectionThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_RelationProjectionThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

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
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GroupJoin_ThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(GroupJoin_ThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

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
