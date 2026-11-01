using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Extensions.DependencyInjection;
using DataLinq.Extensions.Hosting;
using DataLinq.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DataLinq.Tests.Unit.SQLite;

public sealed partial class SQLiteNativeAsyncMetadataTests
{
    [Test]
    [Arguments(false, DataLinqOwnership.Application)]
    [Arguments(true, DataLinqOwnership.Application)]
    [Arguments(false, DataLinqOwnership.Container)]
    [Arguments(true, DataLinqOwnership.Container)]
    public async Task Startup_NativeSQLiteRetainsW4BorrowedAndTransferredOwnership(bool asynchronous, DataLinqOwnership ownership)
    {
        using var fixture = new Fixture(true, "CREATE TABLE native_observation(id INTEGER PRIMARY KEY)");
        var database = new ValidationDatabase(fixture.Provider);
        var services = new ServiceCollection();
        services.AddDataLinqSchemaValidation().ValidateDatabase<DataLinq.Tests.Models.Employees.EmployeesDb>();
        services.AddDataLinq<DataLinq.Tests.Models.Employees.EmployeesDb>("native").UseInstance(database, ownership)
            .AsDefault().ValidateSchemaOnStartup(options => options.Mode = DataLinqSchemaStartupMode.WarningOnly);
        var container = services.BuildServiceProvider();
        await ((IHostedLifecycleService)container.GetServices<IHostedService>().Single()).StartingAsync(default);
        if (asynchronous) await container.DisposeAsync(); else container.Dispose();
        if (ownership == DataLinqOwnership.Application)
        {
            await Assert.That(fixture.Provider.TableExists("native_observation")).IsTrue();
            await Assert.That((await database.ValidateSchemaAsync()).DatabaseTableCount).IsEqualTo(1);
        }
        else
        {
            await Assert.That(() => database.ValidateSchema()).Throws<ObjectDisposedException>();
            await Assert.That(async () => { await DataLinqSchemaValidator.ValidateAsync(fixture.Provider); }).Throws<ObjectDisposedException>();
        }
    }
}
