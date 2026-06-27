using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class QueryPlanSqlParityTests
{
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

        var currentSql = CurrentQueryTranslationInspection.BuildSql(databaseScope.Database, query);
        var planSql = CurrentQueryTranslationInspection.BuildPlanSql(databaseScope.Database, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(planSql.Text);

        await Assert.That(normalized).Contains("WHERE");
        await Assert.That(normalized).Contains("ORDER BY t0.");
        await Assert.That(normalized).Contains("LIMIT");
        await Assert.That(planSql.Parameters.Select(x => x.Value!).ToArray()).IsEquivalentTo(currentSql.Parameters.Select(x => x.Value!).ToArray());
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
}
