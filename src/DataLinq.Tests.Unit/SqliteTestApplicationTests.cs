using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.MySql.Extensions.DependencyInjection;
using DataLinq.SQLite;
using DataLinq.SQLite.Extensions.DependencyInjection;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using DataLinq.Tests.Unit.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DataLinq.Tests.Unit;

public sealed class SqliteTestApplicationTests
{
    private sealed class RenameDepartment(IDataLinqUnitOfWorkFactory<EmployeesDb> factory)
    {
        public void Execute(string newName, string newCode) => factory.Execute(session =>
        {
            var department = session.Query().Departments.Single(d => d.DeptNo == "a");
            var mutable = department.Mutate();
            mutable.Name = newName;
            session.Update(mutable);
            if (session.Query().Departments.Single(d => d.DeptNo == "a").Name != newName)
                throw new InvalidOperationException("The transaction lost its own update.");
            session.Insert(new MutableDepartment(newCode, "New department"));
        });
    }

    [Test]
    public async Task HandlerCommitsOrRollsBackTheWholeOperationAndKeepsCachedReadsFresh()
    {
        using var fixture = SqliteTestDatabase<EmployeesDb>.Create();
        fixture.Seed(s => { s.Insert(new MutableDepartment("a", "Original")); });
        var handler = new RenameDepartment(fixture.UnitOfWorkFactory);

        async Task AssertStored(string name, int count)
        {
            await Assert.That(fixture.Query().Departments.Single(d => d.DeptNo == "a").Name).IsEqualTo(name);
            var access = fixture.Database.Provider.DatabaseAccess;
            await Assert.That(access.ExecuteScalar<string>("SELECT dept_name FROM departments WHERE dept_no = 'a'")).IsEqualTo(name);
            await Assert.That(access.ExecuteScalar<long>("SELECT COUNT(*) FROM departments")).IsEqualTo((long)count);
        }

        await AssertStored("Original", 1);
        await Assert.That(() => handler.Execute("Pending", "a")).Throws<SqliteException>();
        await AssertStored("Original", 1);
        handler.Execute("Renamed", "b");
        await AssertStored("Renamed", 2);
        using (var abandoned = fixture.UnitOfWorkFactory.Create())
            abandoned.Delete(abandoned.Query().Departments.Single(d => d.DeptNo == "b"));
        await AssertStored("Renamed", 2);
        fixture.UnitOfWorkFactory.Execute(s => s.Delete(s.Query().Departments.Single(d => d.DeptNo == "b")));
        await Assert.That(fixture.Query().Departments.Any(d => d.DeptNo == "b")).IsFalse();
        await AssertStored("Renamed", 1);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task HostRoutesCanonicalAliasesGeneratedServiceAndValidationToBorrowedFixture(bool sqlite)
    {
        await using var fixture = await SqliteTestDatabase<TransactionMutationGuardDb>.CreateAsync();
        await using var other = SqliteTestDatabase<TransactionMutationGuardDb>.Create();
        var builder = Host.CreateApplicationBuilder();
        var services = builder.Services;
        services.AddSingleton(new GeneratedServiceProbe());
        services.AddDataLinq<TransactionMutationGuardDb>("other").UseInstance(other.Database);
        var selected = services.AddDataLinq<TransactionMutationGuardDb>("selected");
        if (sqlite) selected.UseSQLiteConnectionString("missing");
        else selected.UseMySqlConnectionString("missing");
        selected.AsDefault().AddScopedService<GeneratedPlainService>().ValidateSchemaOnStartup();
        services.AddDataLinqSchemaValidation().ValidateDatabase<TransactionMutationGuardDb>("selected");
        services.ReplaceDataLinqWithSqliteInMemory(fixture, sqlite ? "selected" : null);
        using (var host = builder.Build())
        {
            await host.StartAsync();
            await using var scope = host.Services.CreateAsyncScope();
            var p = scope.ServiceProvider;
            await Assert.That(p.GetRequiredKeyedService<SQLiteDatabase<TransactionMutationGuardDb>>("selected")).IsSameReferenceAs(fixture.Database);
            await Assert.That(p.GetRequiredService<SQLiteDatabase<TransactionMutationGuardDb>>()).IsSameReferenceAs(fixture.Database);
            await Assert.That(() => p.GetRequiredKeyedService<MySqlDatabase<TransactionMutationGuardDb>>("selected")).Throws<InvalidOperationException>();
            await Assert.That(() => p.GetRequiredService<MySqlDatabase<TransactionMutationGuardDb>>()).Throws<InvalidOperationException>();
            await Assert.That(p.GetRequiredKeyedService<Database<TransactionMutationGuardDb>>("other")).IsSameReferenceAs(other.Database);
            await Assert.That(p.GetRequiredService<IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>>()).IsSameReferenceAs(fixture.UnitOfWorkFactory);
            await Assert.That(p.GetRequiredKeyedService<TransactionMutationGuardDb>("selected")).IsSameReferenceAs(fixture.Query());
            var service = p.GetRequiredService<GeneratedPlainService>();
            await Assert.That(p.GetRequiredKeyedService<GeneratedPlainService>("selected")).IsSameReferenceAs(service);
            await Assert.That(service.Provider()).IsEqualTo(fixture.Database.Provider.TelemetryInstanceId);
            await host.StopAsync();
        }
        var inserted = fixture.Seed(s => s.Insert(new MutableTransactionMutationGuardAutoRow { Value = "after disposal" }));
        await Assert.That(fixture.Query().AutoRows.Single().Id).IsEqualTo(inserted.Id);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task StartupObservesDriftOnTheReplacedTargetUnlessDisabled(bool enabled)
    {
        using var fixture = SqliteTestDatabase<TransactionMutationGuardDb>.Create();
        fixture.Database.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE transaction_mutation_guard_rows");
        var services = new ServiceCollection();
        services.AddDataLinq<TransactionMutationGuardDb>().UseSQLiteConnectionString("missing");
        services.AddDataLinqSchemaValidation(options => options.Mode = enabled ? DataLinqSchemaStartupMode.FailStartup
            : DataLinqSchemaStartupMode.Disabled).ValidateDatabase<TransactionMutationGuardDb>();
        if (!enabled)
            services.AddDataLinq<TransactionMutationGuardDb>("unused").UseFactory<Database<TransactionMutationGuardDb>>(
                _ => throw new Exception("Disabled startup activated an unused target.")).ValidateSchemaOnStartup();
        services.ReplaceDataLinqWithSqliteInMemory(fixture);
        using var container = services.BuildServiceProvider();
        var startup = (IHostedLifecycleService)container.GetServices<IHostedService>().Single();
        if (enabled)
        {
            var failure = await Assert.That(() => startup.StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
            await Assert.That(failure!.Outcomes.Single().Result!.HasFailures).IsTrue();
        }
        else await startup.StartingAsync(default);
    }

    [Test]
    public async Task ExplicitNonDefaultKeyPreservesTheOriginalDefault()
    {
        using var original = SqliteTestDatabase<EmployeesDb>.Create();
        using var replacement = SqliteTestDatabase<EmployeesDb>.Create();
        var services = new ServiceCollection();
        services.AddDataLinq<EmployeesDb>().UseInstance(original.Database);
        services.AddDataLinq<EmployeesDb>("reporting").UseMySqlConnectionString("unused");
        services.ReplaceDataLinqWithSqliteInMemory(replacement, "reporting");
        using var container = services.BuildServiceProvider();
        await Assert.That(container.GetRequiredService<SQLiteDatabase<EmployeesDb>>()).IsSameReferenceAs(original.Database);
        await Assert.That(container.GetRequiredKeyedService<SQLiteDatabase<EmployeesDb>>("reporting")).IsSameReferenceAs(replacement.Database);
    }

    [Test]
    public async Task MissingDefaultOrExplicitKeyFailsDuringSelection()
    {
        using var fixture = SqliteTestDatabase<EmployeesDb>.Create();
        var services = new ServiceCollection();
        var only = services.AddDataLinq<EmployeesDb>("only").UseSQLite();
        var missingDefault = await Assert.That(() => services.ReplaceDataLinqWithSqliteInMemory(fixture)).Throws<InvalidOperationException>();
        await Assert.That(missingDefault!.Message).Contains(nameof(EmployeesDb)).And.Contains("default");
        only.AsDefault();
        var missingKey = await Assert.That(() => services.ReplaceDataLinqWithSqliteInMemory(fixture, "missing")).Throws<InvalidOperationException>();
        await Assert.That(missingKey!.Message).Contains(nameof(EmployeesDb)).And.Contains("missing");
    }
}
