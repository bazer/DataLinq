using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesProjectionTranslationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ComputedAnonymousProjection_AppliesAfterSqlFilteringOrderingAndPaging(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ComputedAnonymousProjection_AppliesAfterSqlFilteringOrderingAndPaging),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.first_name, StringComparer.Ordinal)
            .ThenBy(x => x.emp_no)
            .Skip(2)
            .Take(5)
            .Select(x => new
            {
                x.emp_no,
                FullName = x.first_name + " " + x.last_name,
                Normalized = x.first_name.Trim().ToUpper(),
                NameLength = x.first_name.Trim().Length
            })
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.first_name)
            .ThenBy(x => x.emp_no)
            .Skip(2)
            .Take(5)
            .Select(x => new
            {
                x.emp_no,
                FullName = x.first_name + " " + x.last_name,
                Normalized = x.first_name.Trim().ToUpper(),
                NameLength = x.first_name.Trim().Length
            })
            .ToArray();

        await Assert.That(actual.Length).IsEqualTo(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].FullName).IsEqualTo(expected[index].FullName);
            await Assert.That(actual[index].Normalized).IsEqualTo(expected[index].Normalized);
            await Assert.That(actual[index].NameLength).IsEqualTo(expected[index].NameLength);
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ComputedScalarProjection_MatchesPostMaterializationBehavior(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ComputedScalarProjection_MatchesPostMaterializationBehavior),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.first_name + ":" + x.emp_no!.Value)
            .ToArray();

        var actual = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.first_name + ":" + x.emp_no!.Value)
            .ToArray();

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ComputedProjection_ExecutesParsedInvocationAndRebindsCapturedValue(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ComputedProjection_ExecutesParsedInvocationAndRebindsCapturedValue),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var prefix = "first:";
        var query = employeesDatabase.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Select(employee => prefix + employee.first_name);
        var firstInvocation = ExpressionQueryPlanParser.Convert(employeesDatabase, query);
        var prefixDeclaration = firstInvocation.Template.BindingDeclarations.Items
            .Single(declaration => declaration.ModelType == typeof(string));

        prefix = "mutated-after-parse:";
        var secondValues = firstInvocation.Values.Items.Select(value =>
            value.Id == prefixDeclaration.Id
                ? new QueryPlanInvocationValue.Scalar(value.Id, "second:")
                : value);
        var secondInvocation = QueryPlanInvocation.Bind(firstInvocation.Template, secondValues);

        var firstResults = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(
                employeesDatabase.Provider.ReadOnlyAccess,
                firstInvocation)
            .ToArray();
        var secondResults = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(
                employeesDatabase.Provider.ReadOnlyAccess,
                secondInvocation)
            .ToArray();

        await Assert.That(firstResults.Length).IsGreaterThan(0);
        await Assert.That(firstResults.All(result => result.StartsWith("first:", StringComparison.Ordinal))).IsTrue();
        await Assert.That(secondResults.All(result => result.StartsWith("second:", StringComparison.Ordinal))).IsTrue();
        await Assert.That(firstResults.Select(result => result["first:".Length..]))
            .IsEquivalentTo(secondResults.Select(result => result["second:".Length..]));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CapturedOnlyProjection_PreservesSourceCardinalityWithoutOriginalExpression(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(CapturedOnlyProjection_PreservesSourceCardinalityWithoutOriginalExpression),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var captured = "constant-per-row";
        var sourceCount = employeesDatabase.Query().Employees.Count();
        var query = employeesDatabase.Query().Employees.Select(_ => captured);
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, query);

        captured = "changed-after-parse";
        var results = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(
                employeesDatabase.Provider.ReadOnlyAccess,
                invocation)
            .ToArray();

        await Assert.That(results.Length).IsEqualTo(sourceCount);
        await Assert.That(results.All(result => result == "constant-per-row")).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task AotSafeOperatorRecipe_MatchesLiftedFloatingStringAndReferenceSemantics(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(AotSafeOperatorRecipe_MatchesLiftedFloatingStringAndReferenceSemantics),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var firstNaN = double.NaN;
        var secondNaN = double.NaN;
        string? firstNull = null;
        string? secondNull = null;
        var firstReference = new ProjectionReferenceProbe(7);
        var secondReference = new ProjectionReferenceProbe(7);

        object?[] Project(DataLinq.Tests.Models.Employees.Employee employee) =>
        [
            !employee.IsDeleted,
            employee.emp_no + 1,
            employee.emp_no > 10000,
            double.NaN < (double)employee.emp_no!.Value,
            (double)employee.emp_no.Value > double.NaN,
            firstNaN == secondNaN,
            firstNull + secondNull,
            firstReference == secondReference
        ];

        var expected = employeesDatabase.Query().Employees
            .ToList()
            .OrderBy(employee => employee.emp_no)
            .Select(Project)
            .ToArray();
        var query = employeesDatabase.Query().Employees
            .OrderBy(employee => employee.emp_no)
            .Select(employee => new object?[]
            {
                !employee.IsDeleted,
                employee.emp_no + 1,
                employee.emp_no > 10000,
                double.NaN < (double)employee.emp_no!.Value,
                (double)employee.emp_no.Value > double.NaN,
                firstNaN == secondNaN,
                firstNull + secondNull,
                firstReference == secondReference
            });
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, query);
        var actual = ExpressionQueryPlanExecutor.ExecuteEnumerable<object?[]>(
                employeesDatabase.Provider.ReadOnlyAccess,
                invocation,
                ProjectionEvaluationOptions.AotStrict)
            .ToArray();

        await Assert.That(invocation.Template.Projection.Disposition)
            .IsEqualTo(QueryPlanProjectionDisposition.AotSafe);
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var rowIndex = 0; rowIndex < expected.Length; rowIndex++)
        {
            await Assert.That(actual[rowIndex].Length).IsEqualTo(expected[rowIndex].Length);
            for (var valueIndex = 0; valueIndex < expected[rowIndex].Length; valueIndex++)
                await Assert.That(actual[rowIndex][valueIndex]).IsEqualTo(expected[rowIndex][valueIndex]);
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBackedAnonymousProjection_MatchesInMemoryAndSelectsAliases(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlBackedAnonymousProjection_MatchesInMemoryAndSelectsAliases),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(row => row.emp_no < 990000)
            .OrderBy(row => row.emp_no)
            .Take(5)
            .Select(row => new
            {
                EmployeeNumber = row.emp_no,
                row.first_name
            })
            .ToArray();

        var query = employeesDatabase.Query().Employees
            .Where(row => row.emp_no < 990000)
            .OrderBy(row => row.emp_no)
            .Take(5)
            .Select(row => new
            {
                EmployeeNumber = row.emp_no,
                row.first_name
            });

        var actual = query.ToArray();
        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(employeesDatabase, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("emp_no");
        await Assert.That(normalized).Contains("first_name");
        await Assert.That(normalized).Contains("EmployeeNumber");
        await Assert.That(FormatProjectionRows(actual)).IsEqualTo(FormatProjectionRows(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ImplicitSingularRelationProjection_MatchesInMemoryAndUsesJoin(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ImplicitSingularRelationProjection_MatchesInMemoryAndUsesJoin),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Where(row => row.emp_no < 990000)
            .OrderBy(row => row.emp_no)
            .Take(10)
            .Select(row => new
            {
                row.emp_no,
                DepartmentName = row.departments.Name
            })
            .ToArray();

        var query = employeesDatabase.Query().DepartmentEmployees
            .Where(row => row.emp_no < 990000)
            .OrderBy(row => row.emp_no)
            .Take(10)
            .Select(row => new
            {
                row.emp_no,
                DepartmentName = row.departments.Name
            });

        var actual = query.ToArray();
        var sql = CurrentQueryTranslationInspection.BuildExpressionPlanSql(employeesDatabase, query);
        var normalized = CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text);

        await Assert.That(normalized).Contains("JOIN");
        await Assert.That(normalized).Contains("dept_name");
        await Assert.That(normalized).Contains("DepartmentName");
        await Assert.That(FormatDepartmentProjectionRows(actual)).IsEqualTo(FormatDepartmentProjectionRows(expected));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RowLocalImplicitSingularRelationProjection_ExecutesParsedInvocation(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RowLocalImplicitSingularRelationProjection_ExecutesParsedInvocation),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var expected = employeesDatabase.Query().DepartmentEmployees
            .ToList()
            .Where(row => row.emp_no < 990000)
            .OrderBy(row => row.emp_no)
            .ThenBy(row => row.dept_no, StringComparer.Ordinal)
            .Take(10)
            .Select(row => row.departments.Name.Trim())
            .ToArray();
        var query = employeesDatabase.Query().DepartmentEmployees
            .Where(row => row.emp_no < 990000)
            .OrderBy(row => row.emp_no)
            .ThenBy(row => row.dept_no)
            .Take(10)
            .Select(row => row.departments.Name.Trim());
        var invocation = ExpressionQueryPlanParser.Convert(employeesDatabase, query);
        var projection = invocation.Template.Projection as QueryPlanProjection.JoinedRowLocal;

        var actual = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(
                employeesDatabase.Provider.ReadOnlyAccess,
                invocation)
            .ToArray();

        QueryTranslationException? strictException = null;
        try
        {
            _ = ExpressionQueryPlanExecutor.ExecuteEnumerable<string>(
                    employeesDatabase.Provider.ReadOnlyAccess,
                    invocation,
                    ProjectionEvaluationOptions.AotStrict)
                .ToArray();
        }
        catch (QueryTranslationException caught)
        {
            strictException = caught;
        }

        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.Disposition).IsEqualTo(QueryPlanProjectionDisposition.SqlOnlyCompatibility);
        await Assert.That(invocation.Template.Sources.Any(source => source.Kind == QueryPlanSourceKind.ImplicitJoin)).IsTrue();
        await Assert.That(strictException).IsNotNull();
        await Assert.That(strictException!.Message).Contains("requires SQL-only compatibility execution");
        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ImplicitSingularRelationProjection_WorksFromTransactionRoot(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(ImplicitSingularRelationProjection_WorksFromTransactionRoot),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        using var transaction = employeesDatabase.Transaction();

        var readOnlyRows = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(row => row.emp_no)
            .Take(10)
            .Select(row => new
            {
                row.emp_no,
                DepartmentName = row.departments.Name
            })
            .ToArray();

        var transactionRows = transaction.Query().DepartmentEmployees
            .OrderBy(row => row.emp_no)
            .Take(10)
            .Select(row => new
            {
                row.emp_no,
                DepartmentName = row.departments.Name
            })
            .ToArray();

        await Assert.That(FormatDepartmentProjectionRows(transactionRows)).IsEqualTo(FormatDepartmentProjectionRows(readOnlyRows));
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationProjection_ThrowsQueryTranslationException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationProjection_ThrowsQueryTranslationException),
            EmployeesSeedMode.Bogus);

        QueryTranslationException? exception = null;

        try
        {
            _ = databaseScope.Database.Query().Departments
                .Select(x => new { x.DeptNo, ManagerCount = x.Managers.Count })
                .ToList();
        }
        catch (QueryTranslationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("collection relation 'Managers'");
        await Assert.That(exception.Message).Contains("not supported");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedProjectionMethod_ThrowsWithoutInvokingMethod(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedProjectionMethod_ThrowsWithoutInvokingMethod),
            EmployeesSeedMode.Bogus);

        var probe = new ProjectionMethodProbe();
        QueryTranslationException? exception = null;

        try
        {
            _ = databaseScope.Database.Query().Employees
                .OrderBy(x => x.emp_no)
                .Take(1)
                .Select(x => probe.FormatName(x.first_name))
                .ToList();
        }
        catch (QueryTranslationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Projection method 'FormatName'");
        await Assert.That(probe.InvocationCount).IsEqualTo(0);
    }

    private sealed class ProjectionMethodProbe
    {
        public int InvocationCount { get; private set; }

        public string FormatName(string value)
        {
            InvocationCount++;
            return value.Trim().ToUpperInvariant();
        }
    }

    private sealed class ProjectionReferenceProbe(int value)
    {
        public override bool Equals(object? obj)
            => obj is ProjectionReferenceProbe other && other.Value == Value;

        public override int GetHashCode() => Value;

        private int Value { get; } = value;
    }

    private static string FormatProjectionRows<T>(T[] rows)
    {
        return string.Join(
            "|",
            rows.Select(row =>
            {
                var type = row!.GetType();
                var employeeNumber = type.GetProperty("EmployeeNumber")!.GetValue(row);
                var firstName = type.GetProperty("first_name")!.GetValue(row);
                return $"{employeeNumber}:{firstName}";
            }));
    }

    private static string FormatDepartmentProjectionRows<T>(T[] rows)
    {
        return string.Join(
            "|",
            rows.Select(row =>
            {
                var type = row!.GetType();
                var employeeNumber = type.GetProperty("emp_no")!.GetValue(row);
                var departmentName = type.GetProperty("DepartmentName")!.GetValue(row);
                return $"{employeeNumber}:{departmentName}";
            }));
    }
}
