using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;
using DataLinq.Query;
using DataLinq.Testing;
using DataLinq.Tests.Models.Employees;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Compliance;

public sealed class PublicAsyncAdministrationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RootProviderAndRegisteredFactoriesExposeRealAdministrativeOperations(TestProviderDescriptor descriptor)
    {
        using var scope = EmployeesTestDatabase.CreateIsolated(descriptor, nameof(RootProviderAndRegisteredFactoriesExposeRealAdministrativeOperations), EmployeesFixtureProfile.TinySeeded);
        var database = scope.Database;
        var provider = database.Provider;
        using var readOnlyPool = descriptor.IsSQLite ? new ReadOnlyPoolCleanup(provider.ConnectionString) : null;
        var departmentTable = provider.Metadata.GetTableModel(typeof(Department)).Table.DbName;
        await Assert.That(await database.FileOrServerExistsAsync()).IsTrue();
        await Assert.That(await database.DatabaseExistsAsync()).IsTrue();
        await Assert.That(await database.TableExistsAsync(departmentTable, cancellationToken: default)).IsTrue();
        await Assert.That(await provider.TableExistsAsync("public_admin_missing")).IsFalse();
        await Assert.That(async () => { await database.TableExistsAsync(departmentTable, cancellationToken: new(true)); }).Throws<OperationCanceledException>();
        var factory = PluginHook.MetadataFromSqlFactories[provider.DatabaseType].GetMetadataFromSqlFactory(new());
        var metadata = (await factory.ParseDatabaseAsync("PublicAsync", "PublicDb", "PublicModels", provider.DatabaseName!, provider.ConnectionString,
            cancellationToken: default)).ValueOrException();
        await Assert.That(metadata.TableModels.Any(t => t.Table.DbName == departmentTable)).IsTrue();
        await provider.DatabaseType.CreateDatabaseFromSqlAsync(new Sql("CREATE TABLE public_admin_added (id INTEGER)"),
            provider.DatabaseName!, provider.ConnectionString, foreignKeyRestrict: false, cancellationToken: default);
        await Assert.That(await database.TableExistsAsync("public_admin_added")).IsTrue();
    }

    // Metadata/probes use a separate read-only pool. Clear that fixture-owned pool
    // before the temporary scope deletes its Windows file; do not change runtime pooling.
    private sealed class ReadOnlyPoolCleanup(string connectionString) : IDisposable
    {
        public void Dispose()
        {
            var options = new SqliteConnectionStringBuilder(connectionString);
            if (options.Mode == SqliteOpenMode.Memory) return;
            options.Mode = SqliteOpenMode.ReadOnly;
            using var connection = new SqliteConnection(options.ConnectionString);
            SqliteConnection.ClearPool(connection);
        }
    }
}
