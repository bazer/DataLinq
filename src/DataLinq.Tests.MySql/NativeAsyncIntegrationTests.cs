using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.MySql;

public sealed class NativeAsyncIntegrationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task QueryPlansPreparedScalarsAndKeyLookupsMatchSynchronousResults(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(QueryPlansPreparedScalarsAndKeyLookupsMatchSynchronousResults), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var expected = database.Query().Departments.OrderBy(row => row.DeptNo).ToArray();
        database.Provider.State.ClearCache();
        var rows = await Rows(AsyncPlan(database.Query().Departments.OrderBy(row => row.DeptNo)));
        await Assert.That(rows.Select(row => row.DeptNo).ToArray()).IsEquivalentTo(expected.Select(row => row.DeptNo).ToArray());
        var first = rows[0];
        await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<Department>([first.DeptNo], database.Provider.ReadOnlyAccess)).IsSameReferenceAs(first);
        await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["none"], database.Provider.ReadOnlyAccess)).IsNull();
        var count = database.PrepareQuery(0, _ => database.Query().Departments.Count());
        await Assert.That(await count.ExecuteAsyncCore(database, 0)).IsEqualTo(expected.Length);
        var any = database.PrepareQuery(first.DeptNo, key => database.Query().Departments.Any(row => row.DeptNo == key));
        await Assert.That(await any.ExecuteAsyncCore(database, first.DeptNo)).IsTrue();
        await Assert.That(await any.ExecuteAsyncCore(database, "none")).IsFalse();
        var names = await Rows(AsyncPlan(database.Query().Departments.OrderBy(row => row.DeptNo).Select(row => row.Name)));
        await Assert.That(names.ToArray()).IsEquivalentTo(expected.Select(row => row.Name).ToArray());
        var transaction = database.Transaction();
        try
        {
            await Assert.That(await count.ExecuteAsyncCore(transaction, 0)).IsEqualTo(expected.Length);
            var owned = await Rows(AsyncPlan(transaction.Query().Departments.OrderBy(row => row.DeptNo)));
            await Assert.That(owned.Select(row => row.Name).ToArray()).IsEquivalentTo(names.ToArray());
            var fluent = await transaction.From<Department>().OrderBy("dept_no").SelectQuery().ExecuteBufferedAsyncCore();
            await Assert.That(fluent.Cast<Department>().Select(row => row.DeptNo).ToArray()).IsEquivalentTo(expected.Select(row => row.DeptNo).ToArray());
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task NativeRelationRowsShareCompleteCachedSnapshot(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(NativeRelationRowsShareCompleteCachedSnapshot), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var parent = database.Query().Departments.OrderBy(row => row.DeptNo).First();
        var expected = parent.DepartmentEmployees.Values.Select(row => row.emp_no).OrderBy(value => value).ToArray();
        database.Provider.State.ClearCache();
        var property = database.Provider.Metadata.GetTableModel(typeof(Department)).Model.RelationProperties[nameof(Department.DepartmentEmployees)];
        var relation = new ImmutableRelation<Dept_emp, string>(parent.DeptNo, database.Provider.ReadOnlyAccess, property);
        var rows = await relation.GetValuesAsyncCore();
        await Assert.That(rows.Select(row => row.emp_no).OrderBy(value => value).ToArray()).IsEquivalentTo(expected);
        var keyed = await relation.GetInstancesAsyncCore();
        foreach (var row in rows) await Assert.That(keyed[row.PrimaryKeys()]).IsSameReferenceAs(row);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.That(async () => { await relation.GetValuesAsyncCore(canceled.Token); }).Throws<OperationCanceledException>();
        var transaction = database.Transaction();
        try
        {
            var owned = new ImmutableRelation<Dept_emp, string>(parent.DeptNo, transaction, property);
            await Assert.That((await owned.GetValuesAsyncCore()).Select(row => row.emp_no).OrderBy(value => value).ToArray()).IsEquivalentTo(expected);
        }
        finally { await transaction.DisposeAsyncCore(); }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task MutationsHydratePrivatelyAndPublishOnlyAfterCommit(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(MutationsHydratePrivatelyAndPublishOnlyAfterCommit), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w201"], database.Provider.ReadOnlyAccess)).IsNull();
        var mutable = new MutableDepartment { DeptNo = "w201", Name = "Native inserted" };
        var transaction = database.Transaction();
        try
        {
            var inserted = await transaction.InsertAsyncCore(mutable);
            await Assert.That(inserted.Name).IsEqualTo("Native inserted");
            await Assert.That(mutable.IsNew()).IsFalse();
            await Assert.That(mutable.HasChanges()).IsFalse();
            await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w201"], database.Provider.ReadOnlyAccess)).IsNull();
            mutable.Name = "Native updated";
            var updated = await transaction.UpdateAsyncCore(mutable);
            await Assert.That(updated.Name).IsEqualTo("Native updated");
            await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w201"], transaction))!.Name).IsEqualTo("Native updated");
            await Assert.That(transaction.Changes.Count).IsEqualTo(2);
            await transaction.CommitAsyncCore();
        }
        finally { await transaction.DisposeAsyncCore(); }
        var committed = await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w201"], database.Provider.ReadOnlyAccess);
        await Assert.That(committed!.Name).IsEqualTo("Native updated");
        var deleting = database.Transaction();
        try { await deleting.DeleteAsyncCore(committed); await deleting.CommitAsyncCore(); }
        finally { await deleting.DisposeAsyncCore(); }
        await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<Department>(["w201"], database.Provider.ReadOnlyAccess)).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.ServerFamily)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task RollbackPreservesCommittedCacheAndDiscardsHydratedWrites(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(RollbackPreservesCommittedCacheAndDiscardsHydratedWrites), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var original = database.Query().Departments.OrderBy(row => row.DeptNo).First();
        var mutable = original.Mutate();
        var transaction = database.Transaction();
        try
        {
            mutable.Name = "Must roll back";
            var pending = await transaction.SaveAsyncCore(mutable);
            await Assert.That(pending.Name).IsEqualTo("Must roll back");
            await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<Department>([original.DeptNo], database.Provider.ReadOnlyAccess))!.Name).IsEqualTo(original.Name);
            await transaction.RollbackAsyncCore();
        }
        finally { await transaction.DisposeAsyncCore(); }
        await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<Department>([original.DeptNo], database.Provider.ReadOnlyAccess))!.Name).IsEqualTo(original.Name);
        database.Provider.State.ClearCache();
        await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<Department>([original.DeptNo], database.Provider.ReadOnlyAccess))!.Name).IsEqualTo(original.Name);
        await Assert.That(((IMutableLifecycle)mutable).Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(((IMutableLifecycle)mutable).Lifecycle.InvalidationReason).IsEqualTo(MutableInvalidationReason.RolledBack);
        var retry = database.Transaction();
        try { await Assert.That(async () => { await retry.UpdateAsyncCore(mutable); }).Throws<InvalidOperationException>(); }
        finally { await retry.DisposeAsyncCore(); }
    }

    private static IAsyncEnumerable<T> AsyncPlan<T>(IQueryable<T> query) =>
        ((ExpressionQueryPlanProvider)query.Provider).ExecuteEnumerableAsyncCore<T>(query.Expression, default);

    private static async Task<List<T>> Rows<T>(IAsyncEnumerable<T> rows)
    {
        var result = new List<T>();
        await foreach (var row in rows) result.Add(row);
        return result;
    }
}
