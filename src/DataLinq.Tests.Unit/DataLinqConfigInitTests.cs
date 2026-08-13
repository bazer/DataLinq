using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.CLI;
using DataLinq.Config;

namespace DataLinq.Tests.Unit;

public class DataLinqConfigInitTests
{
    [Test]
    public async Task DetectState_ReturnsExpectedModeForEachConfigFileState()
    {
        using var fixture = ConfigInitFixture.Create();

        await Assert.That(CliConfigInit.DetectState(fixture.ConfigPath).Mode).IsEqualTo(ConfigInitMode.NewProject);

        fixture.WriteMainConfig();
        await Assert.That(CliConfigInit.DetectState(fixture.ConfigPath).Mode).IsEqualTo(ConfigInitMode.CompleteUserConfig);

        fixture.WriteUserConfig();
        await Assert.That(CliConfigInit.DetectState(fixture.ConfigPath).Mode).IsEqualTo(ConfigInitMode.InspectExisting);

        File.Delete(fixture.ConfigPath);
        await Assert.That(CliConfigInit.DetectState(fixture.ConfigPath).Mode).IsEqualTo(ConfigInitMode.RepairOrphanedUserConfig);
    }

    [Test]
    public async Task NewProjectPlan_CreatesMainAndUserConfigsWithSchemaAndCompleteConnection()
    {
        using var fixture = ConfigInitFixture.Create();
        fixture.CreateGitRepository();
        var state = CliConfigInit.DetectState(fixture.ConfigPath);

        var plan = CliConfigInit.CreateNewProjectPlan(
            state.Paths,
            CreateDatabaseInput(),
            addGitignore: true);
        CliConfigInit.Apply(plan);

        var mainConfig = ConfigReader.Read(fixture.ConfigPath)!;
        var userConfig = ConfigReader.Read(fixture.UserConfigPath)!;

        await Assert.That(mainConfig.Schema).IsEqualTo(CliConfigSchema.SchemaUrl);
        await Assert.That(mainConfig.Databases.Single().ModelDirectory).IsEqualTo("Models");
        await Assert.That(mainConfig.Databases.Single().Connections).IsEmpty();
        await Assert.That(userConfig.Schema).IsNull();
        await Assert.That(userConfig.Databases.Single().Connections.Single().Type).IsEqualTo("SQLite");
        await Assert.That(userConfig.Databases.Single().Connections.Single().ConnectionString).IsEqualTo("Data Source=app.local.db;");
        await Assert.That(File.ReadAllText(fixture.GitignorePath)).Contains("datalinq.user.json");
    }

    [Test]
    public async Task DefaultSqliteConnection_OmitsFileSharedCache()
    {
        var connection = CliConfigInit.CreateDefaultConnectionInput(
            "AppDb",
            DataLinq.DatabaseType.SQLite,
            "app.local.db");

        await Assert.That(connection.ConnectionString)
            .IsEqualTo("Data Source=app.local.db;");
    }

    [Test]
    public async Task MissingUserConfigPlan_CreatesOnlyUserConfigAndDoesNotRewriteMainConfig()
    {
        using var fixture = ConfigInitFixture.Create();
        var originalMainConfig =
            """
            // keep this comment
            {
              "Databases": [
                {
                  "Name": "AppDb",
                  "ModelDirectory": "Models"
                }
              ]
            }
            """;
        File.WriteAllText(fixture.ConfigPath, originalMainConfig);

        var state = CliConfigInit.DetectState(fixture.ConfigPath);
        var plan = CliConfigInit.CreateMissingUserConfigPlan(
            state.Paths,
            [
                // Generated file defaults omit Cache, but an explicit caller choice must round-trip.
                new ConfigInitConnectionInput(
                    "AppDb",
                    "SQLite",
                    "app.local.db",
                    "Data Source=app.local.db;Cache=Shared;")
            ],
            addGitignore: false);
        CliConfigInit.Apply(plan);

        await Assert.That(File.ReadAllText(fixture.ConfigPath)).IsEqualTo(originalMainConfig);
        var userConfig = ConfigReader.Read(fixture.UserConfigPath)!;
        var connection = userConfig.Databases.Single().Connections.Single();
        await Assert.That(userConfig.Databases.Single().Name).IsEqualTo("AppDb");
        await Assert.That(connection.Type).IsEqualTo("SQLite");
        await Assert.That(connection.DataSourceName).IsEqualTo("app.local.db");
        await Assert.That(connection.ConnectionString).IsEqualTo("Data Source=app.local.db;Cache=Shared;");
    }

    [Test]
    public async Task NewProjectPlan_AddsNarrowGitignoreEntryForNestedConfig()
    {
        using var fixture = ConfigInitFixture.Create();
        fixture.CreateGitRepository();
        var projectPath = Path.Combine(fixture.BasePath, "src", "MyApp");
        Directory.CreateDirectory(projectPath);
        var state = CliConfigInit.DetectState(projectPath);

        var plan = CliConfigInit.CreateNewProjectPlan(
            state.Paths,
            CreateDatabaseInput(),
            addGitignore: true);
        CliConfigInit.Apply(plan);

        await Assert.That(File.ReadAllText(fixture.GitignorePath)).Contains("src/MyApp/datalinq.user.json");
    }

