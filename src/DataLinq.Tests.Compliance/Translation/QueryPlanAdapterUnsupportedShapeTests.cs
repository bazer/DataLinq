using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class QueryPlanAdapterUnsupportedShapeTests
{
    [Test]
    public async Task AdapterRejectsPostPagingFilter()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(AdapterRejectsPostPagingFilter),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .Skip(1)
            .Where(x => x.emp_no > 0);

        var exception = Capture<QueryTranslationException>(() =>
            RemotionQueryPlanAdapter.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("LINQ operators after Skip(...) or Take(...)");
    }

    [Test]
    public async Task AdapterRejectsGroupJoin()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(AdapterRejectsGroupJoin),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Departments
            .GroupJoin(
                databaseScope.Database.Query().Managers,
                department => department.DeptNo,
                manager => manager.dept_fk,
                (department, managers) => new { department.DeptNo, ManagerCount = managers.Count() });

        var exception = Capture<QueryTranslationException>(() =>
            RemotionQueryPlanAdapter.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("GroupJoin");
        await Assert.That(exception.Message).Contains("not supported");
    }

    [Test]
    public async Task AdapterRejectsFilterOverExplicitJoin()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(AdapterRejectsFilterOverExplicitJoin),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
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
            .Where(row => row.dept_no == "d001");

        var exception = Capture<QueryTranslationException>(() =>
            RemotionQueryPlanAdapter.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Join queries currently support only the Join body clause");
        await Assert.That(exception.Message).Contains("Filtering");
    }

    private static TException? Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return null;
        }
        catch (TException exception)
        {
            return exception;
        }
    }
}
