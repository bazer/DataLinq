using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesOptimizationTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_SingleKey_ReturnsDataLinqKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_SingleKey_ReturnsDataLinqKey),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("employees")
            .Where("emp_no").EqualTo(1001)
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNotNull();
        await Assert.That(key!.Value.ValueCount).IsEqualTo(1);
        await Assert.That(key.Value.GetValue(0)).IsEqualTo(1001);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_CompositePrimaryKey_ReturnsDataLinqKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_CompositePrimaryKey_ReturnsDataLinqKey),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("dept-emp")
            .Where("dept_no").EqualTo("d001")
            .And("emp_no").EqualTo(1001)
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNotNull();
        await Assert.That(key!.Value.ValueCount).IsEqualTo(2);
        await Assert.That(key.Value.GetValue(0)).IsEqualTo("d001");
        await Assert.That(key.Value.GetValue(1)).IsEqualTo(1001);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_NonPrimaryKeyPredicate_ReturnsNull(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_NonPrimaryKeyPredicate_ReturnsNull),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("employees")
            .Where("first_name").EqualTo("Bob")
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_PrimaryKeyAndOtherPredicate_ReturnsNull(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_PrimaryKeyAndOtherPredicate_ReturnsNull),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("employees")
            .Where("emp_no").EqualTo(1001)
            .And("first_name").EqualTo("Bob")
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_PartialCompositePrimaryKey_ReturnsNull(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_PartialCompositePrimaryKey_ReturnsNull),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("dept-emp")
            .Where("dept_no").EqualTo("d001")
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_OrCondition_ReturnsNull(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_OrCondition_ReturnsNull),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("employees")
            .Where("emp_no").EqualTo(1001)
            .Or("emp_no").EqualTo(1002)
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_NegatedPredicate_ReturnsNull(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_NegatedPredicate_ReturnsNull),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("employees")
            .WhereNot("emp_no").EqualTo(1001)
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_WorksWithEvaluatedVariable(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_WorksWithEvaluatedVariable),
            EmployeesSeedMode.Bogus);

        var employeeId = 9999;
        Expression<System.Func<Employee, bool>> expression = employee => employee.emp_no == employeeId;
        var queryable = databaseScope.Database.Query().Employees.Where(expression);

        await Assert.That(queryable).IsNotNull();

        var key = databaseScope.Database
            .From("employees")
            .Where("emp_no").EqualTo(employeeId)
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNotNull();
        await Assert.That(key!.Value.ValueCount).IsEqualTo(1);
        await Assert.That(key.Value.GetValue(0)).IsEqualTo(employeeId);
    }
}
