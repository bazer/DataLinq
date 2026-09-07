using System.Linq;
using System.Threading.Tasks;
using DataLinq.Query;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public class PrimaryKeyQueryShapeTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PrimaryKeySelectionRespectsPaginationWithColdAndWarmCache(TestProviderDescriptor provider)
    {
        using var scope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(PrimaryKeySelectionRespectsPaginationWithColdAndWarmCache), EmployeesFixtureProfile.FullSeeded);
        var database = scope.Database;
        foreach (var warm in new[] { false, true })
        {
            database.Provider.State.ClearCache();
            if (warm) _ = database.Query().Departments.Single(row => row.DeptNo == "d001");

            foreach (var page in new[] { (limit: 0, offset: 0), (limit: 1, offset: 1), (limit: 1, offset: 0) })
            {
                var query = new SqlQuery<Department>(database.Provider.ReadOnlyAccess).Limit(page.limit, page.offset);
                query.Where("dept_no").EqualTo("d001");
                var expected = query.SelectQuery().ReadReader().Count();
                await Assert.That(query.Select().Count()).IsEqualTo(expected);
                await Assert.That(expected).IsEqualTo(page.limit == 1 && page.offset == 0 ? 1 : 0);
            }

            await Assert.That(database.Query().Departments.Where(row => row.DeptNo == "d001").Take(0).ToArray()).IsEmpty();
            await Assert.That(database.Query().Departments.Where(row => row.DeptNo == "d001").Skip(1).Take(1).ToArray()).IsEmpty();
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PrimaryKeySelectionRespectsRestrictiveJoinWithColdAndWarmCache(TestProviderDescriptor provider)
    {
        using var scope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(PrimaryKeySelectionRespectsRestrictiveJoinWithColdAndWarmCache), EmployeesFixtureProfile.FullSeeded);
        foreach (var warm in new[] { false, true })
        {
            scope.Database.Provider.State.ClearCache();
            if (warm) _ = scope.Database.Query().Departments.Single(row => row.DeptNo == "d001");
            var query = new SqlQuery<Department>(scope.Database.Provider.ReadOnlyAccess, "d");
            query.Join("departments", "allowed").On(on => on.Where("dept_no", "allowed").EqualTo("missing"));
            query.Where("dept_no", "d").EqualTo("d001");
            await Assert.That(query.SelectQuery().ReadReader().Count()).IsEqualTo(0);
            await Assert.That(query.Select().ToArray()).IsEmpty();
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CompositePrimaryKeySelectionRespectsZeroLimit(TestProviderDescriptor provider)
    {
        using var scope = EmployeesTestDatabase.OpenSharedSeeded(provider, nameof(CompositePrimaryKeySelectionRespectsZeroLimit), EmployeesFixtureProfile.FullSeeded);
        var row = scope.Database.Query().DepartmentEmployees.First();
        var query = scope.Database.From("dept-emp").Where("dept_no").EqualTo(row.dept_no).And("emp_no").EqualTo(row.emp_no).Query.Limit(0);
        await Assert.That(query.SelectQuery().ReadReader().Count()).IsEqualTo(0);
        await Assert.That(query.Select().ToArray()).IsEmpty();
    }
}
