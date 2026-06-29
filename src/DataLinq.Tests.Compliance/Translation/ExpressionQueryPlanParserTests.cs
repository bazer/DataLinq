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
    private static readonly string[] BannedLegacyParserTerms =
    [
        "QueryModel",
        "WhereClause",
        "OrderByClause",
        "ResultOperator",
        "QuerySourceReferenceExpression"
    ];

    [Test]
    public async Task ExpressionParser_BasicQueryShapeParsesToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_BasicQueryShapeParsesToDataLinqPlan),
            EmployeesSeedMode.Bogus);

        var threshold = 10010;
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no != threshold && !Array.Empty<int>().Contains(x.emp_no!.Value))
            .OrderBy(x => x.last_name)
            .ThenByDescending(x => x.emp_no)
            .Skip(2)
            .Take(3)
            .Select(x => new { x.emp_no, x.first_name });

        await AssertParserProducesDataLinqPlan(databaseScope.Database, query);

        var querySyntax =
            from departmentEmployee in databaseScope.Database.Query().DepartmentEmployees
            join department in databaseScope.Database.Query().Departments
                on departmentEmployee.dept_no equals department.DeptNo
            where department.Name.StartsWith("S")
            orderby department.Name, departmentEmployee.emp_no
            select new
            {
                departmentEmployee.emp_no,
                DepartmentName = department.Name
            };

        await AssertParserProducesDataLinqPlan(databaseScope.Database, querySyntax);
    }

    [Test]
    public async Task ExpressionParser_ResultAndAggregateShapesParseToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_ResultAndAggregateShapesParseToDataLinqPlan),
            EmployeesSeedMode.Bogus);

        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Employees.Count());
        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Employees.Any());
        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Employees.FirstOrDefault());
        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Employees.Last());
        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Employees.SingleOrDefault(x => x.emp_no == 12345));
        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Managers
            .Where(x => x.dept_fk.StartsWith("d00"))
            .Sum(x => x.emp_no));
    }

    [Test]
    public async Task ExpressionParser_GroupedAggregateProjectionParsesToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_GroupedAggregateProjectionParsesToDataLinqPlan),
            EmployeesSeedMode.Bogus);

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .Where(x => x.dept_no.StartsWith("d00"))
                .GroupBy(x => x.dept_no)
                .Select(group => new
                {
                    DeptNo = group.Key,
                    Count = group.Count()
                }));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new
                {
                    DeptNo = group.Key,
                    SumEmployeeNumbers = group.Sum(row => row.emp_no),
                    MinEmployeeNumber = group.Min(row => row.emp_no),
                    MaxEmployeeNumber = group.Max(row => row.emp_no),
                    AverageEmployeeNumber = group.Average(row => row.emp_no)
                }));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Where(group => group.Count() > 0 && group.Sum(row => row.emp_no) > 0)
                .Select(group => new
                {
                    DeptNo = group.Key,
                    Count = group.Count(),
                    SumEmployeeNumbers = group.Sum(row => row.emp_no)
                }));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new
                {
                    DeptNo = group.Key,
                    Count = group.Count()
                })
                .Where(row => row.Count > 0)
                .OrderByDescending(row => row.Count)
                .ThenBy(row => row.DeptNo)
                .Skip(1)
                .Take(2));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            () => databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new
                {
                    DeptNo = group.Key,
                    Count = group.Count()
                })
                .Where(row => row.Count > 0)
                .Count());

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
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
                }));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
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
                    Count = group.Count(),
                    SumEmployeeNumbers = group.Sum(row => row.emp_no)
                }));
    }

    [Test]
    public async Task ExpressionParser_RelationAndLocalMembershipShapesParseToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_RelationAndLocalMembershipShapesParseToDataLinqPlan),
            EmployeesSeedMode.Bogus);

        var managerNumber = 110022;
        var localIds = new[] { new LocalEmployeeId(10001), new LocalEmployeeId(10002) };

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Departments
                .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
                .Select(department => department.DeptNo));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => localIds.Any(id => id.Value == x.emp_no!.Value)));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => x.dept_manager.Count() == 0));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => !(x.dept_manager.Count() == 0)));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .Select(row => new
                {
                    row.emp_no,
                    DepartmentName = row.departments.Name
                }));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees.Select(row => row.departments.Name));
    }

    [Test]
    public async Task ExpressionParser_StringDateNullableAndBooleanShapesParseToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_StringDateNullableAndBooleanShapesParseToDataLinqPlan),
            EmployeesSeedMode.Bogus);

        var login = new TimeOnly(9, 15, 0);
        var testDate = new DateOnly(2021, 7, 3);
        var testDateTime = new DateTime(2021, 7, 3, 11, 23, 42, 123);

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Where(x =>
                !x.DeptNo.StartsWith("d00") ||
                x.Name.Contains("Sales") ||
                string.IsNullOrWhiteSpace(x.Name)));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees.Where(x =>
                x.from_date.Year == testDate.Year &&
                x.from_date.DayOfWeek == testDate.DayOfWeek));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x =>
                x.last_login != null &&
                x.last_login != login &&
                x.created_at!.Value.Minute == testDateTime.Minute));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x =>
                x.emp_no.HasValue &&
                (x.gender == Employee.Employeegender.M || x.gender == Employee.Employeegender.F)));
    }

    [Test]
    public async Task ExpressionParser_LocalSequenceAndAggregateVariantsParseToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_LocalSequenceAndAggregateVariantsParseToDataLinqPlan),
            EmployeesSeedMode.Bogus);

        var employeeNumbers = new[] { 10001, 10002, 10003 };
        var departmentNumbers = new[] { "d001", "d002" };

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Where(x => employeeNumbers.Contains(x.emp_no!.Value)));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Where(x => !departmentNumbers.Contains(x.DeptNo)));

        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Managers.Min(x => x.emp_no));
        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Managers.Max(x => x.emp_no));
        await AssertParserProducesDataLinqPlan(databaseScope.Database, () => databaseScope.Database.Query().Managers.Average(x => x.emp_no));
    }

    [Test]
    public async Task ExpressionParser_BarePagingShapesParseToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_BarePagingShapesParseToDataLinqPlan),
            EmployeesSeedMode.Bogus);

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Take(5));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            databaseScope.Database.Query().Employees.Skip(10));

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees.Take(5).Count());

        await AssertParserProducesDataLinqPlan(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees.Skip(10).Any());
    }

    [Test]
    public async Task ExpressionParser_ExplicitJoinShapeParsesToDataLinqPlan()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_ExplicitJoinShapeParsesToDataLinqPlan),
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

        await AssertParserProducesDataLinqPlan(databaseScope.Database, query);
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

        var productionQuery = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no != threshold && x.first_name.StartsWith("A"))
            .OrderBy(x => x.last_name)
            .Take(5)
            .Select(x => new { x.emp_no, x.first_name });

        await AssertParserMatchesProductionRoot(databaseScope.Database, productionQuery, expressionQuery);

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

        var productionJoin = databaseScope.Database.Query().DepartmentEmployees.Join(
            databaseScope.Database.Query().Departments,
            departmentEmployee => departmentEmployee.dept_no,
            department => department.DeptNo,
            (departmentEmployee, department) => new
            {
                departmentEmployee.emp_no,
                departmentEmployee.dept_no,
                DepartmentName = department.Name
            });

        await AssertParserMatchesProductionRoot(databaseScope.Database, productionJoin, expressionJoin);
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

        await AssertParserMatchesProductionRoot(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees.Count(x => x.emp_no > 10010),
            () => employees.Count(x => x.emp_no > 10010));

        await AssertParserMatchesProductionRoot(
            databaseScope.Database,
            () => databaseScope.Database.Query().Managers
                .Where(x => x.dept_fk.StartsWith("d00"))
                .Average(x => x.emp_no),
            () => managers
                .Where(x => x.dept_fk.StartsWith("d00"))
                .Average(x => x.emp_no));
    }

    [Test]
    public async Task ExpressionParser_PostPagingFilterRecordsPushdown()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_PostPagingFilterRecordsPushdown),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Skip(1)
            .Where(x => x.gender == Employee.Employeegender.M);

        var plan = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);
        var snapshot = QueryPlanDebugWriter.Write(plan);

        await Assert.That(snapshot).Contains("pushdown");
        await Assert.That(snapshot).Contains("skip");
        await Assert.That(snapshot).Contains("where compare(column(s0.gender:Employeegender)");
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
            databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Where(group => group.Any(row => row.emp_no > 10000))
                .Select(group => new { group.Key, Count = group.Count() }),
            "Grouped predicate expression",
            "Only comparisons over group.Key");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => new { x.dept_no, x.emp_no })
                .Select(group => new { group.Key, Count = group.Count() }),
            "Whole composite group.Key projection",
            "group.Key.Member");

        await AssertParserFailure(
            databaseScope.Database,
            () => databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new { group.Key, Count = group.Count() })
                .FirstOrDefault(),
            "Terminal operator 'FirstOrDefault'",
            "grouped aggregate projections");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .GroupBy(x => x.dept_no)
                .Select(group => new { group.Key, Count = group.Count() })
                .Take(1)
                .Where(row => row.Count > 0),
            "after Skip(...) or Take(...) over grouped aggregate projection rows",
            "not supported yet");

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
                .OrderBy(row => row.emp_no)
                .Take(10)
                .Where(row => row.dept_no == "d001"),
            "after Skip(...) or Take(...) over a joined query",
            "not supported");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Select(department => department.Managers),
            "Collection relation property 'Managers'",
            "row-local LINQ Select projection");

        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().Departments.Select(department =>
                databaseScope.Database.Query().Managers.Count(manager => manager.dept_fk == department.DeptNo)),
            "Nested database query projection",
            "LINQ Select projection");

        var filteredDepartments = databaseScope.Database.Query().Departments
            .Where(department => department.Name == "Sales");
        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().DepartmentEmployees
                .Join(
                    filteredDepartments,
                    departmentEmployee => departmentEmployee.dept_no,
                    department => department.DeptNo,
                    (departmentEmployee, department) => new
                    {
                        departmentEmployee.emp_no,
                        departmentEmployee.dept_no,
                        DepartmentName = department.Name
                    }),
            "Join inner sequence",
            "Only direct DataLinq query sources");

        var employeeNumbers = databaseScope.Database.Query().Employees
            .Select(employee => employee.emp_no!.Value);
        await AssertParserFailure(
            databaseScope.Database,
            databaseScope.Database.Query().Managers
                .Where(manager => employeeNumbers.Contains(manager.emp_no)),
            "IQueryable expression",
            "local sequence");
    }

    [Test]
    public async Task ExpressionParser_LocalMethodEvaluationFailsWithoutInvokingMethod()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_LocalMethodEvaluationFailsWithoutInvokingMethod),
            EmployeesSeedMode.Bogus);

        var probe = new LocalMethodProbe();

        var scalarQuery = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no == probe.GetEmployeeNumber());

        var scalarException = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, scalarQuery));

        await Assert.That(scalarException).IsNotNull();
        await Assert.That(scalarException!.Message).Contains("Local method call 'GetEmployeeNumber'");
        await Assert.That(probe.EmployeeNumberInvocationCount).IsEqualTo(0);

        var sequenceQuery = databaseScope.Database.Query().Employees
            .Where(x => probe.GetEmployeeNumbers().Contains(x.emp_no!.Value));

        var sequenceException = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, sequenceQuery));

        await Assert.That(sequenceException).IsNotNull();
        await Assert.That(sequenceException!.Message).Contains("Local method call 'GetEmployeeNumbers'");
        await Assert.That(probe.EmployeeNumbersInvocationCount).IsEqualTo(0);
    }

    [Test]
    public async Task ExpressionParser_AotStrictLocalEvaluationRejectsCapturedMemberReflection()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_AotStrictLocalEvaluationRejectsCapturedMemberReflection),
            EmployeesSeedMode.Bogus);

        var threshold = 10010;
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no == threshold);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(
                databaseScope.Database.Provider.Metadata,
                query.Expression,
                typeof(Employee),
                ExpressionQueryPlanParserOptions.AotStrict));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("requires compatibility member reflection");
    }

    private static async Task AssertParserProducesDataLinqPlan<T>(Database<EmployeesDb> database, IQueryable<T> query)
    {
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, query));

        await AssertNoLegacyParserTerms(expressionSnapshot);
    }

    private static async Task AssertParserMatchesProductionRoot<T>(Database<EmployeesDb> database, IQueryable<T> productionQuery, IQueryable<T> expressionQuery)
    {
        var productionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, productionQuery));
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database.Provider.Metadata, expressionQuery.Expression, typeof(T)));

        await Assert.That(expressionSnapshot).IsEqualTo(productionSnapshot);
        await AssertNoLegacyParserTerms(expressionSnapshot);
    }

    private static async Task AssertParserProducesDataLinqPlan<TResult>(Database<EmployeesDb> database, Expression<Func<TResult>> query)
    {
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, query));

        await AssertNoLegacyParserTerms(expressionSnapshot);
    }

    private static async Task AssertParserMatchesProductionRoot<TResult>(
        Database<EmployeesDb> database,
        Expression<Func<TResult>> productionQuery,
        Expression<Func<TResult>> expressionQuery)
    {
        var productionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database, productionQuery));
        var expressionSnapshot = QueryPlanDebugWriter.Write(ExpressionQueryPlanParser.Convert(database.Provider.Metadata, expressionQuery.Body, typeof(TResult)));

        await Assert.That(expressionSnapshot).IsEqualTo(productionSnapshot);
        await AssertNoLegacyParserTerms(expressionSnapshot);
    }

    private static async Task AssertNoLegacyParserTerms(string snapshot)
    {
        foreach (var term in BannedLegacyParserTerms)
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

    private static async Task AssertParserFailure<TResult>(Database<EmployeesDb> database, Expression<Func<TResult>> query, params string[] expectedMessageFragments)
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

    private sealed class LocalMethodProbe
    {
        public int EmployeeNumberInvocationCount { get; private set; }

        public int EmployeeNumbersInvocationCount { get; private set; }

        public int GetEmployeeNumber()
        {
            EmployeeNumberInvocationCount++;
            return 10001;
        }

        public int[] GetEmployeeNumbers()
        {
            EmployeeNumbersInvocationCount++;
            return [10001, 10002];
        }
    }
}
