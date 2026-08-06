using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataLinq.DevTools;

internal static class ApiCompatibilityBaselineLock
{
    public const string SchemaVersion = "v0.9.api-package-baseline-lock.v1";

    public static ApiCompatibilityBaselineLockReport Load(
        string path,
        string expectedVersion,
        IReadOnlyCollection<string> expectedPackageIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedVersion);
        ArgumentNullException.ThrowIfNull(expectedPackageIds);

        var canonicalPath = Path.GetFullPath(path);
        BaselineLockDocument? document;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(canonicalPath);
            using (var json = JsonDocument.Parse(bytes))
                RejectDuplicateProperties(json.RootElement, "$", canonicalPath);
            document = JsonSerializer.Deserialize<BaselineLockDocument>(
                bytes,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = false,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
                });
        }
        catch (Exception exception) when (exception is IOException or JsonException or NotSupportedException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"API baseline lock '{canonicalPath}' is invalid: {exception.Message}",
                exception);
        }

        if (document is null)
            throw new InvalidDataException($"API baseline lock '{canonicalPath}' is empty.");

        var issues = new List<string>();
        RequireExact(document.SchemaVersion, SchemaVersion, "schemaVersion", issues);
        RequireExact(document.BaselineVersion, expectedVersion, "baselineVersion", issues);
        RequireNonblank(document.PackageSource, "packageSource", issues);
        RequireNonblank(document.RepositoryUrl, "repositoryUrl", issues);
        RequireGitCommit(document.RepositoryCommit, "repositoryCommit", issues);
        RequireNonblank(document.RepositoryTag, "repositoryTag", issues);
        RequireExact(document.RepositoryTagObjectType, "commit", "repositoryTagObjectType", issues);
        RequireNonblank(document.ProvenanceNote, "provenanceNote", issues);

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document.Packages is null)
        {
            issues.Add("packages is required.");
        }
        else
        {
            foreach (var package in document.Packages)
            {
                if (package is null || string.IsNullOrWhiteSpace(package.Id))
                {
                    issues.Add("Every packages entry requires a nonblank id.");
                    continue;
                }

                if (package.Id != package.Id.Trim())
                    issues.Add($"Package id '{package.Id}' has surrounding whitespace.");
                if (!IsSha256(package.Sha256))
                {
                    issues.Add($"Package '{package.Id}' has an invalid SHA-256 value.");
                    continue;
                }

                if (!hashes.TryAdd(package.Id, package.Sha256!.ToLowerInvariant()))
                    issues.Add($"Package '{package.Id}' is duplicated case-insensitively.");
            }
        }

        var expectedIds = expectedPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in expectedIds.Where(id => !hashes.ContainsKey(id)))
            issues.Add($"Locked package set is missing '{id}'.");
        foreach (var id in hashes.Keys.Where(id => !expectedIds.Contains(id)))
            issues.Add($"Locked package set contains unexpected package '{id}'.");

        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                $"API baseline lock '{canonicalPath}' failed validation:{Environment.NewLine}" +
                string.Join(Environment.NewLine, issues));
        }

        return new ApiCompatibilityBaselineLockReport(
            document.SchemaVersion!,
            document.BaselineVersion!,
            document.PackageSource!,
            document.RepositoryUrl!,
            document.RepositoryCommit!.ToLowerInvariant(),
            document.RepositoryTag!,
            document.RepositoryTagObjectType!,
            document.ProvenanceNote!,
            hashes,
            canonicalPath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            CanonicalTrackedPolicy: false);
    }

    private static void RequireExact(
        string? value,
        string expected,
        string field,
        ICollection<string> issues)
    {
        if (!string.Equals(value, expected, StringComparison.Ordinal))
            issues.Add($"{field} must be exact '{expected}', found '{value ?? "<missing>"}'.");
    }

    private static void RequireNonblank(string? value, string field, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            issues.Add($"{field} must be nonblank and have no surrounding whitespace.");
    }

    private static void RequireGitCommit(string? value, string field, ICollection<string> issues)
    {
        if (value is null || value.Length != 40 || !value.All(Uri.IsHexDigit))
            issues.Add($"{field} must be an exact 40-character hexadecimal Git commit.");
    }

    private static bool IsSha256(string? value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void RejectDuplicateProperties(JsonElement element, string path, string sourcePath)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"API baseline lock '{sourcePath}' contains duplicate JSON property '{property.Name}' at '{path}'.");
                }

                RejectDuplicateProperties(property.Value, $"{path}.{property.Name}", sourcePath);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{path}[{index}]", sourcePath);
                index++;
            }
        }
    }

    private sealed record BaselineLockDocument(
        string? SchemaVersion,
        string? BaselineVersion,
        string? PackageSource,
        string? RepositoryUrl,
        string? RepositoryCommit,
        string? RepositoryTag,
        string? RepositoryTagObjectType,
        string? ProvenanceNote,
        IReadOnlyList<BaselineLockPackage?>? Packages);

    private sealed record BaselineLockPackage(string? Id, string? Sha256);
}
