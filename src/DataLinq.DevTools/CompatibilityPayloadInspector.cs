using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DataLinq.DevTools;

public static class CompatibilityPayloadInspector
{
    private static readonly HashSet<string> SymbolExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb",
        ".dbg",
        ".dSYM",
        ".mdb"
    };

    public static CompatibilityPayloadInspectionResult Inspect(
        string publishDirectory,
        int largestFileCount,
        long? totalSizeWarningBytes,
        long? symbolExcludedSizeWarningBytes,
        int? fileCountWarning)
    {
        if (!Directory.Exists(publishDirectory))
        {
            return new CompatibilityPayloadInspectionResult(
                new CompatibilityPayloadSizeSummary(0, 0, 0),
                [],
                [],
                new CompatibilityCompressedAssetSummary(".br", 0, 0),
                new CompatibilityCompressedAssetSummary(".gz", 0, 0),
                []);
        }

        var root = Path.GetFullPath(publishDirectory);
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => CreatePayloadFile(root, path))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var totalBytes = files.Sum(static file => file.SizeBytes);
        var symbolExcludedBytes = files
            .Where(static file => !IsSymbolFile(file.RelativePath))
            .Sum(static file => file.SizeBytes);

        var payload = new CompatibilityPayloadSizeSummary(totalBytes, symbolExcludedBytes, files.Length);
        var largestFiles = files
            .OrderByDescending(static file => file.SizeBytes)
            .ThenBy(static file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, largestFileCount))
            .Select(static file => new CompatibilityLargestFile(file.RelativePath, file.SizeBytes))
            .ToArray();

        var bannedPayloads = files
            .SelectMany(static file => FindBannedPayloads(file))
            .OrderBy(static x => x.Rule, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var brotliAssets = CreateCompressedAssetSummary(files, ".br");
        var gzipAssets = CreateCompressedAssetSummary(files, ".gz");
        var thresholdWarnings = FindThresholdWarnings(payload, totalSizeWarningBytes, symbolExcludedSizeWarningBytes, fileCountWarning);

        return new CompatibilityPayloadInspectionResult(
            payload,
            largestFiles,
            bannedPayloads,
            brotliAssets,
            gzipAssets,
            thresholdWarnings);
    }

    public static string FormatBytes(long bytes)
    {
        const double kb = 1024d;
        const double mb = kb * 1024d;
        const double gb = mb * 1024d;

        return bytes switch
        {
            >= 1024L * 1024L * 1024L => FormattableString.Invariant($"{bytes / gb:0.##} GB"),
            >= 1024L * 1024L => FormattableString.Invariant($"{bytes / mb:0.##} MB"),
            >= 1024L => FormattableString.Invariant($"{bytes / kb:0.##} KB"),
            _ => FormattableString.Invariant($"{bytes} B")
        };
    }

    private static CompatibilityPayloadFile CreatePayloadFile(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
            relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');

        return new CompatibilityPayloadFile(relativePath, new FileInfo(path).Length);
    }

    private static bool IsSymbolFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);

        if (relativePath.Contains(".dSYM/", StringComparison.OrdinalIgnoreCase))
            return true;

        return SymbolExtensions.Contains(Path.GetExtension(fileName));
    }

    private static IReadOnlyList<CompatibilityBannedPayloadFinding> FindBannedPayloads(CompatibilityPayloadFile file)
    {
        var fileName = Path.GetFileName(file.RelativePath);
        var findings = new List<CompatibilityBannedPayloadFinding>();

        if (string.Equals(fileName, "Microsoft.CodeAnalysis.dll", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Microsoft.CodeAnalysis.dll",
                file.RelativePath,
                file.SizeBytes));
        }

        if (string.Equals(fileName, "Microsoft.CodeAnalysis.CSharp.dll", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Microsoft.CodeAnalysis.CSharp.dll",
                file.RelativePath,
                file.SizeBytes));
        }

        if (fileName.StartsWith("Microsoft.CodeAnalysis.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Roslyn satellite resource payload",
                file.RelativePath,
                file.SizeBytes));
        }

        if (fileName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Microsoft.CodeAnalysis*.wasm",
                file.RelativePath,
                file.SizeBytes));
        }

        return findings;
    }

    private static CompatibilityCompressedAssetSummary CreateCompressedAssetSummary(
        IReadOnlyList<CompatibilityPayloadFile> files,
        string extension)
    {
        var matching = files
            .Where(file => file.RelativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new CompatibilityCompressedAssetSummary(
            extension,
            matching.Length,
            matching.Sum(static file => file.SizeBytes));
    }

    private static IReadOnlyList<CompatibilityThresholdFinding> FindThresholdWarnings(
        CompatibilityPayloadSizeSummary payload,
        long? totalSizeWarningBytes,
        long? symbolExcludedSizeWarningBytes,
        int? fileCountWarning)
    {
        var findings = new List<CompatibilityThresholdFinding>();

        if (totalSizeWarningBytes.HasValue && payload.TotalBytes > totalSizeWarningBytes.Value)
        {
            findings.Add(new CompatibilityThresholdFinding(
                "total-size",
                payload.TotalBytes,
                totalSizeWarningBytes.Value,
                "warning",
                $"Total payload size {FormatBytes(payload.TotalBytes)} exceeds warning threshold {FormatBytes(totalSizeWarningBytes.Value)}."));
        }

        if (symbolExcludedSizeWarningBytes.HasValue && payload.SymbolExcludedBytes > symbolExcludedSizeWarningBytes.Value)
        {
            findings.Add(new CompatibilityThresholdFinding(
                "symbol-excluded-size",
                payload.SymbolExcludedBytes,
                symbolExcludedSizeWarningBytes.Value,
                "warning",
                $"Symbol-excluded payload size {FormatBytes(payload.SymbolExcludedBytes)} exceeds warning threshold {FormatBytes(symbolExcludedSizeWarningBytes.Value)}."));
        }

        if (fileCountWarning.HasValue && payload.FileCount > fileCountWarning.Value)
        {
            findings.Add(new CompatibilityThresholdFinding(
                "file-count",
                payload.FileCount,
                fileCountWarning.Value,
                "warning",
                $"Payload file count {payload.FileCount} exceeds warning threshold {fileCountWarning.Value}."));
        }

        return findings;
    }

    private sealed record CompatibilityPayloadFile(
        string RelativePath,
        long SizeBytes);
}

public sealed record CompatibilityPayloadInspectionResult(
    CompatibilityPayloadSizeSummary Payload,
    IReadOnlyList<CompatibilityLargestFile> LargestFiles,
    IReadOnlyList<CompatibilityBannedPayloadFinding> BannedPayloads,
    CompatibilityCompressedAssetSummary BrotliAssets,
    CompatibilityCompressedAssetSummary GzipAssets,
    IReadOnlyList<CompatibilityThresholdFinding> ThresholdWarnings);
