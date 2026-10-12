using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq;
using DataLinq.Mutation;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Tests.Compliance;

public sealed class GeneratedServiceTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NestedGeneratedServicesUseTheSuppliedDatabaseAndPreserveMappedVetoes(TestProviderDescriptor descriptor)
    {
        using var selected = EmployeesTestDatabase.CreateIsolated(descriptor, "GeneratedSelected", EmployeesFixtureProfile.TinySeeded);
        using var other = EmployeesTestDatabase.CreateIsolated(descriptor, "GeneratedOther", EmployeesFixtureProfile.TinySeeded);
        var services = new ServiceCollection();
        services.AddDataLinq<EmployeesDb>("selected").UseInstance(selected.Database).AddSingletonService<GeneratedDepartmentService>().AsDefault();
        services.AddDataLinq<EmployeesDb>("other").UseInstance(other.Database).AddSingletonService<GeneratedDepartmentService>();
        await using var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var parent = container.GetRequiredService<GeneratedDepartmentService>();
        var child = container.GetRequiredKeyedService<GeneratedDepartmentService>("other");
        var prepared = other.Database.PrepareQuery("w4p", prefix => other.Database.Query().Departments.Count(row => row.DeptNo.StartsWith(prefix)));
        // An owning child call really selects the other registration.
        var standalone = child.Write("w4p1", prepared, 1, false);
        await Assert.That(standalone.IsSuccess).IsTrue();
        await Assert.That(await selected.Database.GetAsync<Department>(DataLinqKey.FromValue("w4p1"))).IsNull();
        await other.Database.DeleteAsync(standalone.SuccessValue);
        foreach (var asynchronous in new[] { false, true })
        foreach (var veto in new[] { false, true })
        {
            var result = asynchronous ? await parent.ComposeAsync(child, prepared, veto) : parent.Compose(child, prepared, veto);
            await Assert.That(result.IsSuccess).IsEqualTo(!veto);
            if (veto) await Assert.That(result.Reason).IsEqualTo("mapped parent outcome");
            else await Assert.That(result.SuccessValue).IsEqualTo(2);
            foreach (var id in new[] { "w4p0", "w4p1" })
            {
                await Assert.That(await other.Database.GetAsync<Department>(DataLinqKey.FromValue(id))).IsNull();
                var row = await selected.Database.GetAsync<Department>(DataLinqKey.FromValue(id));
                if (veto) await Assert.That(row).IsNull();
                else { await Assert.That(row).IsNotNull(); await selected.Database.DeleteAsync(row!); }
            }
        }
    }
}

public sealed partial class GeneratedDepartmentService(IDataLinqUnitOfWorkFactory<EmployeesDb> factory) : IDataLinqService<EmployeesDb>
{
    [DataLinqOperation]
    private TransactionResult<Department> WriteCore(IDataLinqSession<EmployeesDb> session, string id,
        PreparedQuery<EmployeesDb, string, int> prepared, int expected, bool veto)
    {
        var row = session.Insert(new MutableDepartment { DeptNo = id, Name = "generated child" });
        if (session.Execute(prepared, "w4p") != expected) throw new InvalidOperationException("Prepared child execution lost the supplied transaction's writes.");
        return veto ? TransactionResult<Department>.FromRollback(session.RequestRollback("child veto")) : row;
    }

    [DataLinqOperation]
    private async Task<TransactionResult<Department>> WriteCoreAsync(IDataLinqSession<EmployeesDb> session, string id,
        PreparedQuery<EmployeesDb, string, int> prepared, int expected, bool veto, CancellationToken cancellationToken)
    {
        var row = await session.InsertAsync(new MutableDepartment { DeptNo = id, Name = "generated child async" }, cancellationToken);
        if (await session.ExecuteAsync(prepared, "w4p", cancellationToken) != expected) throw new InvalidOperationException("Prepared async child execution lost the supplied transaction's writes.");
        return veto ? TransactionResult<Department>.FromRollback(session.RequestRollback("child veto")) : row;
    }

    [DataLinqOperation(ResultAware = true)]
    private TransactionResult<int> ComposeCore(IDataLinqSession<EmployeesDb> session, GeneratedDepartmentService child,
        PreparedQuery<EmployeesDb, string, int> prepared, bool veto)
    {
        session.Insert(new MutableDepartment { DeptNo = "w4p0", Name = "generated parent" });
        var result = child.Write(session, "w4p1", prepared, 2, veto);
        return Map(session, result);
    }

    [DataLinqOperation(ResultAware = true)]
    private async Task<TransactionResult<int>> ComposeCoreAsync(IDataLinqSession<EmployeesDb> session, GeneratedDepartmentService child,
        PreparedQuery<EmployeesDb, string, int> prepared, bool veto, CancellationToken cancellationToken)
    {
        await session.InsertAsync(new MutableDepartment { DeptNo = "w4p0", Name = "generated parent async" }, cancellationToken);
        var result = await child.WriteAsync(session, "w4p1", prepared, 2, veto, cancellationToken);
        return Map(session, result);
    }

    private static TransactionResult<int> Map(IDataLinqSession<EmployeesDb> session, TransactionResult<Department> child)
    {
        if (child.IsSuccess) return 2;
        if (!session.IsRollbackRequested || !session.TryGetRollbackReason<string>(out var reason) || reason != "child veto")
            throw new InvalidOperationException("Participating child did not preserve its explicit veto.");
        session.RequestRollback("mapped parent veto");
        if (!session.TryGetRollbackReason<string>(out reason) || reason != "child veto")
            throw new InvalidOperationException("Parent mapping replaced the first reason.");
        return new Rollback<string>("mapped parent outcome");
    }
}
