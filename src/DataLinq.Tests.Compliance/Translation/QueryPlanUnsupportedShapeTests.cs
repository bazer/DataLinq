using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class QueryPlanUnsupportedShapeTests
{
    [Test]
    public async Task ParserRejectsPostPagingFilter()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsPostPagingFilter),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .Skip(1)
            .Where(x => x.emp_no > 0);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("LINQ operators after Skip(...) or Take(...)");
    }

    [Test]
    public async Task ParserRejectsGroupJoin()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsGroupJoin),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Departments
            .GroupJoin(
                databaseScope.Database.Query().Managers,
                department => department.DeptNo,
                manager => manager.dept_fk,
                (department, managers) => new { department.DeptNo, ManagerCount = managers.Count() });

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("GroupJoin");
        await Assert.That(exception.Message).Contains("not supported");
    }

    [Test]
    public async Task ParserRejectsFilterOverExplicitJoin()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsFilterOverExplicitJoin),
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
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Join queries currently support only the Join body clause");
        await Assert.That(exception.Message).Contains("Filtering");
    }

    [Test]
    public async Task ParserRejectsRelationPropertyProjection()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsRelationPropertyProjection),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Departments
            .Select(department => department.Managers);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Relation property 'Managers'");
        await Assert.That(exception.Message).Contains("LINQ Select projection");
    }

    [Test]
    public async Task ParserRejectsNestedDatabaseSubqueryProjection()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsNestedDatabaseSubqueryProjection),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Departments
            .Select(department => databaseScope.Database.Query().Managers.Count(manager => manager.dept_fk == department.DeptNo));

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Nested database query projection");
        await Assert.That(exception.Message).Contains("LINQ Select projection");
    }

    [Test]
    public async Task ParserRejectsRelationPropertyProjectionInsideExplicitJoinSelector()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsRelationPropertyProjectionInsideExplicitJoinSelector),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees
            .Join(
                databaseScope.Database.Query().Departments,
                departmentEmployee => departmentEmployee.dept_no,
                department => department.DeptNo,
                (departmentEmployee, department) => new
                {
                    departmentEmployee.emp_no,
                    ManagerCount = department.Managers.Count
                });

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Relation property 'Managers'");
        await Assert.That(exception.Message).Contains("LINQ Select projection");
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
