using System;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Compliance;

public sealed class UnitOfWorkFactoryTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task OwningCallbacksCommitDataOrReportVetoAfterConfirmedCleanup(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(OwningCallbacksCommitDataOrReportVetoAfterConfirmedCleanup), EmployeesFixtureProfile.TinySeeded);
        IDataLinqUnitOfWorkFactory<EmployeesDb> factory = new DataLinqUnitOfWorkFactory<EmployeesDb>(scope.Database);
        foreach (var asynchronous in new[] { false, true })
        foreach (var veto in new[] { false, true })
        {
            var calls = 0;
            IDataLinqSession<EmployeesDb>? captured = null;
            TransactionResult<int> result = default;
            Exception? failure = null;
            try
            {
                if (asynchronous)
                    result = await factory.ExecuteAsync(async (session, token) =>
                    {
                        captured = session;
                        calls++;
                        await session.InsertAsync(NewDepartment(), token);
                        if (veto) session.RequestRollback("original reason");
                        return (TransactionResult<int>)new Rollback<string>("mapped data");
                    });
                else
                    result = factory.Execute(session =>
                    {
                        captured = session;
                        calls++;
                        session.Insert(NewDepartment());
                        if (veto) session.RequestRollback("original reason");
                        return (TransactionResult<int>)new Rollback<string>("mapped data");
                    });
            }
            catch (Exception error) { failure = error; }
            await Assert.That(calls).IsEqualTo(1);
            if (veto)
            {
                await Assert.That(failure).IsTypeOf<DataLinqRollbackRequestedException>();
                var context = DataLinqFailure.GetContext(failure!)!;
                await Assert.That(context.CompletionOutcome).IsEqualTo(DataLinqCompletionOutcome.RolledBack);
                await Assert.That(context.RecoveryActions).IsEqualTo(DataLinqRecoveryActions.None);
                await Assert.That(context.SecondaryFailures).IsEmpty();
            }
            else
            {
                await Assert.That(failure).IsNull();
                await Assert.That(result.Reason).IsEqualTo("mapped data");
            }
            await Assert.That(() => captured!.Query()).Throws<ObjectDisposedException>();
            var row = await scope.Database.GetAsync<Department>(DataLinqKey.FromValue("w480"));
            if (veto) await Assert.That(row).IsNull();
            else
            {
                await Assert.That(row!.Name).IsEqualTo("factory");
                await scope.Database.DeleteAsync(row);
            }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ResultAwareCallbacksRequireExplicitVetoAndFinishRecoveryBeforeReturning(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(ResultAwareCallbacksRequireExplicitVetoAndFinishRecoveryBeforeReturning), EmployeesFixtureProfile.TinySeeded);
        IDataLinqUnitOfWorkFactory<EmployeesDb> factory = new DataLinqUnitOfWorkFactory<EmployeesDb>(scope.Database);
        foreach (var asynchronous in new[] { false, true })
        foreach (var write in new[] { false, true })
        foreach (var veto in new[] { false, true })
        {
            IDataLinqSession<EmployeesDb>? captured = null;
            TransactionResult<int> Result(IDataLinqSession<EmployeesDb> session)
            {
                captured = session;
                if (veto) session.RequestRollback("first diagnostic reason");
                return new Rollback<string>("mapped outcome");
            }
            var result = asynchronous
                ? await factory.ExecuteResultAsync(async (session, token) =>
                {
                    if (write) await session.InsertAsync(NewDepartment(), token);
                    return Result(session);
                })
                : factory.ExecuteResult(session =>
                {
                    if (write) session.Insert(NewDepartment());
                    return Result(session);
                });
            await Assert.That(result.Reason).IsEqualTo("mapped outcome");
            await Assert.That(captured!.IsRollbackRequested).IsEqualTo(veto);
            if (veto)
            {
                await Assert.That(captured.TryGetRollbackReason<string>(out var reason)).IsTrue();
                await Assert.That(reason).IsEqualTo("first diagnostic reason");
            }
            await Assert.That(() => captured.Query()).Throws<ObjectDisposedException>();
            var row = await scope.Database.GetAsync<Department>(DataLinqKey.FromValue("w480"));
            if (veto || !write) await Assert.That(row).IsNull();
            else
            {
                await Assert.That(row!.Name).IsEqualTo("factory");
                await scope.Database.DeleteAsync(row);
            }
        }
    }

    private static MutableDepartment NewDepartment() => new() { DeptNo = "w480", Name = "factory" };
}
