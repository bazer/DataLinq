using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DataLinq.DevTools;

public static class CompatibilityPayloadInspector
{
    private static readonly string[] MemoryProviderTokens =
    [
        "DataLinq.SQLite",
        "DataLinq.MySql",
        "Microsoft.Data.Sqlite",
        "MySqlConnector",
        "SQLitePCLRaw",
        "e_sqlite3"
    ];

    private static readonly EncodedToken[] MemoryProviderPatterns = MemoryProviderTokens
        .SelectMany(static token => new[]
        {
            new EncodedToken(token, Encoding.UTF8.GetBytes(token)),
            new EncodedToken(token, Encoding.Unicode.GetBytes(token)),
            new EncodedToken(token, Encoding.BigEndianUnicode.GetBytes(token))
        })
        .ToArray();

    private static readonly HashSet<string> SymbolExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb",
        ".dbg",
        ".dSYM",
        ".mdb"
    };

    public static CompatibilityPayloadInspectionResult Inspect(
        CompatibilityTargetDefinition target,
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
            .SelectMany(file => FindBannedPayloads(target, file))
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

        return new CompatibilityPayloadFile(path, relativePath, new FileInfo(path).Length);
    }

    private static bool IsSymbolFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);

        if (relativePath.Contains(".dSYM/", StringComparison.OrdinalIgnoreCase))
            return true;

        return SymbolExtensions.Contains(Path.GetExtension(fileName));
    }

    private static IReadOnlyList<CompatibilityBannedPayloadFinding> FindBannedPayloads(
        CompatibilityTargetDefinition target,
        CompatibilityPayloadFile file)
    {
        var fileName = Path.GetFileName(file.RelativePath);
        var payloadFileName = StripCompressionSuffix(fileName);
        var findings = new List<CompatibilityBannedPayloadFinding>();

        if (string.Equals(payloadFileName, "Microsoft.CodeAnalysis.dll", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Microsoft.CodeAnalysis.dll",
                file.RelativePath,
                file.SizeBytes));
        }

        if (string.Equals(payloadFileName, "Microsoft.CodeAnalysis.CSharp.dll", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Microsoft.CodeAnalysis.CSharp.dll",
                file.RelativePath,
                file.SizeBytes));
        }

        if (payloadFileName.StartsWith("Microsoft.CodeAnalysis.", StringComparison.OrdinalIgnoreCase) &&
            payloadFileName.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Roslyn satellite resource payload",
                file.RelativePath,
                file.SizeBytes));
        }

        if (payloadFileName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) &&
            payloadFileName.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                "Microsoft.CodeAnalysis*.wasm",
                file.RelativePath,
                file.SizeBytes));
        }

        if (target.RuntimeGraph == CompatibilityRuntimeGraph.Memory)
            AddMemoryProviderFindings(findings, file);

        return findings;
    }

    private static string StripCompressionSuffix(string fileName) =>
        fileName.EndsWith(".br", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^3]
            : fileName;

    private static void AddMemoryProviderFindings(
        List<CompatibilityBannedPayloadFinding> findings,
        CompatibilityPayloadFile file)
    {
        var locations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in MemoryProviderTokens)
        {
            if (file.RelativePath.Contains(token, StringComparison.OrdinalIgnoreCase))
                locations[token] = "path";
        }

        var contentTokens = MemoryProviderTokens
            .Where(token => !locations.ContainsKey(token))
            .ToArray();
        if (contentTokens.Length > 0)
        {
            foreach (var token in FindContentTokens(file.FullPath, contentTokens))
                locations[token] = "content";
        }

        foreach (var token in MemoryProviderTokens.Where(locations.ContainsKey))
        {
            findings.Add(new CompatibilityBannedPayloadFinding(
                $"Memory provider-free boundary ({token}, {locations[token]})",
                file.RelativePath,
                file.SizeBytes));
        }
    }

    private static IReadOnlySet<string> FindContentTokens(
        string path,
        IReadOnlyList<string> tokens)
    {
        var requested = tokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var patterns = MemoryProviderPatterns
            .Where(pattern => requested.Contains(pattern.Token))
            .ToArray();
        var longestToken = patterns.Max(static pattern => pattern.Bytes.Length);
        var buffer = new byte[64 * 1024 + longestToken - 1];
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retained = 0;

        using var file = File.OpenRead(path);
        Stream stream = file;
        try
        {
            if (path.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
                stream = new BrotliStream(file, CompressionMode.Decompress);
            else if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                stream = new GZipStream(file, CompressionMode.Decompress);

            return FindContentTokens(stream, tokens, patterns, buffer, matches, retained);
        }
        catch (InvalidDataException)
        {
            return matches;
        }
        finally
        {
            if (!ReferenceEquals(stream, file))
                stream.Dispose();
        }
    }

    private static IReadOnlySet<string> FindContentTokens(
        Stream stream,
        IReadOnlyList<string> tokens,
        IReadOnlyList<EncodedToken> patterns,
        byte[] buffer,
        HashSet<string> matches,
        int retained)
    {
        var longestToken = patterns.Max(static pattern => pattern.Bytes.Length);
        while (true)
        {
            var read = stream.Read(buffer, retained, buffer.Length - retained);
            var available = retained + read;
            foreach (var pattern in patterns)
            {
                if (!matches.Contains(pattern.Token) &&
                    buffer.AsSpan(0, available).IndexOf(pattern.Bytes) >= 0)
                {
                    matches.Add(pattern.Token);
                }
            }

            if (matches.Count == tokens.Count)
                return matches;

            if (read == 0)
                return matches;

            retained = Math.Min(longestToken - 1, available);
            buffer.AsSpan(available - retained, retained).CopyTo(buffer);
        }
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
        string FullPath,
        string RelativePath,
        long SizeBytes);

    private sealed record EncodedToken(string Token, byte[] Bytes);
}

public sealed record CompatibilityPayloadInspectionResult(
    CompatibilityPayloadSizeSummary Payload,
    IReadOnlyList<CompatibilityLargestFile> LargestFiles,
    IReadOnlyList<CompatibilityBannedPayloadFinding> BannedPayloads,
    CompatibilityCompressedAssetSummary BrotliAssets,
    CompatibilityCompressedAssetSummary GzipAssets,
    IReadOnlyList<CompatibilityThresholdFinding> ThresholdWarnings);
