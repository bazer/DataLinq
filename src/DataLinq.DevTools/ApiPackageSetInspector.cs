using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public sealed record ApiPackageSetInspectionOptions(
    string PackageDirectory,
    string ExpectedVersion,
    IReadOnlyCollection<string> ExpectedPackageIds,
    IReadOnlyDictionary<string, string>? LockedSha256ByPackageId = null,
    string? ExpectedRepositoryCommit = null,
    string? ExpectedRepositoryUrl = null);

public sealed record ApiPackageSetInspection(
    string PackageDirectory,
    string Version,
    string RepositoryUrl,
    string RepositoryCommit,
    IReadOnlyList<ApiPackageArchiveInspection> Packages);

public sealed record ApiPackageArchiveInspection(
    string Id,
    string Version,
    string PackagePath,
    long SizeBytes,
    string Sha256,
    string RepositoryUrl,
    string RepositoryCommit,
    IReadOnlyList<ApiPackagePrimaryAsset> PrimaryAssets);

public sealed record ApiPackagePrimaryAsset(
    string TargetFramework,
    string ArchivePath,
    long SizeBytes);

/// <summary>
/// Inspects an explicit, already acquired package set for use as API-comparison input.
/// The inspector never discovers packages in caches and never performs network access.
/// </summary>
public static class ApiPackageSetInspector
{
    private const long MaximumPackageBytes = PackageInspectionPolicy.MaximumPackageArchiveBytes;
    private const int MaximumPackageArchives = 32;
    private const int MaximumArchiveEntries = 4096;
    private const long MaximumArchiveEntryBytes = 128L * 1024 * 1024;
    private const long MaximumArchiveUncompressedBytes = 1024L * 1024 * 1024;
    private const int MaximumArchivePathCharacters = 1024;
    private const int MaximumNuspecBytes = 1024 * 1024;
    private const int MaximumPrimaryAssetBytes = PackageInspectionPolicy.MaximumPrimaryManagedAssetBytes;

    private const string NuspecNamespace2012 = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd";
    private const string NuspecNamespace2013 = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    private static readonly HashSet<string> SupportedNuspecNamespaceUris = new(StringComparer.Ordinal)
    {
        NuspecNamespace2012,
        NuspecNamespace2013
    };

    private static readonly IReadOnlyList<string> ExpectedTargetFrameworks =
        PackageInspectionPolicy.PublicTargetFrameworks;

    private static readonly HashSet<string> LibraryPackageIds = PackageInspectionPolicy.PublicPackageIds
        .Where(static id => !id.Equals(PackageInspectionPolicy.CliPackageId, StringComparison.Ordinal))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private const string CliPackageId = PackageInspectionPolicy.CliPackageId;

    public static ApiPackageSetInspection Inspect(ApiPackageSetInspectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PackageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExpectedVersion);
        ArgumentNullException.ThrowIfNull(options.ExpectedPackageIds);

        if (!options.ExpectedVersion.Equals(options.ExpectedVersion.Trim(), StringComparison.Ordinal) ||
            !IsValidPackageVersion(options.ExpectedVersion))
        {
            throw new ArgumentException(
                $"Expected version '{options.ExpectedVersion}' is not a valid exact package version.",
                nameof(options));
        }

        var expectedPackageIds = ValidateExpectedPackageIds(options.ExpectedPackageIds);
        var lockedHashes = ValidateLockedHashes(options.LockedSha256ByPackageId, expectedPackageIds);
        ValidateOptionalExactValue(options.ExpectedRepositoryCommit, nameof(options.ExpectedRepositoryCommit));
        var expectedRepositoryUrl = options.ExpectedRepositoryUrl is null
            ? null
            : ValidateCanonicalRepositoryUrl(options.ExpectedRepositoryUrl, nameof(options.ExpectedRepositoryUrl));

