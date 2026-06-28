using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Linq.Planning.Expressions;
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

    private static bool NearlyEqual(double actual, double expected)
        => Math.Abs(actual - expected) < 0.0001;
}
