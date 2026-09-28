using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Linq;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class PublicAsyncRelationLookupTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicCanonicalLookupMatchesCacheTransactionAndMissingKeySemantics(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicCanonicalLookupMatchesCacheTransactionAndMissingKeySemantics), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var expected = database.Query().Departments.First();
        var key = expected.PrimaryKeys();
        database.Cache.Clear();
        var loaded = await database.GetAsync<Department>(key);
        await Assert.That(loaded!.DeptNo).IsEqualTo(expected.DeptNo);
        await Assert.That(await database.GetAsync<Department>(key)).IsSameReferenceAs(loaded);
        await Assert.That(await IImmutable<Department>.GetByProviderKeyAsync(expected.DeptNo, database.Provider.ReadOnlyAccess)).IsSameReferenceAs(loaded);
        var cache = database.Provider.GetTableCache(database.Provider.Metadata.GetTableModel(typeof(Department)).Table);
        await Assert.That(await cache.GetRowAsync(key, null!)).IsSameReferenceAs(loaded);
        await Assert.That(await database.GetAsync<Department>(DataLinqKey.FromValue("none"))).IsNull();
        await Assert.That(await database.GetAsync<Department>(DataLinqKey.Null)).IsNull();
        using var transaction = database.Transaction();
        await Assert.That((await transaction.GetAsync<Department>(key))!.DeptNo).IsEqualTo(expected.DeptNo);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.That(async () => { await transaction.GetAsync<Department>(key, canceled.Token); }).Throws<OperationCanceledException>();
        await Assert.That((await transaction.GetAsync<Department>(key))!.DeptNo).IsEqualTo(expected.DeptNo);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicRelationMembersPreserveMembershipAndUseSharedWarmSnapshots(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicRelationMembersPreserveMembershipAndUseSharedWarmSnapshots), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var child = database.Query().DepartmentEmployees.First();
        var parent = child.departments;
        var expected = parent.DepartmentEmployees.Values;
        database.Cache.Clear();
        var property = database.Provider.Metadata.GetTableModel(typeof(Department)).Model.RelationProperties[nameof(Department.DepartmentEmployees)];
        var concrete = new ImmutableRelation<Dept_emp, string>(parent.DeptNo, database.Provider.ReadOnlyAccess, property);
        IImmutableRelation<Dept_emp> relation = concrete;
        var rows = await concrete.ValuesAsync();
        await Assert.That(rows.Select(row => row.emp_no).OrderBy(value => value).ToArray())
            .IsEquivalentTo(expected.Select(row => row.emp_no).OrderBy(value => value).ToArray());
        await Assert.That(await relation.CountAsync()).IsEqualTo(rows.Length);
        await Assert.That(await relation.SumAsync(row => row.emp_no)).IsEqualTo(rows.Sum(row => row.emp_no));
        var first = rows[0];
        await Assert.That(await concrete.GetAsync(first.PrimaryKeys())).IsSameReferenceAs(first);
        await Assert.That(await relation.ContainsKeyAsync(first.PrimaryKeys())).IsTrue();
        await Assert.That((await concrete.ToFrozenDictionaryAsync())[first.PrimaryKeys()]).IsSameReferenceAs(first);
        await Assert.That((await relation.ToListAsync())[0]).IsSameReferenceAs(first);
        await Assert.That(await relation.GetAsync(DataLinqKey.FromValues(["none", first.emp_no]))).IsNull();
        await Assert.That(async () => { await concrete.ValuesAsync(new(true)); }).Throws<OperationCanceledException>();
        concrete.Clear();
        await Assert.That((await concrete.ValuesAsync()).Length).IsEqualTo(expected.Length);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicReferenceCapabilitySharesStateAndRelationHandlesTransitionAfterCommit(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicReferenceCapabilitySharesStateAndRelationHandlesTransitionAfterCommit), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var child = database.Query().DepartmentEmployees.First();
        var referenceProperty = database.Provider.Metadata.GetTableModel(typeof(Dept_emp)).Model.RelationProperties[nameof(Dept_emp.departments)];
        var reference = new ImmutableForeignKey<Department, string>(child.dept_no, database.Provider.ReadOnlyAccess, referenceProperty);
        IAsyncImmutableForeignKey<Department> asyncReference = reference;
        IImmutableForeignKey<IImmutableInstance> covariant = reference;
        var row = await asyncReference.GetAsync();
        await Assert.That(covariant.Value).IsSameReferenceAs(row);
        await Assert.That(async () => { await reference.GetAsync(new(true)); }).Throws<OperationCanceledException>();
        reference.Clear();
        await Assert.That((await reference.GetAsync())!.DeptNo).IsEqualTo(child.dept_no);
        using var transaction = database.Transaction();
        var property = database.Provider.Metadata.GetTableModel(typeof(Department)).Model.RelationProperties[nameof(Department.DepartmentEmployees)];
        var relation = new ImmutableRelation<Dept_emp, string>(child.dept_no, transaction, property);
        var before = await relation.ValuesAsync();
        transaction.Commit();
        var after = await relation.ToArrayAsync();
        await Assert.That(after.Select(value => value.PrimaryKeys()).ToArray()).IsEquivalentTo(before.Select(value => value.PrimaryKeys()).ToArray());
    }
}
