using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Query;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesSqlQueryTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlQuery_SimpleWhereSelectsDepartmentAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlQuery_SimpleWhereSelectsDepartmentAcrossProviders),
            EmployeesSeedMode.Bogus);

        var departments = databaseScope.Database
            .From<Department>()
            .Where("dept_no").EqualTo("d005")
            .Select();

        await Assert.That(departments.Count).IsEqualTo(1);
        await Assert.That(departments.Single().DeptNo).IsEqualTo("d005");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlQuery_GetFromQueryReturnsExpectedDepartmentAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlQuery_GetFromQueryReturnsExpectedDepartmentAcrossProviders),
            EmployeesSeedMode.Bogus);
        using var transaction = databaseScope.Database.Transaction();

        var departments = transaction
            .GetFromQuery<Department>("SELECT * FROM departments WHERE dept_no = 'd005'");

        await Assert.That(departments.Count).IsEqualTo(1);
        await Assert.That(departments.Single().DeptNo).IsEqualTo("d005");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_JoinWhereOrderLimitRendersProviderSpecificSql(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlBuilder_JoinWhereOrderLimitRendersProviderSpecificSql));

        var employeesDatabase = databaseScope.Database;
        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(employeesDatabase);

        var sql = employeesDatabase
            .From("departments d")
            .Join("dept_manager m").On(on => on.Where("d.dept_no").EqualToColumn("m.dept_no"))
            .Where("m.dept_no").EqualTo("d005")
            .Limit(1)
            .OrderByDesc("d.dept_no")
            .SelectQuery()
            .ToSql();

        var expectedSql = $@"SELECT d.{escapeCharacter}dept_no{escapeCharacter}, d.{escapeCharacter}dept_name{escapeCharacter} FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} d
