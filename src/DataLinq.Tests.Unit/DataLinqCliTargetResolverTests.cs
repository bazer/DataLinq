using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.CLI;
using DataLinq.Metadata;
using DataLinq.SQLite;

namespace DataLinq.Tests.Unit;

public class DataLinqCliTargetResolverTests
{
    [Test]
    public async Task Expand_FiltersTargetsByDatabaseAndProvider()
    {
        SQLiteProvider.RegisterProvider();
        using var fixture = CliTargetResolverFixture.Create();
        fixture.WriteConfig();

        var expansion = CliTargetResolver.Expand(
            fixture.ConfigPath,
            new CliTargetFilter("AppDb", "SQLite"),
            recursive: false,
            _ => { });

        await Assert.That(expansion.Failures).IsEmpty();
        await Assert.That(expansion.Targets.Count).IsEqualTo(1);

        var target = expansion.Targets.Single();
        await Assert.That(target.Identity.DatabaseName).IsEqualTo("AppDb");
        await Assert.That(target.Identity.Provider).IsEqualTo(DatabaseType.SQLite);
        await Assert.That(target.Identity.DataSourceName).IsEqualTo("app.db");
    }

    [Test]
    public async Task Expand_Recursive_ContinuesThroughUnreadableConfigs()
    {
        SQLiteProvider.RegisterProvider();
        using var fixture = CliTargetResolverFixture.Create();
        fixture.WriteConfig(Path.Combine("one", "datalinq.json"));
        fixture.WriteInvalidConfig(Path.Combine("two", "datalinq.json"));

        var expansion = CliTargetResolver.Expand(
            fixture.BasePath,
            new CliTargetFilter(null, "SQLite"),
            recursive: true,
            _ => { });

        await Assert.That(expansion.Targets.Count).IsEqualTo(2);
        await Assert.That(expansion.Failures.Count).IsEqualTo(1);
    }

    private sealed class CliTargetResolverFixture : IDisposable
    {
        private CliTargetResolverFixture(string basePath)
        {
            BasePath = basePath;
            ConfigPath = Path.Combine(basePath, "datalinq.json");
        }

        public string BasePath { get; }
        public string ConfigPath { get; }

        public static CliTargetResolverFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-cli-targets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new CliTargetResolverFixture(basePath);
        }

        public void WriteConfig(string relativePath = "datalinq.json")
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
                        },
                        {
                          "Type": "Unknown",
                          "DataSourceName": "ignored.db",
                          "ConnectionString": "Data Source=ignored.db"
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
