using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class PublicAsyncMutationLifecycleTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicOwnedMutationHelpersPersistAndExtensionsRespectTransactionOwnership(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicOwnedMutationHelpersPersistAndExtensionsRespectTransactionOwnership), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var mutable = NewDepartment("w301", "public insert");
        var inserted = await database.InsertAsync(mutable);
        await Assert.That(inserted.Name).IsEqualTo("public insert");
        mutable["Name"] = "public update";
        await Assert.That((await database.UpdateAsync(mutable)).Name).IsEqualTo("public update");
        mutable["Name"] = "public save";
        await Assert.That((await database.SaveAsync(mutable)).Name).IsEqualTo("public save");
        await database.DeleteAsync(mutable);
        await Assert.That(await database.GetAsync<Department>(DataLinqKey.FromValue("w301"))).IsNull();
        await using (var transaction = database.Transaction())
        {
            var added = await NewDepartment("w302", "transaction").InsertAsync(transaction);
            var changed = new Mutable<Department>(added);
            changed["Name"] = "changed";
            await changed.UpdateAsync(transaction);
            changed["Name"] = "saved";
            await changed.SaveAsync(transaction);
            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
            await added.DeleteAsync(transaction);
            await transaction.CommitAsync();
        }
        await Assert.That(await database.GetAsync<Department>(DataLinqKey.FromValue("w302"))).IsNull();
        var sourceLess = await database.InsertAsync(NewDepartment("w303", "independent"));
        await sourceLess.DeleteAsync();
        await Assert.That(await database.GetAsync<Department>(sourceLess.PrimaryKeys())).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicCallbacksCommitOnceAndRecoverFailureWithoutReturningPartialResults(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicCallbacksCommitOnceAndRecoverFailureWithoutReturningPartialResults), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var result = await database.CommitAsync(async (transaction, ct) =>
        {
            var rows = await transaction.InsertAsync(new[] { NewDepartment("w311", "one"), NewDepartment("w312", "two") }, ct);
            return rows.Count;
        });
        await Assert.That(result).IsEqualTo(2);
        IDatabaseProvider provider = database.Provider;
        await provider.CommitAsync(async (transaction, ct) =>
        {
            await transaction.UpdateAsync((await database.GetAsync<Department>(DataLinqKey.FromValue("w311"), ct))!, changes => changes["Name"] = "edited", ct);
        });
        await Assert.That((await database.GetAsync<Department>(DataLinqKey.FromValue("w311")))!.Name).IsEqualTo("edited");
        var expected = new InvalidOperationException("callback failed");
        Exception? actual = null;
        try
        {
            await database.CommitAsync(async transaction =>
            {
                await transaction.SaveAsync((Department?)null, changes => { changes["DeptNo"] = "w313"; changes["Name"] = "rollback"; });
                throw expected;
            });
        }
        catch (Exception error) { actual = error; }
        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(await database.GetAsync<Department>(DataLinqKey.FromValue("w313"))).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicCompletionCancellationAndAwaitUsingLeaveNoCommittedChanges(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PublicCompletionCancellationAndAwaitUsingLeaveNoCommittedChanges), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        await using (var transaction = database.Transaction())
        {
            await transaction.InsertAsync(NewDepartment("w321", "rollback"));
            await Assert.That(async () => await transaction.CommitAsync(new(true))).Throws<OperationCanceledException>();
            await transaction.RollbackAsync();
        }
        await using (var transaction = database.Transaction())
            await transaction.InsertAsync(NewDepartment("w322", "dispose"));
        await Assert.That(await database.Query().Departments.AnyAsync(row => row.DeptNo == "w321" || row.DeptNo == "w322")).IsFalse();
        await database.DisposeAsync();
        await ((IAsyncDisposable)database.Provider).DisposeAsync();
    }

    private static Mutable<Department> NewDepartment(string key, string name)
    {
        var model = new Mutable<Department>();
        model["DeptNo"] = key;
        model["Name"] = name;
        return model;
    }
}
