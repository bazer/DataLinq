using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Tests.TUnit;

public class ProviderMatrixTests
{
    [Test]
    public async Task ComplianceMatrix_HasExpectedProvidersInOrder()
    {
        await Assert.That(TestProviderMatrix.Compliance.Count).IsEqualTo(4);
        await Assert.That(TestProviderMatrix.Compliance[0].Name).IsEqualTo("sqlite-file");
        await Assert.That(TestProviderMatrix.Compliance[1].Name).IsEqualTo("sqlite-memory");
        await Assert.That(TestProviderMatrix.Compliance[2].Name).IsEqualTo("mysql");
        await Assert.That(TestProviderMatrix.Compliance[3].Name).IsEqualTo("mariadb");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ServerBackedProviders))]
    public async Task ServerBackedProviders_AreMarkedAsPodmanManaged(TestProviderDescriptor provider)
    {
        await Assert.That(provider.RequiresExternalServer).IsTrue();
        await Assert.That(provider.UsesPodman).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.AllProviders))]
    public async Task EnvironmentSettings_CanCreateConnectionDefinitions(TestProviderDescriptor provider)
    {
        var settings = PodmanTestEnvironmentSettings.FromEnvironment();
        var connection = settings.CreateConnection(provider, "datalinq_employees");

        await Assert.That(connection.DatabaseType).IsEqualTo(provider.DatabaseType);
        await Assert.That(string.IsNullOrWhiteSpace(connection.ConnectionString)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(connection.DataSourceName)).IsFalse();
    }
}
