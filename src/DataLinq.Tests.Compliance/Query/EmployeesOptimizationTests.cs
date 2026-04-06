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
    public async Task Query_TryGetSimplePrimaryKey_SingleKey_ReturnsIntKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_SingleKey_ReturnsIntKey),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("employees")
            .Where("emp_no").EqualTo(1001)
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNotNull();
        await Assert.That(key).IsTypeOf<IntKey>();
        await Assert.That(((IntKey)key!).Value).IsEqualTo(1001);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TryGetSimplePrimaryKey_CompositeKey_ReturnsCompositeKey(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_CompositeKey_ReturnsCompositeKey),
            EmployeesSeedMode.Bogus);

        var key = databaseScope.Database
            .From("dept-emp")
            .Where("dept_no").EqualTo("d001")
            .And("emp_no").EqualTo(1001)
            .Query.TryGetSimplePrimaryKey();

        await Assert.That(key).IsNotNull();
        await Assert.That(key).IsTypeOf<CompositeKey>();

        var compositeKey = (CompositeKey)key!;
        await Assert.That(compositeKey.Values.Length).IsEqualTo(2);
        await Assert.That(compositeKey.Values.Contains("d001")).IsTrue();
        await Assert.That(compositeKey.Values.Contains(1001)).IsTrue();
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
    public async Task Query_TryGetSimplePrimaryKey_PartialCompositeKey_ReturnsNull(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_TryGetSimplePrimaryKey_PartialCompositeKey_ReturnsNull),
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
        await Assert.That(key).IsTypeOf<IntKey>();
        await Assert.That(((IntKey)key!).Value).IsEqualTo(employeeId);
    }
}
