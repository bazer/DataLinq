using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class ExpressionQueryPlanParserTests
{
    private static readonly string[] BannedRemotionTerms =
    [
        "QueryModel",
        "WhereClause",
        "OrderByClause",
        "ResultOperator",
        "QuerySourceReferenceExpression"
    ];

    [Test]
    public async Task ExpressionParser_BasicQueryShapeMatchesRemotionPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_BasicQueryShapeMatchesRemotionPlan),
            EmployeesSeedMode.Bogus);

        var threshold = 10010;
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no != threshold && !Array.Empty<int>().Contains(x.emp_no!.Value))
            .OrderBy(x => x.last_name)
            .ThenByDescending(x => x.emp_no)
            .Skip(2)
            .Take(3)
            .Select(x => new { x.emp_no, x.first_name });

        await AssertParserMatchesRemotion(databaseScope.Database, query);
    }

    [Test]
    public async Task ExpressionParser_ResultAndAggregateShapesMatchRemotionPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_ResultAndAggregateShapesMatchRemotionPlan),
            EmployeesSeedMode.Bogus);

        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Employees.Count());
        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Employees.Any());
        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Employees.FirstOrDefault());
        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Employees.Last());
        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Employees.SingleOrDefault(x => x.emp_no == 12345));
        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Managers
            .Where(x => x.dept_fk.StartsWith("d00"))
            .Sum(x => x.emp_no));
    }

    [Test]
    public async Task ExpressionParser_RelationAndLocalMembershipShapesMatchRemotionPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_RelationAndLocalMembershipShapesMatchRemotionPlan),
            EmployeesSeedMode.Bogus);

        var managerNumber = 110022;
        var localIds = new[] { new LocalEmployeeId(10001), new LocalEmployeeId(10002) };

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Departments
                .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
                .Select(department => department.DeptNo));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => localIds.Any(id => id.Value == x.emp_no!.Value)));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => x.dept_manager.Count() == 0));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => !(x.dept_manager.Count() == 0)));
    }

    [Test]
    public async Task ExpressionParser_StringDateNullableAndBooleanShapesMatchRemotionPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_StringDateNullableAndBooleanShapesMatchRemotionPlan),
            EmployeesSeedMode.Bogus);

        var login = new TimeOnly(9, 15, 0);
        var testDate = new DateOnly(2021, 7, 3);
        var testDateTime = new DateTime(2021, 7, 3, 11, 23, 42, 123);

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Where(x =>
                !x.DeptNo.StartsWith("d00") ||
                x.Name.Contains("Sales") ||
                string.IsNullOrWhiteSpace(x.Name)));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees.Where(x =>
                x.from_date.Year == testDate.Year &&
                x.from_date.DayOfWeek == testDate.DayOfWeek));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x =>
                x.last_login != null &&
                x.last_login != login &&
                x.created_at!.Value.Minute == testDateTime.Minute));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x =>
                x.emp_no.HasValue &&
                (x.gender == Employee.Employeegender.M || x.gender == Employee.Employeegender.F)));
    }

    [Test]
    public async Task ExpressionParser_LocalSequenceAndAggregateVariantsMatchRemotionPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_LocalSequenceAndAggregateVariantsMatchRemotionPlan),
            EmployeesSeedMode.Bogus);

        var employeeNumbers = new[] { 10001, 10002, 10003 };
        var departmentNumbers = new[] { "d001", "d002" };

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => employeeNumbers.Contains(x.emp_no!.Value)));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Where(x => !departmentNumbers.Contains(x.DeptNo)));

        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Managers.Min(x => x.emp_no));
        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Managers.Max(x => x.emp_no));
        await AssertParserMatchesRemotion(databaseScope.Database, () => databaseScope.Database.Query().Managers.Average(x => x.emp_no));
    }

    [Test]
    public async Task ExpressionParser_ExplicitJoinShapeMatchesRemotionPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_ExplicitJoinShapeMatchesRemotionPlan),
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
                });

        await AssertParserMatchesRemotion(databaseScope.Database, query);
    }

    [Test]
    public async Task DataLinqExpressionProvider_QueryableChainsParseToMatchingPlans()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(DataLinqExpressionProvider_QueryableChainsParseToMatchingPlans),
            EmployeesSeedMode.Bogus);

        var provider = new ExpressionQueryPlanProvider(databaseScope.Database.Provider.Metadata);
        var employees = provider.CreateRoot<Employee>();
        var departmentEmployees = provider.CreateRoot<Dept_emp>();
        var departments = provider.CreateRoot<Department>();
        var threshold = 10010;

        var expressionQuery = employees
            .Where(x => x.emp_no != threshold && x.first_name.StartsWith("A"))
            .OrderBy(x => x.last_name)
            .Take(5)
            .Select(x => new { x.emp_no, x.first_name });

        var remotionQuery = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no != threshold && x.first_name.StartsWith("A"))
            .OrderBy(x => x.last_name)
            .Take(5)
            .Select(x => new { x.emp_no, x.first_name });

        await AssertParserMatchesRemotion(databaseScope.Database, remotionQuery, expressionQuery);

        var expressionJoin = departmentEmployees.Join(
            departments,
            departmentEmployee => departmentEmployee.dept_no,
            department => department.DeptNo,
            (departmentEmployee, department) => new
            {
                departmentEmployee.emp_no,
                departmentEmployee.dept_no,
                DepartmentName = department.Name
            });

        var remotionJoin = databaseScope.Database.Query().DepartmentEmployees.Join(
            databaseScope.Database.Query().Departments,
            departmentEmployee => departmentEmployee.dept_no,
            department => department.DeptNo,
            (departmentEmployee, department) => new
            {
                departmentEmployee.emp_no,
                departmentEmployee.dept_no,
                DepartmentName = department.Name
            });

        await AssertParserMatchesRemotion(databaseScope.Database, remotionJoin, expressionJoin);
    }

    [Test]
    public async Task DataLinqExpressionProvider_TerminalExpressionsParseToMatchingPlans()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(DataLinqExpressionProvider_TerminalExpressionsParseToMatchingPlans),
            EmployeesSeedMode.Bogus);

        var provider = new ExpressionQueryPlanProvider(databaseScope.Database.Provider.Metadata);
        var employees = provider.CreateRoot<Employee>();
        var managers = provider.CreateRoot<Manager>();

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees.Count(x => x.emp_no > 10010),
            () => employees.Count(x => x.emp_no > 10010));

        await AssertParserMatchesRemotion(
            databaseScope.Database,
            () => databaseScope.Database.Query().Managers
                .Where(x => x.dept_fk.StartsWith("d00"))
                .Average(x => x.emp_no),
            () => managers
                .Where(x => x.dept_fk.StartsWith("d00"))
                .Average(x => x.emp_no));
    }

    [Test]
    public async Task ExpressionParser_PostPagingFilterKeepsFocusedDiagnostic()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_PostPagingFilterKeepsFocusedDiagnostic),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Skip(1)
            .Where(x => x.gender == Employee.Employeegender.M);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("after Skip(...) or Take(...)");
        await Assert.That(exception.Message).Contains("subquery pushdown");
    }

    [Test]
    public async Task ExpressionParser_UnsupportedShapesKeepFocusedDiagnostics()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_UnsupportedShapesKeepFocusedDiagnostics),
            EmployeesSeedMode.Bogus);

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.GroupBy(x => x.gender),
            "GroupBy",
            "not supported");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.GroupJoin(
                databaseScope.Database.Query().Managers,
                department => department.DeptNo,
                manager => manager.dept_fk,
                (department, managers) => new { department.DeptNo, ManagerCount = managers.Count() }),
            "GroupJoin",
            "not supported");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
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
                .Where(row => row.dept_no == "d001"),
            "Join queries currently support only the Join body clause",
            "Filtering");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Select(department => department.Managers),
            "Relation property 'Managers'",
            "LINQ Select projection");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Select(department =>
                databaseScope.Database.Query().Managers.Count(manager => manager.dept_fk == department.DeptNo)),
            "Nested database query projection",
            "LINQ Select projection");
    }

    private static async Task AssertParserMatchesRemotion<T>(Database<EmployeesDb> database, IQueryable<T> query)
    {
        var remotionSnapshot = QueryPlanDebugWriter.Write(RemotionQueryPlanAdapter.Convert(database, query));
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, query));

        await Assert.That(expressionSnapshot).IsEqualTo(remotionSnapshot);
        await AssertNoRemotionTerms(expressionSnapshot);
    }

    private static async Task AssertParserMatchesRemotion<T>(Database<EmployeesDb> database, IQueryable<T> remotionQuery, IQueryable<T> expressionQuery)
    {
        var remotionSnapshot = QueryPlanDebugWriter.Write(RemotionQueryPlanAdapter.Convert(database, remotionQuery));
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database.Provider.Metadata, expressionQuery.Expression, typeof(T)));

        await Assert.That(expressionSnapshot).IsEqualTo(remotionSnapshot);
        await AssertNoRemotionTerms(expressionSnapshot);
    }

    private static async Task AssertParserMatchesRemotion<TResult>(Database<EmployeesDb> database, Expression<Func<TResult>> query)
    {
        var remotionSnapshot = QueryPlanDebugWriter.Write(RemotionQueryPlanAdapter.Convert(database, query));
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, query));

        await Assert.That(expressionSnapshot).IsEqualTo(remotionSnapshot);
        await AssertNoRemotionTerms(expressionSnapshot);
    }

    private static async Task AssertParserMatchesRemotion<TResult>(
        Database<EmployeesDb> database,
        Expression<Func<TResult>> remotionQuery,
        Expression<Func<TResult>> expressionQuery)
    {
        var remotionSnapshot = QueryPlanDebugWriter.Write(RemotionQueryPlanAdapter.Convert(database, remotionQuery));
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database.Provider.Metadata, expressionQuery.Body, typeof(TResult)));

        await Assert.That(expressionSnapshot).IsEqualTo(remotionSnapshot);
        await AssertNoRemotionTerms(expressionSnapshot);
    }

    private static async Task AssertNoRemotionTerms(string snapshot)
    {
        foreach (var term in BannedRemotionTerms)
            await Assert.That(snapshot).DoesNotContain(term);
    }

    private static async Task AssertParserFailure<T>(Database<EmployeesDb> database, IQueryable<T> query, params string[] expectedMessageFragments)
    {
        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(database, query));

        await Assert.That(exception).IsNotNull();
        foreach (var fragment in expectedMessageFragments)
            await Assert.That(exception!.Message).Contains(fragment);
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

    private sealed record LocalEmployeeId(int Value);
}
