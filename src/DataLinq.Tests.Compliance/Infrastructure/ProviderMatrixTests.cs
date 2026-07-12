using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Tests.Compliance;

public class ProviderMatrixTests
{
    [Test]
    public async Task DatabaseServerMatrix_LoadsCurrentLtsTargetsAndProfiles()
    {
        await Assert.That(DatabaseServerMatrix.Targets.Count).IsEqualTo(4);
        await Assert.That(DatabaseServerMatrix.GetTarget("mysql-8.4").IsLts).IsTrue();
        await Assert.That(DatabaseServerMatrix.GetTarget("mariadb-10.11").IsLts).IsTrue();
        await Assert.That(DatabaseServerMatrix.GetTarget("mariadb-11.4").IsLts).IsTrue();
        await Assert.That(DatabaseServerMatrix.GetTarget("mariadb-11.8").IsLts).IsTrue();
        await Assert.That(DatabaseServerMatrix.Targets.Select(x => x.HostPort).Distinct().Count()).IsEqualTo(DatabaseServerMatrix.Targets.Count);
        await Assert.That(DatabaseServerMatrix.DefaultProfile.Id).IsEqualTo("current-lts");
    }

    [Test]
    public async Task ActiveProfile_ResolvesTheConfiguredProfile()
    {
        var settings = PodmanTestEnvironmentSettings.FromEnvironment();

        await Assert.That(settings.ActiveProfile.Id).IsEqualTo(settings.ProfileId);
        await Assert.That(settings.ActiveProfile.MySqlTarget).IsNotNull();
        await Assert.That(settings.ActiveProfile.MariaDbTarget).IsNotNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.AllLtsServerProviders))]
    public async Task AllLtsServerProviders_AreVersionedAndPodmanManaged(TestProviderDescriptor provider)
    {
        await Assert.That(provider.Kind).IsEqualTo(TestProviderKind.Server);
        await Assert.That(provider.RequiresExternalServer).IsTrue();
        await Assert.That(provider.UsesPodman).IsTrue();
        await Assert.That(provider.ServerTarget).IsNotNull();
        await Assert.That(provider.ServerTarget!.Version).IsNotNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EnvironmentSettings_CanCreateConnectionDefinitionsForActiveProviders(TestProviderDescriptor provider)
    {
        var settings = PodmanTestEnvironmentSettings.FromEnvironment();
        var connection = settings.CreateConnection(provider, "datalinq_employees");

        await Assert.That(connection.DatabaseType).IsEqualTo(provider.DatabaseType);
        await Assert.That(string.IsNullOrWhiteSpace(connection.ConnectionString)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(connection.DataSourceName)).IsFalse();
    }

    [Test]
    [NotInParallel]
    public async Task SqliteConnectionDefinitions_DefaultFilesAndKeepNamedMemoryShared()
    {
        var artifactRoot = Path.Combine(
            Path.GetTempPath(),
            $"datalinq-sqlite-defaults-{Guid.NewGuid():N}");
        var settings = new PodmanTestEnvironmentSettings(
            RepositoryRoot: artifactRoot,
            ArtifactRoot: artifactRoot,
            ContainerPrefix: "datalinq-tests",
            ProfileId: DatabaseServerMatrix.DefaultProfile.Id,
            Host: "127.0.0.1",
            AdminUser: "datalinq",
            AdminPassword: "datalinq",
            ApplicationUser: "datalinq",
            ApplicationPassword: "datalinq",
            TargetPorts: new Dictionary<string, int>(),
            AvailableTargetIds: []);

        try
        {
            var fileDefinition = settings.CreateConnection(
                TestProviderMatrix.SQLiteFile,
                "sqlite_defaults");
            var fileBuilder = new SqliteConnectionStringBuilder(
                fileDefinition.ConnectionString);

            await Assert.That(fileBuilder.Cache)
                .IsEqualTo(SqliteCacheMode.Default);
            await Assert.That(fileDefinition.ConnectionString.Contains(
                "Cache=",
                StringComparison.OrdinalIgnoreCase)).IsFalse();
            await Assert.That(fileBuilder.DataSource).IsEqualTo(
                Path.Combine(artifactRoot, "sqlite", "sqlite_defaults.db"));

            using (var connection = new SqliteConnection(
                fileDefinition.ConnectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                await Assert.That(Convert.ToInt64(command.ExecuteScalar()))
                    .IsEqualTo(1L);
            }

            var memoryDefinition = settings.CreateConnection(
                TestProviderMatrix.SQLiteInMemory,
                "sqlite_defaults");
            var memoryBuilder = new SqliteConnectionStringBuilder(
                memoryDefinition.ConnectionString);

            await Assert.That(memoryBuilder.Mode)
                .IsEqualTo(SqliteOpenMode.Memory);
            await Assert.That(memoryBuilder.Cache)
                .IsEqualTo(SqliteCacheMode.Shared);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(artifactRoot))
                Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Test]
    public async Task MySql84ConnectionDefinitions_AllowPublicKeyRetrievalForContainerAuth()
    {
        var target = DatabaseServerMatrix.GetTarget("mysql-8.4");
        var settings = new PodmanTestEnvironmentSettings(
            RepositoryRoot: string.Empty,
            ArtifactRoot: string.Empty,
            ContainerPrefix: "datalinq-tests",
            ProfileId: DatabaseServerMatrix.DefaultProfile.Id,
            Host: "127.0.0.1",
            AdminUser: "datalinq",
            AdminPassword: "datalinq",
            ApplicationUser: "datalinq",
            ApplicationPassword: "datalinq",
            TargetPorts: new Dictionary<string, int>(),
            AvailableTargetIds: []);

        var provider = new TestProviderDescriptor(
            Name: target.Id,
            Kind: TestProviderKind.Server,
            DatabaseType: target.DatabaseType,
            RequiresExternalServer: true,
            UsesPodman: true,
            ServerTarget: target);

        var adminConnection = new MySqlConnectionStringBuilder(settings.CreateAdminConnectionString(target));
        var applicationConnection = new MySqlConnectionStringBuilder(settings.CreateConnection(provider, "datalinq_employees").ConnectionString);

        await Assert.That(adminConnection.AllowPublicKeyRetrieval).IsTrue();
        await Assert.That(applicationConnection.AllowPublicKeyRetrieval).IsTrue();
    }
}
