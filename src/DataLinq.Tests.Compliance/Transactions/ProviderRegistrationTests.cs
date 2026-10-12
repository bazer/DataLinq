using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.MariaDB.Extensions.DependencyInjection;
using DataLinq.Mutation;
using DataLinq.MySql.Extensions.DependencyInjection;
using DataLinq.SQLite.Extensions.DependencyInjection;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataLinq.Tests.Compliance;

public sealed class ProviderRegistrationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SelectedProviderCapturesSettingsAndRunsOwnedUnitsThroughEveryAlias(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(SelectedProviderCapturesSettingsAndRunsOwnedUnitsThroughEveryAlias), EmployeesFixtureProfile.TinySeeded);
        foreach (var asynchronous in new[] { false, true })
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:active"] = scope.Connection.ConnectionString,
                ["DataLinq:DatabaseName"] = scope.Connection.DataSourceName,
                ["DataLinq:Execution:RecoveryRollbackTimeout"] = "00:00:09"
            }).Build();
            var calls = 0;
            DataLinqProviderOptions? retained = null;
            void Capture(DataLinqProviderOptions options) { calls++; retained = options; }
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(config);
            var builder = services.AddDataLinq<EmployeesDb>("selected").AsDefault();
            switch (descriptor.DatabaseType)
            {
                case DatabaseType.SQLite: builder.UseSQLiteConnectionString("active").Bind(config.GetSection("DataLinq")).Configure(Capture); break;
                case DatabaseType.MySQL: builder.UseMySqlConnectionString("active").Bind(config.GetSection("DataLinq")).Configure(Capture); break;
                case DatabaseType.MariaDB: builder.UseMariaDBConnectionString("active").Bind(config.GetSection("DataLinq")).Configure(Capture); break;
                default: throw new NotSupportedException();
            }
            var container = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            try
            {
                await Assert.That(calls).IsEqualTo(0);
                using var child = container.CreateScope();
                var factory = child.ServiceProvider.GetRequiredKeyedService<IDataLinqUnitOfWorkFactory<EmployeesDb>>("selected");
                var database = child.ServiceProvider.GetRequiredService<Database<EmployeesDb>>();
                await Assert.That(database.Provider.DatabaseType).IsEqualTo(descriptor.DatabaseType);
                await Assert.That(database.Provider.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(9));
                await Assert.That(child.ServiceProvider.GetRequiredService<EmployeesDb>()).IsSameReferenceAs(database.Query());
                await Assert.That(child.ServiceProvider.GetRequiredKeyedService<ReadOnlyAccess<EmployeesDb>>("selected")).IsSameReferenceAs(database.Provider.ReadOnlyAccess);
                await Assert.That(child.ServiceProvider.GetRequiredService<IDataLinqUnitOfWorkFactory<EmployeesDb>>()).IsSameReferenceAs(factory);
                config["ConnectionStrings:active"] = "invalid after capture";
                retained!.Execution = new() { RecoveryRollbackTimeout = TimeSpan.FromSeconds(1) };
                await Assert.That(database.Provider.ExecutionOptions.RecoveryRollbackTimeout).IsEqualTo(TimeSpan.FromSeconds(9));
                foreach (var veto in new[] { false, true })
                {
                    TransactionResult<int> Result(IDataLinqSession<EmployeesDb> session)
                    {
                        if (veto) session.RequestRollback("configured veto");
                        return new Rollback<string>("data");
                    }
                    if (asynchronous)
                        await factory.ExecuteResultAsync(async (session, token) =>
                        {
                            await session.InsertAsync(new MutableDepartment { DeptNo = "w4c0", Name = "configured" }, token);
                            return Result(session);
                        });
                    else
                        factory.ExecuteResult(session =>
                        {
                            session.Insert(new MutableDepartment { DeptNo = "w4c0", Name = "configured" });
                            return Result(session);
                        });
                    var saved = await database.GetAsync<Department>(DataLinqKey.FromValue("w4c0"));
                    if (veto) await Assert.That(saved).IsNull();
                    else { await Assert.That(saved!.Name).IsEqualTo("configured"); await database.DeleteAsync(saved); }
                }
                await Assert.That(calls).IsEqualTo(1);
            }
            finally { if (asynchronous) await container.DisposeAsync(); else container.Dispose(); }
            // The independently owned fixture provider must survive container shutdown.
            await Assert.That(await scope.Database.GetAsync<Department>(DataLinqKey.FromValue("w4c0"))).IsNull();
        }
    }
}
