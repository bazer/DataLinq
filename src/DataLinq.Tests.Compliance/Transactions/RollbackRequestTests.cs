using System;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class RollbackRequestTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ExplicitVetoPreventsManualAndCallbackCommitsAcrossExecutionModes(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(ExplicitVetoPreventsManualAndCallbackCommitsAcrossExecutionModes), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        foreach (var asynchronous in new[] { false, true })
        foreach (var callback in new[] { false, true })
        {
            if (callback)
            {
                if (asynchronous)
                    await Assert.That(async () => await database.CommitAsync(async transaction =>
                    {
                        await transaction.InsertAsync(NewDepartment());
                        transaction.RequestRollback("business rule"); // Intentionally ignore the marker.
                    })).Throws<DataLinqRollbackRequestedException>();
                else
                    await Assert.That(() => database.Commit(transaction =>
                    {
                        transaction.Insert(NewDepartment());
                        transaction.RequestRollback("business rule");
                    })).Throws<DataLinqRollbackRequestedException>();
            }
            else
            {
                await using var transaction = database.Transaction();
                if (asynchronous) await transaction.InsertAsync(NewDepartment()); else transaction.Insert(NewDepartment());
                transaction.RequestRollback("business rule");
                if (asynchronous)
                {
                    await Assert.That(async () => await transaction.CommitAsync()).Throws<DataLinqRollbackRequestedException>();
                    await transaction.RollbackAsync();
                }
                else
                {
                    await Assert.That(transaction.Commit).Throws<DataLinqRollbackRequestedException>();
                    transaction.Rollback();
                }
                await Assert.That(() => transaction.RequestRollback("terminal")).Throws<InvalidOperationException>();
            }
            await Assert.That(await database.GetAsync<Department>(DataLinqKey.FromValue("w401"))).IsNull();
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task OptionalFailureDataDoesNotUndoWritesAndActiveReaderCanFinishAfterVeto(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(OptionalFailureDataDoesNotUndoWritesAndActiveReaderCanFinishAfterVeto), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var result = await database.CommitAsync(async transaction =>
        {
            await transaction.InsertAsync(NewDepartment());
            try { _ = default(TransactionResult<int>).TryUnwrap(out _, out _); }
            catch (InvalidOperationException) { }
            return (TransactionResult<int>)new Rollback<string>("ordinary failure data");
        });
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That((await database.GetAsync<Department>(DataLinqKey.FromValue("w401")))!.Name).IsEqualTo("W4 rollback");
        await using var transaction = database.Transaction();
        var deferred = transaction.DatabaseAccess.ReadReaderAsync("SELECT 1 UNION ALL SELECT 2");
        await using var unused = deferred.GetAsyncEnumerator();
        await using (var reader = await transaction.DatabaseAccess.ExecuteReaderAsync("SELECT 1 UNION ALL SELECT 2"))
        {
            await Assert.That(await reader.ReadNextRowAsync()).IsTrue();
            transaction.RequestRollback("between rows");
            await Assert.That(await reader.ReadNextRowAsync()).IsTrue();
            await Assert.That(reader.GetInt32(0)).IsEqualTo(2);
            await Assert.That(await reader.ReadNextRowAsync()).IsFalse();
        }
        await Assert.That(async () => await unused.MoveNextAsync()).Throws<DataLinqRollbackRequestedException>();
        await transaction.RollbackAsync();
    }

    private static Mutable<Department> NewDepartment()
    {
        var row = new Mutable<Department>();
        row["DeptNo"] = "w401";
        row["Name"] = "W4 rollback";
        return row;
    }
}
