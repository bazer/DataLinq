using System;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class GeneratedAsyncMutationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GeneratedMutationReceiverFamiliesPersistAndLeaveBorrowedTransactionsOpen(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(GeneratedMutationReceiverFamiliesPersistAndLeaveBorrowedTransactionsOpen), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var mutable = new MutableDepartment("w341", "generated 1");
        var row = await mutable.InsertAsync(database);
        await Assert.That(row.Name).IsEqualTo("generated 1");
        row = await new MutableDepartment("w342", "before").InsertAsync(m => m.Name = "generated 2", database);
        await Assert.That(row.Name).IsEqualTo("generated 2");
        row = await row.UpdateAsync(m => m.Name = "generated 3");
        row = await database.UpdateAsync(row, m => m.Name = "generated 4");
        mutable = row.Mutate();
        mutable.Name = "generated 5";
        row = await mutable.UpdateAsync(database);
        row = await row.SaveAsync(m => m.Name = "generated 6");
        row = await database.SaveAsync(row, m => m.Name = "generated 7");
        row = await row.SaveAsync(m => m.Name = "generated 8", database);
        mutable = row.Mutate();
        mutable.Name = "generated 9";
        row = await mutable.SaveAsync(database);
        row = await mutable.SaveAsync(m => m.Name = "generated 10", database);
        await Assert.That((await Department.GetAsync("w342", database))!.Name).IsEqualTo("generated 10");

        await using (var transaction = database.Transaction())
        {
            var inserted = await new MutableDepartment("w343", "before").InsertAsync(m => m.Name = "generated 11", transaction);
            inserted = await DepartmentExtensions.InsertAsync(transaction, new MutableDepartment("w344", "before"), m => m.Name = "generated 12");
            inserted = await inserted.UpdateAsync(m => m.Name = "generated 13", transaction);
            inserted = await DepartmentExtensions.UpdateAsync(transaction, inserted, m => m.Name = "generated 14");
            inserted = await inserted.SaveAsync(m => m.Name = "generated 15", transaction);
            inserted = await DepartmentExtensions.SaveAsync(transaction, inserted, m => m.Name = "generated 16");
            mutable = inserted.Mutate();
            inserted = await mutable.SaveAsync(m => m.Name = "generated 17", transaction);
            mutable.Name = "generated 18";
            inserted = await mutable.SaveAsync(transaction);
            inserted = await DepartmentExtensions.SaveAsync(transaction, mutable, m => m.Name = "generated 19");
            await Assert.That(inserted.Name).IsEqualTo("generated 19");
            await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
            await transaction.CommitAsync();
        }
        await Assert.That((await Department.GetAsync("w344", database))!.Name).IsEqualTo("generated 19");
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GeneratedMutationCancellationAndImmutableSaveDoNotRunUnexpectedEdits(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(GeneratedMutationCancellationAndImmutableSaveDoNotRunUnexpectedEdits), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var mutable = new MutableDepartment("w345", "unchanged");
        var edits = 0;
        await Assert.That(async () => await mutable.InsertAsync(m => { edits++; m.Name = "unexpected"; }, database, new(true)))
            .Throws<OperationCanceledException>();
        await Assert.That(edits).IsEqualTo(0);
        await Assert.That(await Department.GetAsync("w345", database)).IsNull();
        var row = await mutable.SaveAsync(m => m.Name = "saved new", database);
        await Assert.That(row.Name).IsEqualTo("saved new");
        Department absent = null!;
        await Assert.That(async () => await absent.SaveAsync(m => edits++, database)).Throws<ArgumentNullException>();
        await Assert.That(edits).IsEqualTo(0);
        await using (var transaction = database.Transaction())
        {
            await row.UpdateAsync(m => m.Name = "rolled back", transaction);
            await transaction.RollbackAsync();
        }
        await Assert.That((await Department.GetAsync("w345", database))!.Name).IsEqualTo("saved new");
    }
}
