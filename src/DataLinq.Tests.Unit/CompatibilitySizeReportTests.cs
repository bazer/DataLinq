using System;
using System.IO;
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

    private static void WriteFile(string path, int byteCount)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[byteCount]);
    }
}
