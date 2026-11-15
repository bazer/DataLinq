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
using Microsoft.Extensions.Configuration;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReplacingOnOwnerKeepsSiblingWritesServicesAndStartupBoundToItsOriginalStore(bool resolveSiblingFirst)
    {
        using var original = SqliteTestDatabase<TransactionMutationGuardDb>.Create();
        using var replacement = SqliteTestDatabase<TransactionMutationGuardDb>.Create();
        IServiceCollection owner = new ServiceCollection();
        owner.AddSingleton(new GeneratedServiceProbe());
        owner.AddDataLinq<TransactionMutationGuardDb>("orders").UseInstance(original.Database)
            .AsDefault().AddScopedService<GeneratedPlainService>().ValidateSchemaOnStartup();
        IServiceCollection sibling = new ServiceCollection();
        foreach (var descriptor in owner) sibling.Add(descriptor);

        if (!resolveSiblingFirst) owner.ReplaceDataLinqWithSqliteInMemory(replacement);
        using var siblingHost = sibling.BuildServiceProvider();
        if (resolveSiblingFirst)
        {
            siblingHost.GetRequiredService<IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>>();
            owner.ReplaceDataLinqWithSqliteInMemory(replacement);
        }
        using var ownerHost = owner.BuildServiceProvider();
        foreach (var (host, fixture, value) in new[] { (siblingHost, original, "sibling"), (ownerHost, replacement, "owner") })
        {
            using var scope = host.CreateScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredKeyedService<SQLiteDatabase<TransactionMutationGuardDb>>("orders");
            await Assert.That(db).IsSameReferenceAs(fixture.Database);
            services.GetRequiredService<IDataLinqUnitOfWorkFactory<TransactionMutationGuardDb>>()
                .Execute(s => s.Insert(new MutableTransactionMutationGuardAutoRow { Value = value }));
            await Assert.That(db.Provider.DatabaseAccess.ExecuteScalar<string>(
                "SELECT value FROM transaction_mutation_guard_auto_rows")).IsEqualTo(value);
            var service = services.GetRequiredService<GeneratedPlainService>();
            await Assert.That(service.Provider()).IsEqualTo(db.Provider.TelemetryInstanceId);
        }

        original.Database.Provider.DatabaseAccess.ExecuteNonQuery("DROP TABLE transaction_mutation_guard_rows");
        var ownerStartup = (IHostedLifecycleService)ownerHost.GetServices<IHostedService>().Single();
        var siblingStartup = (IHostedLifecycleService)siblingHost.GetServices<IHostedService>().Single();
        await ownerStartup.StartingAsync(default);
        await Assert.That(() => siblingStartup.StartingAsync(default)).Throws<DataLinqSchemaStartupException>();
    }

    [Test]
    public async Task SiblingFactoryCannotTakeOwnershipOfItsOriginalBorrowedProviderAfterOwnerReplacement()
    {
        using var original = SqliteTestDatabase<EmployeesDb>.Create();
        using var replacement = SqliteTestDatabase<EmployeesDb>.Create();
        IServiceCollection owner = new ServiceCollection();
        owner.AddDataLinq<EmployeesDb>().UseInstance(original.Database);
        owner.AddDataLinq<EmployeesDb>("conflict").UseFactory(_ => original.Database);
        IServiceCollection sibling = new ServiceCollection();
        foreach (var descriptor in owner) sibling.Add(descriptor);
        owner.ReplaceDataLinqWithSqliteInMemory(replacement);

        using (var host = sibling.BuildServiceProvider())
        {
            var failure = await Assert.That(() => host.GetRequiredKeyedService<Database<EmployeesDb>>("conflict"))
                .Throws<InvalidOperationException>();
            await Assert.That(failure!.Message).Contains("container-owned");
        }
        await Assert.That(original.Query().Departments.Any()).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisplacedProviderBuilderCannotRedirectSiblingWrites(bool freezeBeforeReplacement)
    {
        using var original = SqliteTestDatabase<EmployeesDb>.Create();
        using var replacement = SqliteTestDatabase<EmployeesDb>.Create();
        IServiceCollection owner = new ServiceCollection();
        var builder = owner.AddDataLinq<EmployeesDb>().UseSQLite(original.Database.Provider.ConnectionString);
        IServiceCollection sibling = new ServiceCollection();
        foreach (var descriptor in owner) sibling.Add(descriptor);
        if (freezeBeforeReplacement)
        {
            using var first = sibling.BuildServiceProvider();
            first.GetRequiredService<IDataLinqUnitOfWorkFactory<EmployeesDb>>();
            await Assert.That(() => builder.Configure(_ => { })).Throws<InvalidOperationException>();
        }

        owner.ReplaceDataLinqWithSqliteInMemory(replacement);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        configuration["ConnectionString"] = replacement.Database.Provider.ConnectionString;
        await Assert.That(() => builder.Bind(configuration)).Throws<InvalidOperationException>();
        await Assert.That(() => builder.Configure(options => options.ConnectionString = replacement.Database.Provider.ConnectionString))
            .Throws<InvalidOperationException>();

        await Assert.That(() => builder.AsDefault()).Throws<InvalidOperationException>();
        await Assert.That(() => builder.ValidateSchemaOnStartup()).Throws<InvalidOperationException>();

        using var host = sibling.BuildServiceProvider();
        host.GetRequiredService<IDataLinqUnitOfWorkFactory<EmployeesDb>>()
            .Execute(s => s.Insert(new MutableDepartment("a", "Original")));
        const string count = "SELECT COUNT(*) FROM departments";
        await Assert.That(original.Database.Provider.DatabaseAccess.ExecuteScalar<long>(count)).IsEqualTo(1L);
        await Assert.That(replacement.Database.Provider.DatabaseAccess.ExecuteScalar<long>(count)).IsEqualTo(0L);
    }

    [Test]
    public async Task ReplacementOnCopiedDescriptorsFailsBeforeEitherHostChanges()
    {
        using var original = SqliteTestDatabase<EmployeesDb>.Create();
        using var replacement = SqliteTestDatabase<EmployeesDb>.Create();
        IServiceCollection template = new ServiceCollection();
        template.AddDataLinq<EmployeesDb>().UseInstance(original.Database);
        IServiceCollection copy = new ServiceCollection();
        foreach (var descriptor in template) copy.Add(descriptor);
        var before = template.ToArray();

        var failure = await Assert.That(() => copy.ReplaceDataLinqWithSqliteInMemory(replacement)).Throws<InvalidOperationException>();
        await Assert.That(failure!.Message).Contains("original service collection");
        await Assert.That(template.SequenceEqual(before)).IsTrue();
        await Assert.That(copy.SequenceEqual(before)).IsTrue();

        foreach (var (services, code) in new[] { (template, "a"), (copy, "b") })
        {
            using var host = services.BuildServiceProvider();
            await Assert.That(host.GetRequiredService<SQLiteDatabase<EmployeesDb>>()).IsSameReferenceAs(original.Database);
            var factory = host.GetRequiredService<IDataLinqUnitOfWorkFactory<EmployeesDb>>();
            factory.Execute(session => session.Insert(new MutableDepartment(code, "Department " + code)));
        }
        await Assert.That(original.Query().Departments.Count()).IsEqualTo(2);
        await Assert.That(replacement.Query().Departments.Any()).IsFalse();
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
