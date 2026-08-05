using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public enum CompatibilityDependencySource
{
    ProjectReferences,
    PackedPackages
}

public sealed record CompatibilityCandidatePackage(
    string Id,
    string Version,
    string PackagePath,
    long SizeBytes,
    string Sha256,
    string? RepositoryCommit);

public sealed record CompatibilityPackageInput(
    string PackageDirectory,
    string Version,
    string AggregateIdentity,
    string ScratchIdentity,
    IReadOnlyList<CompatibilityCandidatePackage> Packages);

public static class CompatibilityPackageInputInspector
{
    private const int MaximumNuspecBytes = 1024 * 1024;
    private const string AggregateIdentityFormat = "DataLinq compatibility package input v1";

    public static CompatibilityPackageInput Inspect(string packageDirectory, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        if (!version.Equals(version.Trim(), StringComparison.Ordinal) || !IsValidPackageVersion(version))
            throw new ArgumentException($"Package version '{version}' is not a valid exact package version.", nameof(version));

        var canonicalDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory));
        RejectReparsePointTraversal(canonicalDirectory, "package directory");

        if (!Directory.Exists(canonicalDirectory))
            throw new DirectoryNotFoundException($"Compatibility package directory '{canonicalDirectory}' does not exist.");

        var packagePaths = Directory.EnumerateFiles(canonicalDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsRuntimePackage)
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();

        var packages = packagePaths.Select(InspectPackage).ToArray();
        ValidatePackageSet(packages, version);

        var sortedPackages = packages
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Id, StringComparer.Ordinal)
            .ToArray();
        var aggregateIdentity = ComputeAggregateIdentity(canonicalDirectory, version, sortedPackages);

        return new CompatibilityPackageInput(
            canonicalDirectory,
            version,
            aggregateIdentity,
            $"pkg-{aggregateIdentity[..16]}",
            Array.AsReadOnly(sortedPackages));
    }

    private static CompatibilityCandidatePackage InspectPackage(string packagePath)
    {
        var canonicalPath = Path.GetFullPath(packagePath);
        RejectReparsePointTraversal(canonicalPath, "candidate package");

        try
        {
            using var stream = new FileStream(
                canonicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            var sizeBytes = stream.Length;
            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var nuspecEntries = archive.Entries
                .Where(static entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (nuspecEntries.Length != 1)
            {
                throw new InvalidDataException(
                    $"Expected exactly one nuspec, found {nuspecEntries.Length.ToString(CultureInfo.InvariantCulture)}.");
            }

            var nuspecEntry = nuspecEntries[0];
            if (nuspecEntry.Length > MaximumNuspecBytes)
            {
                throw new InvalidDataException(
                    $"Nuspec '{nuspecEntry.FullName}' exceeds the {MaximumNuspecBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
            }

            using var nuspecStream = nuspecEntry.Open();
            using var reader = XmlReader.Create(
                nuspecStream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumNuspecBytes
                });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Root?.Name.LocalName != "package")
                throw new InvalidDataException("Nuspec root element must be package.");
            var metadata = SingleElement(document.Root?.Elements(), "metadata");
            var id = RequiredValue(metadata, "id");
            var packageVersion = RequiredValue(metadata, "version");
            if (!IsValidPackageVersion(packageVersion))
                throw new InvalidDataException($"Nuspec version '{packageVersion}' is malformed.");

            var repositoryElements = metadata.Elements()
                .Where(static element => element.Name.LocalName == "repository")
                .ToArray();
            if (repositoryElements.Length > 1)
                throw new InvalidDataException($"Expected at most one repository element, found {repositoryElements.Length}.");
            var repositoryCommit = ((string?)repositoryElements.SingleOrDefault()?.Attribute("commit"))?.Trim();
            if (string.IsNullOrWhiteSpace(repositoryCommit))
                repositoryCommit = null;

            return new CompatibilityCandidatePackage(
                id,
                packageVersion,
                canonicalPath,
                sizeBytes,
                sha256,
                repositoryCommit);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or XmlException)
        {
            throw new InvalidDataException(
                $"Compatibility candidate package '{canonicalPath}' is malformed: {exception.Message}",
                exception);
        }
    }

    private static XElement SingleElement(IEnumerable<XElement>? elements, string localName)
    {
        var matches = elements?
            .Where(element => element.Name.LocalName == localName)
            .ToArray() ?? [];
        if (matches.Length != 1)
            throw new InvalidDataException($"Expected exactly one {localName} element, found {matches.Length}.");
        return matches[0];
    }

    private static string RequiredValue(XElement metadata, string localName)
    {
        var value = SingleElement(metadata.Elements(), localName).Value.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Nuspec {localName} must not be blank.");
        return value;
    }

    private static void ValidatePackageSet(
        IReadOnlyList<CompatibilityCandidatePackage> packages,
        string requestedVersion)
    {
        var expectedIds = PackageInspectionPolicy.PublicPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();

        foreach (var package in packages.Where(package => !expectedIds.Contains(package.Id)))
            issues.Add($"Unexpected public package id '{package.Id}' in '{package.PackagePath}'.");

        foreach (var expectedId in PackageInspectionPolicy.PublicPackageIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))
        {
            var matches = packages
                .Where(package => package.Id.Equals(expectedId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
                issues.Add($"Missing required public package '{expectedId}'.");
            else if (matches.Length > 1)
                issues.Add($"Duplicate public package '{expectedId}': found {matches.Length.ToString(CultureInfo.InvariantCulture)} runtime nupkg files.");
        }

        foreach (var package in packages.Where(package =>
                     !package.Version.Equals(requestedVersion, StringComparison.Ordinal)))
        {
            issues.Add(
                $"Package '{package.Id}' has version '{package.Version}', expected exact version '{requestedVersion}'.");
        }

        foreach (var package in packages.Where(static package =>
                     string.IsNullOrWhiteSpace(package.RepositoryCommit)))
        {
            issues.Add($"Package '{package.Id}' is missing its nuspec repository commit.");
        }

        var repositoryCommits = packages
            .Select(static package => package.RepositoryCommit)
            .Where(static commit => !string.IsNullOrWhiteSpace(commit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (repositoryCommits.Length > 1)
        {
            issues.Add(
                $"Candidate packages identify {repositoryCommits.Length.ToString(CultureInfo.InvariantCulture)} different repository commits; " +
                "package-backed compatibility evidence requires one coherent candidate commit.");
        }

        if (issues.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
    }

    private static string ComputeAggregateIdentity(
        string canonicalDirectory,
        string version,
        IReadOnlyList<CompatibilityCandidatePackage> packages)
    {
        var builder = new StringBuilder();
        AppendIdentityValue(builder, AggregateIdentityFormat);
        AppendIdentityValue(builder, canonicalDirectory);
        AppendIdentityValue(builder, version);
        foreach (var package in packages)
        {
            AppendIdentityValue(builder, package.Id);
            AppendIdentityValue(builder, package.Version);
            AppendIdentityValue(builder, package.Sha256);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void AppendIdentityValue(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');

    private static bool IsRuntimePackage(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase) &&
               !fileName.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidPackageVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || !version.Equals(version.Trim(), StringComparison.Ordinal))
            return false;

        var buildSeparator = version.IndexOf('+');
        if (buildSeparator >= 0 && version.IndexOf('+', buildSeparator + 1) >= 0)
            return false;
        var withoutBuild = buildSeparator >= 0 ? version[..buildSeparator] : version;
        var build = buildSeparator >= 0 ? version[(buildSeparator + 1)..] : null;

        var prereleaseSeparator = withoutBuild.IndexOf('-');
        var core = prereleaseSeparator >= 0 ? withoutBuild[..prereleaseSeparator] : withoutBuild;
        var prerelease = prereleaseSeparator >= 0 ? withoutBuild[(prereleaseSeparator + 1)..] : null;
        var numericParts = core.Split('.');

        return numericParts.Length is >= 1 and <= 4 &&
               numericParts.All(IsAsciiNumericComponent) &&
               (prerelease is null || IsValidLabelSequence(prerelease)) &&
               (build is null || IsValidLabelSequence(build));
    }

    private static bool IsAsciiNumericComponent(string value) =>
        value.Length > 0 &&
        value.All(static character => character is >= '0' and <= '9') &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsValidLabelSequence(string value) =>
        value.Split('.').All(static part =>
            part.Length > 0 &&
            part.All(static character =>
                character is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '-'));

    private static void RejectReparsePointTraversal(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"Could not determine the filesystem root for {label} '{fullPath}'.");
        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
                break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Compatibility {label} traverses reparse point '{current}', which is not allowed for release evidence.");
            }
        }
    }
}
