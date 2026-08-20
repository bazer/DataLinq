using System;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Unit;

public sealed class MySqlProviderDisposalTests
{
    [Test]
    public async Task DisposingDatabase_DisposesOwnedMySqlDataSource()
    {
        ProviderRegistration.EnsureRegistered();
        var creator = PluginHook.DatabaseProviders[DatabaseType.MySQL];
        var database = creator.GetDatabaseProvider<EmployeesDb>(
            "Server=127.0.0.1;Port=1;User ID=unused;Password=unused;Pooling=false;Connection Timeout=1",
            "provider_disposal");
        var databaseAccess = database.Provider.DatabaseAccess;

        database.Dispose();

        Exception? exception = null;
        try
        {
            databaseAccess.ExecuteScalar("SELECT 1");
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }
}
