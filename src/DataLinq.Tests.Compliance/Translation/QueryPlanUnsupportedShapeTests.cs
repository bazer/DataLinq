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
    public async Task ParserRejectsFilterAfterProjection()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsFilterAfterProjection),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .Select(x => new { x.emp_no })
            .Where(x => x.emp_no > 0);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("after Select");
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
    public async Task ParserRejectsPostPagingFilterOverRowLocalExplicitJoin()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsPostPagingFilterOverRowLocalExplicitJoin),
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
                        DepartmentName = department.Name,
                        Label = department.Name.ToUpper()
                    })
            .OrderBy(row => row.emp_no)
            .Take(10)
            .Where(row => row.dept_no == "d001");

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("SQL-backed joined projection rows");
        await Assert.That(exception.Message).Contains("row-local joined projections");
    }

    [Test]
    public async Task ParserRejectsQuerySyntaxJoinProjectionOfWholeSourceEntities()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsQuerySyntaxJoinProjectionOfWholeSourceEntities),
            EmployeesSeedMode.Bogus);

        var query =
            from departmentEmployee in databaseScope.Database.Query().DepartmentEmployees
            join department in databaseScope.Database.Query().Departments
                on departmentEmployee.dept_no equals department.DeptNo
            where department.Name.Contains("e")
            select new
            {
                departmentEmployee,
                department
            };

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("whole source entities");
        await Assert.That(exception.Message).Contains("not supported");
    }

    [Test]
    public async Task ParserRejectsComputedQuerySyntaxJoinProjection()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ParserRejectsComputedQuerySyntaxJoinProjection),
            EmployeesSeedMode.Bogus);

        var query =
            from departmentEmployee in databaseScope.Database.Query().DepartmentEmployees
            join department in databaseScope.Database.Query().Departments
                on departmentEmployee.dept_no equals department.DeptNo
            where department.Name.Contains("e")
            select new
            {
                Label = departmentEmployee.dept_no + ":" + department.Name
            };

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("transparent identifiers");
        await Assert.That(exception.Message).Contains("SQL-backed projection rows");
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
        await Assert.That(exception!.Message).Contains("Collection relation property 'Managers'");
        await Assert.That(exception.Message).Contains("row-local LINQ Select projection");
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
        await Assert.That(exception!.Message).Contains("collection relation 'Managers'");
        await Assert.That(exception.Message).Contains("not supported");
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
