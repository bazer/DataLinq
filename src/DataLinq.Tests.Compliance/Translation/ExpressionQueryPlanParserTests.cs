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
    public async Task ExpressionParser_RejectsCapturedRelationCountThresholdsThatWouldChangeTemplateShape()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_RejectsCapturedRelationCountThresholdsThatWouldChangeTemplateShape),
            EmployeesSeedMode.Bogus);

        var threshold = 0;
        var query = databaseScope.Database.Query().Employees
            .Where(employee => employee.dept_manager.Count() == threshold);

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Captured or computed threshold");
        await Assert.That(exception.Message).Contains("without an exact scalar-value specialization");
    }

    [Test]
    public async Task ExpressionParser_NormalizesLocalBooleanPredicatesIntoInvocationValues()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_NormalizesLocalBooleanPredicatesIntoInvocationValues),
            EmployeesSeedMode.Bogus);

        var includeRows = true;
        var query = databaseScope.Database.Query().Employees
            .Where(_ => includeRows);

        var invocation = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);
        var snapshot = QueryPlanDebugWriter.WriteTemplate(invocation.Template);
        var scalar = invocation.Values.Items.OfType<QueryPlanInvocationValue.Scalar>().Single();

        await Assert.That(snapshot).Contains("compare(scalar-binding(p0:Boolean) == intrinsic(true:Boolean))");
        await Assert.That(snapshot).DoesNotContain("fixed(true)");
        await Assert.That((bool)scalar.Value!).IsTrue();
    }

    [Test]
    public async Task ExpressionParser_EmptyUnsupportedLocalPredicateRetainsSequenceShapeSpecialization()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_EmptyUnsupportedLocalPredicateRetainsSequenceShapeSpecialization),
            EmployeesSeedMode.Bogus);

        var ids = Array.Empty<int>();
        var query = databaseScope.Database.Query().Employees
            .Where(employee => ids.Any(id => id > 1000 && id == employee.emp_no!.Value));

        var invocation = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);
        var where = invocation.Template.Operations.OfType<QueryPlanOperation.Where>().Single();
        var specialization = invocation.Template.Specialization.Items
            .OfType<QueryPlanBindingSpecialization.LocalSequenceShape>()
            .Single();
        var values = invocation.Values.Items.OfType<QueryPlanInvocationValue.LocalSequence>().Single();

        await Assert.That(where.Predicate).IsEqualTo(new QueryPlanPredicate.Fixed(false));
        await Assert.That(specialization.Count).IsEqualTo(0);
        await Assert.That(specialization.NullCount).IsEqualTo(0);
        await Assert.That(values.Values).IsEmpty();
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
        var snapshot = QueryPlanDebugWriter.WriteTemplate(plan.Template);

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
                        DepartmentName = department.Name,
                        Label = department.Name.ToUpper()
                    })
                .OrderBy(row => row.emp_no)
                .Take(10)
                .Where(row => row.dept_no == "d001"),
            "SQL-backed joined projection rows",
            "row-local joined projections");

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
    public async Task ExpressionParser_LocalMethodEvaluationCapturesParameterIndependentMethods()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_LocalMethodEvaluationCapturesParameterIndependentMethods),
            EmployeesSeedMode.Bogus);

        var probe = new LocalMethodProbe();

        var scalarQuery = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no == probe.GetEmployeeNumber());

        await AssertParserProducesDataLinqPlan(databaseScope.Database, scalarQuery);
        await Assert.That(probe.EmployeeNumberInvocationCount).IsEqualTo(1);

        var sequenceQuery = databaseScope.Database.Query().Employees
            .Where(x => probe.GetEmployeeNumbers().Contains(x.emp_no!.Value));

        await AssertParserProducesDataLinqPlan(databaseScope.Database, sequenceQuery);
        await Assert.That(probe.EmployeeNumbersInvocationCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExpressionParser_LocalMethodEvaluationStillRejectsQueryDependentMethods()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_LocalMethodEvaluationStillRejectsQueryDependentMethods),
            EmployeesSeedMode.Bogus);

        var probe = new LocalMethodProbe();

        var query = databaseScope.Database.Query().Employees
            .Where(x => probe.IsEmployeeNumber(x.emp_no!.Value));

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(databaseScope.Database, query));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Method 'IsEmployeeNumber' is not supported");
        await Assert.That(probe.IsEmployeeNumberInvocationCount).IsEqualTo(0);
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

    [Test]
    public async Task ExpressionParser_AotStrictLocalEvaluationRejectsCompatibilityMethodReflection()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_AotStrictLocalEvaluationRejectsCompatibilityMethodReflection),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no == ThrowIfInvokedEmployeeNumber());

        var exception = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanParser.Convert(
                databaseScope.Database.Provider.Metadata,
                query.Expression,
                typeof(Employee),
                ExpressionQueryPlanParserOptions.AotStrict));

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Local method call 'ThrowIfInvokedEmployeeNumber' requires compatibility method reflection");
    }

    [Test]
    public async Task ExpressionParser_NormalizesAotSafeProjectionRecipeWithoutInvocationValues()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_NormalizesAotSafeProjectionRecipeWithoutInvocationValues),
            EmployeesSeedMode.Bogus);

        IQueryable<object[]> CreateQuery(int offset, int start, int length)
            => databaseScope.Database.Query().Employees.Select(employee => new object[]
            {
                employee.emp_no!.Value + offset,
                !employee.IsDeleted!.Value,
                employee.last_login.HasValue,
                employee.first_name.Length,
                employee.first_name.Trim().ToUpper().Substring(start, length),
                employee.birth_date.Year,
                employee.last_login!.Value.Hour,
                employee.IsDeleted == true ? employee.first_name : null!
            });

        var first = ExpressionQueryPlanParser.Convert(databaseScope.Database, CreateQuery(7, 0, 2));
        var second = ExpressionQueryPlanParser.Convert(databaseScope.Database, CreateQuery(11, 1, 3));
        var firstProjection = first.Template.Projection as QueryPlanProjection.ComputedRowLocal;
        var firstTemplate = QueryPlanDebugWriter.WriteTemplate(first.Template);
        var secondTemplate = QueryPlanDebugWriter.WriteTemplate(second.Template);

        await Assert.That(firstProjection).IsNotNull();
        await Assert.That(firstProjection!.Disposition).IsEqualTo(QueryPlanProjectionDisposition.AotSafe);
        await Assert.That(firstProjection.Recipe).IsTypeOf<QueryPlanProjectionRecipe.NewArray>();
        await Assert.That(firstTemplate).IsEqualTo(secondTemplate);
        await Assert.That(first.Values.Count).IsEqualTo(3);
        await Assert.That(second.Values.Count).IsEqualTo(3);
        await Assert.That(firstTemplate).Contains("disposition=aot-safe");
        await Assert.That(firstTemplate).Contains("new-array(");
        await Assert.That(firstTemplate).Contains("binary(add");
        await Assert.That(firstTemplate).Contains("not(");
        await Assert.That(firstTemplate).Contains("member(nullable-value");
        await Assert.That(firstTemplate).Contains("member(nullable-has-value");
        await Assert.That(firstTemplate).Contains("member(string-length");
        await Assert.That(firstTemplate).Contains("function(string-trim");
        await Assert.That(firstTemplate).Contains("function(string-to-upper");
        await Assert.That(firstTemplate).Contains("function(string-substring");
        await Assert.That(firstTemplate).Contains("function(date-part-year");
        await Assert.That(firstTemplate).Contains("function(time-part-hour");
        await Assert.That(firstTemplate).Contains("conditional(");
    }

    [Test]
    public async Task ExpressionParser_ConstructorRecipeIsExplicitlySqlOnlyCompatibility()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_ConstructorRecipeIsExplicitlySqlOnlyCompatibility),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .Select(employee => new ProjectionDto(employee.first_name.Trim()));
        var invocation = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);
        var projection = invocation.Template.Projection as QueryPlanProjection.Anonymous;
        var snapshot = QueryPlanDebugWriter.WriteTemplate(invocation.Template);

        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.Disposition).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(projection.Recipe).IsTypeOf<QueryPlanProjectionRecipe.CompatibilityConstructor>();
        await Assert.That(snapshot).Contains("disposition=sql-only-compatibility");
        await Assert.That(snapshot).Contains("compat-constructor(");
    }

    [Test]
    public async Task ExpressionParser_RejectsUnsupportedProjectionOperatorsAndOverloadsEarly()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_RejectsUnsupportedProjectionOperatorsAndOverloadsEarly),
            EmployeesSeedMode.Bogus);

        var userDefinedOperator = databaseScope.Database.Query().Employees
            .Select(employee =>
                new ProjectionNumber(employee.emp_no!.Value) + new ProjectionNumber(1));
        var checkedArithmetic = databaseScope.Database.Query().Employees
            .Select(employee => checked(employee.emp_no!.Value + 1));
        var unsupportedOverload = databaseScope.Database.Query().Employees
            .Select(employee => employee.first_name.Trim('A'));

        await AssertParserFailure(
            databaseScope.Database,
            userDefinedOperator,
            "user-defined binary operator");
        await AssertParserFailure(
            databaseScope.Database,
            checkedArithmetic,
            "normalized row-local projection recipes",
            "AddChecked");
        await AssertParserFailure(
            databaseScope.Database,
            unsupportedOverload,
            "Projection method 'Trim' is not supported");
    }

    [Test]
    public async Task ExpressionParser_CapturesProjectionScalarExactlyOnce()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_CapturesProjectionScalarExactlyOnce),
            EmployeesSeedMode.Bogus);

        var probe = new ProjectionCaptureProbe();
        var query = databaseScope.Database.Query().Employees
            .Select(employee => new ProjectionCaptureDto(
                employee.first_name.Trim(),
                probe.Value));

        var invocation = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);

        await Assert.That(probe.InvocationCount).IsEqualTo(1);
        await Assert.That(invocation.Values.Items.Count(value => value is QueryPlanInvocationValue.Scalar)).IsEqualTo(1);
        await Assert.That(invocation.Template.Projection.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
    }

    [Test]
    public async Task ExpressionParser_RejectsCheckedNarrowingAndCoerciveProjectionConversions()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_RejectsCheckedNarrowingAndCoerciveProjectionConversions),
            EmployeesSeedMode.Bogus);

        object boxedShort = (short)1;
        var checkedTopLevel = databaseScope.Database.Query().Employees
            .Select(employee => checked((short)employee.emp_no!.Value));
        var checkedNested = databaseScope.Database.Query().Employees
            .Select(employee => new object[] { checked((short)employee.emp_no!.Value) });
        var narrowing = databaseScope.Database.Query().Employees
            .Select(employee => new object[] { (short)employee.emp_no!.Value });
        var coerciveUnboxing = databaseScope.Database.Query().Employees
            .Select(_ => new object[] { (int)boxedShort });

        await AssertParserFailure(
            databaseScope.Database,
            checkedTopLevel,
            "Checked projection conversions");
        await AssertParserFailure(
            databaseScope.Database,
            checkedNested,
            "Checked projection conversions");
        await AssertParserFailure(
            databaseScope.Database,
            narrowing,
            "Projection conversion",
            "not supported");
        await AssertParserFailure(
            databaseScope.Database,
            coerciveUnboxing,
            "Projection conversion",
            "not supported");
    }

    [Test]
    public async Task ExpressionParser_PreservesImplicitWideningProjectionConversion()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_PreservesImplicitWideningProjectionConversion),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().Employees
            .Select(employee => new object[] { (long)employee.emp_no!.Value });
        var invocation = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);
        var snapshot = QueryPlanDebugWriter.WriteTemplate(invocation.Template);

        await Assert.That(invocation.Template.Projection.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.AotSafe);
        await Assert.That(snapshot).Contains("convert(");
        await Assert.That(snapshot).Contains("Int64");
    }

    [Test]
    public async Task ExpressionParser_ScalarJoinRecipeHasNoClientExpressionPlaceholder()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionParser_ScalarJoinRecipeHasNoClientExpressionPlaceholder),
            EmployeesSeedMode.Bogus);

        var query = databaseScope.Database.Query().DepartmentEmployees.Join(
            databaseScope.Database.Query().Departments,
            departmentEmployee => departmentEmployee.dept_no,
            department => department.DeptNo,
            (departmentEmployee, department) =>
                departmentEmployee.dept_no + ":" + department.Name.Trim());
        var invocation = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);
        var projection = invocation.Template.Projection as QueryPlanProjection.JoinedRowLocal;
        var snapshot = QueryPlanDebugWriter.WriteTemplate(invocation.Template);

        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.Members).IsEmpty();
        await Assert.That(projection.Disposition).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(snapshot).DoesNotContain("client-expression");
        await Assert.That(snapshot).Contains("recipe=binary(add");
    }

    private static async Task AssertParserProducesDataLinqPlan<T>(Database<EmployeesDb> database, IQueryable<T> query)
    {
        var expressionSnapshot = QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database, query).Template);

        await AssertNoLegacyParserTerms(expressionSnapshot);
    }

    private static async Task AssertParserMatchesProductionRoot<T>(Database<EmployeesDb> database, IQueryable<T> productionQuery, IQueryable<T> expressionQuery)
    {
        var productionSnapshot = QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database, productionQuery).Template);
        var expressionSnapshot = QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database.Provider.Metadata, expressionQuery.Expression, typeof(T)).Template);

        await Assert.That(expressionSnapshot).IsEqualTo(productionSnapshot);
        await AssertNoLegacyParserTerms(expressionSnapshot);
    }

    private static async Task AssertParserProducesDataLinqPlan<TResult>(Database<EmployeesDb> database, Expression<Func<TResult>> query)
    {
        var expressionSnapshot = QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database, query).Template);

        await AssertNoLegacyParserTerms(expressionSnapshot);
    }

    private static async Task AssertParserMatchesProductionRoot<TResult>(
        Database<EmployeesDb> database,
        Expression<Func<TResult>> productionQuery,
        Expression<Func<TResult>> expressionQuery)
    {
        var productionSnapshot = QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database, productionQuery).Template);
        var expressionSnapshot = QueryPlanDebugWriter.WriteTemplate(ExpressionQueryPlanParser.Convert(database.Provider.Metadata, expressionQuery.Body, typeof(TResult)).Template);

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

    private static int ThrowIfInvokedEmployeeNumber()
        => throw new InvalidOperationException("AOT-strict local method evaluation should reject before invocation.");

    private sealed record LocalEmployeeId(int Value);

    private sealed record ProjectionDto(string Value);

    private sealed record ProjectionCaptureDto(string Value, int Captured);

    private sealed class ProjectionCaptureProbe
    {
        public int InvocationCount { get; private set; }

        public int Value
        {
            get
            {
                InvocationCount++;
                return 7;
            }
        }
    }

    private readonly record struct ProjectionNumber(int Value)
    {
        public static ProjectionNumber operator +(ProjectionNumber left, ProjectionNumber right)
            => new(left.Value + right.Value);
    }

    private sealed class LocalMethodProbe
    {
        public int EmployeeNumberInvocationCount { get; private set; }

        public int EmployeeNumbersInvocationCount { get; private set; }

        public int IsEmployeeNumberInvocationCount { get; private set; }

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

        public bool IsEmployeeNumber(int employeeNumber)
        {
            IsEmployeeNumberInvocationCount++;
            return employeeNumber == 10001;
        }
    }
}