        var canonicalDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.PackageDirectory));
        RejectReparsePointTraversal(canonicalDirectory, "package directory");
        if (!Directory.Exists(canonicalDirectory))
            throw new DirectoryNotFoundException($"API package directory '{canonicalDirectory}' does not exist.");

        var packagePaths = Directory.EnumerateFiles(canonicalDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(static path =>
                Path.GetFileName(path).EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(path).EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => Path.GetFileName(path), StringComparer.Ordinal)
            .Take(MaximumPackageArchives + 1)
            .ToArray();
        if (packagePaths.Length > MaximumPackageArchives)
        {
            throw new InvalidDataException(
                $"API package directory '{canonicalDirectory}' contains more than the {MaximumPackageArchives.ToString(CultureInfo.InvariantCulture)} archive inspection limit.");
        }

        var inspectedPackages = packagePaths
            .Select(InspectPackage)
            .ToArray();
        var issues = inspectedPackages
            .SelectMany(static package => package.Issues)
            .ToList();

        ValidatePackageSet(
            inspectedPackages,
            expectedPackageIds,
            options.ExpectedVersion,
            lockedHashes,
            options.ExpectedRepositoryCommit,
            expectedRepositoryUrl,
            issues);

        if (issues.Count > 0)
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));

        var packages = inspectedPackages
            .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static package => package.Id, StringComparer.Ordinal)
            .Select(static package => new ApiPackageArchiveInspection(
                package.Id,
                package.Version,
                package.PackagePath,
                package.SizeBytes,
                package.Sha256,
                package.RepositoryUrl!,
                package.RepositoryCommit!,
                Array.AsReadOnly(package.PrimaryAssets.ToArray())))
            .ToArray();

        return new ApiPackageSetInspection(
            canonicalDirectory,
            options.ExpectedVersion,
            packages[0].RepositoryUrl,
            packages[0].RepositoryCommit,
            Array.AsReadOnly(packages));
    }

    private static InspectedPackage InspectPackage(string packagePath)
    {
        var canonicalPath = Path.GetFullPath(packagePath);
        RejectReparsePointTraversal(canonicalPath, "package archive");

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
            if (sizeBytes > MaximumPackageBytes)
            {
                throw new InvalidDataException(
                    $"Package archive exceeds the {MaximumPackageBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var pathIssues = new List<string>();
            var entries = NormalizeArchiveEntries(archive, pathIssues);
            var nuspecEntries = entries
                .Where(static entry =>
                    !entry.IsDirectory &&
                    entry.NormalizedPath.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (nuspecEntries.Length != 1)
            {
                pathIssues.Add(
                    $"Package '{canonicalPath}' contains {nuspecEntries.Length.ToString(CultureInfo.InvariantCulture)} nuspec entries; expected exactly one.");
                throw new InvalidDataException(string.Join(Environment.NewLine, pathIssues));
            }

            var nuspecEntry = nuspecEntries[0];
            if (nuspecEntry.Entry.Length > MaximumNuspecBytes)
            {
                throw new InvalidDataException(
                    $"Nuspec '{nuspecEntry.NormalizedPath}' exceeds the {MaximumNuspecBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
            }

            var metadata = ReadNuspec(nuspecEntry.Entry);
            var issues = new List<string>(pathIssues);
            var expectedFileName = $"{metadata.Id}.{metadata.Version}.nupkg";
            var actualFileName = Path.GetFileName(canonicalPath);
            if (!actualFileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"Package filename '{actualFileName}' does not match nuspec identity '{metadata.Id}' and version '{metadata.Version}'; expected '{expectedFileName}'.");
            }

            var expectedNuspecPath = $"{metadata.Id}.nuspec";
            if (!nuspecEntry.NormalizedPath.Equals(expectedNuspecPath, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Package '{actualFileName}' has nuspec path '{nuspecEntry.NormalizedPath}'; expected exact path '{expectedNuspecPath}'.");
            }

            if (string.IsNullOrWhiteSpace(metadata.RepositoryUrl))
                issues.Add($"Package '{metadata.Id}' is missing its nuspec repository URL.");
            if (string.IsNullOrWhiteSpace(metadata.RepositoryCommit))
                issues.Add($"Package '{metadata.Id}' is missing its nuspec repository commit.");

            var primaryAssets = IsSupportedPackageId(metadata.Id)
                ? InspectPrimaryAssets(metadata.Id, entries, issues)
                : [];

            return new InspectedPackage(
                metadata.Id,
                metadata.Version,
                canonicalPath,
                sizeBytes,
                sha256,
                metadata.RepositoryUrl,
                metadata.RepositoryCommit,
                primaryAssets,
                issues);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or XmlException)
        {
            throw new InvalidDataException(
                $"API package '{canonicalPath}' is malformed: {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<NormalizedArchiveEntry> NormalizeArchiveEntries(
        ZipArchive archive,
        ICollection<string> issues)
    {
        if (archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException(
                $"Archive contains {archive.Entries.Count.ToString(CultureInfo.InvariantCulture)} entries; the inspection limit is {MaximumArchiveEntries.ToString(CultureInfo.InvariantCulture)}.");
        }

        var entries = new List<NormalizedArchiveEntry>();
        long totalUncompressedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumArchiveEntryBytes)
            {
                issues.Add(
                    $"Archive entry '{entry.FullName}' has uncompressed length {entry.Length.ToString(CultureInfo.InvariantCulture)} bytes; the per-entry inspection limit is {MaximumArchiveEntryBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
            }

            if (entry.Length > MaximumArchiveUncompressedBytes - totalUncompressedBytes)
            {
                throw new InvalidDataException(
                    $"Archive exceeds the {MaximumArchiveUncompressedBytes.ToString(CultureInfo.InvariantCulture)} byte total uncompressed inspection limit.");
            }

            totalUncompressedBytes += entry.Length;
            if (!TryNormalizeArchivePath(entry.FullName, out var normalizedPath, out var reason))
            {
                issues.Add($"Archive entry path '{entry.FullName}' is invalid: {reason}.");
                continue;
            }

            entries.Add(new NormalizedArchiveEntry(entry, normalizedPath, entry.Name.Length == 0));
        }

        foreach (var duplicate in entries
                     .GroupBy(static entry => entry.NormalizedPath, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            issues.Add(
                $"Archive contains duplicate normalized ZIP path '{duplicate.Key}' ({duplicate.Count().ToString(CultureInfo.InvariantCulture)} entries).");
        }

        return entries;
    }

    private static NuspecMetadata ReadNuspec(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumNuspecBytes
            });
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        if (root is null ||
            !root.Name.LocalName.Equals("package", StringComparison.Ordinal) ||
            !SupportedNuspecNamespaceUris.Contains(root.Name.NamespaceName))
        {
            throw new InvalidDataException(
                $"Nuspec root element must be package in a supported namespace ({string.Join(", ", SupportedNuspecNamespaceUris.Order(StringComparer.Ordinal))}).");
        }

        var nuspecNamespace = root.Name.Namespace;
        var metadata = SingleNuspecElement(root, "metadata", nuspecNamespace);
        var id = RequiredValue(metadata, "id", nuspecNamespace);
        var version = RequiredValue(metadata, "version", nuspecNamespace);
        if (!IsValidPackageVersion(version))
            throw new InvalidDataException($"Nuspec version '{version}' is malformed.");

        var repositoryElements = NuspecElements(metadata, "repository", nuspecNamespace);
        if (repositoryElements.Length > 1)
        {
            throw new InvalidDataException(
                $"Expected at most one repository element, found {repositoryElements.Length.ToString(CultureInfo.InvariantCulture)}.");
        }

        var repository = repositoryElements.SingleOrDefault();
        if (repository is not null)
        {
            if (repository.Nodes().Any(static node =>
                    node is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
            {
                throw new InvalidDataException("Nuspec repository element must not contain child content.");
            }

            var unsupportedAttributes = repository.Attributes()
                .Where(static attribute =>
                    !attribute.IsNamespaceDeclaration &&
                    (attribute.Name.Namespace != XNamespace.None ||
                     attribute.Name.LocalName is not ("type" or "url" or "branch" or "commit")))
                .Select(static attribute => attribute.Name.ToString())
                .ToArray();
            if (unsupportedAttributes.Length > 0)
            {
                throw new InvalidDataException(
                    $"Nuspec repository element contains unsupported attributes: {string.Join(", ", unsupportedAttributes)}.");
            }

            var repositoryType = RequiredAttributeValue(repository, "type");
            if (!repositoryType.Equals("git", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Nuspec repository type must be exact value 'git', found '{repositoryType}'.");
            }
        }

        var repositoryUrl = NormalizeOptionalAttributeValue(repository, "url");
        if (repositoryUrl is not null)
        {
            try
            {
                repositoryUrl = ValidateCanonicalRepositoryUrl(repositoryUrl, "nuspec repository URL");
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(exception.Message, exception);
            }
        }

        return new NuspecMetadata(
            id,
            version,
            repositoryUrl,
            NormalizeOptionalAttributeValue(repository, "commit"));
    }

    private static IReadOnlyList<ApiPackagePrimaryAsset> InspectPrimaryAssets(
        string packageId,
        IReadOnlyList<NormalizedArchiveEntry> entries,
        ICollection<string> issues)
    {
        var canonicalAssemblyName = packageId.Equals(CliPackageId, StringComparison.OrdinalIgnoreCase)
            ? CliPackageId
            : CanonicalLibraryPackageId(packageId);
        var allowedPrimaryPaths = ExpectedTargetFrameworks
            .SelectMany(targetFramework => GetAllowedPrimaryAssetPaths(packageId, targetFramework))
            .ToHashSet(StringComparer.Ordinal);
        var primaryAssets = new List<ApiPackagePrimaryAsset>();

        if (!packageId.Equals(CliPackageId, StringComparison.OrdinalIgnoreCase))
            ValidateReferenceAssetGroups(packageId, entries, issues);

        foreach (var targetFramework in ExpectedTargetFrameworks)
        {
            var expectedPath = GetSelectedPrimaryAssetPath(packageId, targetFramework, entries);
            var matches = entries
                .Where(entry =>
                    !entry.IsDirectory &&
                    entry.NormalizedPath.Equals(expectedPath, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0)
            {
                if (expectedPath.StartsWith("ref/", StringComparison.Ordinal))
                {
                    issues.Add(
                        $"Package '{packageId}' has a '{targetFramework}' reference-asset group that takes NuGet compile precedence, but is missing primary managed asset '{expectedPath}'.");
                }
                else
                {
                    issues.Add(
                        $"Package '{packageId}' is missing primary managed asset '{expectedPath}'.");
                }
            }
            else if (matches.Length > 1)
            {
                issues.Add(
                    $"Package '{packageId}' contains {matches.Length.ToString(CultureInfo.InvariantCulture)} copies of primary managed asset '{expectedPath}'; expected exactly one.");
            }
            else
            {
                ValidateManagedAssembly(
                    packageId,
                    expectedPath,
                    matches[0].Entry,
                    canonicalAssemblyName,
                    issues);
                primaryAssets.Add(new ApiPackagePrimaryAsset(
                    targetFramework,
                    expectedPath,
                    matches[0].Entry.Length));
            }
        }

        foreach (var unexpected in entries.Where(entry =>
                     !entry.IsDirectory &&
                     IsPotentialPrimaryAsset(packageId, entry.NormalizedPath) &&
                     !allowedPrimaryPaths.Contains(entry.NormalizedPath)))
        {
            issues.Add(
                $"Package '{packageId}' contains unexpected primary managed asset '{unexpected.NormalizedPath}'; exact TFMs are {string.Join(", ", ExpectedTargetFrameworks)}.");
        }

        return primaryAssets;
    }

    private static string GetSelectedPrimaryAssetPath(
        string packageId,
        string targetFramework,
        IReadOnlyList<NormalizedArchiveEntry> entries)
    {
        if (packageId.Equals(CliPackageId, StringComparison.OrdinalIgnoreCase))
            return $"tools/{targetFramework}/any/{CliPackageId}.dll";

        var hasReferenceGroup = entries.Any(entry =>
            !entry.IsDirectory && IsInAssetGroup(entry.NormalizedPath, "ref", targetFramework));
        var root = hasReferenceGroup ? "ref" : "lib";
        return $"{root}/{targetFramework}/{CanonicalLibraryPackageId(packageId)}.dll";
    }

    private static IEnumerable<string> GetAllowedPrimaryAssetPaths(string packageId, string targetFramework)
    {
        if (packageId.Equals(CliPackageId, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"tools/{targetFramework}/any/{CliPackageId}.dll";
            yield break;
        }

        var assemblyName = CanonicalLibraryPackageId(packageId);
        yield return $"ref/{targetFramework}/{assemblyName}.dll";
        yield return $"lib/{targetFramework}/{assemblyName}.dll";
    }

    private static void ValidateReferenceAssetGroups(
        string packageId,
        IReadOnlyList<NormalizedArchiveEntry> entries,
        ICollection<string> issues)
    {
        foreach (var entry in entries.Where(static entry =>
                     !entry.IsDirectory &&
                     entry.NormalizedPath.StartsWith("ref/", StringComparison.OrdinalIgnoreCase)))
        {
            var segments = entry.NormalizedPath.Split('/');
            if (segments.Length < 3 || !segments[0].Equals("ref", StringComparison.Ordinal))
            {
                issues.Add(
                    $"Package '{packageId}' contains reference asset '{entry.NormalizedPath}' outside an exact supported ref/<tfm>/ group; compile-asset precedence cannot be established safely.");
                continue;
            }

            if (!ExpectedTargetFrameworks.Contains(segments[1], StringComparer.Ordinal))
            {
                issues.Add(
                    $"Package '{packageId}' contains unsupported reference-asset group 'ref/{segments[1]}'; exact ref groups are {string.Join(", ", ExpectedTargetFrameworks)} so NuGet compile fallback is unambiguous.");
            }
        }
    }

    private static bool IsInAssetGroup(string path, string root, string targetFramework)
    {
        var segments = path.Split('/');
        return segments.Length >= 3 &&
               segments[0].Equals(root, StringComparison.Ordinal) &&
               segments[1].Equals(targetFramework, StringComparison.Ordinal);
    }

    private static void ValidateManagedAssembly(
        string packageId,
        string archivePath,
        ZipArchiveEntry entry,
        string expectedAssemblyName,
        ICollection<string> issues)
    {
        if (entry.Length <= 0 || entry.Length > MaximumPrimaryAssetBytes)
        {
            issues.Add(
                $"Package '{packageId}' primary managed asset '{archivePath}' has length {entry.Length.ToString(CultureInfo.InvariantCulture)} bytes; expected 1 to {MaximumPrimaryAssetBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
            return;
        }

        try
        {
            using var content = new MemoryStream(capacity: checked((int)entry.Length));
            using (var entryStream = entry.Open())
                entryStream.CopyTo(content);
            if (content.Length != entry.Length)
            {
                issues.Add(
                    $"Package '{packageId}' primary managed asset '{archivePath}' decompressed to {content.Length.ToString(CultureInfo.InvariantCulture)} bytes, but the ZIP directory declared {entry.Length.ToString(CultureInfo.InvariantCulture)} bytes.");
                return;
            }

            content.Position = 0;
            using var peReader = new PEReader(content, PEStreamOptions.LeaveOpen);
            if (peReader.PEHeaders.CorHeader is null || !peReader.HasMetadata)
            {
                issues.Add(
                    $"Package '{packageId}' primary managed asset '{archivePath}' is not a managed PE assembly.");
                return;
            }

            var metadataReader = peReader.GetMetadataReader();
            if (!metadataReader.IsAssembly)
            {
                issues.Add(
                    $"Package '{packageId}' primary managed asset '{archivePath}' contains managed metadata but is not an assembly.");
                return;
            }

            var actualAssemblyName = metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name);
            if (!actualAssemblyName.Equals(expectedAssemblyName, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Package '{packageId}' primary managed asset '{archivePath}' has assembly identity '{actualAssemblyName}', expected exact simple name '{expectedAssemblyName}'.");
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or BadImageFormatException or IOException or InvalidDataException or InvalidOperationException)
        {
            issues.Add(
                $"Package '{packageId}' primary managed asset '{archivePath}' is not a valid managed PE assembly: {exception.Message}");
        }
    }

    private static void ValidatePackageSet(
        IReadOnlyList<InspectedPackage> packages,
        IReadOnlyList<string> expectedPackageIds,
        string expectedVersion,
        IReadOnlyDictionary<string, string>? lockedHashes,
        string? expectedRepositoryCommit,
        string? expectedRepositoryUrl,
        ICollection<string> issues)
    {
        var expectedIds = expectedPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages.Where(package => !expectedIds.Contains(package.Id)))
            issues.Add($"Unexpected API package id '{package.Id}' in '{package.PackagePath}'.");

        foreach (var expectedId in expectedPackageIds)
        {
            var matches = packages
                .Where(package => package.Id.Equals(expectedId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
                issues.Add($"Missing required API package '{expectedId}'.");
            else if (matches.Length > 1)
            {
                issues.Add(
                    $"Duplicate API package '{expectedId}': found {matches.Length.ToString(CultureInfo.InvariantCulture)} top-level nupkg files.");
            }
        }

        foreach (var package in packages)
        {
            if (!package.Version.Equals(expectedVersion, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Package '{package.Id}' has version '{package.Version}', expected exact version '{expectedVersion}'.");
            }

            if (lockedHashes is not null &&
                lockedHashes.TryGetValue(package.Id, out var lockedHash) &&
                !package.Sha256.Equals(lockedHash, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"Package '{package.Id}' has SHA-256 '{package.Sha256}', expected locked SHA-256 '{lockedHash.ToLowerInvariant()}'.");
            }

            if (expectedRepositoryCommit is not null &&
                !string.IsNullOrWhiteSpace(package.RepositoryCommit) &&
                !package.RepositoryCommit.Equals(expectedRepositoryCommit, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"Package '{package.Id}' has repository commit '{package.RepositoryCommit}', expected '{expectedRepositoryCommit}'.");
            }

            if (expectedRepositoryUrl is not null &&
                !string.IsNullOrWhiteSpace(package.RepositoryUrl) &&
                !package.RepositoryUrl.Equals(expectedRepositoryUrl, StringComparison.Ordinal))
            {
                issues.Add(
                    $"Package '{package.Id}' has repository URL '{package.RepositoryUrl}', expected '{expectedRepositoryUrl}'.");
            }
        }

        var repositoryUrls = packages
            .Select(static package => package.RepositoryUrl)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (repositoryUrls.Length > 1)
        {
            issues.Add(
                $"API packages identify {repositoryUrls.Length.ToString(CultureInfo.InvariantCulture)} different repository URLs; expected one common URL.");
        }

        var repositoryCommits = packages
            .Select(static package => package.RepositoryCommit)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (repositoryCommits.Length > 1)
        {
            issues.Add(
                $"API packages identify {repositoryCommits.Length.ToString(CultureInfo.InvariantCulture)} different repository commits; expected one common commit.");
        }
    }

    private static IReadOnlyList<string> ValidateExpectedPackageIds(IReadOnlyCollection<string> packageIds)
    {
        if (packageIds.Count == 0)
            throw new ArgumentException("At least one expected API package id is required.", nameof(packageIds));

        var issues = new List<string>();
        foreach (var packageId in packageIds)
        {
            if (string.IsNullOrWhiteSpace(packageId) || !packageId.Equals(packageId.Trim(), StringComparison.Ordinal))
                issues.Add("Expected API package ids must not be blank or contain surrounding whitespace.");
            else if (!IsSupportedPackageId(packageId))
                issues.Add($"Expected API package id '{packageId}' has no primary-asset policy.");
        }

        foreach (var duplicate in packageIds
                     .Where(static id => !string.IsNullOrWhiteSpace(id))
                     .GroupBy(static id => id, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            issues.Add($"Expected API package id '{duplicate.Key}' is duplicated.");
        }

        if (issues.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, issues), nameof(packageIds));

        return packageIds
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string>? ValidateLockedHashes(
        IReadOnlyDictionary<string, string>? lockedHashes,
        IReadOnlyList<string> expectedPackageIds)
    {
        if (lockedHashes is null)
            return null;

        var issues = new List<string>();
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in lockedHashes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !pair.Key.Equals(pair.Key.Trim(), StringComparison.Ordinal))
            {
                issues.Add("Locked SHA-256 package ids must not be blank or contain surrounding whitespace.");
                continue;
            }

            if (!IsSha256(pair.Value))
            {
                issues.Add($"Locked SHA-256 for package '{pair.Key}' is not exactly 64 hexadecimal characters.");
                continue;
            }

            if (!normalized.TryAdd(pair.Key, pair.Value.ToLowerInvariant()))
                issues.Add($"Locked SHA-256 package id '{pair.Key}' is duplicated case-insensitively.");
        }

        var expectedIds = expectedPackageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedId in expectedPackageIds.Where(id => !normalized.ContainsKey(id)))
            issues.Add($"Locked SHA-256 map is missing expected package '{expectedId}'.");
        foreach (var unexpectedId in normalized.Keys.Where(id => !expectedIds.Contains(id)))
            issues.Add($"Locked SHA-256 map contains unexpected package '{unexpectedId}'.");

        if (issues.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, issues), nameof(lockedHashes));

        return normalized;
    }

    private static bool TryNormalizeArchivePath(
        string archivePath,
        out string normalizedPath,
        out string reason)
    {
        normalizedPath = string.Empty;
        reason = string.Empty;
        if (string.IsNullOrEmpty(archivePath))
        {
            reason = "the path is empty";
            return false;
        }

        if (archivePath.Length > MaximumArchivePathCharacters)
        {
            reason = $"the path exceeds the {MaximumArchivePathCharacters.ToString(CultureInfo.InvariantCulture)} character inspection limit";
            return false;
        }

        if (archivePath.Contains('\\'))
        {
            reason = "backslash separators are not allowed";
            return false;
        }

        if (archivePath.StartsWith("/", StringComparison.Ordinal))
        {
            reason = "rooted paths are not allowed";
            return false;
        }

        if (archivePath.EndsWith("//", StringComparison.Ordinal))
        {
            reason = "empty path segments are not allowed";
            return false;
        }

        var withoutTrailingSlash = archivePath.EndsWith("/", StringComparison.Ordinal)
            ? archivePath[..^1]
            : archivePath;
        if (withoutTrailingSlash.Length == 0)
        {
            reason = "the path contains no segments";
            return false;
        }

        var segments = withoutTrailingSlash.Split('/');
        if (segments.Any(static segment => segment.Length == 0))
        {
            reason = "empty path segments are not allowed";
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                reason = "dot and parent traversal segments are not allowed";
                return false;
            }

            if (segment.Contains(':'))
            {
                reason = "colon characters are not allowed in path segments";
                return false;
            }

            if (segment.Any(char.IsControl))
            {
                reason = "control characters are not allowed";
                return false;
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                reason = "segments ending in a space or dot are not portable";
                return false;
            }
        }

        normalizedPath = string.Join('/', segments.Select(static segment => segment.Normalize(NormalizationForm.FormC)));
        return true;
    }

    private static bool IsPotentialPrimaryAsset(string packageId, string path)
    {
        var segments = path.Split('/');
        if (packageId.Equals(CliPackageId, StringComparison.OrdinalIgnoreCase))
        {
            return segments.Length == 4 &&
                   segments[0].Equals("tools", StringComparison.OrdinalIgnoreCase) &&
                   segments[2].Equals("any", StringComparison.OrdinalIgnoreCase) &&
                   segments[3].Equals($"{CliPackageId}.dll", StringComparison.OrdinalIgnoreCase);
        }

        return segments.Length == 3 &&
               (segments[0].Equals("ref", StringComparison.OrdinalIgnoreCase) ||
                segments[0].Equals("lib", StringComparison.OrdinalIgnoreCase)) &&
               segments[2].Equals($"{CanonicalLibraryPackageId(packageId)}.dll", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalLibraryPackageId(string packageId) =>
        LibraryPackageIds.Single(id => id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

    private static bool IsSupportedPackageId(string packageId) =>
        packageId.Equals(CliPackageId, StringComparison.OrdinalIgnoreCase) ||
        LibraryPackageIds.Contains(packageId);

    private static XElement[] NuspecElements(
        XElement parent,
        string localName,
        XNamespace nuspecNamespace)
    {
        var namedElements = parent.Elements()
            .Where(element => element.Name.LocalName == localName)
            .ToArray();
        var wrongNamespace = namedElements
            .Where(element => element.Name.Namespace != nuspecNamespace)
            .Select(static element => element.Name.NamespaceName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (wrongNamespace.Length > 0)
        {
            throw new InvalidDataException(
                $"Nuspec {localName} element must use its package namespace '{nuspecNamespace.NamespaceName}', not '{string.Join("', '", wrongNamespace)}'.");
        }

        return namedElements;
    }

    private static XElement SingleNuspecElement(
        XElement parent,
        string localName,
        XNamespace nuspecNamespace)
    {
        var matches = NuspecElements(parent, localName, nuspecNamespace);
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one {localName} element, found {matches.Length.ToString(CultureInfo.InvariantCulture)}.");
        }

        return matches[0];
    }

    private static string RequiredValue(
        XElement metadata,
        string localName,
        XNamespace nuspecNamespace)
    {
        var element = SingleNuspecElement(metadata, localName, nuspecNamespace);
        if (element.HasAttributes || element.Nodes().Any(static node => node is not XText))
        {
            throw new InvalidDataException(
                $"Nuspec {localName} must be a text-only scalar element without attributes.");
        }

        var rawValue = element.Value;
        var value = rawValue.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"Nuspec {localName} must not be blank.");
        if (!rawValue.Equals(value, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Nuspec {localName} must not contain surrounding whitespace.");
        }

        return value;
    }

    private static string RequiredAttributeValue(XElement element, string localName)
    {
        var attribute = element.Attribute(localName)
            ?? throw new InvalidDataException($"Nuspec {element.Name.LocalName} must define a {localName} attribute.");
        if (string.IsNullOrWhiteSpace(attribute.Value) ||
            !attribute.Value.Equals(attribute.Value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Nuspec {element.Name.LocalName} {localName} attribute must be nonblank and contain no surrounding whitespace.");
        }

        return attribute.Value;
    }

    private static string? NormalizeOptionalAttributeValue(XElement? element, string localName)
    {
        var value = (string?)element?.Attribute(localName);
        if (value is null)
            return null;
        if (string.IsNullOrWhiteSpace(value) || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Nuspec {element!.Name.LocalName} {localName} attribute must be nonblank and contain no surrounding whitespace when present.");
        }

        return value;
    }

    private static void ValidateOptionalExactValue(string? value, string parameterName)
    {
        if (value is not null &&
            (string.IsNullOrWhiteSpace(value) || !value.Equals(value.Trim(), StringComparison.Ordinal)))
        {
            throw new ArgumentException("Optional locked values must be nonblank and have no surrounding whitespace.", parameterName);
        }
    }

    private static string ValidateCanonicalRepositoryUrl(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Repository URL must be nonblank and contain no surrounding whitespace.",
                parameterName);
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath == "/")
        {
            throw new ArgumentException(
                $"Repository URL '{value}' must be an absolute HTTP(S) repository URI without user info, query, or fragment.",
                parameterName);
        }

        var canonical = uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.UriEscaped);
        if (!value.Equals(canonical, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Repository URL '{value}' is not canonical; expected exact URI '{canonical}'.",
                parameterName);
        }

        return canonical;
    }

    private static bool IsSha256(string? value) =>
        value is not null && value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

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
                    $"API {label} traverses reparse point '{current}', which is not allowed for release evidence.");
            }
        }
    }

    private sealed record NuspecMetadata(
        string Id,
        string Version,
        string? RepositoryUrl,
        string? RepositoryCommit);

    private sealed record NormalizedArchiveEntry(
        ZipArchiveEntry Entry,
        string NormalizedPath,
        bool IsDirectory);

    private sealed record InspectedPackage(
        string Id,
        string Version,
        string PackagePath,
        long SizeBytes,
        string Sha256,
        string? RepositoryUrl,
        string? RepositoryCommit,
        IReadOnlyList<ApiPackagePrimaryAsset> PrimaryAssets,
        IReadOnlyList<string> Issues);
}
