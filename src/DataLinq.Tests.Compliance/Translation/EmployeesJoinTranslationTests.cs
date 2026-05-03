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
    public async Task ExplicitInnerJoin_CompositeKeysThrowQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_CompositeKeysThrowQueryTranslationException),
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
    public async Task ExplicitInnerJoin_FilterOverJoinedResultThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExplicitInnerJoin_FilterOverJoinedResultThrowsQueryTranslationException),
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
                        DepartmentName = department.Name
                    })
                .Where(row => row.dept_no == "d001")
                .ToList(),
            "Join queries currently support only the Join body clause",
            "Filtering");
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
            "Relation property 'Managers'",
            "LINQ Select projection");
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
