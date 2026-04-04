using System.Threading.Tasks;
using DataLinq.Testing;

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
        await Assert.That(DatabaseServerMatrix.DefaultProfile.Id).IsEqualTo("current-lts");
    }

    [Test]
    public async Task ActiveProfile_DefaultsToCurrentLts()
    {
        var settings = PodmanTestEnvironmentSettings.FromEnvironment();

        await Assert.That(settings.ActiveProfile.Id).IsEqualTo("current-lts");
        await Assert.That(settings.ActiveProfile.MySqlTarget!.Id).IsEqualTo("mysql-8.4");
        await Assert.That(settings.ActiveProfile.MariaDbTarget!.Id).IsEqualTo("mariadb-11.8");
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
}
