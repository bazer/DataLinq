using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
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

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_RelationTraversal_ColdCacheMiss_LoadsAndStoresRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_RelationTraversal_ColdCacheMiss_LoadsAndStoresRows),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        var employeeNumber = database.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no)
            .First();

        database.Provider.State.ClearCache();

        DataLinqMetrics.Reset();
        var departmentName = database.Query().Employees
            .Single(x => x.emp_no == employeeNumber)
            .dept_emp
            .First()
            .departments
            .Name;
        var snapshot = DataLinqMetrics.Snapshot();

        await Assert.That(departmentName).IsNotNull();
        await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(1);
        await Assert.That(snapshot.Commands.ReaderExecutions).IsEqualTo(3);
        await Assert.That(snapshot.RowCache.Hits).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.Misses).IsEqualTo(3);
        await Assert.That(snapshot.RowCache.Stores).IsEqualTo(3);
        await Assert.That(snapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(3);
        await Assert.That(snapshot.RowCache.Materializations).IsEqualTo(3);
        await Assert.That(snapshot.Relations.CollectionLoads).IsEqualTo(1);
        await Assert.That(snapshot.Relations.ReferenceLoads).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationIndex_IntegralColdLoadPopulatesWarmKeyPath(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(RelationIndex_IntegralColdLoadPopulatesWarmKeyPath),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        var employeeNumber = database.Query().salaries
            .OrderBy(static salary => salary.emp_no)
            .Select(static salary => salary.emp_no)
            .First();
        var employeeTable = database.Provider.Metadata
            .GetTableModel(typeof(Employee))
            .Table;
        var salariesTable = database.Provider.Metadata
            .GetTableModel(typeof(Salaries))
            .Table;
        var relation = employeeTable.Model.RelationProperties[nameof(Employee.salaries)];
        var relationIndex = relation.RelationPart.GetOtherSide().ColumnIndex;
        var salariesCache = database.Provider.GetTableCache(salariesTable);

        database.Provider.State.ClearCache();
        DataLinqMetrics.Reset();

        try
        {
            var coldRows = salariesCache
                .GetRows(employeeNumber, relation, database.Provider.ReadOnlyAccess)
                .Cast<Salaries>()
                .ToArray();
            var coldSnapshot = DataLinqMetrics.Snapshot();

            var warmRows = salariesCache
                .GetRows(employeeNumber, relation, database.Provider.ReadOnlyAccess)
                .Cast<Salaries>()
                .ToArray();
            var warmSnapshot = DataLinqMetrics.Snapshot();

            await Assert.That(coldRows).IsNotEmpty();
            await Assert.That(warmRows.Select(static row => row.PrimaryKeys()).ToArray())
                .IsEquivalentTo(coldRows.Select(static row => row.PrimaryKeys()).ToArray());
            await Assert.That(warmRows.Length).IsEqualTo(coldRows.Length);
            for (var index = 0; index < coldRows.Length; index++)
                await Assert.That(warmRows[index]).IsSameReferenceAs(coldRows[index]);

            await Assert.That(salariesCache.IndicesCount
                .Single(item => item.index == relationIndex.Name)
                .count).IsEqualTo(1);

            await Assert.That(coldSnapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(coldSnapshot.RowCache.Hits).IsEqualTo(0);
            await Assert.That(coldSnapshot.RowCache.Misses).IsEqualTo(coldRows.Length);
            await Assert.That(coldSnapshot.RowCache.Stores).IsEqualTo(coldRows.Length);
            await Assert.That(coldSnapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(coldRows.Length);
            await Assert.That(coldSnapshot.RowCache.Materializations).IsEqualTo(coldRows.Length);

            await Assert.That(warmSnapshot.Commands.ReaderExecutions).IsEqualTo(1);
            await Assert.That(warmSnapshot.RowCache.Hits).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.Misses).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.Stores).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(coldRows.Length);
            await Assert.That(warmSnapshot.RowCache.Materializations).IsEqualTo(coldRows.Length);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_PrimaryKeySingle_ColdCacheMiss_LoadsAndStoresRow(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_PrimaryKeySingle_ColdCacheMiss_LoadsAndStoresRow),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        var employeeNumber = database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no!.Value)
            .First();

        database.Provider.State.ClearCache();

        DataLinqMetrics.Reset();
        var employee = database.Query().Employees.Single(x => x.emp_no == employeeNumber);
        var snapshot = DataLinqMetrics.Snapshot();

        await Assert.That(employee.emp_no).IsEqualTo(employeeNumber);
        await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(1);
        await Assert.That(snapshot.Commands.ReaderExecutions).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Hits).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.Misses).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Stores).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Materializations).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_PrimaryKeySingleOrDefault_MissingRowPreservesTelemetryWithoutMaterialization(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_PrimaryKeySingleOrDefault_MissingRowPreservesTelemetryWithoutMaterialization),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        const int missingEmployeeNumber = int.MaxValue;

        database.Provider.State.ClearCache();

        DataLinqMetrics.Reset();
        var employee = database.Query().Employees.SingleOrDefault(x => x.emp_no == missingEmployeeNumber);
        var snapshot = DataLinqMetrics.Snapshot();

        await Assert.That(employee).IsNull();
        await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(1);
        await Assert.That(snapshot.Commands.ReaderExecutions).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Hits).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.Misses).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Stores).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.Materializations).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_PrimaryKeySingle_WarmCacheHit_PreservesQueryTelemetryWithoutCommand(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Query_PrimaryKeySingle_WarmCacheHit_PreservesQueryTelemetryWithoutCommand),
            EmployeesSeedMode.Bogus);

        var database = databaseScope.Database;
        var employeeNumber = database.Query().Employees
            .OrderBy(x => x.emp_no)
            .Select(x => x.emp_no!.Value)
            .First();

        database.Provider.State.ClearCache();

        var coldEmployee = database.Query().Employees.Single(x => x.emp_no == employeeNumber);

        DataLinqMetrics.Reset();
        var warmEmployee = database.Query().Employees.Single(x => x.emp_no == employeeNumber);
        var snapshot = DataLinqMetrics.Snapshot();

        await Assert.That(ReferenceEquals(coldEmployee, warmEmployee)).IsTrue();
        await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(1);
        await Assert.That(snapshot.Commands.ReaderExecutions).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.Hits).IsEqualTo(1);
        await Assert.That(snapshot.RowCache.Misses).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.Stores).IsEqualTo(0);
        await Assert.That(snapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(0);
    }
}
