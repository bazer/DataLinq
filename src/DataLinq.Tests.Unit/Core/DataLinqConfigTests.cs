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

    [Test]
    public async Task DatabaseConfig_ModelDirectory_UsesDestinationDirectoryAlias()
    {
        using var fixture = DataLinqConfigFixture.Create();

        var config = new DataLinqConfig(
            fixture.BasePath,
            new ConfigFile
            {
                Databases = [CreateDatabaseConfig(destinationDirectory: "Models/Generated")]
            });

        var database = config.Databases.Single();
        await Assert.That(database.ModelDirectory).IsEqualTo("Models/Generated");
        await Assert.That(database.DestinationDirectory).IsEqualTo("Models/Generated");
    }

    [Test]
    public async Task DatabaseConfig_ModelDirectory_UsesNewName()
    {
        using var fixture = DataLinqConfigFixture.Create();

        var config = new DataLinqConfig(
            fixture.BasePath,
            new ConfigFile
            {
                Databases =
                [
                    CreateDatabaseConfig(
                        modelDirectory: "Models",
                        destinationDirectory: null)
                ]
            });

        var database = config.Databases.Single();
        await Assert.That(database.ModelDirectory).IsEqualTo("Models");
        await Assert.That(database.DestinationDirectory).IsEqualTo("Models");
    }

    [Test]
    public async Task DatabaseConfig_ModelDirectoryAndDestinationDirectoryConflict_ThrowsClearError()
    {
        using var fixture = DataLinqConfigFixture.Create();

        ArgumentException? exception = null;
        try
        {
            new DataLinqConfig(
                fixture.BasePath,
                new ConfigFile
                {
                    Databases =
                    [
                        CreateDatabaseConfig(
                            modelDirectory: "Models",
                            destinationDirectory: "Generated")
                    ]
                });
        }
        catch (ArgumentException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("ModelDirectory and DestinationDirectory");
    }

    [Test]
    public async Task DatabaseConfig_ModelLayout_DefaultsToColumnTopInlineBottom()
    {
        using var fixture = DataLinqConfigFixture.Create();

        var config = new DataLinqConfig(
            fixture.BasePath,
            new ConfigFile
            {
                Databases = [CreateDatabaseConfig()]
            });

        var layout = config.Databases.Single().ModelLayout;
        await Assert.That(layout.PropertyOrder).IsEqualTo(DataLinqModelPropertyOrder.Column);
        await Assert.That(layout.PrimaryKeyPlacement).IsEqualTo(DataLinqModelPrimaryKeyPlacement.Top);
        await Assert.That(layout.ForeignKeyPlacement).IsEqualTo(DataLinqModelForeignKeyPlacement.Inline);
        await Assert.That(layout.RelationPlacement).IsEqualTo(DataLinqModelRelationPlacement.Bottom);
    }

    [Test]
    public async Task DatabaseConfig_ModelLayout_MergesPartialValues()
    {
        using var fixture = DataLinqConfigFixture.Create();

        var config = new DataLinqConfig(
            fixture.BasePath,
            new ConfigFile
            {
                Databases =
                [
                    CreateDatabaseConfig(modelLayout: new ConfigFileModelLayout
                    {
                        PropertyOrder = "Alphabetical"
                    })
                ]
            },
            new ConfigFile
            {
                Databases =
                [
                    CreateDatabaseConfig(modelLayout: new ConfigFileModelLayout
                    {
                        PrimaryKeyPlacement = "Inline",
                        ForeignKeyPlacement = "Top",
                        RelationPlacement = "WithForeignKey"
                    })
                ]
            });

        var layout = config.Databases.Single().ModelLayout;
        await Assert.That(layout.PropertyOrder).IsEqualTo(DataLinqModelPropertyOrder.Alphabetical);
        await Assert.That(layout.PrimaryKeyPlacement).IsEqualTo(DataLinqModelPrimaryKeyPlacement.Inline);
        await Assert.That(layout.ForeignKeyPlacement).IsEqualTo(DataLinqModelForeignKeyPlacement.Top);
        await Assert.That(layout.RelationPlacement).IsEqualTo(DataLinqModelRelationPlacement.WithForeignKey);
    }

    [Test]
    public async Task DatabaseConfig_ModelLayout_UnknownValueThrowsClearError()
    {
        using var fixture = DataLinqConfigFixture.Create();

        ArgumentException? exception = null;
        try
        {
            new DataLinqConfig(
                fixture.BasePath,
                new ConfigFile
                {
                    Databases =
                    [
                        CreateDatabaseConfig(modelLayout: new ConfigFileModelLayout
                        {
                            PropertyOrder = "Source"
                        })
                    ]
                });
        }
        catch (ArgumentException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Unknown ModelLayout.PropertyOrder value 'Source'");
    }

    private static ConfigFileDatabase CreateDatabaseConfig(
        bool? useNullableReferenceTypes = null,
        string? modelDirectory = null,
        string? destinationDirectory = "Models",
        ConfigFileModelLayout? modelLayout = null) =>
        new()
        {
            Name = "test_db",
            CsType = "TestDb",
            Namespace = "TestModels",
            ModelDirectory = modelDirectory,
            DestinationDirectory = destinationDirectory,
            ModelLayout = modelLayout,
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
