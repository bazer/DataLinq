using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class SessionTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ParticipantMutationFamiliesReturnRowsAndOnlyOwnerCompletes(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(ParticipantMutationFamiliesReturnRowsAndOnlyOwnerCompletes), EmployeesFixtureProfile.TinySeeded);
        await using IDataLinqUnitOfWork<EmployeesDb> owner = scope.Database.Transaction();
        IDataLinqSession<EmployeesDb> session = owner;
        var index = 410;
        Mutable<Department> NewRow()
        {
            var row = new Mutable<Department>();
            row["DeptNo"] = $"w{index++}";
            row["Name"] = $"session {index}";
            return row;
        }
        var inserted = session.Insert(NewRow());
        var withChanges = session.Insert(NewRow(), row => row["Name"] = "sync changes");
        var typed = session.Insert<Department, MutableDepartment>(new() { DeptNo = "w490", Name = "typed" }, row => row.Name = "typed insert");
        var collection = session.Insert(new[] { NewRow(), NewRow() });
        var asyncInserted = await session.InsertAsync(NewRow());
        var asyncChanges = await session.InsertAsync(NewRow(), row => row["Name"] = "async changes");
        var asyncTyped = await session.InsertAsync<Department, MutableDepartment>(new() { DeptNo = "w491", Name = "async typed" }, row => row.Name = "async typed insert");
        var asyncCollection = await session.InsertAsync(new[] { NewRow(), NewRow() });
        await Assert.That(collection.Count + asyncCollection.Count).IsEqualTo(4);
        await Assert.That(withChanges.Name).IsEqualTo("sync changes");
        await Assert.That(asyncChanges.Name).IsEqualTo("async changes");
        await Assert.That(typed.Name).IsEqualTo("typed insert");
        await Assert.That(asyncTyped.Name).IsEqualTo("async typed insert");

        var mutable = new Mutable<Department>(inserted);
        mutable["Name"] = "update one";
        await Assert.That(session.Update(mutable).Name).IsEqualTo("update one");
        await Assert.That(session.Update(mutable, row => row["Name"] = "update two").Name).IsEqualTo("update two");
        await Assert.That(session.Update<Department, MutableDepartment>(typed.Mutate(), row => row.Name = "update typed").Name).IsEqualTo("update typed");
        inserted = session.Update(inserted, row => row["Name"] = "update immutable");
        mutable["Name"] = "update async one";
        await Assert.That((await session.UpdateAsync(mutable)).Name).IsEqualTo("update async one");
        await Assert.That((await session.UpdateAsync(mutable, row => row["Name"] = "update async two")).Name).IsEqualTo("update async two");
        await Assert.That((await session.UpdateAsync<Department, MutableDepartment>(asyncTyped.Mutate(), row => row.Name = "update async typed")).Name).IsEqualTo("update async typed");
        await Assert.That((await session.UpdateAsync(asyncInserted, row => row["Name"] = "update async immutable")).Name).IsEqualTo("update async immutable");

        mutable["Name"] = "save one";
        await Assert.That(session.Save(mutable).Name).IsEqualTo("save one");
        await Assert.That(session.Save(mutable, row => row["Name"] = "save two").Name).IsEqualTo("save two");
        await Assert.That(session.Save<Department, MutableDepartment>(typed.Mutate(), row => row.Name = "save typed").Name).IsEqualTo("save typed");
        await Assert.That(session.Save(inserted, row => row["Name"] = "save immutable").Name).IsEqualTo("save immutable");
        mutable["Name"] = "save async one";
        await Assert.That((await session.SaveAsync(mutable)).Name).IsEqualTo("save async one");
        await Assert.That((await session.SaveAsync(mutable, row => row["Name"] = "save async two")).Name).IsEqualTo("save async two");
        await Assert.That((await session.SaveAsync<Department, MutableDepartment>(asyncTyped.Mutate(), row => row.Name = "save async typed")).Name).IsEqualTo("save async typed");
        await Assert.That((await session.SaveAsync(asyncInserted, row => row["Name"] = "save async immutable")).Name).IsEqualTo("save async immutable");

        void Initialize(Mutable<Department> row) { row["DeptNo"] = $"w{index++}"; row["Name"] = $"nullable {index}"; }
        var nullableImmutable = session.Save((Department?)null, Initialize);
        var nullableMutable = session.Save((Mutable<Department>?)null, Initialize);
        var nullableAsyncImmutable = await session.SaveAsync((Department?)null, Initialize);
        var nullableAsyncMutable = await session.SaveAsync((Mutable<Department>?)null, Initialize);
        await Assert.That(nullableImmutable.DeptNo != nullableMutable.DeptNo).IsTrue();
        await Assert.That(nullableAsyncImmutable.DeptNo != nullableAsyncMutable.DeptNo).IsTrue();
        session.Delete(withChanges);
        await session.DeleteAsync(asyncChanges);
        await Assert.That(session.Get<Department>(withChanges.PrimaryKeys())).IsNull();
        await Assert.That(await session.GetAsync<Department>(asyncChanges.PrimaryKeys())).IsNull();
        await Assert.That(session.Query()).IsSameReferenceAs(session.Query());
        await Assert.That(session.From<Department>().Select().Any(row => row.DeptNo == typed.DeptNo)).IsTrue();
        session.RequestRollback("parent veto");
        await Assert.That(owner.IsRollbackRequested).IsTrue();
        await Assert.That(owner.TryGetRollbackReason<string>(out var reason)).IsTrue();
        await Assert.That(reason).IsEqualTo("parent veto");
        await Assert.That(async () => await owner.CommitAsync()).Throws<DataLinqRollbackRequestedException>();
        await owner.RollbackAsync();
        await Assert.That(await scope.Database.GetAsync<Department>(inserted.PrimaryKeys())).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PreparedParticipantsUseSuppliedTransactionAndCaptureBeforeEnumeration(TestProviderDescriptor descriptor)
    {
        using var preparation = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PreparedParticipantsUseSuppliedTransactionAndCaptureBeforeEnumeration) + "_prepare", EmployeesFixtureProfile.TinySeeded);
        using var execution = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(PreparedParticipantsUseSuppliedTransactionAndCaptureBeforeEnumeration) + "_execute", EmployeesFixtureProfile.TinySeeded);
        var db = preparation.Database;
        var scalar = db.PrepareQuery("w499", key => db.Query().Departments.Count(row => row.DeptNo == key));
        var rowQuery = db.PrepareQuery("w499", key => db.Query().Departments.SingleOrDefault(row => row.DeptNo == key));
        var sequence = db.PrepareSequenceQuery(new[] { "w499" }, keys => db.Query().Departments.Where(row => keys.Contains(row.DeptNo)));
        await using IDataLinqUnitOfWork<EmployeesDb> owner = execution.Database.Transaction();
        IDataLinqSession<EmployeesDb> session = owner;
        await session.InsertAsync(new MutableDepartment { DeptNo = "w499", Name = "only execution" });
        await Assert.That(session.Execute(scalar, "w499")).IsEqualTo(1);
        await Assert.That(await session.ExecuteAsync(scalar, "w499")).IsEqualTo(1);
        await Assert.That(session.Execute(rowQuery, "w499")!.Name).IsEqualTo("only execution");
        await Assert.That((await session.ExecuteAsync(rowQuery, "w499"))!.Name).IsEqualTo("only execution");
        await Assert.That(session.Execute(rowQuery, "none")).IsNull();
        await Assert.That(await session.ExecuteAsync(rowQuery, "none")).IsNull();
        var values = new[] { "w499" };
        var pending = session.Execute(sequence, values);
        var pendingAsync = session.ExecuteAsync(sequence, values);
        values[0] = "none";
        await Assert.That(pending.Single().DeptNo).IsEqualTo("w499");
        await Assert.That((await pendingAsync.ToArrayAsync()).Single().DeptNo).IsEqualTo("w499");
        await Assert.That(scalar.Execute(db, "w499")).IsEqualTo(0);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.That(() => session.Execute(scalar, "w499", cancelled.Token)).Throws<OperationCanceledException>();
        await Assert.That(async () => await session.ExecuteAsync(scalar, "w499", cancelled.Token)).Throws<OperationCanceledException>();
        var deferred = session.ExecuteAsync(sequence, new[] { "w499" });
        session.RequestRollback("after capture");
        await Assert.That(async () => await deferred.ToArrayAsync()).Throws<DataLinqRollbackRequestedException>();
        await owner.RollbackAsync();
        await Assert.That(await execution.Database.GetAsync<Department>(DataLinqKey.FromValue("w499"))).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GeneratedParticipantHelpersPreserveSuppliedOwnerAcrossSyncAndAsyncCalls(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(GeneratedParticipantHelpersPreserveSuppliedOwnerAcrossSyncAndAsyncCalls), EmployeesFixtureProfile.TinySeeded);
        await using IDataLinqUnitOfWork<EmployeesDb> owner = scope.Database.Transaction();
        IDataLinqSession<EmployeesDb> session = owner;
        var mutable = new MutableDepartment { DeptNo = "w498", Name = "initial" };
        var inserted = mutable.Insert(changes: row => row.Name = "inserted", transaction: session);
        var updated = inserted.Update(changes: row => row.Name = "updated", transaction: session);
        var saved = session.Save(model: updated, changes: (MutableDepartment row) => row.Name = "saved");
        await Assert.That(saved.Name).IsEqualTo("saved");
        saved = await saved.SaveAsync(changes: row => row.Name = "saved async", transaction: session);
        mutable = saved.Mutate();
        mutable.Name = "mutable save";
        await Assert.That(mutable.Save(transaction: session).Name).IsEqualTo("mutable save");
        await Assert.That((await mutable.SaveAsync(changes: row => row.Name = "mutable async", transaction: session)).Name).IsEqualTo("mutable async");
        await Assert.That((await session.UpdateAsync(saved, (MutableDepartment row) => row.Name = "extension async")).Name).IsEqualTo("extension async");
        await owner.RollbackAsync();
        await Assert.That(await scope.Database.GetAsync<Department>(DataLinqKey.FromValue("w498"))).IsNull();
    }
}
