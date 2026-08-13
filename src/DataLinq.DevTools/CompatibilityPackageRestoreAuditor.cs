using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DataLinq.DevTools;

public sealed record CompatibilityPackageResolutionReport(
    string AssetsPath,
    IReadOnlyList<string> ProjectLibraries,
    IReadOnlyList<CompatibilityResolvedPackage> ResolvedPackages,
    IReadOnlyList<CompatibilityPackageResolutionFinding> Findings,
    bool Passed);

public sealed record CompatibilityResolvedPackage(
    string Id,
    string Version,
    string AssetsLibraryKey,
    string PackageCacheDirectory,
    string MetadataPath,
    string? Source,
    string CachedPackagePath,
    string CandidatePackagePath,
    string CandidateSha256,
    string? CachedSha256,
    bool ExactVersion,
    bool SourceMatchesPackageDirectory,
    bool HashMatchesCandidate,
    bool ExtractedFilesMatchArchive,
    int VerifiedExtractedFileCount);

public sealed record CompatibilityPackageResolutionFinding(
    string Code,
    string Message);

public static class CompatibilityPackageRestoreAuditor
{
    public static CompatibilityPackageResolutionReport Audit(
        CompatibilityTargetDefinition target,
        string repositoryRoot,
        string runtimeIdentifier,
        string buildScratchDirectory,
        string packagesCacheDirectory,
        string nugetConfigPath,
        CompatibilityPackageInput input)
    {
        var findings = new List<CompatibilityPackageResolutionFinding>();
        var projectLibraries = new List<string>();
        var resolvedPackages = new List<CompatibilityResolvedPackage>();
        var assetsPath = FindHostAssetsPath(target, buildScratchDirectory, findings);

        if (assetsPath is null)
        {
            var expectedPath = Path.Combine(
                buildScratchDirectory,
                "obj",
                GetHostProjectName(target),
                "project.assets.json");
            return CreateReport(expectedPath, projectLibraries, resolvedPackages, findings);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(assetsPath, Encoding.UTF8));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AddFinding(
                findings,
                "assets-file-invalid",
                $"Could not read the isolated host project assets file '{assetsPath}': {exception.Message}");
            return CreateReport(assetsPath, projectLibraries, resolvedPackages, findings);
        }

        using (document)
        {
            var root = document.RootElement;
            ValidateRestoreState(
                root,
                target,
                repositoryRoot,
                packagesCacheDirectory,
                nugetConfigPath,
                findings);

            if (!root.TryGetProperty("libraries", out var libraries) ||
                libraries.ValueKind != JsonValueKind.Object)
            {
                AddFinding(
                    findings,
                    "assets-libraries-missing",
                    "project.assets.json does not contain a libraries object.");
                return CreateReport(assetsPath, projectLibraries, resolvedPackages, findings);
            }

            var libraryEntries = ReadLibraries(libraries);
            ValidateProjectLibraries(
                target,
                repositoryRoot,
                libraryEntries,
                projectLibraries,
                findings);
            ValidatePackageLibraries(target, input, libraryEntries, findings);
            ValidateActiveTargetGraph(root, target, runtimeIdentifier, input, libraryEntries, findings);

            foreach (var expectedId in ExpectedPackageIds(target))
            {
                AuditExpectedPackage(
                    expectedId,
                    libraryEntries,
                    packagesCacheDirectory,
                    input,
                    resolvedPackages,
                    findings);
            }
        }

