using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public sealed class PackageInspector
{
    private const string SchemaVersion = "phase8c.package-inspection-report.v1";

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
        var packages = Directory.Exists(packageDirectory)
            ? Directory.EnumerateFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly)
                .Where(static path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(InspectPackage)
                .ToArray()
            : [];

        var findings = CreateFindings(packages);
        var report = new PackageInspectionReport(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            options.RepositoryRoot,
            packageDirectory,
            reportDirectory,
            packages,
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

    private PackageInspectionPackageReport InspectPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Select(static entry => NormalizeEntryName(entry.FullName))
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nuspec = ReadNuspec(archive, packagePath);
        var id = ReadMetadataValue(nuspec, "id") ?? Path.GetFileNameWithoutExtension(packagePath);
        var version = ReadMetadataValue(nuspec, "version") ?? "unknown";
        var symbolPackagePath = FindSymbolPackagePath(packagePath);
        var symbolFiles = symbolPackagePath is null ? [] : ReadSymbolFiles(symbolPackagePath);
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
            ReadDependencyGroups(nuspec),
            CreateAssetSummary(entries, symbolFiles));
    }

    private IReadOnlyList<PackageInspectionFinding> CreateFindings(
        IReadOnlyList<PackageInspectionPackageReport> packages)
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

            if (package.Id.Equals("DataLinq", StringComparison.OrdinalIgnoreCase) &&
                !package.Assets.AnalyzerFiles.Any(static file => file.Equals("analyzers/dotnet/cs/DataLinq.Generators.dll", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.MissingAnalyzerAsset,
                    package.Id,
                    null,
                    "DataLinq package does not contain the generated model source generator under analyzers/dotnet/cs."));
            }

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
            PackageInspectionFindingKind.AnalyzerAssetLeak or
                PackageInspectionFindingKind.MissingAnalyzerAsset => options.FailOnAnalyzerAssetLeak,
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
            symbolFiles);
    }

    private static string? FindSymbolPackagePath(string packagePath)
    {
        var directory = Path.GetDirectoryName(packagePath);
        if (directory is null)
            return null;

        var fileName = Path.GetFileNameWithoutExtension(packagePath) + ".snupkg";
        var symbolPackagePath = Path.Combine(directory, fileName);
        return File.Exists(symbolPackagePath) ? symbolPackagePath : null;
    }

    private static IReadOnlyList<string> ReadSymbolFiles(string symbolPackagePath)
    {
        using var archive = ZipFile.OpenRead(symbolPackagePath);
        return archive.Entries
            .Select(static entry => NormalizeEntryName(entry.FullName))
            .Where(static entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeEntryName(string entryName) =>
        entryName
            .Replace('\\', '/')
            .Trim('/');

    private static bool IsRoslynPackageId(string packageId) =>
        packageId.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase);

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
