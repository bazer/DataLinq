using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Config;

namespace DataLinq.Tests.Unit.Core;

public class DataLinqConfigTests
{
    [Test]
    public async Task DatabaseConfig_UseNullableReferenceTypes_DefaultsToTrue()
    {
        using var fixture = DataLinqConfigFixture.Create();

        var config = new DataLinqConfig(
            fixture.BasePath,
            new ConfigFile
            {
                Databases = [CreateDatabaseConfig()]
            });

        await Assert.That(config.Databases.Single().UseNullableReferenceTypes).IsTrue();
    }

    [Test]
    public async Task DatabaseConfig_UseNullableReferenceTypes_CanBeExplicitlyDisabled()
    {
        using var fixture = DataLinqConfigFixture.Create();

        var config = new DataLinqConfig(
            fixture.BasePath,
            new ConfigFile
            {
                Databases =
                [
                    CreateDatabaseConfig(useNullableReferenceTypes: false)
                ]
            });

        await Assert.That(config.Databases.Single().UseNullableReferenceTypes).IsFalse();
    }

    private static ConfigFileDatabase CreateDatabaseConfig(bool? useNullableReferenceTypes = null) =>
        new()
        {
            Name = "test_db",
            CsType = "TestDb",
            Namespace = "TestModels",
            DestinationDirectory = "Models",
            UseNullableReferenceTypes = useNullableReferenceTypes,
            Connections =
            [
                new ConfigFileDatabaseConnection
                {
                    Type = "SQLite",
                    DataSourceName = "test.db",
                    ConnectionString = "Data Source=test.db"
                }
            ]
        };

    private sealed class DataLinqConfigFixture : IDisposable
    {
        private DataLinqConfigFixture(string basePath)
        {
            BasePath = basePath;
        }

        public string BasePath { get; }

        public static DataLinqConfigFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-config-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new DataLinqConfigFixture(basePath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(BasePath))
                    Directory.Delete(BasePath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
