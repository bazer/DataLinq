using System.Linq;
using System.Threading.Tasks;
using DataLinq.Query;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.TUnit;

public class EmployeesSqlQueryTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SqlQuery_SimpleWhereSelectsDepartmentAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
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
        using var databaseScope = EmployeesTestDatabase.Create(
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
        using var databaseScope = EmployeesTestDatabase.Create(
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
