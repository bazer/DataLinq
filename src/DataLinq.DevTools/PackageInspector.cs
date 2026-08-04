using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public sealed class PackageInspector
{
    private const string SchemaVersion = "v0.9.package-inspection-report.v3";

    private readonly DevToolPaths paths;
    private readonly PackageInspectionOptions options;

    public PackageInspector(DevToolPaths paths, PackageInspectionOptions options)
    {
        this.paths = paths;
        this.options = options;
    }

    public PackageInspectionReport CreateReport()
    {
        paths.EnsureCreated();

        var packageDirectory = Path.GetFullPath(options.PackageDirectory);
        var reportDirectory = CreateReportDirectory(paths.ArtifactRoot);
        var symbolPackages = Directory.Exists(packageDirectory)
            ? Directory.EnumerateFiles(packageDirectory, "*.snupkg", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(InspectSymbolPackage)
                .ToArray()
            : [];
        var packages = Directory.Exists(packageDirectory)
            ? Directory.EnumerateFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                .Where(static path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => InspectPackage(path, symbolPackages))
                .ToArray()
            : [];

        var findings = CreateFindings(packages, symbolPackages);
        var report = new PackageInspectionReport(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            options.RepositoryRoot,
            packageDirectory,
            reportDirectory,
            packages,
            symbolPackages,
            findings,
            CreateSummary(packages, findings));

        WriteReportArtifacts(report);
        return report;
    }

    public static string ToMarkdown(PackageInspectionReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Package Inspection Report");
        builder.AppendLine();
        builder.AppendLine($"Generated UTC: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Package directory: `{report.PackageDirectory}`");
        builder.AppendLine();
        builder.AppendLine("| Package | Version | Runtime | Tool | Symbols | lib | analyzers | tools | runtimes |");
        builder.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: |");

        foreach (var package in report.Packages)
        {
            builder.AppendLine(string.Join(" | ", [
                $"| `{EscapeTable(package.Id)}`",
                $"`{EscapeTable(package.Version)}`",
                package.IsRuntimePackage ? "yes" : "no",
                package.IsDotnetTool ? "yes" : "no",
                package.SymbolPackagePath is null ? "missing" : "yes",
                package.Assets.LibFileCount.ToString(),
                package.Assets.AnalyzerFileCount.ToString(),
                package.Assets.ToolFileCount.ToString(),
                $"{package.Assets.RuntimeFileCount} |"
            ]));
        }

        builder.AppendLine();
        builder.AppendLine("## Symbol Packages");
        builder.AppendLine();
        builder.AppendLine("| Package | Version | PDBs | Entries |");
        builder.AppendLine("| --- | --- | ---: | ---: |");

        foreach (var symbolPackage in report.SymbolPackages)
        {
            builder.AppendLine(
                $"| `{EscapeTable(symbolPackage.Id)}` | `{EscapeTable(symbolPackage.Version)}` | " +
                $"{symbolPackage.PdbFiles.Count} | {symbolPackage.AllFiles.Count} |");
        }

        if (report.Findings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();

            foreach (var finding in report.Findings)
            {
                var target = string.IsNullOrWhiteSpace(finding.TargetFramework)
                    ? ""
                    : $" `{finding.TargetFramework}`";
                builder.AppendLine($"- `{finding.Kind}` `{finding.PackageId}`{target}: {finding.Message}");
            }
        }

        foreach (var package in report.Packages)
        {
            builder.AppendLine();
            builder.AppendLine($"## {package.Id}");
            builder.AppendLine();
            builder.AppendLine($"- description: `{EscapeTable(package.Metadata.Description ?? "missing")}`");
            builder.AppendLine($"- repository: `{EscapeTable(package.Metadata.RepositoryUrl ?? "missing")}`");
            builder.AppendLine($"- repository commit: `{EscapeTable(package.Metadata.RepositoryCommit ?? "missing")}`");
            builder.AppendLine($"- license: `{EscapeTable(package.Metadata.License ?? "missing")}`");
            builder.AppendLine($"- readme: `{EscapeTable(package.Metadata.Readme ?? "missing")}`");
            builder.AppendLine();

            foreach (var group in package.DependencyGroups)
            {
                builder.AppendLine($"### {group.TargetFramework}");
                builder.AppendLine();

                if (group.Dependencies.Count == 0)
                {
                    builder.AppendLine("- no dependencies");
                    continue;
                }

                foreach (var dependency in group.Dependencies)
                {
                    var exclude = string.IsNullOrWhiteSpace(dependency.Exclude)
                        ? ""
                        : $", exclude `{dependency.Exclude}`";
                    builder.AppendLine($"- `{dependency.Id}` `{dependency.Version}`{exclude}");
                }
            }
        }

        return builder.ToString();
    }

    private PackageInspectionPackageReport InspectPackage(
        string packagePath,
        IReadOnlyList<PackageInspectionSymbolPackageReport> symbolPackages)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Select(static entry => NormalizeEntryName(entry.FullName))
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nuspec = ReadNuspec(archive, packagePath);
        var metadata = ReadPackageMetadata(nuspec);
        var id = metadata.Id ?? Path.GetFileNameWithoutExtension(packagePath);
        var version = metadata.Version ?? "unknown";
        var symbolPackage = symbolPackages.FirstOrDefault(symbol =>
            symbol.Id.Equals(id, StringComparison.OrdinalIgnoreCase) &&
            symbol.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        var symbolPackagePath = symbolPackage?.PackagePath;
        var symbolFiles = symbolPackage?.PdbFiles ?? [];
        var isRuntimePackage = options.RuntimePackageIds.Contains(id);
        var isExpectedPackage = options.ExpectedPackageIds.Contains(id);

        return new PackageInspectionPackageReport(
            id,
            version,
            packagePath,
            symbolPackagePath,
            isRuntimePackage,
            isExpectedPackage,
            IsDotnetToolPackage(nuspec),
            metadata,
            symbolPackage?.Id,
            symbolPackage?.Version,
            ReadDependencyGroups(nuspec),
            CreateAssetSummary(entries, symbolFiles),
            ReadPayloadTokenMatches(archive, id),
            ReadBinaryPayloadMatches(archive, id),
            ReadManagedAssemblyInspections(archive, id));
    }

    private IReadOnlyList<PackageInspectionFinding> CreateFindings(
        IReadOnlyList<PackageInspectionPackageReport> packages,
        IReadOnlyList<PackageInspectionSymbolPackageReport> symbolPackages)
    {
        var findings = new List<PackageInspectionFinding>();
        var packageIds = packages.Select(static package => package.Id).ToArray();
        var packageIdSet = packageIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedId in options.ExpectedPackageIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!packageIdSet.Contains(expectedId))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.MissingExpectedPackage,
                    expectedId,
                    null,
                    "Expected public package was not present in the inspected package directory."));
            }
        }

        foreach (var duplicateGroup in packages.GroupBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.DuplicatePackage,
                duplicateGroup.Key,
                null,
                $"Package directory contains {duplicateGroup.Count()} packages with this id. Inspect a fresh release folder, not an accumulated cache."));
        }

        AddVersionAlignmentFindings(packages, findings);
        AddSymbolPackageFindings(packages, symbolPackages, findings);

        foreach (var package in packages)
        {
            if (!package.IsExpectedPackage)
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.UnexpectedPackage,
                    package.Id,
                    null,
                    "Package id is not part of the public release package set."));
            }

            if (package.SymbolPackagePath is null)
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.MissingSymbolPackage,
                    package.Id,
                    null,
                    "No matching .snupkg was found beside the .nupkg."));
            }

            if (package.IsExpectedPackage)
                AddMetadataFindings(package, findings);

            if (package.Id.Equals(PackageInspectionPolicy.CorePackageId, StringComparison.OrdinalIgnoreCase) &&
                !package.Assets.AnalyzerFiles.Any(static file => file.Equals("analyzers/dotnet/cs/DataLinq.Generators.dll", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.MissingAnalyzerAsset,
                    package.Id,
                    null,
                    "DataLinq package does not contain the generated model source generator under analyzers/dotnet/cs."));
            }

            if (package.Id.Equals(PackageInspectionPolicy.MemoryPackageId, StringComparison.OrdinalIgnoreCase))
                AddMemoryPackageFindings(package, findings);

            if (!package.IsRuntimePackage)
                continue;

            foreach (var group in package.DependencyGroups)
            {
                foreach (var dependency in group.Dependencies.Where(static dependency => IsRoslynPackageId(dependency.Id)))
                {
                    findings.Add(new PackageInspectionFinding(
                        PackageInspectionFindingKind.RuntimeRoslynDependency,
                        package.Id,
                        group.TargetFramework,
                        $"Runtime dependency group references Roslyn package '{dependency.Id}'."));
                }

                foreach (var dependency in group.Dependencies.Where(static dependency => IsRemotionPackageId(dependency.Id)))
                {
                    findings.Add(new PackageInspectionFinding(
                        PackageInspectionFindingKind.RuntimeRemotionDependency,
                        package.Id,
                        group.TargetFramework,
                        $"Runtime dependency group references Remotion package '{dependency.Id}'."));
                }
            }

            foreach (var asset in package.Assets.LibFiles.Concat(package.Assets.RuntimeFiles)
                         .Where(static asset => Path.GetFileName(asset).StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.RuntimeRoslynAsset,
                    package.Id,
                    null,
                    $"Runtime package contains Roslyn payload asset '{asset}'."));
            }

            foreach (var asset in package.Assets.LibFiles.Concat(package.Assets.RuntimeFiles)
                         .Where(static asset => Path.GetFileName(asset).StartsWith("Remotion.", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.RuntimeRemotionAsset,
                    package.Id,
                    null,
                    $"Runtime package contains Remotion payload asset '{asset}'."));
            }

            foreach (var asset in package.Assets.LibFiles.Concat(package.Assets.RuntimeFiles)
                         .Where(static asset => Path.GetFileName(asset).StartsWith("DataLinq.Generators", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.AnalyzerAssetLeak,
                    package.Id,
                    null,
                    $"Analyzer payload is outside analyzer assets at '{asset}'."));
            }
        }

        return findings;
    }

    private static void AddVersionAlignmentFindings(
        IReadOnlyList<PackageInspectionPackageReport> packages,
        ICollection<PackageInspectionFinding> findings)
    {
        var expectedPackages = packages
            .Where(static package => package.IsExpectedPackage)
            .ToArray();
        var versions = expectedPackages
            .Select(static package => package.Metadata.Version)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static version => version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (versions.Length <= 1)
            return;

        var versionList = string.Join(", ", versions.Select(static version => $"'{version}'"));
        foreach (var package in expectedPackages)
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.PackageVersionMismatch,
                package.Id,
                null,
                $"Expected public package versions are not aligned. Found {versionList}."));
        }
    }

    private static void AddSymbolPackageFindings(
        IReadOnlyList<PackageInspectionPackageReport> packages,
        IReadOnlyList<PackageInspectionSymbolPackageReport> symbolPackages,
        ICollection<PackageInspectionFinding> findings)
    {
        foreach (var duplicateGroup in symbolPackages
                     .GroupBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.DuplicateSymbolPackage,
                duplicateGroup.Key,
                null,
                $"Package directory contains {duplicateGroup.Count()} symbol packages with this id. A fresh candidate must contain exactly one symbol package per public package."));
        }

        foreach (var symbolPackage in symbolPackages)
        {
            var matchingRuntimePackages = packages
                .Where(package =>
                    package.Id.Equals(symbolPackage.Id, StringComparison.OrdinalIgnoreCase) &&
                    package.Version.Equals(symbolPackage.Version, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchingRuntimePackages.Length == 0)
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.OrphanSymbolPackage,
                    symbolPackage.Id,
                    null,
                    $"Symbol package '{Path.GetFileName(symbolPackage.PackagePath)}' has no matching .nupkg with the same nuspec id and version."));
            }

            var isExpected = matchingRuntimePackages.Any(static package => package.IsExpectedPackage);
            if (isExpected)
                AddSymbolIdentityFindings(symbolPackage, findings);

            if (symbolPackage.Id.Equals(PackageInspectionPolicy.MemoryPackageId, StringComparison.OrdinalIgnoreCase))
                AddMemorySymbolPackageFindings(symbolPackage, findings);
        }
    }

    private static void AddSymbolIdentityFindings(
        PackageInspectionSymbolPackageReport symbolPackage,
        ICollection<PackageInspectionFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(symbolPackage.Metadata.Id) ||
            string.IsNullOrWhiteSpace(symbolPackage.Metadata.Version))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingPackageMetadata,
                symbolPackage.Id,
                null,
                "Expected symbol package does not contain a complete nuspec id and version."));
            return;
        }

        var expectedFileName = $"{symbolPackage.Metadata.Id}.{symbolPackage.Metadata.Version}.snupkg";
        var actualFileName = Path.GetFileName(symbolPackage.PackagePath);
        if (!string.Equals(actualFileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.PackageIdentityMismatch,
                symbolPackage.Id,
                null,
                $"Symbol package filename '{actualFileName}' does not match nuspec identity '{expectedFileName}'."));
        }
    }

    private static void AddMemorySymbolPackageFindings(
        PackageInspectionSymbolPackageReport symbolPackage,
        ICollection<PackageInspectionFinding> findings)
    {
        var expectedPdbFiles = PackageInspectionPolicy.MemoryTargetFrameworks
            .Select(static framework => $"lib/{framework}/DataLinq.Memory.pdb")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualPdbFiles = symbolPackage.PdbFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var missing in expectedPdbFiles.Where(file => !actualPdbFiles.Contains(file)).OrderBy(static file => file, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingRequiredPackageAsset,
                symbolPackage.Id,
                null,
                $"DataLinq.Memory symbol package is missing required PDB asset '{missing}'."));
        }

        foreach (var unexpected in actualPdbFiles.Where(file => !expectedPdbFiles.Contains(file)).OrderBy(static file => file, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedSymbolPackageAsset,
                symbolPackage.Id,
                null,
                $"DataLinq.Memory symbol package contains unexpected PDB asset '{unexpected}'."));
        }

        foreach (var duplicate in symbolPackage.AllFiles
                     .GroupBy(static file => file, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedSymbolPackageAsset,
                symbolPackage.Id,
                null,
                $"DataLinq.Memory symbol package contains duplicate archive entry '{duplicate.Key}'."));
        }

        foreach (var asset in symbolPackage.AllFiles.Where(asset => !IsAllowedMemorySymbolPackageAsset(asset, expectedPdbFiles)))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedSymbolPackageAsset,
                symbolPackage.Id,
                null,
                $"DataLinq.Memory symbol package contains non-allowlisted asset '{asset}'."));
        }

        foreach (var asset in symbolPackage.AllFiles)
        {
            foreach (var token in PackageInspectionPolicy.MemoryBannedPayloadTokens
                         .Where(token => asset.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.BannedSymbolPackageAsset,
                    symbolPackage.Id,
                    null,
                    $"DataLinq.Memory symbol package asset path '{asset}' contains banned payload token '{token}'."));
            }
        }

        foreach (var match in symbolPackage.BinaryPayloadMatches)
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.BannedSymbolPackageAsset,
                symbolPackage.Id,
                null,
                $"DataLinq.Memory symbol package asset '{match.Asset}' contains executable/native signature '{match.Signature}'."));
        }
    }

    private static void AddMetadataFindings(
        PackageInspectionPackageReport package,
        ICollection<PackageInspectionFinding> findings)
    {
        AddRequiredMetadataFinding(package, findings, "id", package.Metadata.Id);
        AddRequiredMetadataFinding(package, findings, "version", package.Metadata.Version);
        AddRequiredMetadataFinding(package, findings, "description", package.Metadata.Description);
        AddRequiredMetadataFinding(package, findings, "repository type", package.Metadata.RepositoryType);
        AddRequiredMetadataFinding(package, findings, "repository URL", package.Metadata.RepositoryUrl);
        AddRequiredMetadataFinding(package, findings, "repository commit", package.Metadata.RepositoryCommit);
        AddRequiredMetadataFinding(package, findings, "license type", package.Metadata.LicenseType);
        AddRequiredMetadataFinding(package, findings, "license", package.Metadata.License);
        AddRequiredMetadataFinding(package, findings, "readme", package.Metadata.Readme);

        if (!string.IsNullOrWhiteSpace(package.Metadata.Id) &&
            !string.IsNullOrWhiteSpace(package.Metadata.Version))
        {
            var expectedFileName = $"{package.Metadata.Id}.{package.Metadata.Version}.nupkg";
            var actualFileName = Path.GetFileName(package.PackagePath);
            if (!string.Equals(actualFileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.PackageIdentityMismatch,
                    package.Id,
                    null,
                    $"Package filename '{actualFileName}' does not match nuspec identity '{expectedFileName}'."));
            }
        }

        AddExactMetadataFinding(package, findings, "repository type", package.Metadata.RepositoryType, "git");
        AddExactMetadataFinding(package, findings, "repository URL", NormalizeRepositoryUrl(package.Metadata.RepositoryUrl), PackageInspectionPolicy.RepositoryUrl);
        AddExactMetadataFinding(package, findings, "license type", package.Metadata.LicenseType, "file");
        AddExactMetadataFinding(package, findings, "license", package.Metadata.License, PackageInspectionPolicy.LicenseFile);
        AddExactMetadataFinding(package, findings, "readme", package.Metadata.Readme, PackageInspectionPolicy.ReadmeFile);

        if (!package.Assets.AllFiles.Contains(PackageInspectionPolicy.LicenseFile, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingRequiredPackageAsset,
                package.Id,
                null,
                $"Package does not contain root license asset '{PackageInspectionPolicy.LicenseFile}'."));
        }

        if (!package.Assets.AllFiles.Contains(PackageInspectionPolicy.ReadmeFile, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingRequiredPackageAsset,
                package.Id,
                null,
                $"Package does not contain root readme asset '{PackageInspectionPolicy.ReadmeFile}'."));
        }

        if (package.SymbolPackagePath is null)
            return;

        if (string.IsNullOrWhiteSpace(package.SymbolPackageId) || string.IsNullOrWhiteSpace(package.SymbolPackageVersion))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingPackageMetadata,
                package.Id,
                null,
                "Matching symbol package does not contain a complete nuspec id and version."));
            return;
        }

        if (!string.Equals(package.SymbolPackageId, package.Metadata.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(package.SymbolPackageVersion, package.Metadata.Version, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.PackageIdentityMismatch,
                package.Id,
                null,
                $"Symbol package identity '{package.SymbolPackageId} {package.SymbolPackageVersion}' does not match runtime package identity '{package.Metadata.Id} {package.Metadata.Version}'."));
        }
    }

    private static void AddRequiredMetadataFinding(
        PackageInspectionPackageReport package,
        ICollection<PackageInspectionFinding> findings,
        string field,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return;

        findings.Add(new PackageInspectionFinding(
            PackageInspectionFindingKind.MissingPackageMetadata,
            package.Id,
            null,
            $"Package nuspec is missing required {field} metadata."));
    }

    private static void AddExactMetadataFinding(
        PackageInspectionPackageReport package,
        ICollection<PackageInspectionFinding> findings,
        string field,
        string? actual,
        string expected)
    {
        if (string.IsNullOrWhiteSpace(actual) ||
            string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        findings.Add(new PackageInspectionFinding(
            PackageInspectionFindingKind.InvalidPackageMetadata,
            package.Id,
            null,
            $"Package nuspec {field} is '{actual}'; expected '{expected}'."));
    }

    private static void AddMemoryPackageFindings(
        PackageInspectionPackageReport package,
        ICollection<PackageInspectionFinding> findings)
    {
        AddExactMetadataFinding(
            package,
            findings,
            "description",
            package.Metadata.Description,
            PackageInspectionPolicy.MemoryDescription);

        if (package.SymbolPackagePath is null)
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingRequiredPackageAsset,
                package.Id,
                null,
                "DataLinq.Memory requires a matching symbol package."));
        }

        var expectedLibFiles = PackageInspectionPolicy.MemoryTargetFrameworks
            .Select(static framework => $"lib/{framework}/DataLinq.Memory.dll")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedSymbolFiles = PackageInspectionPolicy.MemoryTargetFrameworks
            .Select(static framework => $"lib/{framework}/DataLinq.Memory.pdb")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AddExactAssetSetFindings(package, findings, "runtime assembly", expectedLibFiles, package.Assets.LibFiles);
        AddExactAssetSetFindings(package, findings, "symbol", expectedSymbolFiles, package.Assets.SymbolFiles);

        foreach (var assembly in package.ManagedAssemblies)
        {
            if (assembly.Error is not null)
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.InvalidManagedAssembly,
                    package.Id,
                    null,
                    $"DataLinq.Memory runtime asset '{assembly.Asset}' is not a valid managed assembly: {assembly.Error}"));
            }
            else if (!string.Equals(assembly.AssemblyName, PackageInspectionPolicy.MemoryPackageId, StringComparison.Ordinal))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.InvalidManagedAssembly,
                    package.Id,
                    null,
                    $"DataLinq.Memory runtime asset '{assembly.Asset}' has assembly definition name '{assembly.AssemblyName ?? "<missing>"}'; expected exactly '{PackageInspectionPolicy.MemoryPackageId}'."));
            }
        }

        foreach (var duplicate in package.Assets.AllFiles
                     .GroupBy(static file => file, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedPackageAsset,
                package.Id,
                null,
                $"DataLinq.Memory contains duplicate archive entry '{duplicate.Key}'."));
        }

        foreach (var asset in package.Assets.AllFiles.Where(asset => !IsAllowedMemoryRuntimePackageAsset(asset, expectedLibFiles)))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedPackageAsset,
                package.Id,
                null,
                $"DataLinq.Memory contains non-allowlisted package asset '{asset}'."));
        }

        foreach (var asset in package.Assets.AllFiles)
        {
            foreach (var token in PackageInspectionPolicy.MemoryBannedPayloadTokens
                         .Where(token => asset.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.BannedRuntimeAsset,
                    package.Id,
                    null,
                    $"DataLinq.Memory package asset path '{asset}' contains banned payload token '{token}'."));
            }
        }

        foreach (var match in package.PayloadTokenMatches)
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.BannedRuntimeAsset,
                package.Id,
                null,
                $"DataLinq.Memory managed asset '{match.Asset}' contains banned payload token '{match.Token}'."));
        }

        foreach (var match in package.BinaryPayloadMatches.Where(match => !expectedLibFiles.Contains(match.Asset)))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.BannedRuntimeAsset,
                package.Id,
                null,
                $"DataLinq.Memory package asset '{match.Asset}' contains executable/native signature '{match.Signature}'."));
        }

        AddMemoryDependencyFindings(package, findings);
    }

    private static void AddExactAssetSetFindings(
        PackageInspectionPackageReport package,
        ICollection<PackageInspectionFinding> findings,
        string assetKind,
        IReadOnlySet<string> expected,
        IReadOnlyList<string> actual)
    {
        var actualSet = actual.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var missing in expected.Where(asset => !actualSet.Contains(asset)).OrderBy(static asset => asset, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingRequiredPackageAsset,
                package.Id,
                null,
                $"DataLinq.Memory is missing required {assetKind} asset '{missing}'."));
        }

        foreach (var unexpected in actualSet.Where(asset => !expected.Contains(asset)).OrderBy(static asset => asset, StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedPackageAsset,
                package.Id,
                null,
                $"DataLinq.Memory contains unexpected {assetKind} asset '{unexpected}'."));
        }
    }

    private static void AddMemoryDependencyFindings(
        PackageInspectionPackageReport package,
        ICollection<PackageInspectionFinding> findings)
    {
        var expectedFrameworks = PackageInspectionPolicy.MemoryTargetFrameworks
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = package.DependencyGroups
            .GroupBy(static group => group.TargetFramework, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var framework in expectedFrameworks.OrderBy(static framework => framework, StringComparer.OrdinalIgnoreCase))
        {
            if (!groups.TryGetValue(framework, out var matchingGroups))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.MissingDependencyGroup,
                    package.Id,
                    framework,
                    "DataLinq.Memory is missing its required target-framework dependency group."));
                continue;
            }

            if (matchingGroups.Length != 1)
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.UnexpectedDependencyGroup,
                    package.Id,
                    framework,
                    $"DataLinq.Memory contains {matchingGroups.Length} dependency groups for this target framework; expected exactly one."));
            }

            foreach (var group in matchingGroups)
                AddMemoryDependencyGroupFindings(package, group, findings);
        }

        foreach (var group in package.DependencyGroups.Where(group => !expectedFrameworks.Contains(group.TargetFramework)))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedDependencyGroup,
                package.Id,
                group.TargetFramework,
                "DataLinq.Memory contains an unsupported dependency group; only net8.0, net9.0, and net10.0 are allowed."));

            foreach (var dependency in group.Dependencies.Where(static dependency => IsMemoryBannedDependency(dependency.Id)))
                AddBannedMemoryDependencyFinding(package, group, dependency, findings);
        }
    }

    private static void AddMemoryDependencyGroupFindings(
        PackageInspectionPackageReport package,
        PackageDependencyGroup group,
        ICollection<PackageInspectionFinding> findings)
    {
        var coreDependencies = group.Dependencies
            .Where(static dependency => dependency.Id.Equals(PackageInspectionPolicy.CorePackageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (coreDependencies.Length == 0)
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.MissingRequiredPackageDependency,
                package.Id,
                group.TargetFramework,
                "DataLinq.Memory dependency group does not contain its required DataLinq core dependency."));
        }
        else if (coreDependencies.Length > 1)
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.UnexpectedPackageDependency,
                package.Id,
                group.TargetFramework,
                $"DataLinq.Memory dependency group contains {coreDependencies.Length} DataLinq dependencies; expected exactly one."));
        }

        foreach (var dependency in group.Dependencies)
        {
            if (!dependency.Id.Equals(PackageInspectionPolicy.CorePackageId, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.UnexpectedPackageDependency,
                    package.Id,
                    group.TargetFramework,
                    $"DataLinq.Memory dependency group contains unexpected package '{dependency.Id}'. Only DataLinq is allowed."));

                if (IsMemoryBannedDependency(dependency.Id))
                    AddBannedMemoryDependencyFinding(package, group, dependency, findings);

                continue;
            }

            if (!string.Equals(dependency.Version, package.Version, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.PackageDependencyVersionMismatch,
                    package.Id,
                    group.TargetFramework,
                    $"DataLinq dependency version '{dependency.Version}' does not match Memory package version '{package.Version}'."));
            }

            if (!HasExactBuildAnalyzerExclusion(dependency.Exclude))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.PackageDependencyExclusionMismatch,
                    package.Id,
                    group.TargetFramework,
                    $"DataLinq dependency excludes '{dependency.Exclude ?? "<none>"}'; expected exactly 'Build,Analyzers'."));
            }
        }
    }

    private static void AddBannedMemoryDependencyFinding(
        PackageInspectionPackageReport package,
        PackageDependencyGroup group,
        PackageDependency dependency,
        ICollection<PackageInspectionFinding> findings)
    {
        findings.Add(new PackageInspectionFinding(
            PackageInspectionFindingKind.BannedRuntimeDependency,
            package.Id,
            group.TargetFramework,
            $"DataLinq.Memory references banned runtime dependency '{dependency.Id}'."));
    }

    private PackageInspectionSummary CreateSummary(
        IReadOnlyList<PackageInspectionPackageReport> packages,
        IReadOnlyList<PackageInspectionFinding> findings)
    {
        var hardFailureCount = findings.Count(IsHardFailure);
        return new PackageInspectionSummary(
            packages.Count,
            options.ExpectedPackageIds.Count,
            packages.Count(static package => package.IsRuntimePackage),
            findings.Count,
            hardFailureCount,
            hardFailureCount > 0);
    }

    private bool IsHardFailure(PackageInspectionFinding finding) =>
        finding.Kind switch
        {
            PackageInspectionFindingKind.MissingExpectedPackage => true,
            PackageInspectionFindingKind.DuplicatePackage => true,
            PackageInspectionFindingKind.UnexpectedPackage => options.FailOnUnexpectedPackage,
            PackageInspectionFindingKind.MissingSymbolPackage => options.FailOnMissingSymbolPackage,
            PackageInspectionFindingKind.RuntimeRoslynDependency or
                PackageInspectionFindingKind.RuntimeRoslynAsset => options.FailOnRuntimeRoslyn,
            PackageInspectionFindingKind.RuntimeRemotionDependency or
                PackageInspectionFindingKind.RuntimeRemotionAsset => options.FailOnRuntimeRemotion,
            PackageInspectionFindingKind.AnalyzerAssetLeak or
                PackageInspectionFindingKind.MissingAnalyzerAsset => options.FailOnAnalyzerAssetLeak,
            PackageInspectionFindingKind.PackageVersionMismatch or
                PackageInspectionFindingKind.PackageIdentityMismatch or
                PackageInspectionFindingKind.MissingPackageMetadata or
                PackageInspectionFindingKind.InvalidPackageMetadata or
                PackageInspectionFindingKind.MissingRequiredPackageAsset or
                PackageInspectionFindingKind.UnexpectedPackageAsset or
                PackageInspectionFindingKind.MissingDependencyGroup or
                PackageInspectionFindingKind.UnexpectedDependencyGroup or
                PackageInspectionFindingKind.MissingRequiredPackageDependency or
                PackageInspectionFindingKind.UnexpectedPackageDependency or
                PackageInspectionFindingKind.PackageDependencyVersionMismatch or
                PackageInspectionFindingKind.PackageDependencyExclusionMismatch or
                PackageInspectionFindingKind.BannedRuntimeDependency or
                PackageInspectionFindingKind.BannedRuntimeAsset or
                PackageInspectionFindingKind.OrphanSymbolPackage or
                PackageInspectionFindingKind.DuplicateSymbolPackage or
                PackageInspectionFindingKind.UnexpectedSymbolPackageAsset or
                PackageInspectionFindingKind.BannedSymbolPackageAsset or
                PackageInspectionFindingKind.InvalidManagedAssembly => true,
            _ => false
        };

    private void WriteReportArtifacts(PackageInspectionReport report)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };

        File.WriteAllText(
            Path.Combine(report.ReportDirectory, "report.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(report.ReportDirectory, "report.md"),
            ToMarkdown(report),
            Encoding.UTF8);
    }

    private static XDocument ReadNuspec(ZipArchive archive, string packagePath)
    {
        var entry = archive.Entries.FirstOrDefault(static entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new InvalidOperationException($"Package '{packagePath}' does not contain a .nuspec file.");

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string? ReadMetadataValue(XDocument nuspec, string name)
    {
        var ns = nuspec.Root?.GetDefaultNamespace() ?? XNamespace.None;
        return nuspec.Root?
            .Element(ns + "metadata")?
            .Element(ns + name)?
            .Value;
    }

    private static PackageMetadata ReadPackageMetadata(XDocument nuspec)
    {
        var ns = nuspec.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var metadata = nuspec.Root?.Element(ns + "metadata");
        var license = metadata?.Element(ns + "license");
        var repository = metadata?.Element(ns + "repository");

        return new PackageMetadata(
            metadata?.Element(ns + "id")?.Value,
            metadata?.Element(ns + "version")?.Value,
            metadata?.Element(ns + "description")?.Value,
            (string?)license?.Attribute("type"),
            license?.Value,
            metadata?.Element(ns + "readme")?.Value,
            (string?)repository?.Attribute("type"),
            (string?)repository?.Attribute("url"),
            (string?)repository?.Attribute("branch"),
            (string?)repository?.Attribute("commit"));
    }

    private static bool IsDotnetToolPackage(XDocument nuspec)
    {
        var ns = nuspec.Root?.GetDefaultNamespace() ?? XNamespace.None;
        return nuspec.Root?
            .Element(ns + "metadata")?
            .Element(ns + "packageTypes")?
            .Elements(ns + "packageType")
            .Any(static element => string.Equals((string?)element.Attribute("name"), "DotnetTool", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static IReadOnlyList<PackageDependencyGroup> ReadDependencyGroups(XDocument nuspec)
    {
        var ns = nuspec.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var dependencies = nuspec.Root?
            .Element(ns + "metadata")?
            .Element(ns + "dependencies");

        if (dependencies is null)
            return [];

        var groups = dependencies.Elements(ns + "group").ToArray();
        if (groups.Length == 0)
        {
            return
            [
                new PackageDependencyGroup(
                    "",
                    dependencies.Elements(ns + "dependency").Select(ReadDependency).ToArray())
            ];
        }

        return groups
            .Select(group => new PackageDependencyGroup(
                (string?)group.Attribute("targetFramework") ?? "",
                group.Elements(ns + "dependency").Select(ReadDependency).ToArray()))
            .ToArray();
    }

    private static PackageDependency ReadDependency(XElement dependency) =>
        new(
            (string?)dependency.Attribute("id") ?? "",
            (string?)dependency.Attribute("version") ?? "",
            (string?)dependency.Attribute("exclude"));

    private static PackageAssetSummary CreateAssetSummary(
        IReadOnlyList<string> entries,
        IReadOnlyList<string> symbolFiles)
    {
        var libFiles = entries
            .Where(static entry => entry.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var analyzerFiles = entries
            .Where(static entry => entry.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var toolFiles = entries
            .Where(static entry => entry.StartsWith("tools/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var runtimeFiles = entries
            .Where(static entry => entry.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return new PackageAssetSummary(
            libFiles.Length,
            analyzerFiles.Length,
            toolFiles.Length,
            runtimeFiles.Length,
            libFiles,
            analyzerFiles,
            toolFiles,
            runtimeFiles,
            symbolFiles,
            entries);
    }

    private static PackageInspectionSymbolPackageReport InspectSymbolPackage(string symbolPackagePath)
    {
        using var archive = ZipFile.OpenRead(symbolPackagePath);
        var allFiles = archive.Entries
            .Select(static entry => NormalizeEntryName(entry.FullName))
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pdbFiles = allFiles
            .Where(static entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var nuspec = ReadNuspec(archive, symbolPackagePath);
        var metadata = ReadPackageMetadata(nuspec);
        var id = metadata.Id ?? Path.GetFileNameWithoutExtension(symbolPackagePath);
        var version = metadata.Version ?? "unknown";
        return new PackageInspectionSymbolPackageReport(
            id,
            version,
            symbolPackagePath,
            metadata,
            pdbFiles,
            allFiles,
            ReadBinaryPayloadMatches(archive, id));
    }

    private static IReadOnlyList<PackagePayloadTokenMatch> ReadPayloadTokenMatches(
        ZipArchive archive,
        string packageId)
    {
        if (!packageId.Equals(PackageInspectionPolicy.MemoryPackageId, StringComparison.OrdinalIgnoreCase))
            return [];

        var matches = new List<PackagePayloadTokenMatch>();
        foreach (var entry in archive.Entries.Where(static entry =>
                     NormalizeEntryName(entry.FullName).StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var asset = NormalizeEntryName(entry.FullName);

            foreach (var token in PackageInspectionPolicy.MemoryBannedPayloadTokens)
            {
                if (ContainsToken(bytes, token))
                    matches.Add(new PackagePayloadTokenMatch(asset, token));
            }
        }

        return matches;
    }

    private static bool ContainsToken(byte[] bytes, string token) =>
        bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(token)) >= 0 ||
        bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(token)) >= 0;

    private static IReadOnlyList<PackageBinaryPayloadMatch> ReadBinaryPayloadMatches(
        ZipArchive archive,
        string packageId)
    {
        if (!packageId.Equals(PackageInspectionPolicy.MemoryPackageId, StringComparison.OrdinalIgnoreCase))
            return [];

        var matches = new List<PackageBinaryPayloadMatch>();
        foreach (var entry in archive.Entries.Where(static entry => entry.Length > 0))
        {
            var header = new byte[8];
            using var stream = entry.Open();
            var read = stream.Read(header, 0, header.Length);
            var signature = ClassifyExecutableSignature(header.AsSpan(0, read));
            if (signature is not null)
                matches.Add(new PackageBinaryPayloadMatch(NormalizeEntryName(entry.FullName), signature));
        }

        return matches;
    }

    private static IReadOnlyList<PackageManagedAssemblyInspection> ReadManagedAssemblyInspections(
        ZipArchive archive,
        string packageId)
    {
        if (!packageId.Equals(PackageInspectionPolicy.MemoryPackageId, StringComparison.OrdinalIgnoreCase))
            return [];

        var expectedAssets = PackageInspectionPolicy.MemoryTargetFrameworks
            .Select(static framework => $"lib/{framework}/DataLinq.Memory.dll")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return archive.Entries
            .Where(entry => expectedAssets.Contains(NormalizeEntryName(entry.FullName)))
            .Select(InspectManagedAssembly)
            .ToArray();
    }

    private static PackageManagedAssemblyInspection InspectManagedAssembly(ZipArchiveEntry entry)
    {
        var asset = NormalizeEntryName(entry.FullName);
        try
        {
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            buffer.Position = 0;

            using var peReader = new PEReader(buffer, PEStreamOptions.PrefetchMetadata);
            if (!peReader.HasMetadata)
                return new PackageManagedAssemblyInspection(asset, null, "PE image has no CLI metadata.");

            var metadataReader = peReader.GetMetadataReader();
            if (!metadataReader.IsAssembly)
                return new PackageManagedAssemblyInspection(asset, null, "CLI metadata does not contain an assembly definition.");

            var assemblyDefinition = metadataReader.GetAssemblyDefinition();
            var assemblyName = metadataReader.GetString(assemblyDefinition.Name);
            if (string.IsNullOrWhiteSpace(assemblyName))
                return new PackageManagedAssemblyInspection(asset, null, "Assembly definition name is missing.");

            return new PackageManagedAssemblyInspection(asset, assemblyName, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and
                                          not AccessViolationException and
                                          not OperationCanceledException)
        {
            return new PackageManagedAssemblyInspection(
                asset,
                null,
                $"{exception.GetType().Name} while reading PE/CLI metadata.");
        }
    }

    private static string? ClassifyExecutableSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z')
            return "PE/MZ";
        if (StartsWith(header, 0x7f, (byte)'E', (byte)'L', (byte)'F'))
            return "ELF";
        if (StartsWith(header, 0xfe, 0xed, 0xfa, 0xce) ||
            StartsWith(header, 0xfe, 0xed, 0xfa, 0xcf) ||
            StartsWith(header, 0xce, 0xfa, 0xed, 0xfe) ||
            StartsWith(header, 0xcf, 0xfa, 0xed, 0xfe) ||
            StartsWith(header, 0xca, 0xfe, 0xba, 0xbe))
        {
            return "Mach-O";
        }
        if (StartsWith(header, 0x00, 0x61, 0x73, 0x6d))
            return "WebAssembly";
        if (header.Length >= 8 &&
            header[0] == (byte)'!' && header[1] == (byte)'<' &&
            header[2] == (byte)'a' && header[3] == (byte)'r' &&
            header[4] == (byte)'c' && header[5] == (byte)'h' &&
            header[6] == (byte)'>' && header[7] == (byte)'\n')
            return "archive";

        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, byte first, byte second, byte third, byte fourth) =>
        header.Length >= 4 &&
        header[0] == first &&
        header[1] == second &&
        header[2] == third &&
        header[3] == fourth;

    private static string NormalizeEntryName(string entryName) =>
        entryName
            .Replace('\\', '/')
            .Trim('/');

    private static bool IsRoslynPackageId(string packageId) =>
        packageId.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemotionPackageId(string packageId) =>
        packageId.Equals("Remotion.Linq", StringComparison.OrdinalIgnoreCase) ||
        packageId.StartsWith("Remotion.", StringComparison.OrdinalIgnoreCase);

    private static bool IsMemoryBannedDependency(string packageId) =>
        PackageInspectionPolicy.MemoryBannedPayloadTokens.Any(token =>
            packageId.Equals(token, StringComparison.OrdinalIgnoreCase) ||
            packageId.StartsWith(token + ".", StringComparison.OrdinalIgnoreCase));

    private static bool IsAllowedMemoryRuntimePackageAsset(
        string asset,
        IReadOnlySet<string> expectedLibFiles)
    {
        var normalized = NormalizeEntryName(asset);
        return expectedLibFiles.Contains(normalized) ||
               normalized.Equals("DataLinq.Memory.nuspec", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(PackageInspectionPolicy.LicenseFile, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(PackageInspectionPolicy.ReadmeFile, StringComparison.OrdinalIgnoreCase) ||
               IsAllowedNuGetStructuralAsset(normalized);
    }

    private static bool IsAllowedMemorySymbolPackageAsset(
        string asset,
        IReadOnlySet<string> expectedPdbFiles)
    {
        var normalized = NormalizeEntryName(asset);
        return expectedPdbFiles.Contains(normalized) ||
               normalized.Equals("DataLinq.Memory.nuspec", StringComparison.OrdinalIgnoreCase) ||
               IsAllowedNuGetStructuralAsset(normalized);
    }

    private static bool IsAllowedNuGetStructuralAsset(string asset)
    {
        if (asset.Equals("_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
            asset.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
            asset.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string corePropertiesPrefix = "package/services/metadata/core-properties/";
        if (!asset.StartsWith(corePropertiesPrefix, StringComparison.OrdinalIgnoreCase) ||
            !asset.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = asset[corePropertiesPrefix.Length..];
        return fileName.Length > ".psmdcp".Length && !fileName.Contains('/');
    }

    private static bool HasExactBuildAnalyzerExclusion(string? exclude)
    {
        if (string.IsNullOrWhiteSpace(exclude))
            return false;

        var parts = exclude.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        return parts.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(["Build", "Analyzers"]);
    }

    private static string? NormalizeRepositoryUrl(string? repositoryUrl) =>
        repositoryUrl?.Trim().TrimEnd('/');

    private static string CreateReportDirectory(string artifactRoot)
    {
        var reportDirectory = Path.Combine(
            artifactRoot,
            "package-report",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(reportDirectory);
        return reportDirectory;
    }

    private static string EscapeTable(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

}
