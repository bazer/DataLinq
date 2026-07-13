using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class QueryPlanSqlParityTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionExecutionProvider_ExecutesEntityQueries(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionExecutionProvider_ExecutesEntityQueries),
            EmployeesSeedMode.Bogus);

        var expressionProvider = ExpressionQueryPlanProvider.ForExecution(databaseScope.Database.Provider.ReadOnlyAccess);
        var employees = expressionProvider.CreateRoot<Employee>();
        var departments = expressionProvider.CreateRoot<Department>();
        var managerNumber = databaseScope.Database.Query().Managers
            .OrderBy(x => x.emp_no)
            .First()
            .emp_no;

        var expectedEmployees = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no > 10005 && x.last_login.HasValue)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.emp_no!.Value)
            .ToArray();
        var actualEmployees = employees
            .Where(x => x.emp_no > 10005 && x.last_login.HasValue)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .ToArray()
            .Select(x => x.emp_no!.Value)
            .ToArray();

        var expectedDepartments = databaseScope.Database.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber)
            .ToArray();
        var actualDepartments = departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .OrderBy(department => department.DeptNo)
            .ToArray()
            .Select(department => department.DeptNo)
            .ToArray();

        await Assert.That(actualEmployees).IsEquivalentTo(expectedEmployees);
        await Assert.That(actualDepartments).IsEquivalentTo(expectedDepartments);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionExecutionProvider_ExecutesScalarResults(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionExecutionProvider_ExecutesScalarResults),
            EmployeesSeedMode.Bogus);

        var expressionProvider = ExpressionQueryPlanProvider.ForExecution(databaseScope.Database.Provider.ReadOnlyAccess);
        var employees = expressionProvider.CreateRoot<Employee>();
        var managers = expressionProvider.CreateRoot<Manager>();

        await Assert.That(employees.Count(x => x.emp_no > 10005))
            .IsEqualTo(databaseScope.Database.Query().Employees.Count(x => x.emp_no > 10005));
        await Assert.That(employees.Any(x => x.first_name.StartsWith("A")))
            .IsEqualTo(databaseScope.Database.Query().Employees.Any(x => x.first_name.StartsWith("A")));
        await Assert.That(managers.Where(x => x.dept_fk.StartsWith("d00")).Sum(x => x.emp_no))
            .IsEqualTo(databaseScope.Database.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Sum(x => x.emp_no));
        await Assert.That(managers.Where(x => x.dept_fk.StartsWith("d00")).Min(x => x.emp_no))
            .IsEqualTo(databaseScope.Database.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Min(x => x.emp_no));
        await Assert.That(managers.Where(x => x.dept_fk.StartsWith("d00")).Max(x => x.emp_no))
            .IsEqualTo(databaseScope.Database.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Max(x => x.emp_no));

        await Assert.That(NearlyEqual(
            managers.Where(x => x.dept_fk.StartsWith("d00")).Average(x => x.emp_no),
            databaseScope.Database.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Average(x => x.emp_no))).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionExecutionProvider_ExecutesBarePagingWithoutDroppingOperators(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionExecutionProvider_ExecutesBarePagingWithoutDroppingOperators),
            EmployeesSeedMode.Bogus);

        var expressionProvider = ExpressionQueryPlanProvider.ForExecution(databaseScope.Database.Provider.ReadOnlyAccess);
        var employees = expressionProvider.CreateRoot<Employee>();
        var totalEmployees = databaseScope.Database.Query().Employees.Count();

        await Assert.That(employees.Take(5).ToArray().Length).IsEqualTo(5);
        await Assert.That(employees.Skip(totalEmployees).ToArray()).IsEmpty();
        await Assert.That(employees.Take(5).Count()).IsEqualTo(5);
        await Assert.That(employees.Skip(totalEmployees).Any()).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionPlanSql_RendersSupportedSequenceShapes(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionPlanSql_RendersSupportedSequenceShapes),
            EmployeesSeedMode.Bogus);

        var threshold = 10010;
        var ids = new[] { 10001, 10002, 10003 };
        var managerNumber = databaseScope.Database.Query().Managers
            .OrderBy(x => x.emp_no)
            .First()
            .emp_no;

        var filteredSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Employees
                .Where(x => x.emp_no != threshold && x.last_login.HasValue)
                .OrderBy(x => x.last_name)
                .ThenByDescending(x => x.emp_no)
                .Skip(1)
                .Take(3));
        var filteredNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(filteredSql.Text);
        await Assert.That(filteredNormalized).Contains("WHERE");
        await Assert.That(filteredNormalized).Contains("ORDER BY t0.");
        await Assert.That(filteredNormalized).Contains("LIMIT");
        await Assert.That(filteredSql.Parameters.Select(x => x.Value).ToArray()).Contains(threshold);

        var containsSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Employees
                .Where(x => ids.Contains(x.emp_no!.Value)));
        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(containsSql.Text)).Contains(" IN ");
        await Assert.That(containsSql.Parameters.Select(x => x.Value!).ToArray()).IsEquivalentTo(ids.Cast<object>().ToArray());

        var relationSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Departments
                .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
                .OrderBy(department => department.DeptNo));
        var relationNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(relationSql.Text);
        await Assert.That(relationNormalized).Contains("EXISTS (SELECT 1 FROM");
        await Assert.That(relationNormalized).Contains("ORDER BY t0.");
        await Assert.That(relationSql.Parameters.Select(x => x.Value).ToArray()).Contains(managerNumber);

        var joinSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
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
                    }));
        var joinNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(joinSql.Text);
        await Assert.That(joinNormalized).Contains("JOIN");
        await Assert.That(joinNormalized).Contains("t0.");
        await Assert.That(joinNormalized).Contains("t1.");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionPlanSql_RendersScalarResultShapes(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionPlanSql_RendersScalarResultShapes),
            EmployeesSeedMode.Bogus);

        var countSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees.Count(x => x.emp_no > 10005));
        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(countSql.Text)).Contains("COUNT(*)");

        var anySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees.Any(x => x.first_name.StartsWith("A")));
        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(anySql.Text)).Contains("COUNT(*)");
        await Assert.That(anySql.Parameters.Select(x => x.Value).ToArray()).Contains("A%");

        var sumSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            () => databaseScope.Database.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Sum(x => x.emp_no));
        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sumSql.Text)).Contains("SUM(");

        var averageSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            () => databaseScope.Database.Query().Managers.Where(x => x.dept_fk.StartsWith("d00")).Average(x => x.emp_no));
        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(averageSql.Text)).Contains("AVG(");
    }

    [Test]
    public async Task ExpressionPlanSql_RendersPostPagingPushdownWithSeparateParameters()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionPlanSql_RendersPostPagingPushdownWithSeparateParameters),
            EmployeesSeedMode.Bogus);

        var threshold = 10005;
        var excludedName = "Alice";
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no > threshold)
            .OrderBy(x => x.emp_no)
            .Take(10)
            .Where(x => x.first_name != excludedName)
            .OrderByDescending(x => x.hire_date);

        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);
        var parameterValues = sql.Parameters.Select(x => x.Value).ToArray();

        await Assert.That(normalized).Contains("FROM (SELECT");
        await Assert.That(normalized).Contains("LIMIT 10");
        await Assert.That(normalized).Contains(") t0 WHERE");
        await Assert.That(normalized).Contains("ORDER BY t0.");
        await Assert.That(parameterValues).Contains(threshold);
        await Assert.That(parameterValues).Contains(excludedName);
        await Assert.That(sql.Text).DoesNotContain(threshold.ToString());
        await Assert.That(sql.Text).DoesNotContain(excludedName);
    }

    [Test]
    public async Task ExpressionPlanSql_RendersPagedCountAndAnyAsSqlScalarPushdown()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionPlanSql_RendersPagedCountAndAnyAsSqlScalarPushdown),
            EmployeesSeedMode.Bogus);

        var countSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees
                .OrderBy(x => x.emp_no)
                .Take(5)
                .Count());
        var anySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            () => databaseScope.Database.Query().Employees
                .OrderBy(x => x.emp_no)
                .Skip(5)
                .Take(5)
                .Any());

        var countNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(countSql.Text);
        var anyNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(anySql.Text);

        await Assert.That(countNormalized).Contains("SELECT COUNT(*)");
        await Assert.That(countNormalized).Contains("FROM (SELECT");
        await Assert.That(countNormalized).Contains("LIMIT 5");
        await Assert.That(anyNormalized).Contains("SELECT COUNT(*)");
        await Assert.That(anyNormalized).Contains("FROM (SELECT");
        await Assert.That(anyNormalized).Contains("LIMIT 5");
    }

    [Test]
    public async Task ExpressionPlanSql_RendersDirectPostPagingOrderByPushdown()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionPlanSql_RendersDirectPostPagingOrderByPushdown),
            EmployeesSeedMode.Bogus);

        var takeOrderSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Employees
                .Take(5)
                .OrderBy(x => x.emp_no));
        var skipOrderSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Employees
                .Skip(5)
                .OrderBy(x => x.emp_no));

        var takeOrderNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(takeOrderSql.Text);
        var skipOrderNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(skipOrderSql.Text);

        await Assert.That(takeOrderNormalized).Contains("FROM (SELECT");
        await Assert.That(takeOrderNormalized).Contains("LIMIT 5");
        await Assert.That(takeOrderNormalized).Contains("ORDER BY t0.");
        await Assert.That(skipOrderNormalized).Contains("FROM (SELECT");
        await Assert.That(skipOrderNormalized).Contains("OFFSET 5");
        await Assert.That(skipOrderNormalized).Contains("ORDER BY t0.");
    }

    [Test]
    public async Task ExpressionPlanSql_RendersJoinedPostPagingPushdownWithDerivedAliases()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(ExpressionPlanSql_RendersJoinedPostPagingPushdownWithDerivedAliases),
            EmployeesSeedMode.Bogus);

        var prefix = "S";
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
            .OrderBy(row => row.emp_no)
            .Take(30)
            .Where(row => row.DepartmentName.StartsWith(prefix))
            .OrderByDescending(row => row.dept_no);

        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("FROM (SELECT");
        await Assert.That(normalized).Contains("JOIN");
        await Assert.That(normalized).Contains("LIMIT 30");
        await Assert.That(normalized).Contains(") t0 WHERE");
        await Assert.That(normalized).Contains("t0.\"DepartmentName\"");
        await Assert.That(normalized).Contains("ORDER BY t0.\"dept_no\" DESC");
        await Assert.That(normalized).Contains("dl_0_pk_0");
        await Assert.That(normalized).Contains("dl_1_pk_0");
        await Assert.That(sql.Parameters.Select(x => x.Value).ToArray()).Contains("S%");
        await Assert.That(sql.Text).DoesNotContain("S%");
    }

    [Test]
    public async Task PlanSql_RendersParameterizedPredicatesOrderingAndPaging()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_RendersParameterizedPredicatesOrderingAndPaging),
            EmployeesSeedMode.Bogus);

        var threshold = 10010;
        var excludedName = "SensitiveName";
        var skip = 1;
        var take = 3;
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no != threshold)
            .OrderBy(x => x.last_name)
            .Where(x => x.first_name != excludedName)
            .Skip(skip)
            .Take(take);

        var planSql = CurrentQueryTranslationInspection.BuildPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(planSql.Text);

        await Assert.That(normalized).Contains("WHERE");
        await Assert.That(normalized).Contains("ORDER BY t0.");
        await Assert.That(normalized).Contains("LIMIT");
        await Assert.That(planSql.Parameters.Select(x => x.Value!).ToArray()).Contains(threshold);
        await Assert.That(planSql.Parameters.Select(x => x.Value!).ToArray()).Contains(excludedName);
        await Assert.That(planSql.Text).DoesNotContain(threshold.ToString());
        await Assert.That(planSql.Text).DoesNotContain(excludedName);
    }

    [Test]
    public async Task PlanSql_RendersLocalSequenceMembershipWithParameterizedValues()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_RendersLocalSequenceMembershipWithParameterizedValues),
            EmployeesSeedMode.Bogus);

        var ids = new[] { 10001, 10002, 10003 };
        var query = databaseScope.Database.Query().Employees
            .Where(x => ids.Contains(x.emp_no!.Value));

        var planSql = CurrentQueryTranslationInspection.BuildPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(planSql.Text);

        await Assert.That(normalized).Contains(" IN ");
        await Assert.That(planSql.Parameters.Select(x => x.Value!).ToArray()).IsEquivalentTo(ids.Cast<object>().ToArray());
        foreach (var id in ids)
            await Assert.That(planSql.Text).DoesNotContain(id.ToString());
    }

    [Test]
    public async Task PlanSql_FoldsEmptyLocalSequenceMembershipToFixedConditions()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_FoldsEmptyLocalSequenceMembershipToFixedConditions),
            EmployeesSeedMode.Bogus);

        var ids = Array.Empty<int>();
        var containsSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Employees
                .Where(employee => ids.Contains(employee.emp_no!.Value)));
        var excludesSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Employees
                .Where(employee => !ids.Contains(employee.emp_no!.Value)));
        var containsNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(containsSql.Text);
        var excludesNormalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(excludesSql.Text);

        await Assert.That(containsNormalized).Contains("1=0");
        await Assert.That(excludesNormalized).Contains("1=1");
        await Assert.That(containsNormalized).DoesNotContain(" IN ()");
        await Assert.That(excludesNormalized).DoesNotContain(" IN ()");
        await Assert.That(containsSql.Parameters).IsEmpty();
        await Assert.That(excludesSql.Parameters).IsEmpty();
    }

    [Test]
    public async Task PlanSql_RendersCapturedNullAndNullableMembershipWithCSharpSemantics()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_RendersCapturedNullAndNullableMembershipWithCSharpSemantics),
            EmployeesSeedMode.Bogus);

        TimeOnly? capturedNull = null;
        TimeOnly? capturedLogin = new TimeOnly(9, 15);
        TimeOnly?[] nullOnly = [null];
        TimeOnly?[] loginOnly = [capturedLogin];
        TimeOnly?[] loginAndNull = [capturedLogin, null];
        var rows = databaseScope.Database.Query().Employees;

        var capturedNullEqualitySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => employee.last_login == capturedNull));
        var capturedNullInequalitySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => capturedNull != employee.last_login));
        var capturedLoginInequalitySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => employee.last_login != capturedLogin));
        var nullOnlySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => Enumerable.Contains(nullOnly, employee.last_login)));
        var nullOnlyNegatedSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => !Enumerable.Contains(nullOnly, employee.last_login)));
        var loginOnlyNegatedSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => !Enumerable.Contains(loginOnly, employee.last_login)));
        var negatedCompoundLoginOnlySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => !(Enumerable.Contains(loginOnly, employee.last_login) || employee.emp_no == -1)));
        var mixedSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => Enumerable.Contains(loginAndNull, employee.last_login)));
        var mixedNegatedSql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => !Enumerable.Contains(loginAndNull, employee.last_login)));
        var mixedLocalFirstAnySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => loginAndNull.Any(value => value == employee.last_login)));
        var mixedColumnFirstAnySql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            rows.Where(employee => loginAndNull.Any(value => employee.last_login == value)));

        await AssertSqlShape(capturedNullEqualitySql, " IS NULL", 0);
        await AssertSqlShape(capturedNullInequalitySql, " IS NOT NULL", 0);
        await AssertSqlShape(capturedLoginInequalitySql, " OR ", 1, " IS NULL");
        await AssertSqlShape(nullOnlySql, " IS NULL", 0);
        await AssertSqlShape(nullOnlyNegatedSql, " IS NOT NULL", 0);
        await AssertSqlShape(loginOnlyNegatedSql, " NOT IN ", 1, " OR ", " IS NULL");
        await AssertSqlShape(negatedCompoundLoginOnlySql, "NOT (", 2, " IN ", " AND ", " IS NOT NULL");
        await AssertSqlShape(mixedSql, " IN ", 1, " OR ", " IS NULL");
        await AssertSqlShape(mixedNegatedSql, " NOT IN ", 1, " AND ", " IS NOT NULL");
        await AssertSqlShape(mixedLocalFirstAnySql, " IN ", 1, " OR ", " IS NULL");
        await AssertSqlShape(mixedColumnFirstAnySql, " IN ", 1, " OR ", " IS NULL");
    }

    [Test]
    public async Task PlanSql_RebindsSameNullableSequenceShapeWithFreshValuesAndNullPosition()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_RebindsSameNullableSequenceShapeWithFreshValuesAndNullPosition),
            EmployeesSeedMode.Bogus);

        TimeOnly?[] firstValues = [new TimeOnly(9, 15), null];
        TimeOnly?[] secondValues = [null, new TimeOnly(10, 30)];
        var query = databaseScope.Database.Query().Employees
            .Where(employee => Enumerable.Contains(firstValues, employee.last_login));
        var firstInvocation = ExpressionQueryPlanParser.Convert(databaseScope.Database, query);
        var sequenceBinding = firstInvocation.Values.Items
            .OfType<QueryPlanInvocationValue.LocalSequence>()
            .Single();
        var secondInvocation = QueryPlanInvocation.Bind(
            firstInvocation.Template,
            [new QueryPlanInvocationValue.LocalSequence(
                sequenceBinding.Id,
                secondValues.Cast<object?>().ToArray())]);

        var firstSql = new QueryPlanSqlBuilder(firstInvocation, databaseScope.Database.Provider.ReadOnlyAccess)
            .BuildSelect<Employee>()
            .ToSql();
        var secondSql = new QueryPlanSqlBuilder(secondInvocation, databaseScope.Database.Provider.ReadOnlyAccess)
            .BuildSelect<Employee>()
            .ToSql();

        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(secondSql.Text))
            .IsEqualTo(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(firstSql.Text));
        await Assert.That(firstSql.Parameters.Count).IsEqualTo(1);
        await Assert.That(secondSql.Parameters.Count).IsEqualTo(1);
        await Assert.That(firstSql.Parameters[0].Value).IsNotEqualTo(secondSql.Parameters[0].Value);
        await Assert.That(firstSql.Parameters[0].Value).IsNotNull();
        await Assert.That(secondSql.Parameters[0].Value).IsNotNull();
    }

    [Test]
    public async Task PlanSql_ResolvesRuntimeValuesOnlyFromValidatedInvocation()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_ResolvesRuntimeValuesOnlyFromValidatedInvocation),
            EmployeesSeedMode.Bogus);

        var firstThreshold = 10010;
        var secondThreshold = 10020;
        var query = databaseScope.Database.Query().Employees
            .Where(employee => employee.emp_no > firstThreshold);
        var firstInvocation = ExpressionQueryPlanParser.Convert(
            databaseScope.Database.Provider.Metadata,
            query.Expression,
            typeof(Employee));
        var scalar = firstInvocation.Values.Items
            .OfType<QueryPlanInvocationValue.Scalar>()
            .Single();
        var secondInvocation = QueryPlanInvocation.Bind(
            firstInvocation.Template,
            firstInvocation.Values.Items.Select(value => value == scalar
                ? new QueryPlanInvocationValue.Scalar(scalar.Id, secondThreshold)
                : value));

        var firstSql = new QueryPlanSqlBuilder(firstInvocation, databaseScope.Database.Provider.ReadOnlyAccess)
            .BuildSelect<Employee>()
            .ToSql();
        var secondSql = new QueryPlanSqlBuilder(secondInvocation, databaseScope.Database.Provider.ReadOnlyAccess)
            .BuildSelect<Employee>()
            .ToSql();

        await Assert.That(ReferenceEquals(secondInvocation.Template, firstInvocation.Template)).IsTrue();
        await Assert.That(firstSql.Parameters.Select(parameter => parameter.Value).ToArray()).Contains(firstThreshold);
        await Assert.That(secondSql.Parameters.Select(parameter => parameter.Value).ToArray()).Contains(secondThreshold);
        await Assert.That(secondSql.Parameters.Any(parameter => Equals(parameter.Value, firstThreshold))).IsFalse();
    }

    [Test]
    public async Task PlanSql_ResolvesReboundLocalSequenceOnlyFromValidatedInvocation()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_ResolvesReboundLocalSequenceOnlyFromValidatedInvocation),
            EmployeesSeedMode.Bogus);

        var firstIds = new[] { 10001, 10002 };
        var secondIds = new[] { 10003, 10004 };
        var query = databaseScope.Database.Query().Employees
            .Where(employee => firstIds.Contains(employee.emp_no!.Value));
        var firstInvocation = ExpressionQueryPlanParser.Convert(
            databaseScope.Database.Provider.Metadata,
            query.Expression,
            typeof(Employee));
        var sequence = firstInvocation.Values.Items
            .OfType<QueryPlanInvocationValue.LocalSequence>()
            .Single();
        var secondInvocation = QueryPlanInvocation.Bind(
            firstInvocation.Template,
            firstInvocation.Values.Items.Select(value => value == sequence
                ? new QueryPlanInvocationValue.LocalSequence(sequence.Id, secondIds.Cast<object?>().ToArray())
                : value));

        var firstSql = new QueryPlanSqlBuilder(firstInvocation, databaseScope.Database.Provider.ReadOnlyAccess)
            .BuildSelect<Employee>()
            .ToSql();
        var secondSql = new QueryPlanSqlBuilder(secondInvocation, databaseScope.Database.Provider.ReadOnlyAccess)
            .BuildSelect<Employee>()
            .ToSql();
        var firstParameterValues = firstSql.Parameters.Select(parameter => parameter.Value!).ToArray();
        var secondParameterValues = secondSql.Parameters.Select(parameter => parameter.Value!).ToArray();

        await Assert.That(ReferenceEquals(secondInvocation.Template, firstInvocation.Template)).IsTrue();
        await Assert.That(firstParameterValues).IsEquivalentTo(firstIds.Cast<object>().ToArray());
        await Assert.That(secondParameterValues).IsEquivalentTo(secondIds.Cast<object>().ToArray());
        await Assert.That(secondParameterValues.Any(value => firstIds.Cast<object>().Contains(value))).IsFalse();
    }

    [Test]
    public async Task PlanSql_RendersNullIntrinsicWithoutInvocationParameter()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_RendersNullIntrinsicWithoutInvocationParameter),
            EmployeesSeedMode.Bogus);

        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(
            databaseScope.Database,
            databaseScope.Database.Query().Employees
                .Where(employee => employee.last_login == null));

        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text)).Contains(" IS NULL");
        await Assert.That(sql.Parameters).IsEmpty();
    }

    [Test]
    public async Task PlanSql_RendersStringAndDateTimeFunctions()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_RendersStringAndDateTimeFunctions),
            EmployeesSeedMode.Bogus);

        var prefix = "A";
        var query = databaseScope.Database.Query().Employees
            .Where(x => x.first_name.StartsWith(prefix) && x.last_login!.Value.Hour >= 0);

        var planSql = CurrentQueryTranslationInspection.BuildPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(planSql.Text);

        await Assert.That(normalized).Contains(" LIKE ");
        await Assert.That(normalized).Contains("strftime('%H'");
        await Assert.That(planSql.Parameters.Select(x => x.Value).ToArray()).Contains("A%");
        await Assert.That(planSql.Text).DoesNotContain("A%");
    }

    [Test]
    public async Task PlanSql_RendersRelationExistsPredicate()
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            TestProviderMatrix.SQLiteInMemory,
            nameof(PlanSql_RendersRelationExistsPredicate),
            EmployeesSeedMode.Bogus);

        var managerNumber = databaseScope.Database.Query().Managers
            .OrderBy(x => x.emp_no)
            .First()
            .emp_no;
        var query = databaseScope.Database.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber));

        var planSql = CurrentQueryTranslationInspection.BuildPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(planSql.Text);

        await Assert.That(normalized).Contains("EXISTS (SELECT 1 FROM");
        await Assert.That(normalized).Contains("r0.");
        await Assert.That(normalized).Contains("t0.");
        await Assert.That(planSql.Parameters.Select(x => x.Value).ToArray()).Contains(managerNumber);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionPlanSql_EntityExecutionMatchesProductionForSingleSourceQuery(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionPlanSql_EntityExecutionMatchesProductionForSingleSourceQuery),
            EmployeesSeedMode.Bogus);

        var expected = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no > 10005 && x.last_login.HasValue)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.emp_no!.Value)
            .ToArray();

        var expressionPlanQuery = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no > 10005 && x.last_login.HasValue)
            .OrderBy(x => x.emp_no)
            .Take(5);
        var actual = CurrentQueryTranslationInspection.BuildExpressionPlanSelect(databaseScope.Database, expressionPlanQuery)
            .ExecuteAs<Employee>()
            .Select(x => x.emp_no!.Value)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionPlanSql_RelationExistsExecutionMatchesProduction(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionPlanSql_RelationExistsExecutionMatchesProduction),
            EmployeesSeedMode.Bogus);

        var managerNumber = databaseScope.Database.Query().Managers
            .OrderBy(x => x.emp_no)
            .First()
            .emp_no;
        var expected = databaseScope.Database.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber)
            .ToArray();

        var expressionPlanQuery = databaseScope.Database.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .OrderBy(department => department.DeptNo);
        var actual = CurrentQueryTranslationInspection.BuildExpressionPlanSelect(databaseScope.Database, expressionPlanQuery)
            .ExecuteAs<Department>()
            .Select(department => department.DeptNo)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PlanSql_EntityExecutionMatchesProductionForSingleSourceQuery(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PlanSql_EntityExecutionMatchesProductionForSingleSourceQuery),
            EmployeesSeedMode.Bogus);

        var expected = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no > 10005 && x.last_login.HasValue)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.emp_no!.Value)
            .ToArray();

        var planQuery = databaseScope.Database.Query().Employees
            .Where(x => x.emp_no > 10005 && x.last_login.HasValue)
            .OrderBy(x => x.emp_no)
            .Take(5);
        var actual = CurrentQueryTranslationInspection.BuildPlanSelect(databaseScope.Database, planQuery)
            .ExecuteAs<Employee>()
            .Select(x => x.emp_no!.Value)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PlanSql_RelationExistsExecutionMatchesProduction(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(PlanSql_RelationExistsExecutionMatchesProduction),
            EmployeesSeedMode.Bogus);

        var managerNumber = databaseScope.Database.Query().Managers
            .OrderBy(x => x.emp_no)
            .First()
            .emp_no;
        var expected = databaseScope.Database.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .Select(department => department.DeptNo)
            .OrderBy(departmentNumber => departmentNumber)
            .ToArray();

        var planQuery = databaseScope.Database.Query().Departments
            .Where(department => department.Managers.Any(manager => manager.emp_no == managerNumber))
            .OrderBy(department => department.DeptNo);
        var actual = CurrentQueryTranslationInspection.BuildPlanSelect(databaseScope.Database, planQuery)
            .ExecuteAs<Department>()
            .Select(department => department.DeptNo)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionExecutionProvider_PostPagingFilterAndOrderingMatchInMemory(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionExecutionProvider_PostPagingFilterAndOrderingMatchInMemory),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.birth_date)
            .Take(20)
            .Where(x => x.gender == Employee.Employeegender.M)
            .OrderByDescending(x => x.emp_no)
            .Take(5)
            .ToList();

        var actual = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.birth_date)
            .Take(20)
            .Where(x => x.gender == Employee.Employeegender.M)
            .OrderByDescending(x => x.emp_no)
            .Take(5)
            .ToList();

        await AssertEmployeeSequenceEqual(expected, actual);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionExecutionProvider_PostPagingCompositionWorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionExecutionProvider_PostPagingCompositionWorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnly = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.birth_date)
            .Take(20)
            .Where(x => x.gender == Employee.Employeegender.M)
            .OrderByDescending(x => x.emp_no)
            .Take(5)
            .ToList();

        var transactionRows = transaction.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.birth_date)
            .Take(20)
            .Where(x => x.gender == Employee.Employeegender.M)
            .OrderByDescending(x => x.emp_no)
            .Take(5)
            .ToList();

        await AssertEmployeeSequenceEqual(readOnly, transactionRows);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExpressionExecutionProvider_PagedAggregateUsesPushedDownSource(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ExpressionExecutionProvider_PagedAggregateUsesPushedDownSource),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Managers
            .ToList()
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Sum(x => x.emp_no);

        var actual = employeesDatabase.Query().Managers
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Sum(x => x.emp_no);

        await Assert.That(actual).IsEqualTo(expected);
    }

    private static bool NearlyEqual(double actual, double expected)
        => Math.Abs(actual - expected) < 0.0001;

    private static async Task AssertSqlShape(
        DataLinq.Query.Sql sql,
        string requiredFragment,
        int expectedParameterCount,
        params string[] additionalRequiredFragments)
    {
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains(requiredFragment);
        foreach (var fragment in additionalRequiredFragments)
            await Assert.That(normalized).Contains(fragment);

        await Assert.That(sql.Parameters.Count).IsEqualTo(expectedParameterCount);
    }

    private static async Task AssertEmployeeSequenceEqual(IReadOnlyList<Employee> expected, IReadOnlyList<Employee> actual)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].first_name).IsEqualTo(expected[index].first_name);
            await Assert.That(actual[index].last_name).IsEqualTo(expected[index].last_name);
        }
    }
}
