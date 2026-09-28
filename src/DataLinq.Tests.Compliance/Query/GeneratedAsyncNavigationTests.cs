using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class GeneratedAsyncNavigationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicBaseNavigationSharesSyncStateAndTransitionsAfterCommit(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicBaseNavigationSharesSyncStateAndTransitionsAfterCommit), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var row = database.Query().DepartmentEmployees.First();
        database.Cache.Clear();
        Dept_emp child = (await Dept_emp.GetAsync(row.dept_no, row.emp_no, database))!;
        var parent = await child.departmentsAsync();
        await Assert.That(parent.DeptNo).IsEqualTo(child.dept_no);
        await Assert.That(child.departments).IsSameReferenceAs(parent);
        await Assert.That(await child.departmentsAsync()).IsSameReferenceAs(parent);
        await Assert.That(async () => { await child.departmentsAsync(new(true)); }).Throws<OperationCanceledException>();
        database.Cache.Clear();
        child.ClearLazy();
        await Assert.That((await child.departmentsAsync()).DeptNo).IsEqualTo(child.dept_no);
        await using var transaction = database.Transaction();
        Dept_emp borrowed = (await Dept_emp.GetAsync(child.dept_no, child.emp_no, transaction))!;
        await Assert.That((await borrowed.departmentsAsync()).DeptNo).IsEqualTo(child.dept_no);
        await transaction.CommitAsync();
        await Assert.That((await borrowed.departmentsAsync()).DeptNo).IsEqualTo(child.dept_no);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ConvertedReferenceKeysUseTheSameCanonicalParent(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<TypedIdRelationKeyDb>.Create(descriptor, nameof(ConvertedReferenceKeysUseTheSameCanonicalParent));
        var database = scope.Database;
        database.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO typedidrelationkeyparents (id, name) VALUES (101, 'parent')");
        database.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO typedidrelationkeychildren (id, parent_id, name) VALUES (201, 101, 'child')");
        var child = (await TypedIdRelationKeyChild.GetAsync(new QueryTypedId(201), database))!;
        var parent = await child.ParentAsync(cancellationToken: default);
        await Assert.That(parent.Id).IsEqualTo(new QueryTypedId(101));
        await Assert.That(child.Parent).IsSameReferenceAs(parent);
        await Assert.That(await TypedIdRelationKeyParent.GetAsync(new QueryTypedId(101), database)).IsSameReferenceAs(parent);
    }
}
