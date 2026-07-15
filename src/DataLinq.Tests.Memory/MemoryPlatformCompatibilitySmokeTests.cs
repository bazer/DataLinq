using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.Memory.PlatformCompatibility.Smoke;

namespace DataLinq.Tests.Memory;

public sealed class MemoryPlatformCompatibilitySmokeTests
{
    private static readonly string[] BannedRuntimeTokens =
    [
        "DataLinq.SQLite",
        "Microsoft.Data.Sqlite",
        "SQLitePCLRaw",
        "DataLinq.MySql",
        "MySqlConnector",
        "e_sqlite3"
    ];

    [Test]
    public async Task SharedRunner_ExecutesCurrentMemoryProfileWithoutWideningIt()
    {
        var result = MemoryPlatformSmokeRunner.Run();

        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.SupportedCapabilityTokenCount).IsEqualTo(31);
        await Assert.That(result.CanonicalGuidCellsStoredAsGuid).IsTrue();
        await Assert.That(result.UnsupportedRejectedBeforeWork).IsTrue();
        await Assert.That(result.PreCancellationRejectedBeforeWork).IsTrue();
        await Assert.That(result.UnsupportedDiagnostic).Contains("SourceCount:Multiple");
    }

    [Test]
    public async Task SharedSmokeAssets_ExcludeSqlProvidersAndNativeDatabasePayloads()
    {
        var repositoryRoot = FindRepositoryRoot();
        var assetsPath = Path.Combine(
            repositoryRoot,
            "src",
            "DataLinq.Memory.PlatformCompatibility.Smoke",
            "obj",
            "project.assets.json");

        await Assert.That(File.Exists(assetsPath)).IsTrue();

        await using var assetsStream = File.OpenRead(assetsPath);
        using var assets = await JsonDocument.ParseAsync(assetsStream);
        var runtimeGraph = string.Concat(
            assets.RootElement.GetProperty("targets").GetRawText(),
            assets.RootElement.GetProperty("libraries").GetRawText());
        var bannedRuntimeTokens = BannedRuntimeTokens
            .Where(token => runtimeGraph.Contains(token, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(bannedRuntimeTokens).IsEmpty();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "DataLinq.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the DataLinq repository root above '{AppContext.BaseDirectory}'.");
    }
}
