using System;
using System.IO;
using System.Threading.Tasks;
using DataLinq.CLI;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit;

public class DataLinqCliBatchCommandTests
{
    [Test]
    [NotInParallel]
    public async Task ConfigList_Recursive_ContinuesThroughUnreadableConfigs()
    {
        using var fixture = CliBatchCommandFixture.Create();
        fixture.WriteConfig(Path.Combine("valid", "datalinq.json"));
        fixture.WriteInvalidConfig(Path.Combine("broken", "datalinq.json"));

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "list",
            "--config",
            fixture.BasePath,
            "--recursive");

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(output).Contains("Config:");
        await Assert.That(output).Contains("Failed to read config");
        await Assert.That(output).Contains("Config list summary: configs: 2; listed: 1; failed: 1");
    }

    [Test]
    [NotInParallel]
    public async Task ConfigValidate_RejectsUnknownMembers()
    {
        using var fixture = CliBatchCommandFixture.Create();
        fixture.WriteConfigWithOldKeyPlacement("datalinq.json");

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "validate",
            "--config",
            fixture.BasePath);

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(output).Contains("Failed to validate config");
        await Assert.That(output).Contains("KeyPlacement");
    }

    [Test]
    [NotInParallel]
    public async Task ConfigValidate_Recursive_ContinuesThroughInvalidConfigs()
    {
        using var fixture = CliBatchCommandFixture.Create();
        fixture.WriteConfig(Path.Combine("valid", "datalinq.json"));
        fixture.WriteConfigWithOldKeyPlacement(Path.Combine("broken", "datalinq.json"));

        var (exitCode, output) = await InvokeWithOutput(
            "config",
            "validate",
            "--config",
            fixture.BasePath,
            "--recursive");

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(output).Contains("Config valid:");
        await Assert.That(output).Contains("Failed to validate config");
        await Assert.That(output).Contains("KeyPlacement");
        await Assert.That(output).Contains("Config validation summary: configs: 2; valid: 1; failed: 1");
    }

    [Test]
    [NotInParallel]
    public async Task Validate_Recursive_ContinuesThroughExpansionAndTargetFailures()
    {
        using var fixture = CliBatchCommandFixture.Create();
        fixture.WriteConfig(Path.Combine("valid", "datalinq.json"));
        fixture.WriteInvalidConfig(Path.Combine("broken", "datalinq.json"));

        var (exitCode, output) = await InvokeWithOutput(
            "validate",
            "--config",
            fixture.BasePath,
            "--recursive",
            "--provider",
            "SQLite");

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(output).Contains("Failed to prepare targets");
        await Assert.That(output).Contains("Target:");
        await Assert.That(output).Contains("Validation summary: configs: 2; targets: 2; succeeded: 0; drift: 0; failed: 3");
    }

    [Test]
    [NotInParallel]
    public async Task GenerateModels_Recursive_WritesNothingWhenAnyTargetFails()
    {
        using var fixture = CliBatchCommandFixture.Create();
        fixture.WriteGenerateProject("good", hasInvalidExistingModel: false);
        fixture.WriteGenerateProject("bad", hasInvalidExistingModel: true);

        var exitCode = await Program.InvokeAsync(
        [
            "generate",
            "models",
            "--config",
            fixture.BasePath,
            "--recursive",
            "--provider",
            "SQLite"
        ]);

        await Assert.That(exitCode).IsEqualTo(2);
        await Assert.That(File.Exists(Path.Combine(fixture.BasePath, "good", "Models", "AppDb.cs"))).IsFalse();
        await Assert.That(File.Exists(Path.Combine(fixture.BasePath, "good", "Models", "Account.cs"))).IsFalse();
    }

    private static async Task<(int ExitCode, string Output)> InvokeWithOutput(params string[] args)
    {
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
#pragma warning disable TUnit0055
            Console.SetOut(writer);
#pragma warning restore TUnit0055
            var exitCode = await Program.InvokeAsync(args);
            return (exitCode, writer.ToString());
        }
        finally
        {
#pragma warning disable TUnit0055
            Console.SetOut(originalOut);
#pragma warning restore TUnit0055
        }
    }

    private sealed class CliBatchCommandFixture : IDisposable
    {
        private CliBatchCommandFixture(string basePath)
        {
            BasePath = basePath;
        }

        public string BasePath { get; }

        public static CliBatchCommandFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-cli-batch-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new CliBatchCommandFixture(basePath);
        }

        public void WriteConfig(string relativePath)
        {
            var path = Path.Combine(BasePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """
                {
                  "Databases": [
                    {
                      "Name": "AppDb",
                      "DestinationDirectory": "Models",
                      "Connections": [
                        {
                          "Type": "SQLite",
                          "DataSourceName": "app.db",
                          "ConnectionString": "Data Source=app.db"
                        }
                      ]
                    },
                    {
                      "Name": "LogDb",
                      "DestinationDirectory": "Logs",
                      "Connections": [
                        {
                          "Type": "SQLite",
                          "DataSourceName": "logs.db",
                          "ConnectionString": "Data Source=logs.db"
                        }
                      ]
                    }
                  ]
                }
                """);
        }

        public void WriteInvalidConfig(string relativePath)
        {
            var path = Path.Combine(BasePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ invalid");
        }

        public void WriteConfigWithOldKeyPlacement(string relativePath)
        {
            var path = Path.Combine(BasePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """
                {
                  "Databases": [
                    {
                      "Name": "AppDb",
                      "ModelLayout": {
                        "KeyPlacement": "Top"
                      }
                    }
                  ]
                }
                """);
        }

        public void WriteGenerateProject(string relativeDirectory, bool hasInvalidExistingModel)
        {
            var projectPath = Path.Combine(BasePath, relativeDirectory);
            Directory.CreateDirectory(projectPath);

            var databasePath = Path.Combine(projectPath, "app.db");
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE "account" (
                    "id" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "display_name" TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();

            if (hasInvalidExistingModel)
            {
                var modelPath = Path.Combine(projectPath, "Models");
                Directory.CreateDirectory(modelPath);
                File.WriteAllText(Path.Combine(modelPath, "Broken.cs"), "this is not valid csharp");
            }

            File.WriteAllText(
                Path.Combine(projectPath, "datalinq.json"),
                """
                {
                  "Databases": [
                    {
                      "Name": "app_db",
                      "CsType": "AppDb",
                      "Namespace": "DataLinq.Tests.GeneratedModels",
                      "ModelDirectory": "Models",
                      "CapitalizeNames": true,
                      "FileEncoding": "UTF-8",
                      "Connections": [
                        {
                          "Type": "SQLite",
                          "DataSourceName": "app.db",
                          "ConnectionString": "Data Source=app.db"
                        }
                      ]
                    }
                  ]
                }
                """);
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