JOIN {databasePrefix}{escapeCharacter}dept_manager{escapeCharacter} m ON d.{escapeCharacter}dept_no{escapeCharacter} = m.{escapeCharacter}dept_no{escapeCharacter}
WHERE
m.{escapeCharacter}dept_no{escapeCharacter} = {parameterSign}w0
ORDER BY d.{escapeCharacter}dept_no{escapeCharacter} DESC
LIMIT 1";

        await Assert.That(sql.Text).IsEqualTo(expectedSql);
        await Assert.That(sql.Parameters.Count).IsEqualTo(1);
        await Assert.That(sql.Parameters[0].ParameterName).IsEqualTo($"{parameterSign}w0");
        await Assert.That(sql.Parameters[0].Value).IsEqualTo("d005");
        await Assert.That(sql.Parameters[0].ProviderParameter).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_ToDbCommandMaterializesProviderParametersAtCommandBoundary(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlBuilder_ToDbCommandMaterializesProviderParametersAtCommandBoundary));

        var employeesDatabase = databaseScope.Database;
        var (parameterSign, _, _) = GetSqlConstants(employeesDatabase);

        var select = employeesDatabase
            .From<Department>()
            .Where("dept_no").EqualTo("d005")
            .SelectQuery();
        var sql = select.ToSql();

        await Assert.That(sql.Parameters.Count).IsEqualTo(1);
        await Assert.That(sql.Parameters[0].ParameterName).IsEqualTo($"{parameterSign}w0");
        await Assert.That(sql.Parameters[0].Value).IsEqualTo("d005");
        await Assert.That(sql.Parameters[0].ProviderParameter).IsNull();

        using var command = select.ToDbCommand();

        await Assert.That(command.Parameters.Count).IsEqualTo(1);

        var parameter = (IDataParameter)command.Parameters[0]!;
        await Assert.That(parameter.ParameterName).IsEqualTo($"{parameterSign}w0");
        await Assert.That(parameter.Value).IsEqualTo("d005");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_RepeatedSingleValueEqualityShapeKeepsCurrentParameterValue(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlBuilder_RepeatedSingleValueEqualityShapeKeepsCurrentParameterValue));

        static Sql CreateSql(Database<EmployeesDb> database, string departmentNumber)
            => database
                .From<Department>()
                .What("dept_no")
                .Where("dept_no").EqualTo(departmentNumber)
                .OrderBy("dept_no")
                .Limit(1)
                .SelectQuery()
                .ToSql();

        var firstSql = CreateSql(databaseScope.Database, "d005");
        var secondSql = CreateSql(databaseScope.Database, "d006");

        await Assert.That(secondSql.Text).IsEqualTo(firstSql.Text);
        await Assert.That(firstSql.Parameters.Count).IsEqualTo(1);
        await Assert.That(secondSql.Parameters.Count).IsEqualTo(1);
        await Assert.That(firstSql.Parameters[0].Value).IsEqualTo("d005");
        await Assert.That(secondSql.Parameters[0].Value).IsEqualTo("d006");
        await Assert.That(firstSql.Parameters[0].ParameterName).IsEqualTo(secondSql.Parameters[0].ParameterName);
        await Assert.That(secondSql.Parameters[0].ProviderParameter).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_RepeatedAndEqualityShapeKeepsCurrentParameterValues(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlBuilder_RepeatedAndEqualityShapeKeepsCurrentParameterValues));

        static Sql CreateSql(Database<EmployeesDb> database, string departmentNumber, string departmentName)
            => database
                .From<Department>()
                .What("dept_no")
                .Where("dept_no").EqualTo(departmentNumber)
                .Where("dept_name").EqualTo(departmentName)
                .SelectQuery()
                .ToSql();

        var firstSql = CreateSql(databaseScope.Database, "d005", "Development");
        var secondSql = CreateSql(databaseScope.Database, "d006", "Quality Management");

        await Assert.That(secondSql.Text).IsEqualTo(firstSql.Text);
        await Assert.That(firstSql.Parameters.Count).IsEqualTo(2);
        await Assert.That(secondSql.Parameters.Count).IsEqualTo(2);
        await Assert.That(firstSql.Parameters[0].Value).IsEqualTo("d005");
        await Assert.That(firstSql.Parameters[1].Value).IsEqualTo("Development");
        await Assert.That(secondSql.Parameters[0].Value).IsEqualTo("d006");
        await Assert.That(secondSql.Parameters[1].Value).IsEqualTo("Quality Management");
        await Assert.That(firstSql.Parameters[0].ParameterName).IsEqualTo(secondSql.Parameters[0].ParameterName);
        await Assert.That(firstSql.Parameters[1].ParameterName).IsEqualTo(secondSql.Parameters[1].ParameterName);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlBuilder_RepeatedInPredicateShapeKeepsCurrentParameterValues(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(SqlBuilder_RepeatedInPredicateShapeKeepsCurrentParameterValues));

        static Sql CreateSql(Database<EmployeesDb> database, string firstDepartmentNumber, string secondDepartmentNumber)
            => database
                .From<Department>()
                .What("dept_no")
                .Where("dept_no").In(firstDepartmentNumber, secondDepartmentNumber)
                .OrderBy("dept_no")
                .SelectQuery()
                .ToSql();

        var firstSql = CreateSql(databaseScope.Database, "d005", "d006");
        var secondSql = CreateSql(databaseScope.Database, "d007", "d008");

        await Assert.That(secondSql.Text).IsEqualTo(firstSql.Text);
        await Assert.That(firstSql.Parameters.Count).IsEqualTo(2);
        await Assert.That(secondSql.Parameters.Count).IsEqualTo(2);
        await Assert.That(firstSql.Parameters[0].Value).IsEqualTo("d005");
        await Assert.That(firstSql.Parameters[1].Value).IsEqualTo("d006");
        await Assert.That(secondSql.Parameters[0].Value).IsEqualTo("d007");
        await Assert.That(secondSql.Parameters[1].Value).IsEqualTo("d008");
        await Assert.That(firstSql.Parameters[0].ParameterName).IsEqualTo(secondSql.Parameters[0].ParameterName);
        await Assert.That(firstSql.Parameters[1].ParameterName).IsEqualTo(secondSql.Parameters[1].ParameterName);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Literal_ToDbCommandPreservesSuppliedProviderParameter(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Literal_ToDbCommandPreservesSuppliedProviderParameter));

        var employeesDatabase = databaseScope.Database;
        var (parameterSign, escapeCharacter, databasePrefix) = GetSqlConstants(employeesDatabase);
        using var connection = employeesDatabase.Provider.GetDbConnection();
        using var parameterCommand = connection.CreateCommand();
        var suppliedParameter = parameterCommand.CreateParameter();
        suppliedParameter.ParameterName = $"{parameterSign}deptNo";
        suppliedParameter.Value = "d005";

        var literal = new Literal(
            employeesDatabase.Provider.ReadOnlyAccess,
            $"SELECT * FROM {databasePrefix}{escapeCharacter}departments{escapeCharacter} WHERE {escapeCharacter}dept_no{escapeCharacter} = {parameterSign}deptNo",
            suppliedParameter);
        var sql = literal.ToSql();

        await Assert.That(sql.Parameters.Count).IsEqualTo(1);
        await Assert.That(sql.Parameters[0].ParameterName).IsEqualTo($"{parameterSign}deptNo");
        await Assert.That(sql.Parameters[0].Value).IsEqualTo("d005");
        await Assert.That(sql.Parameters[0].ProviderParameter).IsSameReferenceAs(suppliedParameter);

        using var command = literal.ToDbCommand();

        await Assert.That(command.Parameters.Count).IsEqualTo(1);

        var commandParameter = (IDataParameter)command.Parameters[0]!;
        await Assert.That(commandParameter.ParameterName).IsEqualTo($"{parameterSign}deptNo");
        await Assert.That(commandParameter.Value).IsEqualTo("d005");
    }

    private static (string parameterSign, string escapeCharacter, string databasePrefix) GetSqlConstants(Database<EmployeesDb> employeesDatabase)
    {
        var constants = employeesDatabase.Provider.Constants;
        var databasePrefix = constants.SupportsMultipleDatabases
            ? $"{constants.EscapeCharacter}{employeesDatabase.Provider.DatabaseName}{constants.EscapeCharacter}."
            : string.Empty;

        return (
            constants.ParameterSign,
            constants.EscapeCharacter,
            databasePrefix);
    }
}
