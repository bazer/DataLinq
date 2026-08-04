using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public class CompatibilitySizeReportTests
{
    [Test]
    public async Task TargetCatalog_Phase8CPreservesHistoricalFourTargetSet()
    {
        var targets = CompatibilityTargetCatalog.GetTargets("phase8c");

        await Assert.That(TargetSnapshot(targets)).IsEqualTo(
            "native-aot|NativeAot|SQLite|Native AOT smoke|src\\DataLinq.AotSmoke\\DataLinq.AotSmoke.csproj|net10.0|True|False|DataLinq.AotSmoke|\n" +
            "trimmed|Trimmed|SQLite|Trimmed smoke|src\\DataLinq.TrimSmoke\\DataLinq.TrimSmoke.csproj|net10.0|True|False|DataLinq.TrimSmoke|\n" +
            "wasm|Wasm|SQLite|Blazor WebAssembly no-AOT smoke|src\\DataLinq.BlazorWasm\\DataLinq.BlazorWasm.csproj|net10.0|False|True|DataLinq.BlazorWasm|RunAOTCompilation=false\n" +
            "wasm-aot|WasmAot|SQLite|Blazor WebAssembly AOT smoke|src\\DataLinq.BlazorWasm\\DataLinq.BlazorWasm.csproj|net10.0|False|True|DataLinq.BlazorWasm|RunAOTCompilation=true");
    }

    [Test]
    public async Task TargetCatalog_Version09HasEightUniqueGraphPlatformTargets()
    {
        var targets = CompatibilityTargetCatalog.GetTargets("v0.9");

        await Assert.That(targets.Count).IsEqualTo(8);
        await Assert.That(targets.Select(static target => target.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            .IsEqualTo(8);
        await Assert.That(targets
                .GroupBy(static target => (target.RuntimeGraph, target.Kind))
                .All(static group => group.Count() == 1))
            .IsTrue();
        await Assert.That(string.Join(",", targets.Select(static target => target.Name))).IsEqualTo(
            "sqlite-native-aot,sqlite-trimmed,sqlite-wasm-no-aot,sqlite-wasm-aot," +
            "memory-native-aot,memory-trimmed,memory-wasm-no-aot,memory-wasm-aot");
        await Assert.That(TargetSnapshot(targets)).IsEqualTo(
            "sqlite-native-aot|NativeAot|SQLite|SQLite Native AOT smoke|src\\DataLinq.AotSmoke\\DataLinq.AotSmoke.csproj|net10.0|True|False|DataLinq.AotSmoke|\n" +
            "sqlite-trimmed|Trimmed|SQLite|SQLite trimmed smoke|src\\DataLinq.TrimSmoke\\DataLinq.TrimSmoke.csproj|net10.0|True|False|DataLinq.TrimSmoke|\n" +
            "sqlite-wasm-no-aot|Wasm|SQLite|SQLite WebAssembly no-AOT smoke|src\\DataLinq.BlazorWasm\\DataLinq.BlazorWasm.csproj|net10.0|False|True|DataLinq.BlazorWasm|RunAOTCompilation=false\n" +
            "sqlite-wasm-aot|WasmAot|SQLite|SQLite WebAssembly AOT smoke|src\\DataLinq.BlazorWasm\\DataLinq.BlazorWasm.csproj|net10.0|False|True|DataLinq.BlazorWasm|RunAOTCompilation=true\n" +
            "memory-native-aot|NativeAot|Memory|Memory Native AOT smoke|src\\DataLinq.Memory.AotSmoke\\DataLinq.Memory.AotSmoke.csproj|net10.0|True|False|DataLinq.Memory.AotSmoke|\n" +
            "memory-trimmed|Trimmed|Memory|Memory trimmed smoke|src\\DataLinq.Memory.TrimSmoke\\DataLinq.Memory.TrimSmoke.csproj|net10.0|True|False|DataLinq.Memory.TrimSmoke|\n" +
            "memory-wasm-no-aot|Wasm|Memory|Memory WebAssembly no-AOT smoke|src\\DataLinq.Memory.BlazorWasm\\DataLinq.Memory.BlazorWasm.csproj|net10.0|False|True|DataLinq.Memory.BlazorWasm|RunAOTCompilation=false\n" +
            "memory-wasm-aot|WasmAot|Memory|Memory WebAssembly AOT smoke|src\\DataLinq.Memory.BlazorWasm\\DataLinq.Memory.BlazorWasm.csproj|net10.0|False|True|DataLinq.Memory.BlazorWasm|RunAOTCompilation=true");
        await Assert.That(targets.Count(static target => target.ProjectRelativePath.EndsWith("BlazorWasm.csproj", StringComparison.Ordinal)))
            .IsEqualTo(4);
    }

    [Test]
    public async Task TargetCatalog_SelectorsResolveWithinChosenSetInCatalogOrder()
    {
        var exact = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-native-aot");
        var exactWithCasedSet = CompatibilityTargetCatalog.GetTargets("V0.9", "memory-native-aot");
        var modes = CompatibilityTargetCatalog.GetTargets("v0.9", "wasm-aot,aot,memory-native-aot");
        var memory = CompatibilityTargetCatalog.GetTargets("v0.9", "memory");
        var all = CompatibilityTargetCatalog.GetTargets("v0.9", "all");

        await Assert.That(string.Join(",", exact.Select(static target => target.Name)))
            .IsEqualTo("memory-native-aot");
        await Assert.That(exactWithCasedSet).IsEquivalentTo(exact);
        await Assert.That(CompatibilityTargetCatalog.NormalizeTargetSet("V0.9")).IsEqualTo("v0.9");
        await Assert.That(string.Join(",", modes.Select(static target => target.Name)))
            .IsEqualTo("sqlite-native-aot,sqlite-wasm-aot,memory-native-aot,memory-wasm-aot");
        await Assert.That(string.Join(",", memory.Select(static target => target.Name)))
            .IsEqualTo("memory-native-aot,memory-trimmed,memory-wasm-no-aot,memory-wasm-aot");
        await Assert.That(all.Count).IsEqualTo(8);
    }

    [Test]
    public async Task TargetCatalog_RejectsCrossSetTargetIds()
    {
        foreach (var selectors in new[]
                 {
                     "memory-native-aot",
                     "all,memory-native-aot",
                     "memory-native-aot,all",
                     "all,memory"
                 })
        {
            InvalidOperationException? exception = null;
            try
            {
                _ = CompatibilityTargetCatalog.GetTargets("phase8c", selectors);
            }
            catch (InvalidOperationException caught)
            {
                exception = caught;
            }

            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message).Contains("Unsupported compatibility report selector");
        }
    }

    [Test]
    public async Task CompatibilityReport_UsesVersion09StructuralSchema()
    {
        await Assert.That(CompatibilitySizeReporter.SchemaVersion)
            .IsEqualTo("v0.9.compatibility-size-report.v2");
    }

    [Test]
    public async Task ReportDirectories_AreUniqueAcrossImmediateAllocations()
    {
        var artifactRoot = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilitySizeReportTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var first = CompatibilitySizeReporter.CreateReportDirectory(artifactRoot);
            var second = CompatibilitySizeReporter.CreateReportDirectory(artifactRoot);

            await Assert.That(first).IsNotEqualTo(second);
            await Assert.That(Directory.Exists(first)).IsTrue();
            await Assert.That(Directory.Exists(second)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(artifactRoot))
                Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Test]
    public async Task PublishArguments_IsolateTargetsThatShareOneProject()
    {
        var noAot = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-wasm-no-aot")[0];
        var aot = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-wasm-aot")[0];
        var nativeAot = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-native-aot")[0];
        var artifactRoot = Path.Combine("repo", "artifacts", "dev");
        var noAotBuild = CompatibilitySizeReporter.CreateBuildScratchDirectory(
            artifactRoot,
            "v0.9",
            noAot.Name);
        var aotBuild = CompatibilitySizeReporter.CreateBuildScratchDirectory(
            artifactRoot,
            "v0.9",
            aot.Name);
        var historicalBuild = CompatibilitySizeReporter.CreateBuildScratchDirectory(
            artifactRoot,
            "phase8c",
            "wasm");
        var differentlyCasedBuild = CompatibilitySizeReporter.CreateBuildScratchDirectory(
            artifactRoot,
            "V0.9",
            noAot.Name);

        var noAotArguments = CompatibilitySizeReporter.CreatePublishArguments(
            noAot,
            "memory-wasm.csproj",
            "publish-no-aot",
            noAotBuild,
            "Release",
            "win-x64",
            noRestore: true);
        var aotArguments = CompatibilitySizeReporter.CreatePublishArguments(
            aot,
            "memory-wasm.csproj",
            "publish-aot",
            aotBuild,
            "Release",
            "win-x64",
            noRestore: false);
        var nativeArguments = CompatibilitySizeReporter.CreatePublishArguments(
            nativeAot,
            "memory-native-aot.csproj",
            "publish-native-aot",
            CompatibilitySizeReporter.CreateBuildScratchDirectory(artifactRoot, "v0.9", nativeAot.Name),
            "Release",
            "linux-x64",
            noRestore: false);
        var noAotText = string.Join("\n", noAotArguments);
        var aotText = string.Join("\n", aotArguments);
        var nativeText = string.Join("\n", nativeArguments);

        await Assert.That(noAot.ProjectRelativePath).IsEqualTo(aot.ProjectRelativePath);
        await Assert.That(Path.IsPathFullyQualified(noAotBuild)).IsTrue();
        await Assert.That(differentlyCasedBuild).IsEqualTo(noAotBuild);
        await Assert.That(noAotBuild).IsNotEqualTo(aotBuild);
        await Assert.That(noAotBuild).IsNotEqualTo(historicalBuild);
        await Assert.That(noAotText).Contains("--artifacts-path").And.Contains(noAotBuild);
        await Assert.That(noAotText).Contains("publish-no-aot").And.Contains("RunAOTCompilation=false");
        await Assert.That(noAotText).Contains("--no-restore");
        await Assert.That(aotText).Contains("--artifacts-path").And.Contains(aotBuild);
        await Assert.That(aotText).Contains("publish-aot").And.Contains("RunAOTCompilation=true");
        await Assert.That(aotText).DoesNotContain("--no-restore");
        await Assert.That(nativeText).Contains("linux-x64").And.Contains("--self-contained");
    }

    [Test]
    public async Task BuildScratchLock_RejectsSameTargetButAllowsDifferentTargets()
    {
        var artifactRoot = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilitySizeReportTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            using var first = CompatibilitySizeReporter.AcquireBuildArtifactsLock(
                artifactRoot,
                "V0.9",
                "memory-wasm-aot");
            using var different = CompatibilitySizeReporter.AcquireBuildArtifactsLock(
                artifactRoot,
                "v0.9",
                "memory-wasm-no-aot");
            IOException? exception = null;

            try
            {
                using var duplicate = CompatibilitySizeReporter.AcquireBuildArtifactsLock(
                    artifactRoot,
                    "v0.9",
                    "memory-wasm-aot");
            }
            catch (IOException caught)
            {
                exception = caught;
            }

            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.Message)
                .Contains("v0.9/memory-wasm-aot")
                .And.Contains("already being published");
        }
        finally
        {
            if (Directory.Exists(artifactRoot))
                Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Test]
    public async Task ResetDirectory_RejectsReparseAncestorsWithoutDeletingExternalContent()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilitySizeReportTests",
            Guid.NewGuid().ToString("N"));
        var artifactRoot = Path.Combine(root, "artifacts", "dev");
        var buildRoot = Path.Combine(artifactRoot, "compat-size-build");
        var externalRoot = Path.Combine(root, "external");
        var targetSetLink = Path.Combine(buildRoot, "v0.9");
        var redirectedTarget = Path.Combine(externalRoot, "memory-wasm-aot");
        var sentinel = Path.Combine(redirectedTarget, "sentinel.txt");

        try
        {
            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(redirectedTarget);
            File.WriteAllText(sentinel, "preserve");

            CreateDirectoryLink(targetSetLink, externalRoot);

            IOException? caught = null;
            try
            {
                CompatibilitySizeReporter.ResetDirectory(
                    Path.Combine(targetSetLink, "memory-wasm-aot"),
                    targetSetLink,
                    artifactRoot);
            }
            catch (IOException exception)
            {
                caught = exception;
            }

            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message).Contains("reparse point");
            await Assert.That(File.Exists(sentinel)).IsTrue();

            var paths = DevToolPaths.Create(root);
            var options = new CompatibilityReportOptions(
                RepositoryRoot: root,
                Profile: ToolingProfile.Sandbox,
                TargetSet: "V0.9",
                TargetSelectors: "memory-wasm-aot",
                Configuration: "Release",
                RuntimeIdentifier: "win-x64",
                LargestFileCount: 0,
                NoRestore: false,
                SkipSmoke: true,
                TotalSizeWarningBytes: null,
                SymbolExcludedSizeWarningBytes: null,
                FileCountWarning: null,
                FailOnBannedPayload: false,
                FailOnThresholdWarnings: false,
                ContinueOnPublishFailure: true,
                CleanIntermediateOutputs: true,
                UseReleaseThresholds: false);

            var report = new CompatibilitySizeReporter(paths, options).CreateReport();

            await Assert.That(report.TargetSet).IsEqualTo("v0.9");
            await Assert.That(report.Targets).HasSingleItem();
            await Assert.That(report.Targets[0].Publish.FailureDisposition)
                .IsEqualTo(CompatibilityFailureDisposition.Environment);
            await Assert.That(report.Summary.EnvironmentFailureCount).IsEqualTo(1);
            await Assert.That(report.Summary.ProductPublishFailureCount).IsEqualTo(0);
            await Assert.That(report.Summary.HasHardFailures).IsTrue();
            await Assert.That(File.Exists(sentinel)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(targetSetLink) &&
                (File.GetAttributes(targetSetLink) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(targetSetLink);
            }

            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Reporter_RejectsCleanOutputWithNoRestoreBeforeCreatingArtifacts()
    {
        var repositoryRoot = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilitySizeReportTests",
            Guid.NewGuid().ToString("N"));
        var paths = DevToolPaths.Create(repositoryRoot);
        var options = new CompatibilityReportOptions(
            RepositoryRoot: repositoryRoot,
            Profile: ToolingProfile.Sandbox,
            TargetSet: "v0.9",
            TargetSelectors: "memory-native-aot",
            Configuration: "Release",
            RuntimeIdentifier: "win-x64",
            LargestFileCount: 0,
            NoRestore: true,
            SkipSmoke: true,
            TotalSizeWarningBytes: null,
            SymbolExcludedSizeWarningBytes: null,
            FileCountWarning: null,
            FailOnBannedPayload: false,
            FailOnThresholdWarnings: false,
            ContinueOnPublishFailure: true,
            CleanIntermediateOutputs: true,
            UseReleaseThresholds: false);
        InvalidOperationException? exception = null;

        try
        {
            _ = new CompatibilitySizeReporter(paths, options).CreateReport();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("--clean-output").And.Contains("--no-restore");
        await Assert.That(Directory.Exists(repositoryRoot)).IsFalse();
    }

    [Test]
    public async Task PayloadInspector_FindsRoslynPayloadsAndCompressedAssets()
    {
        var publishDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilitySizeReportTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            WriteFile(Path.Combine(publishDirectory, "DataLinq.dll"), 10);
            WriteFile(Path.Combine(publishDirectory, "Microsoft.CodeAnalysis.dll"), 20);
            WriteFile(Path.Combine(publishDirectory, "Microsoft.CodeAnalysis.CSharp.dll"), 30);
            WriteFile(Path.Combine(publishDirectory, "fr", "Microsoft.CodeAnalysis.resources.dll"), 40);
            WriteFile(Path.Combine(publishDirectory, "_framework", "Microsoft.CodeAnalysis.CSharp.wasm"), 50);
            WriteFile(Path.Combine(publishDirectory, "compressed", "Microsoft.CodeAnalysis.dll.br"), 0);
            WriteFile(Path.Combine(publishDirectory, "compressed", "Microsoft.CodeAnalysis.CSharp.wasm.gz"), 0);
            WriteFile(Path.Combine(publishDirectory, "_framework", "dotnet.native.wasm.br"), 60);
            WriteFile(Path.Combine(publishDirectory, "_framework", "dotnet.native.wasm.gz"), 70);
            WriteFile(Path.Combine(publishDirectory, "DataLinq.pdb"), 80);

            var target = CompatibilityTargetCatalog.GetTargets("phase8c", "native-aot")[0];
            var inspection = CompatibilityPayloadInspector.Inspect(
                target,
                publishDirectory,
                largestFileCount: 3,
                totalSizeWarningBytes: 100,
                symbolExcludedSizeWarningBytes: 100,
                fileCountWarning: 3);

            await Assert.That(inspection.Payload.TotalBytes).IsEqualTo(360);
            await Assert.That(inspection.Payload.SymbolExcludedBytes).IsEqualTo(280);
            await Assert.That(inspection.BannedPayloads.Count).IsEqualTo(6);
            await Assert.That(inspection.BrotliAssets.TotalBytes).IsEqualTo(60);
            await Assert.That(inspection.GzipAssets.TotalBytes).IsEqualTo(70);
            await Assert.That(inspection.ThresholdWarnings.Count).IsEqualTo(3);
            await Assert.That(inspection.LargestFiles.Count).IsEqualTo(3);
        }
        finally
        {
            if (Directory.Exists(publishDirectory))
                Directory.Delete(publishDirectory, recursive: true);
        }
    }

    [Test]
    public async Task PayloadInspector_MemoryGraphFindsProviderPathsAndEncodedContentOnly()
    {
        var publishDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "CompatibilitySizeReportTests",
            Guid.NewGuid().ToString("N"));

        try
        {
            WriteFile(Path.Combine(publishDirectory, "DataLinq.SQLite.dll"), 1);
            WriteTextFile(Path.Combine(publishDirectory, "renamed-mysql.bin"), "DataLinq.MySql", Encoding.UTF8);
            WriteTextFile(Path.Combine(publishDirectory, "renamed-ms-sqlite.bin"), "Microsoft.Data.Sqlite", Encoding.Unicode);
            WriteBoundaryTokenFile(Path.Combine(publishDirectory, "renamed-connector.bin"), "MySqlConnector");
            WriteCompressedTextFile(Path.Combine(publishDirectory, "renamed-pcl.gz"), "SQLitePCLRaw", gzip: true);
            WriteCompressedTextFile(Path.Combine(publishDirectory, "renamed-native.br"), "e_sqlite3", gzip: false);
            WriteFile(Path.Combine(publishDirectory, "renamed-sibling.bin"), 0);
            WriteCompressedTextFile(
                Path.Combine(publishDirectory, "renamed-sibling.bin.br"),
                "SQLitePCLRaw",
                gzip: false);

            var memoryTarget = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-native-aot")[0];
            var sqliteTarget = CompatibilityTargetCatalog.GetTargets("v0.9", "sqlite-native-aot")[0];
            var memoryInspection = CompatibilityPayloadInspector.Inspect(
                memoryTarget,
                publishDirectory,
                largestFileCount: 0,
                totalSizeWarningBytes: null,
                symbolExcludedSizeWarningBytes: null,
                fileCountWarning: null);
            var sqliteInspection = CompatibilityPayloadInspector.Inspect(
                sqliteTarget,
                publishDirectory,
                largestFileCount: 0,
                totalSizeWarningBytes: null,
                symbolExcludedSizeWarningBytes: null,
                fileCountWarning: null);

            await Assert.That(memoryInspection.BannedPayloads.Count).IsEqualTo(7);
            await Assert.That(memoryInspection.BannedPayloads.Any(static finding =>
                    finding.RelativePath.EndsWith("renamed-sibling.bin.br", StringComparison.Ordinal)))
                .IsTrue();
            await Assert.That(string.Join("\n", memoryInspection.BannedPayloads.Select(static finding => finding.Rule)))
                .Contains("DataLinq.SQLite")
                .And.Contains("DataLinq.MySql")
                .And.Contains("Microsoft.Data.Sqlite")
                .And.Contains("MySqlConnector")
                .And.Contains("SQLitePCLRaw")
                .And.Contains("e_sqlite3");
            await Assert.That(sqliteInspection.BannedPayloads.Count).IsEqualTo(0);
        }
        finally
        {
            if (Directory.Exists(publishDirectory))
                Directory.Delete(publishDirectory, recursive: true);
        }
    }

    [Test]
    public async Task BrowserHosts_ExposeOneNeutralTelemetryContract()
    {
        var repositoryRoot = RepositoryRootLocator.Find();
        var sqliteIndex = File.ReadAllText(Path.Combine(repositoryRoot, "src", "DataLinq.BlazorWasm", "wwwroot", "index.html"));
        var memoryIndex = File.ReadAllText(Path.Combine(repositoryRoot, "src", "DataLinq.Memory.BlazorWasm", "wwwroot", "index.html"));
        var memoryHome = File.ReadAllText(Path.Combine(repositoryRoot, "src", "DataLinq.Memory.BlazorWasm", "Pages", "Home.razor"));

        foreach (var index in new[] { sqliteIndex, memoryIndex })
        {
            await Assert.That(index).Contains("id=\"boot-status\"");
            await Assert.That(index).Contains("data-status=\"running\"");
            await Assert.That(index).Contains("window.datalinqLog");
            await Assert.That(index).Contains(
                "currentStatus === \"passed\" && status !== \"failed\"");
        }

        await Assert.That(memoryHome).Contains("data-datalinq-smoke-status");
        await Assert.That(memoryHome).Contains("data-datalinq-smoke-stage");
        await Assert.That(memoryHome).Contains("id=\"datalinq-smoke-result\"");
        await Assert.That(memoryIndex).DoesNotContain("datalinqMemory");
    }

    [Test]
    public async Task WarningClassifier_SplitsDataLinqThirdPartyAndWasmWarnings()
    {
        var nativeTarget = CompatibilityTargetCatalog
            .GetTargets("phase8c", "native-aot")[0];
        var wasmTarget = CompatibilityTargetCatalog
            .GetTargets("phase8c", "wasm-aot")[0];

        var datalinqWarning = new DotnetDiagnostic(
            DotnetDiagnosticKind.Warning,
            "IL2026",
            "Using member DataLinq.Core.Factories.PluginHook requires dynamic access.",
            [@"D:\git\DataLinq\src\DataLinq\DataLinq.csproj"],
            1);
        var thirdPartyWarning = new DotnetDiagnostic(
            DotnetDiagnosticKind.Warning,
            "IL2104",
            "Assembly Remotion.Linq produced trim warnings.",
            [],
            2);
        var wasmWarning = new DotnetDiagnostic(
            DotnetDiagnosticKind.Warning,
            "WASM0001",
            "WebAssembly native varargs are unsupported.",
            [],
            3);
        var sqlitePclHeaderWarning = new DotnetDiagnostic(
            DotnetDiagnosticKind.Warning,
            "WASM0001",
            "Found a native function (sqlite3_config) with varargs in e_sqlite3. Calling such functions is not supported.",
            [@"repo\src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj::TargetFramework=net10.0"],
            1);
        var sqlitePclDatabaseHeaderWarning = new DotnetDiagnostic(
            DotnetDiagnosticKind.Warning,
            "WASM0001",
            "Found a native function (sqlite3_db_config) with varargs in e_sqlite3. Calling such functions is not supported.",
            [@"repo\src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj::TargetFramework=net10.0"],
            1);
        var dataLinqWasmWarning = new DotnetDiagnostic(
            DotnetDiagnosticKind.Warning,
            "WASM0001",
            "Found a native function (datalinq_callback) with varargs in datalinq_native.",
            [@"repo\src\DataLinq.BlazorWasm\DataLinq.BlazorWasm.csproj::TargetFramework=net10.0"],
            1);

        await Assert.That(CompatibilityWarningClassifier.Classify(nativeTarget, datalinqWarning))
            .IsEqualTo(CompatibilityWarningOwner.DataLinqOwned);
        await Assert.That(CompatibilityWarningClassifier.Classify(nativeTarget, thirdPartyWarning))
            .IsEqualTo(CompatibilityWarningOwner.ThirdPartyDependency);
        await Assert.That(CompatibilityWarningClassifier.Classify(wasmTarget, wasmWarning))
            .IsEqualTo(CompatibilityWarningOwner.SdkOrWebAssembly);
        await Assert.That(CompatibilityWarningClassifier.Classify(wasmTarget, sqlitePclHeaderWarning))
            .IsEqualTo(CompatibilityWarningOwner.ThirdPartyDependency);
        await Assert.That(CompatibilityWarningClassifier.Classify(wasmTarget, sqlitePclDatabaseHeaderWarning))
            .IsEqualTo(CompatibilityWarningOwner.ThirdPartyDependency);
        await Assert.That(CompatibilityWarningClassifier.Classify(wasmTarget, dataLinqWasmWarning))
            .IsEqualTo(CompatibilityWarningOwner.DataLinqOwned);
        await Assert.That(CompatibilityWarningClassifier.Classify(wasmTarget, datalinqWarning))
            .IsEqualTo(CompatibilityWarningOwner.DataLinqOwned);
        await Assert.That(CompatibilityWarningClassifier.Classify(wasmTarget, thirdPartyWarning))
            .IsEqualTo(CompatibilityWarningOwner.ThirdPartyDependency);
    }

    [Test]
    public async Task FailureClassifier_ReportsRemotionTrimAnalysisFailures()
    {
        var trimmedTarget = CompatibilityTargetCatalog
            .GetTargets("phase8c", "trimmed")[0];
        var processResult = new ExternalCommandResult(
            1,
            """
            Optimizing assemblies for size. This process might take a while.
            D:\git\DataLinq\.dotnet\.nuget\packages\remotion.linq\2.2.0\lib\netstandard1.0\Remotion.Linq.dll : error IL2104: Assembly 'Remotion.Linq' produced trim warnings. For more information see https://aka.ms/il2104 [D:\git\DataLinq\src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj::TargetFramework=net10.0]
            D:\git\DataLinq\.dotnet\.nuget\packages\microsoft.net.illink.tasks\10.0.9\build\Microsoft.NET.ILLink.targets(103,5): error NETSDK1144: Optimizing assemblies for size failed. [D:\git\DataLinq\src\DataLinq.TrimSmoke\DataLinq.TrimSmoke.csproj::TargetFramework=net10.0]
            """,
            "");
        var analysis = DotnetOutputAnalyzer.Analyze(DotnetCommandType.Publish, processResult);
        var commandResult = new DotnetCommandResult(
            DotnetCommandType.Publish,
            trimmedTarget.Name,
            [],
            processResult,
            "publish.log",
            BinaryLogPath: null,
            analysis);

        await Assert.That(analysis.FailureCategory).IsEqualTo(DotnetFailureCategory.TrimAnalysis);
        await Assert.That(CompatibilityWarningClassifier.ClassifyFailure(trimmedTarget, commandResult))
            .IsEqualTo(CompatibilityFailureClassification.RemotionDependency);
        await Assert.That(CompatibilityWarningClassifier.ClassifyFailureDisposition(commandResult))
            .IsEqualTo(CompatibilityFailureDisposition.Product);
    }

    [Test]
    public async Task FailureClassifier_ReportsNativeAotToolchainFailures()
    {
        var nativeAotTarget = CompatibilityTargetCatalog
            .GetTargets("phase8c", "native-aot")[0];
        var processResult = new ExternalCommandResult(
            1,
            """
            D:\git\DataLinq\.dotnet\.nuget\packages\microsoft.dotnet.ilcompiler\10.0.9\build\Microsoft.NETCore.Native.Windows.targets(142,5): error : Platform linker not found. Ensure you have all the required prerequisites documented at https://aka.ms/nativeaot-prerequisites, in particular the Desktop Development for C++ workload in Visual Studio. For ARM64 development also install C++ ARM64 build tools. [D:\git\DataLinq\src\DataLinq.AotSmoke\DataLinq.AotSmoke.csproj::TargetFramework=net10.0]
            """,
            "");
        var analysis = DotnetOutputAnalyzer.Analyze(DotnetCommandType.Publish, processResult);
        var commandResult = new DotnetCommandResult(
            DotnetCommandType.Publish,
            nativeAotTarget.Name,
            [],
            processResult,
            "publish.log",
            BinaryLogPath: null,
            analysis);

        await Assert.That(CompatibilityWarningClassifier.ClassifyFailure(nativeAotTarget, commandResult))
            .IsEqualTo(CompatibilityFailureClassification.SdkOrWebAssemblyToolchain);
        await Assert.That(CompatibilityWarningClassifier.ClassifyFailureDisposition(commandResult))
            .IsEqualTo(CompatibilityFailureDisposition.Environment);
    }

    [Test]
    public async Task FailureClassifier_DoesNotDowngradeNoAotProductFailures()
    {
        var target = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-wasm-no-aot")[0];
        var processResult = new ExternalCommandResult(
            1,
            "Program.cs(4,9): error CS1002: ; expected",
            "");
        var analysis = DotnetOutputAnalyzer.Analyze(DotnetCommandType.Publish, processResult);
        var commandResult = new DotnetCommandResult(
            DotnetCommandType.Publish,
            target.Name,
            [],
            processResult,
            "publish.log",
            BinaryLogPath: null,
            analysis);

        await Assert.That(CompatibilityWarningClassifier.ClassifyFailure(target, commandResult))
            .IsNotEqualTo(CompatibilityFailureClassification.UnsupportedNoAot);
        await Assert.That(CompatibilityWarningClassifier.ClassifyFailureDisposition(commandResult))
            .IsEqualTo(CompatibilityFailureDisposition.Product);
    }

    [Test]
    public async Task FailureClassifier_TreatsMissingIsolatedRestoreAssetsAsEnvironment()
    {
        var target = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-wasm-no-aot")[0];
        var processResult = new ExternalCommandResult(
            1,
            "error NETSDK1004: Assets file 'artifacts/dev/compat-size-build/v0.9/memory-wasm-no-aot/obj/project.assets.json' not found. Run a NuGet package restore to generate this file.",
            "");
        var commandResult = new DotnetCommandResult(
            DotnetCommandType.Publish,
            target.Name,
            [],
            processResult,
            "publish.log",
            BinaryLogPath: null,
            DotnetOutputAnalyzer.Analyze(DotnetCommandType.Publish, processResult));

        await Assert.That(CompatibilityWarningClassifier.ClassifyFailureDisposition(commandResult))
            .IsEqualTo(CompatibilityFailureDisposition.Environment);
    }

    [Test]
    public async Task ReleaseThresholds_FlagMissingWebAssemblyBrotliAssets()
    {
        var wasmTarget = CompatibilityTargetCatalog
            .GetTargets("phase8c", "wasm-aot")[0];

        var warnings = CompatibilityReleaseThresholds.FindWarnings(
            wasmTarget,
            publishDirectory: AppContext.BaseDirectory,
            new CompatibilityPayloadSizeSummary(0, 0, 0),
            new CompatibilityCompressedAssetSummary(".br", 0, 0));

        await Assert.That(warnings.Select(static warning => warning.Metric))
            .Contains("release-wasm-aot-brotli-assets");

        var sizedWarnings = CompatibilityReleaseThresholds.FindWarnings(
            wasmTarget,
            publishDirectory: AppContext.BaseDirectory,
            new CompatibilityPayloadSizeSummary(0, 0, 0),
            new CompatibilityCompressedAssetSummary(".br", 1, 13 * 1024L * 1024L));
        await Assert.That(sizedWarnings.Single().Message).Contains("compatibility guardrail");
        await Assert.That(sizedWarnings.Single().Message).DoesNotContain("0.8");
    }

    [Test]
    public async Task ReportSummary_PartitionsProductAndEnvironmentFailures()
    {
        var targets = CompatibilityTargetCatalog.GetTargets("v0.9");
        var succeeded = Command(CompatibilityCommandStatus.Succeeded, CompatibilityFailureDisposition.None);
        var skipped = Command(CompatibilityCommandStatus.Skipped, CompatibilityFailureDisposition.None);
        var productFailure = Command(
            CompatibilityCommandStatus.Failed,
            CompatibilityFailureDisposition.Product,
            CompatibilityFailureClassification.ProductRegression);
        var environmentFailure = Command(
            CompatibilityCommandStatus.Failed,
            CompatibilityFailureDisposition.Environment,
            CompatibilityFailureClassification.SdkOrWebAssemblyToolchain);
        var unsupported = Command(
            CompatibilityCommandStatus.Unsupported,
            CompatibilityFailureDisposition.Unsupported,
            CompatibilityFailureClassification.UnsupportedNoAot);
        var productInspectionFailure = Command(
            CompatibilityCommandStatus.Failed,
            CompatibilityFailureDisposition.Product,
            CompatibilityFailureClassification.PayloadInspection);
        var environmentInspectionFailure = Command(
            CompatibilityCommandStatus.Failed,
            CompatibilityFailureDisposition.Environment,
            CompatibilityFailureClassification.PayloadInspection);
        var reports = new[]
        {
            TargetReport(targets[0], productFailure, skipped),
            TargetReport(targets[1], succeeded, productFailure),
            TargetReport(targets[2], environmentFailure, skipped),
            TargetReport(targets[3], succeeded, unsupported),
            TargetReport(targets[4], succeeded, succeeded, productInspectionFailure),
            TargetReport(targets[5], succeeded, succeeded, environmentInspectionFailure)
        };

        var summary = CompatibilitySizeReporter.CreateSummary(
            reports,
            failOnBannedPayload: false,
            failOnThresholdWarnings: false);

        await Assert.That(summary.ProductPublishFailureCount).IsEqualTo(1);
        await Assert.That(summary.ProductSmokeFailureCount).IsEqualTo(1);
        await Assert.That(summary.ProductInspectionFailureCount).IsEqualTo(1);
        await Assert.That(summary.EnvironmentFailureCount).IsEqualTo(2);
        await Assert.That(summary.UnsupportedCount).IsEqualTo(1);
        await Assert.That(summary.HasHardFailures).IsTrue();
    }

    [Test]
    public async Task BrowserTelemetry_IsStructuredInJsonAndMarkdown()
    {
        var target = CompatibilityTargetCatalog.GetTargets("v0.9", "memory-wasm-no-aot")[0];
        var publish = Command(CompatibilityCommandStatus.Succeeded, CompatibilityFailureDisposition.None);
        var smoke = Command(CompatibilityCommandStatus.Succeeded, CompatibilityFailureDisposition.None) with
        {
            Browser = new CompatibilityBrowserSmokeDetails(
                true,
                "passed",
                "completed",
                ["log: memory complete"],
                ["log: browser complete"],
                [])
        };
        var targetReport = TargetReport(target, publish, smoke);
        var report = new CompatibilitySizeReport(
            CompatibilitySizeReporter.SchemaVersion,
            DateTimeOffset.UnixEpoch,
            "repo",
            "v0.9",
            [target.Name],
            8,
            false,
            "Release",
            "win-x64",
            "10.0.100",
            "report",
            [targetReport],
            CompatibilitySizeReporter.CreateSummary([targetReport], false, false));
        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() }
        };
        var json = JsonSerializer.Serialize(report, jsonOptions);
        var markdown = CompatibilitySizeReporter.ToMarkdown(report);

        await Assert.That(json).Contains("\"RuntimeGraph\":\"Memory\"");
        await Assert.That(json).Contains("\"BuildScratchDirectory\":");
        await Assert.That(json).Contains("\"IsFullTargetSet\":false");
        await Assert.That(json).Contains("\"FinalStage\":\"completed\"");
        await Assert.That(markdown).Contains("Browser Smoke Telemetry");
        await Assert.That(markdown).Contains("Target coverage: `1/8` (`subset`)");
        await Assert.That(markdown).Contains("Final stage: `completed`");
    }

    [Test]
    public async Task PackageInspector_CleanRuntimePackageKeepsAnalyzerAssetsOutOfLib()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "PackageInspectorTests",
            Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(root, "packages");

        try
        {
            Directory.CreateDirectory(packageDirectory);
            WritePackage(
                Path.Combine(packageDirectory, "DataLinq.1.0.0.nupkg"),
                "DataLinq",
                "1.0.0",
                """
                <dependencies>
                  <group targetFramework="net10.0">
                    <dependency id="Microsoft.Extensions.Logging.Abstractions" version="10.0.6" exclude="Build,Analyzers" />
                  </group>
                </dependencies>
                """,
                [
                    "lib/net10.0/DataLinq.dll",
                    "analyzers/dotnet/cs/DataLinq.Generators.dll",
                    "analyzers/dotnet/cs/DataLinq.Generators.deps.json",
                    "analyzers/dotnet/cs/ThrowAway.dll"
                ]);
            WritePackage(
                Path.Combine(packageDirectory, "DataLinq.1.0.0.snupkg"),
                "DataLinq",
                "1.0.0",
                "",
                ["lib/net10.0/DataLinq.pdb"]);

            var report = CreatePackageReport(root, packageDirectory, PackageSet("DataLinq"), PackageSet("DataLinq"));

            await Assert.That(report.Summary.HasHardFailures).IsFalse();
            await Assert.That(report.Findings.Count).IsEqualTo(0);
            await Assert.That(report.Packages.Single().Assets.AnalyzerFileCount).IsEqualTo(3);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PackageInspector_FlagsRuntimeRoslynLeaksAndUnexpectedPackages()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "PackageInspectorTests",
            Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(root, "packages");

        try
        {
            Directory.CreateDirectory(packageDirectory);
            WritePackage(
                Path.Combine(packageDirectory, "DataLinq.1.0.0.nupkg"),
                "DataLinq",
                "1.0.0",
                """
                <dependencies>
                  <group targetFramework="net10.0">
                    <dependency id="Microsoft.CodeAnalysis.CSharp" version="5.0.0" />
                  </group>
                </dependencies>
                """,
                [
                    "lib/net10.0/DataLinq.dll",
                    "lib/net10.0/Microsoft.CodeAnalysis.dll",
                    "lib/net10.0/DataLinq.Generators.dll"
                ]);
            WritePackage(
                Path.Combine(packageDirectory, "DataLinq.Tests.Models.1.0.0.nupkg"),
                "DataLinq.Tests.Models",
                "1.0.0",
                "",
                ["lib/net10.0/DataLinq.Tests.Models.dll"]);

            var report = CreatePackageReport(root, packageDirectory, PackageSet("DataLinq"), PackageSet("DataLinq"));
            var findingKinds = report.Findings.Select(static finding => finding.Kind).ToArray();

            await Assert.That(report.Summary.HasHardFailures).IsTrue();
            await Assert.That(findingKinds).Contains(PackageInspectionFindingKind.RuntimeRoslynDependency);
            await Assert.That(findingKinds).Contains(PackageInspectionFindingKind.RuntimeRoslynAsset);
            await Assert.That(findingKinds).Contains(PackageInspectionFindingKind.AnalyzerAssetLeak);
            await Assert.That(findingKinds).Contains(PackageInspectionFindingKind.UnexpectedPackage);
            await Assert.That(findingKinds).Contains(PackageInspectionFindingKind.MissingSymbolPackage);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PackageInspector_FlagsRuntimeRemotionLeaks()
    {
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "PackageInspectorTests",
            Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(root, "packages");

        try
        {
            Directory.CreateDirectory(packageDirectory);
            WritePackage(
                Path.Combine(packageDirectory, "DataLinq.1.0.0.nupkg"),
                "DataLinq",
                "1.0.0",
                """
                <dependencies>
                  <group targetFramework="net10.0">
                    <dependency id="Remotion.Linq" version="2.2.0" />
                  </group>
                </dependencies>
                """,
                [
                    "lib/net10.0/DataLinq.dll",
                    "lib/net10.0/Remotion.Linq.dll",
                    "analyzers/dotnet/cs/DataLinq.Generators.dll"
                ]);
            WritePackage(
                Path.Combine(packageDirectory, "DataLinq.1.0.0.snupkg"),
                "DataLinq",
                "1.0.0",
                "",
                ["lib/net10.0/DataLinq.pdb"]);

            var report = CreatePackageReport(root, packageDirectory, PackageSet("DataLinq"), PackageSet("DataLinq"));
            var findingKinds = report.Findings.Select(static finding => finding.Kind).ToArray();

            await Assert.That(report.Summary.HasHardFailures).IsTrue();
            await Assert.That(findingKinds).Contains(PackageInspectionFindingKind.RuntimeRemotionDependency);
            await Assert.That(findingKinds).Contains(PackageInspectionFindingKind.RuntimeRemotionAsset);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string TargetSnapshot(IReadOnlyList<CompatibilityTargetDefinition> targets) =>
        string.Join(
            "\n",
            targets.Select(static target => string.Join(
                "|",
                target.Name,
                target.Kind,
                target.RuntimeGraph,
                target.DisplayName,
                target.ProjectRelativePath,
                target.TargetFramework,
                target.RequiresRuntimeIdentifier,
                target.IsWebAssembly,
                target.ExecutableName,
                string.Join(",", target.PublishProperties))));

    private static CompatibilityCommandReport Command(
        CompatibilityCommandStatus status,
        CompatibilityFailureDisposition disposition,
        CompatibilityFailureClassification classification = CompatibilityFailureClassification.None) =>
        new(status, null, null, null, disposition, classification, null);

    private static CompatibilityTargetReport TargetReport(
        CompatibilityTargetDefinition target,
        CompatibilityCommandReport publish,
        CompatibilityCommandReport smoke,
        CompatibilityCommandReport? inspection = null) =>
        new(
            target.Name,
            target.Kind,
            target.RuntimeGraph,
            target.DisplayName,
            target.ProjectRelativePath,
            target.Name,
            target.Name + "-build",
            publish,
            smoke,
            inspection ?? Command(CompatibilityCommandStatus.Succeeded, CompatibilityFailureDisposition.None),
            new CompatibilityPayloadSizeSummary(0, 0, 0),
            [],
            [],
            new CompatibilityWarningSummary(0, 0, [], []),
            [],
            new CompatibilityCompressedAssetSummary(".br", 0, 0),
            new CompatibilityCompressedAssetSummary(".gz", 0, 0));

    private static void WriteFile(string path, int byteCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[byteCount]);
    }

    private static void WriteTextFile(string path, string content, Encoding encoding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, encoding);
    }

    private static void WriteBoundaryTokenFile(string path, string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var bytes = new byte[64 * 1024 - 5 + tokenBytes.Length];
        tokenBytes.CopyTo(bytes, 64 * 1024 - 5);
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteCompressedTextFile(string path, string content, bool gzip)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using Stream compression = gzip
            ? new GZipStream(file, CompressionMode.Compress)
            : new BrotliStream(file, CompressionMode.Compress);
        using var writer = new StreamWriter(compression, Encoding.UTF8);
        writer.Write(content);
    }

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(linkPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Could not start cmd.exe to create a test junction.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new IOException(
                $"Could not create test junction (exit {process.ExitCode}): {standardOutput}{standardError}");
        }
    }

    private static PackageInspectionReport CreatePackageReport(
        string root,
        string packageDirectory,
        IReadOnlySet<string> expectedPackages,
        IReadOnlySet<string> runtimePackages)
    {
        var paths = DevToolPaths.Create(root);
        var options = new PackageInspectionOptions(
            root,
            packageDirectory,
            expectedPackages,
            runtimePackages,
            FailOnUnexpectedPackage: true,
            FailOnMissingSymbolPackage: true,
            FailOnRuntimeRoslyn: true,
            FailOnRuntimeRemotion: true,
            FailOnAnalyzerAssetLeak: true);

        return new PackageInspector(paths, options).CreateReport();
    }

    private static IReadOnlySet<string> PackageSet(params string[] packageIds) =>
        packageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void WritePackage(
        string path,
        string id,
        string version,
        string dependencyXml,
        IReadOnlyList<string> entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteZipEntry(
            archive,
            $"{id}.nuspec",
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{{id}}</id>
                <version>{{version}}</version>
                <authors>DataLinq</authors>
                <license type="file">LICENSE.md</license>
                <readme>README.md</readme>
                <description>Test package.</description>
                <repository type="git" url="https://github.com/bazer/DataLinq" branch="refs/heads/test" commit="0123456789abcdef0123456789abcdef01234567" />
                {{dependencyXml}}
              </metadata>
            </package>
            """);

        WriteZipEntry(archive, "LICENSE.md", "license");
        WriteZipEntry(archive, "README.md", "readme");

        foreach (var entry in entries)
            WriteZipEntry(archive, entry, "");
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
