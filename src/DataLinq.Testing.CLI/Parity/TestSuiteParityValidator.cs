using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DataLinq.Testing.CLI;

internal static class TestSuiteParityValidator
{
    private static readonly Regex TestAttributePattern = new(@"^\s*\[(Fact|Theory|Test)\]", RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly string[] DiscoveryRoots =
    [
        Path.Combine("src", "DataLinq.Tests"),
        Path.Combine("src", "DataLinq.MySql.Tests"),
        Path.Combine("src", "DataLinq.Generators.Tests")
    ];

    public static TestSuiteParityValidationResult Validate(string repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot, "docs", "dev-plans", "testing", "test-suite-parity.json");
        if (!File.Exists(manifestPath))
        {
            return new TestSuiteParityValidationResult(
                ManifestPath: manifestPath,
                EntryCount: 0,
                DiscoveredLegacyFiles: [],
                StatusCounts: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                Errors:
                [
                    $"Parity manifest file was not found: '{manifestPath}'."
                ]);
        }

        var manifest = JsonSerializer.Deserialize<TestSuiteParityManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (manifest is null)
        {
            return new TestSuiteParityValidationResult(
                ManifestPath: manifestPath,
                EntryCount: 0,
                DiscoveredLegacyFiles: [],
                StatusCounts: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                Errors:
                [
                    $"Parity manifest could not be parsed: '{manifestPath}'."
                ]);
        }

        var discoveredLegacyFiles = DiscoverLegacyFiles(repositoryRoot);
        var entriesByLegacyFile = manifest.Entries
            .GroupBy(x => NormalizePath(x.LegacyFile), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();

        foreach (var group in entriesByLegacyFile.Where(x => x.Value.Length > 1))
            errors.Add($"Legacy file '{group.Key}' is declared more than once in the parity manifest.");

        foreach (var discoveredFile in discoveredLegacyFiles)
        {
            if (!entriesByLegacyFile.ContainsKey(discoveredFile))
                errors.Add($"Legacy test file '{discoveredFile}' is missing from the parity manifest.");
        }

        foreach (var entry in manifest.Entries)
        {
            var normalizedLegacyFile = NormalizePath(entry.LegacyFile);
            var absoluteLegacyFile = Path.Combine(repositoryRoot, normalizedLegacyFile.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(absoluteLegacyFile))
                errors.Add($"Manifest entry points at a missing legacy file: '{entry.LegacyFile}'.");

            if (!IsValidStatus(entry.Status))
                errors.Add($"Manifest entry '{entry.LegacyFile}' uses unsupported status '{entry.Status}'.");

            if (string.Equals(entry.Status, "retired", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(entry.Note))
            {
                errors.Add($"Retired manifest entry '{entry.LegacyFile}' must include a note explaining why it was retired.");
            }

            if (entry.ReplacementFiles is null || entry.ReplacementFiles.Count == 0)
            {
                errors.Add($"Manifest entry '{entry.LegacyFile}' must declare at least one replacement file.");
                continue;
            }

            foreach (var replacementFile in entry.ReplacementFiles)
            {
                var normalizedReplacementFile = NormalizePath(replacementFile);
                var absoluteReplacementFile = Path.Combine(repositoryRoot, normalizedReplacementFile.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(absoluteReplacementFile))
                {
                    errors.Add($"Manifest entry '{entry.LegacyFile}' points at a missing replacement file '{replacementFile}'.");
                    continue;
                }

                if (!ContainsTestAttributes(absoluteReplacementFile))
                    errors.Add($"Replacement file '{replacementFile}' for legacy file '{entry.LegacyFile}' does not appear to contain any test methods.");
            }
        }

        var statusCounts = manifest.Entries
            .GroupBy(x => x.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        return new TestSuiteParityValidationResult(
            ManifestPath: manifestPath,
            EntryCount: manifest.Entries.Count,
            DiscoveredLegacyFiles: discoveredLegacyFiles,
            StatusCounts: statusCounts,
            Errors: errors);
    }

    private static IReadOnlyList<string> DiscoverLegacyFiles(string repositoryRoot)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in DiscoveryRoots)
        {
            var absoluteRoot = Path.Combine(repositoryRoot, root);
            if (!Directory.Exists(absoluteRoot))
                continue;

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!ContainsTestAttributes(file))
                    continue;

                files.Add(NormalizePath(Path.GetRelativePath(repositoryRoot, file)));
            }
        }

        return files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ContainsTestAttributes(string filePath) =>
        TestAttributePattern.IsMatch(File.ReadAllText(filePath));

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/');

    private static bool IsValidStatus(string status) =>
        string.Equals(status, "migrated", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "split", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "retired", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "migrated-in-place", StringComparison.OrdinalIgnoreCase);
}

internal sealed record TestSuiteParityValidationResult(
    string ManifestPath,
    int EntryCount,
    IReadOnlyList<string> DiscoveredLegacyFiles,
    IReadOnlyDictionary<string, int> StatusCounts,
    IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}