    [Test]
    public async Task NewProjectPlan_DoesNotDuplicateExistingGitignoreEntry()
    {
        using var fixture = ConfigInitFixture.Create();
        fixture.CreateGitRepository();
        File.WriteAllText(fixture.GitignorePath, $"datalinq.user.json{Environment.NewLine}");
        var state = CliConfigInit.DetectState(fixture.ConfigPath);

        var plan = CliConfigInit.CreateNewProjectPlan(
            state.Paths,
            CreateDatabaseInput(),
            addGitignore: true);
        CliConfigInit.Apply(plan);

        var entries = File.ReadAllLines(fixture.GitignorePath)
            .Where(line => line == "datalinq.user.json")
            .ToArray();
        await Assert.That(entries.Length).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task ConfigInit_OrphanedUserConfig_AsksOnlyForMainConfigFields()
    {
        using var fixture = ConfigInitFixture.Create();
        fixture.WriteUserConfig();
        var originalUserConfig = File.ReadAllText(fixture.UserConfigPath);

        var (exitCode, output) = await InvokeWithInput(
            [
                "config",
                "init",
                "--config",
                fixture.BasePath
            ],
            string.Join(
                Environment.NewLine,
                [
                    "y",
                    "LmData",
                    "",
                    "LmData.Logic.Entities",
                    "DataLinq",
                    "y",
                    "y",
                    "y"
                ]) + Environment.NewLine);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(output).Contains("C# database type [LmData]:");
        await Assert.That(output).DoesNotContain("Provider (SQLite, MySQL, MariaDB)");
        await Assert.That(output).DoesNotContain("Local data source name");
        await Assert.That(output).DoesNotContain("Local connection string");
        await Assert.That(File.ReadAllText(fixture.UserConfigPath)).IsEqualTo(originalUserConfig);

        var mainConfig = ConfigReader.Read(fixture.ConfigPath)!;
        var database = mainConfig.Databases.Single();
        await Assert.That(database.Name).IsEqualTo("LmData");
        await Assert.That(database.CsType).IsEqualTo("LmData");
        await Assert.That(database.Namespace).IsEqualTo("LmData.Logic.Entities");
        await Assert.That(database.ModelDirectory).IsEqualTo("DataLinq");
        await Assert.That(database.Connections).IsEmpty();
    }

    private static async Task<(int ExitCode, string Output)> InvokeWithInput(string[] args, string input)
    {
        var originalIn = Console.In;
        var originalOut = Console.Out;
        using var reader = new StringReader(input);
        using var writer = new StringWriter();

        try
        {
#pragma warning disable TUnit0055
            Console.SetIn(reader);
            Console.SetOut(writer);
#pragma warning restore TUnit0055
            var exitCode = await Program.InvokeAsync(args);
            return (exitCode, writer.ToString());
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
#pragma warning restore TUnit0055
        }
    }

    private static ConfigInitDatabaseInput CreateDatabaseInput() =>
        new(
            "AppDb",
            "AppDb",
            "MyApp.Models",
            "Models",
            UseNullableReferenceTypes: true,
            UseFileScopedNamespaces: true,
            CliConfigInit.CreateDefaultConnectionInput(
                "AppDb",
                DataLinq.DatabaseType.SQLite,
                "app.local.db"));

    private sealed class ConfigInitFixture : IDisposable
    {
        private ConfigInitFixture(string basePath)
        {
            BasePath = basePath;
            ConfigPath = Path.Combine(basePath, "datalinq.json");
            UserConfigPath = Path.Combine(basePath, "datalinq.user.json");
            GitignorePath = Path.Combine(basePath, ".gitignore");
        }

        public string BasePath { get; }
        public string ConfigPath { get; }
        public string UserConfigPath { get; }
        public string GitignorePath { get; }

        public static ConfigInitFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-config-init-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new ConfigInitFixture(basePath);
        }

        public void CreateGitRepository() =>
            Directory.CreateDirectory(Path.Combine(BasePath, ".git"));

        public void WriteMainConfig() =>
            File.WriteAllText(
                ConfigPath,
                """
                {
                  "Databases": [
                    {
                      "Name": "AppDb",
                      "ModelDirectory": "Models"
                    }
                  ]
                }
                """);

        public void WriteUserConfig() =>
            File.WriteAllText(
                UserConfigPath,
                """
                {
                  "Databases": [
                    {
                      "Name": "AppDb",
                      "Connections": [
                        {
                          "Type": "SQLite",
                          "DataSourceName": "app.local.db",
                          "ConnectionString": "Data Source=app.local.db;Cache=Shared;"
                        }
                      ]
                    }
                  ]
                }
                """);

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
