using System;
using System.IO;
using System.Threading.Tasks;
using DataLinq.CLI;

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
        await Assert.That(output).Contains("Configs listed: 1; failures: yes");
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
        await Assert.That(output).Contains("Validation targets: 2; failures: yes");
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
