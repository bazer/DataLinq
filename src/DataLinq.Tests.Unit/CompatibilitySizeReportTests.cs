using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public class CompatibilitySizeReportTests
{
    [Test]
    public async Task TargetParser_Phase8CSelectsAllTargets()
    {
        var targets = CompatibilityTargetCatalog.ParseTargetKinds("phase8c");

        await Assert.That(targets).Contains(CompatibilityTargetKind.NativeAot);
        await Assert.That(targets).Contains(CompatibilityTargetKind.Trimmed);
        await Assert.That(targets).Contains(CompatibilityTargetKind.Wasm);
        await Assert.That(targets).Contains(CompatibilityTargetKind.WasmAot);
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
            WriteFile(Path.Combine(publishDirectory, "_framework", "dotnet.native.wasm.br"), 60);
            WriteFile(Path.Combine(publishDirectory, "_framework", "dotnet.native.wasm.gz"), 70);
            WriteFile(Path.Combine(publishDirectory, "DataLinq.pdb"), 80);

            var inspection = CompatibilityPayloadInspector.Inspect(
                publishDirectory,
                largestFileCount: 3,
                totalSizeWarningBytes: 100,
                symbolExcludedSizeWarningBytes: 100,
                fileCountWarning: 3);

            await Assert.That(inspection.Payload.TotalBytes).IsEqualTo(360);
            await Assert.That(inspection.Payload.SymbolExcludedBytes).IsEqualTo(280);
            await Assert.That(inspection.BannedPayloads.Count).IsEqualTo(4);
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
    public async Task WarningClassifier_SplitsDataLinqThirdPartyAndWasmWarnings()
    {
        var nativeTarget = CompatibilityTargetCatalog
            .GetTargets("phase8c", [CompatibilityTargetKind.NativeAot])[0];
        var wasmTarget = CompatibilityTargetCatalog
            .GetTargets("phase8c", [CompatibilityTargetKind.WasmAot])[0];

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

        await Assert.That(CompatibilityWarningClassifier.Classify(nativeTarget, datalinqWarning))
            .IsEqualTo(CompatibilityWarningOwner.DataLinqOwned);
        await Assert.That(CompatibilityWarningClassifier.Classify(nativeTarget, thirdPartyWarning))
            .IsEqualTo(CompatibilityWarningOwner.ThirdPartyDependency);
        await Assert.That(CompatibilityWarningClassifier.Classify(wasmTarget, wasmWarning))
            .IsEqualTo(CompatibilityWarningOwner.SdkOrWebAssembly);
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

    private static void WriteFile(string path, int byteCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[byteCount]);
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
                <description>Test package.</description>
                {{dependencyXml}}
              </metadata>
            </package>
            """);

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
