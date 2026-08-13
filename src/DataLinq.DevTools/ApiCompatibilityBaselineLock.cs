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
    public const string SchemaVersion = "v0.9.api-package-baseline-lock.v2";

    public static ApiCompatibilityBaselineLockReport Load(
        string path,
        string expectedVersion,
        IReadOnlyCollection<string> expectedPackageIds,
        IReadOnlyCollection<string>? expectedDispositionPackageIds = null)
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

        var expectedIds = expectedPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dispositionPackageIds = (expectedDispositionPackageIds ?? expectedPackageIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in dispositionPackageIds.Where(id => !expectedIds.Contains(id)))
            throw new ArgumentException($"Disposition package '{id}' is not in the expected baseline package set.", nameof(expectedDispositionPackageIds));
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

        foreach (var id in expectedIds.Where(id => !hashes.ContainsKey(id)))
            issues.Add($"Locked package set is missing '{id}'.");
        foreach (var id in hashes.Keys.Where(id => !expectedIds.Contains(id)))
            issues.Add($"Locked package set contains unexpected package '{id}'.");

        var dispositions = new List<ApiCompatibilityInheritedFrameworkDisposition>();
        var dispositionIdentities = new HashSet<DispositionIdentity>();
        if (document.InheritedFrameworkDivergences is null)
        {
            issues.Add("inheritedFrameworkDivergences is required; use an empty array when none are approved.");
        }
        else
        {
            foreach (var disposition in document.InheritedFrameworkDivergences)
            {
                if (disposition is null)
                {
                    issues.Add("Every inheritedFrameworkDivergences entry must be an object.");
                    continue;
                }

                var entryIssues = new List<string>();
                RequireNonblank(disposition.PackageId, "inheritedFrameworkDivergences.packageId", entryIssues);
                RequireDiagnosticId(disposition.DiagnosticId, "inheritedFrameworkDivergences.diagnosticId", entryIssues);
                RequireNonblank(disposition.Target, "inheritedFrameworkDivergences.target", entryIssues);
                RequirePackageAssetPath(disposition.Left, "inheritedFrameworkDivergences.left", entryIssues);
                RequirePackageAssetPath(disposition.Right, "inheritedFrameworkDivergences.right", entryIssues);
                RequireNonblank(disposition.Rationale, "inheritedFrameworkDivergences.rationale", entryIssues);
                if (disposition.PackageId is not null && !dispositionPackageIds.Contains(disposition.PackageId))
                    entryIssues.Add($"Disposition package '{disposition.PackageId}' is not eligible for inherited package-framework dispositions.");
                if (entryIssues.Count > 0)
                {
                    foreach (var issue in entryIssues)
                        issues.Add(issue);
                    continue;
                }

                var normalized = new ApiCompatibilityInheritedFrameworkDisposition(
                    disposition.PackageId!,
                    disposition.DiagnosticId!,
                    disposition.Target!,
                    disposition.Left!,
                    disposition.Right!,
                    disposition.Rationale!);
                if (!dispositionIdentities.Add(DispositionIdentity.From(normalized)))
                {
                    issues.Add(
                        $"Inherited framework disposition '{normalized.PackageId}' / '{normalized.DiagnosticId}' / '{normalized.Target}' / '{normalized.Left}' / '{normalized.Right}' is duplicated.");
                    continue;
                }

                dispositions.Add(normalized);
            }
        }

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
            Array.AsReadOnly(dispositions.ToArray()),
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
        else if (value.Any(char.IsControl))
            issues.Add($"{field} must not contain control characters.");
    }

    private static void RequireGitCommit(string? value, string field, ICollection<string> issues)
    {
        if (value is null || value.Length != 40 || !value.All(Uri.IsHexDigit))
            issues.Add($"{field} must be an exact 40-character hexadecimal Git commit.");
    }

    private static bool IsSha256(string? value) =>
        value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void RequireDiagnosticId(string? value, string field, ICollection<string> issues)
    {
        if (value is null || value.Length != 6 ||
            value[0] != 'C' || value[1] != 'P' ||
            !value.Skip(2).All(char.IsAsciiDigit))
        {
            issues.Add($"{field} must be an exact ApiCompat CP#### diagnostic id.");
        }
    }

    private static void RequirePackageAssetPath(string? value, string field, ICollection<string> issues)
    {
        RequireNonblank(value, field, issues);
        if (value is null)
            return;
        if (value.Contains('\\') ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            !value.StartsWith("lib/", StringComparison.Ordinal) ||
            value.Split('/').Any(static segment => segment is "" or "." or ".."))
        {
            issues.Add($"{field} must be a safe forward-slash lib package asset path.");
        }
    }

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
        IReadOnlyList<BaselineLockPackage?>? Packages,
        IReadOnlyList<BaselineLockDisposition?>? InheritedFrameworkDivergences);

    private sealed record BaselineLockPackage(string? Id, string? Sha256);

    private sealed record BaselineLockDisposition(
        string? PackageId,
        string? DiagnosticId,
        string? Target,
        string? Left,
        string? Right,
        string? Rationale);

    private readonly record struct DispositionIdentity(
        string PackageId,
        string DiagnosticId,
        string Target,
        string Left,
        string Right)
    {
        public static DispositionIdentity From(ApiCompatibilityInheritedFrameworkDisposition disposition) =>
            new(
                disposition.PackageId.ToUpperInvariant(),
                disposition.DiagnosticId,
                disposition.Target,
                disposition.Left,
                disposition.Right);
    }
}