        return CreateReport(assetsPath, projectLibraries, resolvedPackages, findings);
    }

    private static string? FindHostAssetsPath(
        CompatibilityTargetDefinition target,
        string buildScratchDirectory,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        var hostProjectName = GetHostProjectName(target);
        var objDirectory = Path.Combine(buildScratchDirectory, "obj");
        var hostObjDirectory = Path.Combine(objDirectory, hostProjectName);
        string[] matches;

        try
        {
            matches = Directory.Exists(objDirectory)
                ? Directory.EnumerateFiles(objDirectory, "project.assets.json", SearchOption.AllDirectories)
                    .Where(path => PathsEqual(Path.GetDirectoryName(path), hostObjDirectory))
                    .Select(Path.GetFullPath)
                    .Distinct(PathComparer)
                    .ToArray()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddFinding(
                findings,
                "assets-file-enumeration-failed",
                $"Could not inspect the isolated host project assets directory '{objDirectory}': {exception.Message}");
            return null;
        }

        if (matches.Length == 1)
            return matches[0];

        AddFinding(
            findings,
            "assets-file-count",
            $"Expected exactly one '{hostProjectName}' host project assets file below '{objDirectory}', found {matches.Length}.");
        return null;
    }

    private static string GetHostProjectName(CompatibilityTargetDefinition target)
    {
        var normalizedPath = target.ProjectRelativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFileNameWithoutExtension(normalizedPath);
    }

    private static IReadOnlyList<AssetsLibrary> ReadLibraries(JsonElement libraries)
    {
        var result = new List<AssetsLibrary>();
        foreach (var library in libraries.EnumerateObject())
        {
            var separator = library.Name.LastIndexOf('/');
            var id = separator > 0 ? library.Name[..separator] : library.Name;
            var version = separator > 0 && separator < library.Name.Length - 1
                ? library.Name[(separator + 1)..]
                : "";
            var type = library.Value.TryGetProperty("type", out var typeElement) &&
                       typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
            var relativePath = library.Value.TryGetProperty("path", out var pathElement) &&
                               pathElement.ValueKind == JsonValueKind.String
                ? pathElement.GetString()
                : null;
            IReadOnlyList<string>? files = null;
            var hasValidFiles = false;
            if (library.Value.TryGetProperty("files", out var filesElement) &&
                filesElement.ValueKind == JsonValueKind.Array)
            {
                var fileElements = filesElement.EnumerateArray().ToArray();
                hasValidFiles = fileElements.All(static file => file.ValueKind == JsonValueKind.String);
                files = fileElements
                    .Select(static file => file.ValueKind == JsonValueKind.String ? file.GetString() : null)
                    .Where(static file => file is not null)
                    .Select(static file => file!)
                    .ToArray();
            }

            result.Add(new AssetsLibrary(library.Name, id, version, type, relativePath, files, hasValidFiles));
        }

        return result;
    }

    private static void ValidateRestoreState(
        JsonElement root,
        CompatibilityTargetDefinition target,
        string repositoryRoot,
        string packagesCacheDirectory,
        string nugetConfigPath,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        if (!root.TryGetProperty("packageFolders", out var packageFolders) ||
            packageFolders.ValueKind != JsonValueKind.Object)
        {
            AddFinding(
                findings,
                "assets-package-folders-missing",
                "project.assets.json does not contain packageFolders.");
        }
        else
        {
            var folders = packageFolders.EnumerateObject().Select(static property => property.Name).ToArray();
            if (folders.Length != 1 || !PathsEqual(folders[0], packagesCacheDirectory))
            {
                AddFinding(
                    findings,
                    "assets-package-folders-mismatch",
                    "project.assets.json must use exactly the isolated compatibility package cache.");
            }
        }

        if (!root.TryGetProperty("project", out var project) ||
            project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("restore", out var restore) ||
            restore.ValueKind != JsonValueKind.Object)
        {
            AddFinding(
                findings,
                "assets-restore-state-missing",
                "project.assets.json does not contain project.restore state.");
            return;
        }

        var packagesPath = restore.TryGetProperty("packagesPath", out var packagesPathElement) &&
                           packagesPathElement.ValueKind == JsonValueKind.String
            ? packagesPathElement.GetString()
            : null;
        if (packagesPath is null || !PathsEqual(packagesPath, packagesCacheDirectory))
        {
            AddFinding(
                findings,
                "assets-packages-path-mismatch",
                "project.restore.packagesPath must match the isolated compatibility package cache.");
        }

        if (!restore.TryGetProperty("configFilePaths", out var configPaths) ||
            configPaths.ValueKind != JsonValueKind.Array)
        {
            AddFinding(
                findings,
                "assets-config-paths-missing",
                "project.restore.configFilePaths is missing.");
        }
        else
        {
            var paths = configPaths.EnumerateArray()
                .Select(static path => path.ValueKind == JsonValueKind.String ? path.GetString() : null)
                .ToArray();
            if (paths.Length != 1 || paths[0] is null || !PathsEqual(paths[0]!, nugetConfigPath))
            {
                AddFinding(
                    findings,
                    "assets-config-paths-mismatch",
                    "project.restore.configFilePaths must contain only the generated compatibility NuGet.Config.");
            }
        }

        if (restore.TryGetProperty("fallbackFolders", out var fallbackFolders) &&
            (fallbackFolders.ValueKind != JsonValueKind.Array || fallbackFolders.GetArrayLength() != 0))
        {
            AddFinding(
                findings,
                "assets-fallback-folders",
                "project.restore must not contain fallback package folders.");
        }

        ValidateRestoreHostProject(restore, target, repositoryRoot, findings);
        ValidateRestoreProjectReferences(restore, target, repositoryRoot, findings);
    }

    private static void ValidateRestoreHostProject(
        JsonElement restore,
        CompatibilityTargetDefinition target,
        string repositoryRoot,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        var expectedHostProjectPath = Path.GetFullPath(Path.Combine(
            repositoryRoot,
            NormalizeRelativePath(target.ProjectRelativePath)));
        foreach (var propertyName in new[] { "projectPath", "projectUniqueName" })
        {
            var value = restore.TryGetProperty(propertyName, out var property) &&
                        property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
            if (!PathsEqual(value, expectedHostProjectPath))
            {
                AddFinding(
                    findings,
                    "assets-host-project-identity-mismatch",
                    $"project.restore.{propertyName} must identify the exact catalog host project '{expectedHostProjectPath}'.");
            }
        }

        if (!IsTrustedExistingPath(repositoryRoot, expectedHostProjectPath, out var reason))
        {
            AddFinding(
                findings,
                "assets-host-project-path-untrusted",
                $"The catalog host project path '{expectedHostProjectPath}' is not trustworthy: {reason}");
        }
    }

    private static void ValidateProjectLibraries(
        CompatibilityTargetDefinition target,
        string repositoryRoot,
        IReadOnlyList<AssetsLibrary> libraries,
        ICollection<string> projectLibraries,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        var allowedProjectId = SharedSmokeProjectId(target);
        var expectedProjectPath = SharedSmokeProjectPath(target, repositoryRoot);
        var hostProjectDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(
            repositoryRoot,
            NormalizeRelativePath(target.ProjectRelativePath))))!;
        foreach (var library in libraries.Where(static library =>
                     string.Equals(library.Type, "project", StringComparison.OrdinalIgnoreCase)))
        {
            projectLibraries.Add(library.Key);
            if (!library.Id.Equals(allowedProjectId, StringComparison.OrdinalIgnoreCase))
            {
                AddFinding(
                    findings,
                    "assets-project-library-not-allowed",
                    $"Project library '{library.Key}' is not allowed in packed-package compatibility evidence; only '{allowedProjectId}' is permitted.");
            }
            else if (!TryResolveTrustedProjectPath(
                         hostProjectDirectory,
                         library.RelativePath,
                         repositoryRoot,
                         expectedProjectPath,
                         out var reason))
            {
                AddFinding(
                    findings,
                    "assets-shared-smoke-project-path-mismatch",
                    $"Project library '{library.Key}' does not resolve to the exact tracked shared smoke project '{expectedProjectPath}': {reason}");
            }
        }

        var allowedCount = libraries.Count(library =>
            string.Equals(library.Type, "project", StringComparison.OrdinalIgnoreCase) &&
            library.Id.Equals(allowedProjectId, StringComparison.OrdinalIgnoreCase));
        if (allowedCount != 1)
        {
            AddFinding(
                findings,
                "assets-shared-smoke-project-count",
                $"Expected exactly one shared smoke project library '{allowedProjectId}', found {allowedCount}.");
        }
    }

    private static void ValidateRestoreProjectReferences(
        JsonElement restore,
        CompatibilityTargetDefinition target,
        string repositoryRoot,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        if (!restore.TryGetProperty("frameworks", out var frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
        {
            AddFinding(
                findings,
                "assets-restore-frameworks-missing",
                "project.restore.frameworks is missing.");
            return;
        }

        var frameworkMatches = frameworks.EnumerateObject()
            .Where(framework => framework.Name.Equals(target.TargetFramework, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (frameworkMatches.Length != 1 || frameworkMatches[0].Value.ValueKind != JsonValueKind.Object)
        {
            AddFinding(
                findings,
                "assets-restore-framework-count",
                $"Expected exactly one project.restore.frameworks entry for '{target.TargetFramework}', found {frameworkMatches.Length}.");
            return;
        }

        var framework = frameworkMatches[0].Value;
        if (!framework.TryGetProperty("projectReferences", out var projectReferences) ||
            projectReferences.ValueKind != JsonValueKind.Object)
        {
            AddFinding(
                findings,
                "assets-project-references-missing",
                $"project.restore.frameworks['{target.TargetFramework}'].projectReferences is missing.");
            return;
        }

        var references = projectReferences.EnumerateObject().ToArray();
        var expectedProjectPath = SharedSmokeProjectPath(target, repositoryRoot);
        if (references.Length != 1)
        {
            AddFinding(
                findings,
                "assets-project-reference-count",
                $"Expected exactly one host project reference to '{expectedProjectPath}', found {references.Length}.");
        }

        var matchingReferences = references.Where(reference =>
                PathsEqual(reference.Name, expectedProjectPath) &&
                reference.Value.ValueKind == JsonValueKind.Object &&
                reference.Value.TryGetProperty("projectPath", out var projectPath) &&
                projectPath.ValueKind == JsonValueKind.String &&
                PathsEqual(projectPath.GetString(), expectedProjectPath))
            .ToArray();
        if (matchingReferences.Length != 1)
        {
            AddFinding(
                findings,
                "assets-shared-smoke-project-reference-mismatch",
                $"The host restore graph must reference the exact tracked shared smoke project '{expectedProjectPath}' by both key and projectPath.");
            return;
        }

        if (!IsTrustedExistingPath(repositoryRoot, expectedProjectPath, out var reason))
        {
            AddFinding(
                findings,
                "assets-shared-smoke-project-reference-reparse",
                $"The tracked shared smoke project path '{expectedProjectPath}' is not trustworthy: {reason}");
        }
    }

    private static void ValidatePackageLibraries(
        CompatibilityTargetDefinition target,
        CompatibilityPackageInput input,
        IReadOnlyList<AssetsLibrary> libraries,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        var expectedIds = new HashSet<string>(ExpectedPackageIds(target), StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries.Where(static library => IsDataLinqId(library.Id)))
        {
            if (string.Equals(library.Type, "project", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase))
            {
                AddFinding(
                    findings,
                    "assets-datalinq-library-type",
                    $"DataLinq library '{library.Key}' has type '{library.Type ?? "missing"}', expected package or the graph's shared smoke project.");
                continue;
            }

            if (!expectedIds.Contains(library.Id))
            {
                AddFinding(
                    findings,
                    "assets-unexpected-datalinq-package",
                    $"Unexpected DataLinq package library '{library.Key}' was resolved.");
            }

            if (!library.Version.Equals(input.Version, StringComparison.OrdinalIgnoreCase))
            {
                AddFinding(
                    findings,
                    "package-version-mismatch",
                    $"DataLinq package library '{library.Key}' does not use exact candidate version '{input.Version}'.");
            }
        }
    }

    private static void ValidateActiveTargetGraph(
        JsonElement root,
        CompatibilityTargetDefinition target,
        string runtimeIdentifier,
        CompatibilityPackageInput input,
        IReadOnlyList<AssetsLibrary> libraries,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
        {
            AddFinding(
                findings,
                "assets-targets-missing",
                "project.assets.json does not contain a targets object.");
            return;
        }

        var activeTargetName = target.IsWebAssembly
            ? $"{target.TargetFramework}/browser-wasm"
            : target.RequiresRuntimeIdentifier
                ? $"{target.TargetFramework}/{runtimeIdentifier}"
                : target.TargetFramework;
        var activeMatches = targets.EnumerateObject()
            .Where(candidate => candidate.Name.Equals(activeTargetName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (activeMatches.Length != 1 || activeMatches[0].Value.ValueKind != JsonValueKind.Object)
        {
            AddFinding(
                findings,
                "assets-active-target-count",
                $"Expected exactly one active restore target '{activeTargetName}', found {activeMatches.Length}.");
            return;
        }

        var activeTarget = activeMatches[0].Value;
        if (!activeTarget.EnumerateObject().Any())
        {
            AddFinding(
                findings,
                "assets-active-target-empty",
                $"Active restore target '{activeTargetName}' is empty.");
            return;
        }

        var expectedLibraryKeys = new List<string>();
        var sharedProjectMatches = libraries.Where(library =>
                string.Equals(library.Type, "project", StringComparison.OrdinalIgnoreCase) &&
                library.Id.Equals(SharedSmokeProjectId(target), StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sharedProjectMatches.Length == 1)
            expectedLibraryKeys.Add(sharedProjectMatches[0].Key);

        foreach (var packageId in ExpectedPackageIds(target))
        {
            var packageMatches = libraries.Where(library =>
                    string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase) &&
                    library.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) &&
                    library.Version.Equals(input.Version, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (packageMatches.Length == 1)
                expectedLibraryKeys.Add(packageMatches[0].Key);
        }

        var activeKeys = activeTarget.EnumerateObject().Select(static property => property.Name).ToArray();
        foreach (var expectedLibraryKey in expectedLibraryKeys)
        {
            if (activeKeys.Count(key => key.Equals(expectedLibraryKey, StringComparison.OrdinalIgnoreCase)) != 1)
            {
                AddFinding(
                    findings,
                    "assets-active-target-library-missing",
                    $"Active restore target '{activeTargetName}' does not contain exact library '{expectedLibraryKey}'.");
            }
        }

        var expectedCount = ExpectedPackageIds(target).Count + 1;
        if (expectedLibraryKeys.Count != expectedCount)
        {
            AddFinding(
                findings,
                "assets-active-target-expected-library-count",
                $"Could identify {expectedLibraryKeys.Count} of {expectedCount} required libraries for active restore target '{activeTargetName}'.");
        }
    }

    private static void AuditExpectedPackage(
        string expectedId,
        IReadOnlyList<AssetsLibrary> libraries,
        string packagesCacheDirectory,
        CompatibilityPackageInput input,
        ICollection<CompatibilityResolvedPackage> resolvedPackages,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        var assetMatches = libraries.Where(library =>
                library.Id.Equals(expectedId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (assetMatches.Length != 1)
        {
            AddFinding(
                findings,
                "resolved-package-count",
                $"Expected exactly one resolved package library for '{expectedId}', found {assetMatches.Length}.");
        }

        var candidateMatches = input.Packages.Where(package =>
                package.Id.Equals(expectedId, StringComparison.OrdinalIgnoreCase) &&
                package.Version.Equals(input.Version, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidateMatches.Length != 1)
        {
            AddFinding(
                findings,
                "candidate-package-count",
                $"Expected exactly one selected candidate package for '{expectedId}/{input.Version}', found {candidateMatches.Length}.");
        }

        if (assetMatches.Length == 0)
            return;

        var library = assetMatches.FirstOrDefault();
        if (library is null)
            return;
        var candidate = candidateMatches.FirstOrDefault();
        var exactVersion = library.Version.Equals(input.Version, StringComparison.OrdinalIgnoreCase);
        var cacheDirectory = ResolveCacheDirectory(
            packagesCacheDirectory,
            library,
            findings);
        var metadataPath = cacheDirectory is null ? "" : Path.Combine(cacheDirectory, ".nupkg.metadata");
        var source = ReadMetadataSource(metadataPath, findings);
        var cachedPackagePath = cacheDirectory is null
            ? ""
            : Path.Combine(
                cacheDirectory,
                $"{library.Id.ToLowerInvariant()}.{library.Version.ToLowerInvariant()}.nupkg");
        var cachedSha = ComputeSha256(cachedPackagePath, findings);
        var sourceMatches = SourceMatchesDirectory(source, input.PackageDirectory);
        var hashMatches = candidate is not null && cachedSha is not null &&
                          cachedSha.Equals(candidate.Sha256, StringComparison.OrdinalIgnoreCase);
        var extractedFiles = VerifyExtractedPackageFiles(
            library,
            packagesCacheDirectory,
            cacheDirectory,
            cachedPackagePath,
            findings);
        var extractedFilesMatchArchive = hashMatches && extractedFiles.Passed;

        if (!exactVersion)
        {
            AddFinding(
                findings,
                "resolved-package-version-mismatch",
                $"Resolved package '{library.Key}' does not use exact candidate version '{input.Version}'.");
        }

        if (!sourceMatches)
        {
            AddFinding(
                findings,
                "package-source-mismatch",
                $"Resolved package '{library.Key}' was not restored from candidate directory '{input.PackageDirectory}'.");
        }

        if (!hashMatches)
        {
            AddFinding(
                findings,
                "package-hash-mismatch",
                $"Cached package '{cachedPackagePath}' does not match the selected candidate SHA-256 for '{expectedId}'.");
        }

        resolvedPackages.Add(new CompatibilityResolvedPackage(
            library.Id,
            library.Version,
            library.Key,
            cacheDirectory ?? "",
            metadataPath,
            source,
            cachedPackagePath,
            candidate?.PackagePath ?? "",
            candidate?.Sha256 ?? "",
            cachedSha,
            exactVersion,
            sourceMatches,
            hashMatches,
            extractedFilesMatchArchive,
            extractedFiles.VerifiedFileCount));
    }

    private static string? ResolveCacheDirectory(
        string packagesCacheDirectory,
        AssetsLibrary library,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        var relativePath = string.IsNullOrWhiteSpace(library.RelativePath)
            ? $"{library.Id.ToLowerInvariant()}/{library.Version.ToLowerInvariant()}"
            : library.RelativePath!;

        try
        {
            var cacheRoot = Path.GetFullPath(packagesCacheDirectory);
            var cacheDirectory = Path.GetFullPath(Path.Combine(
                cacheRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var expectedDirectory = Path.GetFullPath(Path.Combine(
                cacheRoot,
                library.Id.ToLowerInvariant(),
                library.Version.ToLowerInvariant()));

            if (!IsPathInsideOrEqual(cacheRoot, cacheDirectory))
            {
                AddFinding(
                    findings,
                    "package-cache-path-outside",
                    $"Assets cache path '{relativePath}' for '{library.Key}' escapes the isolated package cache.");
                return null;
            }

            if (!PathsEqual(cacheDirectory, expectedDirectory))
            {
                AddFinding(
                    findings,
                    "package-cache-path-mismatch",
                    $"Assets cache path '{relativePath}' for '{library.Key}' is not its exact NuGet id/version cache path.");
            }

            return cacheDirectory;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            AddFinding(
                findings,
                "package-cache-path-invalid",
                $"Assets cache path '{relativePath}' for '{library.Key}' is invalid: {exception.Message}");
            return null;
        }
    }

    private static string? ReadMetadataSource(
        string metadataPath,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        if (string.IsNullOrEmpty(metadataPath) || !File.Exists(metadataPath))
        {
            AddFinding(
                findings,
                "package-metadata-missing",
                $"Expected NuGet restore metadata '{metadataPath}'.");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath, Encoding.UTF8));
            return document.RootElement.TryGetProperty("source", out var source) &&
                   source.ValueKind == JsonValueKind.String
                ? source.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AddFinding(
                findings,
                "package-metadata-invalid",
                $"Could not read NuGet restore metadata '{metadataPath}': {exception.Message}");
            return null;
        }
    }

    private static string? ComputeSha256(
        string packagePath,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
        {
            AddFinding(
                findings,
                "cached-package-missing",
                $"Expected cached package '{packagePath}'.");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(packagePath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AddFinding(
                findings,
                "cached-package-unreadable",
                $"Could not hash cached package '{packagePath}': {exception.Message}");
            return null;
        }
    }

    private static ExtractedPackageVerification VerifyExtractedPackageFiles(
        AssetsLibrary library,
        string packagesCacheDirectory,
        string? cacheDirectory,
        string cachedPackagePath,
        ICollection<CompatibilityPackageResolutionFinding> findings)
    {
        if (!library.HasValidFiles || library.Files is null)
        {
            AddFinding(
                findings,
                "package-files-list-invalid",
                $"Package library '{library.Key}' must contain a files array of strings.");
            return new ExtractedPackageVerification(false, 0);
        }

        if (cacheDirectory is null || string.IsNullOrWhiteSpace(cachedPackagePath))
            return new ExtractedPackageVerification(false, 0);

        if (!IsTrustedExistingPath(packagesCacheDirectory, cacheDirectory, out var cacheReason))
        {
            AddFinding(
                findings,
                "package-cache-reparse-traversal",
                $"Package cache directory '{cacheDirectory}' for '{library.Key}' is not trustworthy: {cacheReason}");
            return new ExtractedPackageVerification(false, 0);
        }

        try
        {
            using var packageStream = new FileStream(
                cachedPackagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
            var passed = true;
            var verifiedFileCount = 0;
            var listedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var listedPath in library.Files.Where(path => !IsNuGetGeneratedCacheFile(path, library)))
            {
                if (!TryNormalizePackagePath(listedPath, out var normalizedPath, out var reason))
                {
                    AddFinding(
                        findings,
                        "package-file-path-invalid",
                        $"Package library '{library.Key}' lists invalid extracted path '{listedPath}': {reason}");
                    passed = false;
                    continue;
                }

                if (!listedFiles.TryAdd(normalizedPath, listedPath))
                {
                    AddFinding(
                        findings,
                        "package-files-list-ambiguous",
                        $"Package library '{library.Key}' lists extracted path '{listedPath}' more than once or with ambiguous casing.");
                    passed = false;
                }
            }

            var archiveEntries = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries.Where(static entry => !string.IsNullOrEmpty(entry.Name)))
            {
                if (!TryNormalizePackagePath(entry.FullName, out var normalizedPath, out var reason))
                {
                    AddFinding(
                        findings,
                        "package-archive-entry-path-invalid",
                        $"Cached package '{library.Key}' contains invalid archive path '{entry.FullName}': {reason}");
                    passed = false;
                    continue;
                }

                if (IsNuGetArchiveInfrastructureEntry(normalizedPath))
                    continue;

                if (!archiveEntries.TryGetValue(normalizedPath, out var matches))
                {
                    matches = [];
                    archiveEntries.Add(normalizedPath, matches);
                }

                matches.Add(entry);
            }

            foreach (var archiveEntry in archiveEntries.Where(static pair => pair.Value.Count != 1))
            {
                AddFinding(
                    findings,
                    "package-archive-entry-ambiguous",
                    $"Cached package '{library.Key}' contains {archiveEntry.Value.Count} archive entries for normalized path '{archiveEntry.Key}'.");
                passed = false;
            }

            foreach (var unlistedPath in archiveEntries.Keys.Where(path => !listedFiles.ContainsKey(path)))
            {
                AddFinding(
                    findings,
                    "package-archive-entry-unlisted",
                    $"Cached package '{library.Key}' contains archive entry '{unlistedPath}' that is absent from libraries[].files.");
                passed = false;
            }

            foreach (var listedFile in listedFiles)
            {
                if (!archiveEntries.TryGetValue(listedFile.Key, out var matchingEntries))
                {
                    AddFinding(
                        findings,
                        "package-archive-entry-missing",
                        $"Package library '{library.Key}' lists extracted file '{listedFile.Value}' that is absent from the cached nupkg archive.");
                    passed = false;
                    continue;
                }

                if (matchingEntries.Count != 1)
                    continue;

                var extractedPath = Path.GetFullPath(Path.Combine(
                    cacheDirectory,
                    listedFile.Key.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsPathInsideOrEqual(cacheDirectory, extractedPath))
                {
                    AddFinding(
                        findings,
                        "package-extracted-file-path-outside",
                        $"Extracted package path '{listedFile.Value}' for '{library.Key}' escapes its package cache directory.");
                    passed = false;
                    continue;
                }

                if (!IsTrustedExistingPath(packagesCacheDirectory, extractedPath, out var extractedReason))
                {
                    AddFinding(
                        findings,
                        "package-extracted-file-untrusted",
                        $"Extracted package file '{extractedPath}' for '{library.Key}' is not trustworthy: {extractedReason}");
                    passed = false;
                    continue;
                }

                if (!FileContentsEqual(matchingEntries[0], extractedPath))
                {
                    AddFinding(
                        findings,
                        "package-extracted-file-content-mismatch",
                        $"Extracted package file '{extractedPath}' does not match archive entry '{matchingEntries[0].FullName}' byte-for-byte.");
                    passed = false;
                    continue;
                }

                verifiedFileCount++;
            }

            return new ExtractedPackageVerification(passed, verifiedFileCount);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AddFinding(
                findings,
                "package-archive-invalid",
                $"Could not audit extracted files for cached package '{cachedPackagePath}': {exception.Message}");
            return new ExtractedPackageVerification(false, 0);
        }
    }

    private static bool IsNuGetGeneratedCacheFile(string path, AssetsLibrary library)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Equals(".nupkg.metadata", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(
                   $"{library.Id}.{library.Version}.nupkg.sha512",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNuGetArchiveInfrastructureEntry(string normalizedPath) =>
        normalizedPath.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.Equals("_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.StartsWith(
            "package/services/metadata/core-properties/",
            StringComparison.OrdinalIgnoreCase) &&
        normalizedPath.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase);

    private static bool TryNormalizePackagePath(
        string path,
        out string normalizedPath,
        out string reason)
    {
        normalizedPath = "";
        reason = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "path is blank";
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(path))
        {
            reason = "rooted paths are not allowed";
            return false;
        }

        var segments = normalized.Split('/');
        if (segments.Any(static segment =>
                segment.Length == 0 ||
                segment.Equals(".", StringComparison.Ordinal) ||
                segment.Equals("..", StringComparison.Ordinal) ||
                segment.Contains(':')))
        {
            reason = "path contains an empty, traversal, or drive/alternate-stream segment";
            return false;
        }

        normalizedPath = string.Join('/', segments);
        return true;
    }

    private static bool FileContentsEqual(ZipArchiveEntry archiveEntry, string extractedPath)
    {
        var extractedInfo = new FileInfo(extractedPath);
        if (!extractedInfo.Exists || extractedInfo.Length != archiveEntry.Length)
            return false;

        using var archiveStream = archiveEntry.Open();
        using var extractedStream = new FileStream(
            extractedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        var archiveHash = SHA256.HashData(archiveStream);
        var extractedHash = SHA256.HashData(extractedStream);
        return CryptographicOperations.FixedTimeEquals(archiveHash, extractedHash);
    }

    private static bool SourceMatchesDirectory(string? source, string packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var sourcePath = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : source;
        return PathsEqual(sourcePath, packageDirectory);
    }

    private static IReadOnlyList<string> ExpectedPackageIds(CompatibilityTargetDefinition target) =>
        target.RuntimeGraph switch
        {
            CompatibilityRuntimeGraph.SQLite => ["DataLinq", "DataLinq.SQLite"],
            CompatibilityRuntimeGraph.Memory => ["DataLinq", "DataLinq.Memory"],
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.RuntimeGraph, "Unsupported compatibility runtime graph.")
        };

    private static string SharedSmokeProjectId(CompatibilityTargetDefinition target) =>
        target.RuntimeGraph switch
        {
            CompatibilityRuntimeGraph.SQLite => "DataLinq.PlatformCompatibility.Smoke",
            CompatibilityRuntimeGraph.Memory => "DataLinq.Memory.PlatformCompatibility.Smoke",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.RuntimeGraph, "Unsupported compatibility runtime graph.")
        };

    private static string SharedSmokeProjectPath(
        CompatibilityTargetDefinition target,
        string repositoryRoot) =>
        Path.GetFullPath(Path.Combine(
            repositoryRoot,
            target.RuntimeGraph switch
            {
                CompatibilityRuntimeGraph.SQLite =>
                    Path.Combine("src", "DataLinq.PlatformCompatibility.Smoke", "DataLinq.PlatformCompatibility.Smoke.csproj"),
                CompatibilityRuntimeGraph.Memory =>
                    Path.Combine("src", "DataLinq.Memory.PlatformCompatibility.Smoke", "DataLinq.Memory.PlatformCompatibility.Smoke.csproj"),
                _ => throw new ArgumentOutOfRangeException(nameof(target), target.RuntimeGraph, "Unsupported compatibility runtime graph.")
            }));

    private static bool TryResolveTrustedProjectPath(
        string baseDirectory,
        string? relativePath,
        string repositoryRoot,
        string expectedProjectPath,
        out string reason)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            reason = "the project library path is missing";
            return false;
        }

        try
        {
            var resolvedPath = Path.GetFullPath(Path.Combine(
                baseDirectory,
                NormalizeRelativePath(relativePath)));
            if (!PathsEqual(resolvedPath, expectedProjectPath))
            {
                reason = $"it resolves to '{resolvedPath}'";
                return false;
            }

            return IsTrustedExistingPath(repositoryRoot, resolvedPath, out reason);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = $"the project library path is invalid: {exception.Message}";
            return false;
        }
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static bool IsTrustedExistingPath(
        string trustedRoot,
        string candidatePath,
        out string reason)
    {
        reason = "";
        string normalizedRoot;
        string normalizedCandidate;
        try
        {
            normalizedRoot = NormalizePath(trustedRoot);
            normalizedCandidate = NormalizePath(candidatePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = $"path is invalid: {exception.Message}";
            return false;
        }

        if (!IsPathInsideOrEqual(normalizedRoot, normalizedCandidate))
        {
            reason = $"path escapes trusted root '{normalizedRoot}'";
            return false;
        }

        if (!Directory.Exists(normalizedRoot) && !File.Exists(normalizedRoot))
        {
            reason = $"trusted root '{normalizedRoot}' does not exist";
            return false;
        }

        var relativePath = Path.GetRelativePath(normalizedRoot, normalizedCandidate);
        var current = normalizedRoot;
        var segments = relativePath.Equals(".", StringComparison.Ordinal)
            ? Array.Empty<string>()
            : relativePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
        {
            reason = $"path traverses reparse point '{current}'";
            return false;
        }

        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                reason = $"path component '{current}' does not exist";
                return false;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                reason = $"path traverses reparse point '{current}'";
                return false;
            }
        }

        return true;
    }

    private static bool IsDataLinqId(string id) =>
        id.Equals("DataLinq", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("DataLinq.", StringComparison.OrdinalIgnoreCase);

    private static CompatibilityPackageResolutionReport CreateReport(
        string assetsPath,
        IReadOnlyList<string> projectLibraries,
        IReadOnlyList<CompatibilityResolvedPackage> resolvedPackages,
        IReadOnlyList<CompatibilityPackageResolutionFinding> findings) =>
        new(
            assetsPath,
            projectLibraries,
            resolvedPackages,
            findings,
            findings.Count == 0);

    private static void AddFinding(
        ICollection<CompatibilityPackageResolutionFinding> findings,
        string code,
        string message) =>
        findings.Add(new CompatibilityPackageResolutionFinding(code, message));

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        try
        {
            return NormalizePath(left).Equals(NormalizePath(right), PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsPathInsideOrEqual(string root, string candidate)
    {
        var normalizedRoot = NormalizePath(root);
        var normalizedCandidate = NormalizePath(candidate);
        if (normalizedRoot.Equals(normalizedCandidate, PathComparison))
            return true;

        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record AssetsLibrary(
        string Key,
        string Id,
        string Version,
        string? Type,
        string? RelativePath,
        IReadOnlyList<string>? Files,
        bool HasValidFiles);

    private readonly record struct ExtractedPackageVerification(
        bool Passed,
        int VerifiedFileCount);
}
