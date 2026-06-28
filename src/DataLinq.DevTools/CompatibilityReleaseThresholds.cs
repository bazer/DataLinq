using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataLinq.DevTools;

public static class CompatibilityReleaseThresholds
{
    private const long MiB = 1024L * 1024L;

    public static IReadOnlyList<CompatibilityThresholdFinding> FindWarnings(
        CompatibilityTargetDefinition target,
        string publishDirectory,
        CompatibilityPayloadSizeSummary payload,
        CompatibilityCompressedAssetSummary brotliAssets)
    {
        var warnings = new List<CompatibilityThresholdFinding>();

        switch (target.Kind)
        {
            case CompatibilityTargetKind.NativeAot:
                AddExecutableWarning(warnings, target, publishDirectory, 20 * MiB);
                AddSizeWarning(
                    warnings,
                    "release-native-aot-symbol-excluded-size",
                    payload.SymbolExcludedBytes,
                    25 * MiB,
                    "Native AOT symbol-excluded publish size");
                break;

            case CompatibilityTargetKind.Trimmed:
                AddSizeWarning(
                    warnings,
                    "release-trimmed-symbol-excluded-size",
                    payload.SymbolExcludedBytes,
                    25 * MiB,
                    "Trimmed self-contained symbol-excluded publish size");
                break;

            case CompatibilityTargetKind.Wasm:
                AddSizeWarning(
                    warnings,
                    "release-wasm-no-aot-brotli-size",
                    brotliAssets.TotalBytes,
                    6 * MiB,
                    "Blazor WebAssembly no-AOT Brotli asset size");
                break;

            case CompatibilityTargetKind.WasmAot:
                AddSizeWarning(
                    warnings,
                    "release-wasm-aot-brotli-size",
                    brotliAssets.TotalBytes,
                    12 * MiB,
                    "Blazor WebAssembly AOT Brotli asset size");
                break;
        }

        return warnings;
    }

    private static void AddExecutableWarning(
        List<CompatibilityThresholdFinding> warnings,
        CompatibilityTargetDefinition target,
        string publishDirectory,
        long limit)
    {
        var executablePath = FindExecutablePath(target, publishDirectory);
        if (executablePath is null)
            return;

        AddSizeWarning(
            warnings,
            "release-native-aot-executable-size",
            new FileInfo(executablePath).Length,
            limit,
            "Native AOT executable size");
    }

    private static string? FindExecutablePath(
        CompatibilityTargetDefinition target,
        string publishDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(publishDirectory, target.ExecutableName),
            Path.Combine(publishDirectory, $"{target.ExecutableName}.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AddSizeWarning(
        List<CompatibilityThresholdFinding> warnings,
        string metric,
        long actual,
        long limit,
        string label)
    {
        if (actual <= limit)
            return;

        warnings.Add(new CompatibilityThresholdFinding(
            metric,
            actual,
            limit,
            "warning",
            $"{label} {CompatibilityPayloadInspector.FormatBytes(actual)} exceeds 0.8 release threshold {CompatibilityPayloadInspector.FormatBytes(limit)}."));
    }
}
