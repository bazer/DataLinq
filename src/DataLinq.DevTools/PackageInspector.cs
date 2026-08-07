using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace DataLinq.DevTools;

public sealed class PackageInspector
{
    public const string SchemaVersion = "v0.9.package-inspection-report.v4";

    private const string CandidateAggregateFormat = "DataLinq package inspection candidate v1";
    private const string ExpectedEntryAssemblyName = "DataLinq.Dev.CLI";
    private const string ExpectedDevToolsAssemblyName = "DataLinq.DevTools";
    private const string CleanRepositoryBuildState = "clean";
    private const int MaximumArchivePathCharacters = 1024;
    private const string NuspecNamespace2012 = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd";
    private const string NuspecNamespace2013 = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly HashSet<string> SupportedNuspecNamespaceUris = new(StringComparer.Ordinal)
    {
        NuspecNamespace2012,
        NuspecNamespace2013
    };

    private readonly DevToolPaths paths;
    private readonly PackageInspectionOptions options;
    private readonly Func<string, TestRunSummaryRepositoryState> captureRepositoryState;
    private readonly Func<(TestRunSummaryRunnerAssembly EntryAssembly, TestRunSummaryRunnerAssembly DevToolsAssembly)> captureRunnerAssemblies;

    public PackageInspector(DevToolPaths paths, PackageInspectionOptions options)
        : this(
            paths,
            options,
            TestRunSummaryReporter.CaptureRepositoryState,
            TestRunSummaryReporter.CaptureRunnerAssemblies)
    {
    }

    internal PackageInspector(
        DevToolPaths paths,
        PackageInspectionOptions options,
        Func<string, TestRunSummaryRepositoryState> captureRepositoryState,
        Func<(TestRunSummaryRunnerAssembly EntryAssembly, TestRunSummaryRunnerAssembly DevToolsAssembly)> captureRunnerAssemblies)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(captureRepositoryState);
        ArgumentNullException.ThrowIfNull(captureRunnerAssemblies);
        this.paths = paths;
        this.options = NormalizeOptions(paths, options);
        this.captureRepositoryState = captureRepositoryState;
        this.captureRunnerAssemblies = captureRunnerAssemblies;
    }

    public static void InvalidateExistingReportDirectory(
        string repositoryRoot,
        string packageDirectory,
        string reportDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportDirectory);
        var normalizedRepositoryRoot = NormalizeDirectory(repositoryRoot, Environment.CurrentDirectory);
        var normalizedPackageDirectory = NormalizeDirectory(packageDirectory, normalizedRepositoryRoot);
        var normalizedReportDirectory = NormalizeDirectory(reportDirectory, normalizedRepositoryRoot);
        ValidateReportDirectoryBoundary(
            normalizedRepositoryRoot,
            normalizedPackageDirectory,
            normalizedReportDirectory);
        if (!Directory.Exists(normalizedReportDirectory))
            return;

        ClearKnownReportArtifacts(normalizedReportDirectory);
    }

    public PackageInspectionReport CreateReport()
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var repositoryStart = captureRepositoryState(options.RepositoryRoot);
        var runnerAssemblies = captureRunnerAssemblies();

        var packageDirectory = options.PackageDirectory;
        var reportDirectory = PrepareReportDirectory(
            options.RepositoryRoot,
            options.OutputDirectory ?? CreateReportDirectory(paths.ArtifactRoot));
        var artifacts = new PackageInspectionArtifacts(
            Path.Combine(reportDirectory, "report.json"),
            Path.Combine(reportDirectory, "report.md"));
        var invocation = CreateInvocation(reportDirectory);
        var stage = "validate-inputs";

        try
        {
            ValidatePackageDirectory(packageDirectory, reportDirectory);
            var archivePaths = EnumeratePackageArchives(packageDirectory);
            stage = "inspect-symbol-packages";
            var symbolPackages = archivePaths
                .Where(static path => path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
                .Select(InspectSymbolPackage)
                .ToArray();
            stage = "inspect-packages";
            var packages = archivePaths
                .Where(static path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                .Select(path => InspectPackage(path, symbolPackages))
                .ToArray();

            stage = "classify-findings";
            var findings = CreateFindings(packages, symbolPackages).ToList();
            AddArchiveStabilityFindings(packageDirectory, archivePaths, packages, symbolPackages, findings);
            var classifiedFindings = findings
                .Select(finding => finding with { IsHardFailure = IsHardFailure(finding) })
                .ToArray();
            var candidate = CreateCandidateIdentity(packages, symbolPackages, classifiedFindings);
            var summary = CreateSummary(packages, classifiedFindings);
            var completedAtUtc = DateTimeOffset.UtcNow;
            var repositoryEnd = captureRepositoryState(options.RepositoryRoot);
            var runner = EvaluateRunnerEvidence(
                repositoryStart,
                repositoryEnd,
                runnerAssemblies.EntryAssembly,
                runnerAssemblies.DevToolsAssembly,
                candidate.RepositoryCommit);
            var canonicalPolicy = IsCanonicalReleasePolicy(invocation);
            var sourceIsRepositoryArtifact = IsPathStrictlyWithin(
                packageDirectory,
                Path.Combine(options.RepositoryRoot, "artifacts"));
            var outcome = summary.HasHardFailures
                ? PackageInspectionOutcome.Failed
                : PackageInspectionOutcome.Passed;
            var validForEvidence = EvaluateValidForEvidence(
                outcome,
                true,
                true,
                canonicalPolicy,
                sourceIsRepositoryArtifact,
                candidate,
                runner);
            var report = new PackageInspectionReport(
                SchemaVersion,
                completedAtUtc,
                options.RepositoryRoot,
                packageDirectory,
                reportDirectory,
                Array.AsReadOnly(packages),
                Array.AsReadOnly(symbolPackages),
                Array.AsReadOnly(classifiedFindings),
                summary)
            {
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                DurationSeconds = Math.Round((completedAtUtc - startedAtUtc).TotalSeconds, 3),
                Invocation = invocation,
                Outcome = outcome,
                InspectionComplete = true,
                ArtifactsComplete = true,
                IsCanonicalReleasePolicy = canonicalPolicy,
                PackageDirectoryIsRepositoryArtifact = sourceIsRepositoryArtifact,
                ValidForEvidence = validForEvidence,
                Artifacts = artifacts,
                Candidate = candidate,
                Runner = runner
            };

            stage = "write-report";
            WriteReportArtifacts(report);
            return report;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and
                                          not AccessViolationException and
                                          not OperationCanceledException)
        {
            var completedAtUtc = DateTimeOffset.UtcNow;
            var repositoryEnd = captureRepositoryState(options.RepositoryRoot);
            var runner = EvaluateRunnerEvidence(
                repositoryStart,
                repositoryEnd,
                runnerAssemblies.EntryAssembly,
                runnerAssemblies.DevToolsAssembly,
                candidateRepositoryCommit: null);
            var failureMessage = TestRunSummaryReporter.SanitizeFailureMessage(exception.Message);
            var failureFinding = new PackageInspectionFinding(
                PackageInspectionFindingKind.InspectionError,
                "<inspection>",
                null,
                failureMessage)
            {
                IsHardFailure = true
            };
            var errorReport = new PackageInspectionReport(
                SchemaVersion,
                completedAtUtc,
                options.RepositoryRoot,
                packageDirectory,
                reportDirectory,
                Array.Empty<PackageInspectionPackageReport>(),
                Array.Empty<PackageInspectionSymbolPackageReport>(),
                [failureFinding],
                new PackageInspectionSummary(
                    PackageCount: 0,
                    ExpectedPackageCount: options.ExpectedPackageIds.Count,
                    RuntimePackageCount: 0,
                    FindingCount: 1,
                    HardFailureCount: 1,
                    HasHardFailures: true))
            {
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                DurationSeconds = Math.Round((completedAtUtc - startedAtUtc).TotalSeconds, 3),
                Invocation = invocation,
                Outcome = PackageInspectionOutcome.Error,
                InspectionComplete = false,
                ArtifactsComplete = true,
                IsCanonicalReleasePolicy = IsCanonicalReleasePolicy(invocation),
                PackageDirectoryIsRepositoryArtifact = IsPathStrictlyWithin(
                    packageDirectory,
                    Path.Combine(options.RepositoryRoot, "artifacts")),
                ValidForEvidence = false,
                Artifacts = artifacts,
                Candidate = CreateCandidateIdentity([], [], [failureFinding]),
                Runner = runner,
                Failure = new PackageInspectionFailure(
                    stage,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    failureMessage)
            };

            try
            {
                WriteReportArtifacts(errorReport);
            }
            catch (Exception reportException)
            {
                throw new AggregateException(
                    "Package inspection failed and its error report could not be written.",
                    exception,
                    reportException);
            }

            throw;
        }
    }

    public static string ToMarkdown(PackageInspectionReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Package Inspection Report");
        builder.AppendLine();
        builder.AppendLine($"- schema: {MarkdownCode(report.SchemaVersion)}");
        builder.AppendLine($"- outcome: {MarkdownCode(report.Outcome.ToString())}");
        builder.AppendLine($"- valid for release evidence: {MarkdownCode(report.ValidForEvidence.ToString())}");
        builder.AppendLine($"- inspection complete: {MarkdownCode(report.InspectionComplete.ToString())}");
        builder.AppendLine($"- artifacts complete: {MarkdownCode(report.ArtifactsComplete.ToString())}");
        builder.AppendLine($"- started UTC: {MarkdownCode(report.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture))}");
        builder.AppendLine($"- completed UTC: {MarkdownCode(report.CompletedAtUtc.ToString("O", CultureInfo.InvariantCulture))}");
        builder.AppendLine($"- duration seconds: {MarkdownCode(report.DurationSeconds.ToString("0.000", CultureInfo.InvariantCulture))}");
        builder.AppendLine($"- repository root: {MarkdownCode(report.RepositoryRoot)}");
        builder.AppendLine($"- package directory: {MarkdownCode(report.PackageDirectory)}");
        builder.AppendLine($"- report JSON: {MarkdownCode(report.Artifacts.JsonPath)}");
        builder.AppendLine($"- report Markdown: {MarkdownCode(report.Artifacts.MarkdownPath)}");
        builder.AppendLine();
        builder.AppendLine("## Invocation and policy");
        builder.AppendLine();
        builder.AppendLine($"- command: {MarkdownCode(report.Invocation.Command)}");
        builder.AppendLine($"- expected version: {MarkdownCode(report.Invocation.ExpectedVersion ?? "not supplied")}");
        builder.AppendLine($"- output format: {MarkdownCode(report.Invocation.OutputFormat)}");
        builder.AppendLine($"- expected package ids: {MarkdownCode(string.Join(", ", report.Invocation.ExpectedPackageIds))}");
        builder.AppendLine($"- runtime package ids: {MarkdownCode(string.Join(", ", report.Invocation.RuntimePackageIds))}");
        builder.AppendLine($"- fail on unexpected package: {MarkdownCode(report.Invocation.FailOnUnexpectedPackage.ToString())}");
        builder.AppendLine($"- fail on missing symbols: {MarkdownCode(report.Invocation.FailOnMissingSymbolPackage.ToString())}");
        builder.AppendLine($"- fail on runtime Roslyn: {MarkdownCode(report.Invocation.FailOnRuntimeRoslyn.ToString())}");
        builder.AppendLine($"- fail on runtime Remotion: {MarkdownCode(report.Invocation.FailOnRuntimeRemotion.ToString())}");
        builder.AppendLine($"- fail on analyzer leaks: {MarkdownCode(report.Invocation.FailOnAnalyzerAssetLeak.ToString())}");
        builder.AppendLine($"- canonical release policy: {MarkdownCode(report.IsCanonicalReleasePolicy.ToString())}");
        builder.AppendLine();
        builder.AppendLine("## Candidate identity");
        builder.AppendLine();
        builder.AppendLine($"- aggregate SHA-256: {MarkdownCode(report.Candidate.AggregateSha256)}");
        builder.AppendLine($"- aligned version: {MarkdownCode(report.Candidate.Version ?? "unavailable")}");
        builder.AppendLine($"- requested version is exact: {MarkdownCode(report.Candidate.VersionConsistent.ToString())}");
        builder.AppendLine($"- repository commit: {MarkdownCode(report.Candidate.RepositoryCommit ?? "unavailable")}");
        builder.AppendLine($"- repository commit is coherent: {MarkdownCode(report.Candidate.RepositoryCommitConsistent.ToString())}");
        builder.AppendLine($"- archives stable during inspection: {MarkdownCode(report.Candidate.ArchivesStable.ToString())}");
        builder.AppendLine($"- package directory is a repository artifact: {MarkdownCode(report.PackageDirectoryIsRepositoryArtifact.ToString())}");
        builder.AppendLine();
        builder.AppendLine("## Runner evidence");
        builder.AppendLine();
        builder.AppendLine($"- start checkout: {MarkdownCode(report.Runner.Start.Commit)} ({MarkdownCode(report.Runner.Start.Branch)}), captured {MarkdownCode(report.Runner.Start.Captured.ToString())}, dirty {MarkdownCode(report.Runner.Start.Dirty.ToString())}");
        builder.AppendLine($"- end checkout: {MarkdownCode(report.Runner.End.Commit)} ({MarkdownCode(report.Runner.End.Branch)}), captured {MarkdownCode(report.Runner.End.Captured.ToString())}, dirty {MarkdownCode(report.Runner.End.Dirty.ToString())}");
        builder.AppendLine($"- checkout changed during inspection: {MarkdownCode(report.Runner.StateChangedDuringRun.ToString())}");
        builder.AppendLine($"- runner assemblies match checkout: {MarkdownCode(report.Runner.AssembliesMatchCheckout.ToString())}");
        builder.AppendLine($"- runner assemblies built clean: {MarkdownCode(report.Runner.AssembliesBuiltFromCleanState.ToString())}");
        builder.AppendLine($"- candidate commit matches checkout: {MarkdownCode(report.Runner.CandidateMatchesCheckout.ToString())}");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- package archives inspected: {MarkdownCode(report.Summary.PackageCount.ToString(CultureInfo.InvariantCulture))}");
        builder.AppendLine($"- configured expected package ids: {MarkdownCode(report.Summary.ExpectedPackageCount.ToString(CultureInfo.InvariantCulture))}");
        builder.AppendLine($"- symbol archives inspected: {MarkdownCode(report.SymbolPackages.Count.ToString(CultureInfo.InvariantCulture))}");
        builder.AppendLine($"- runtime packages: {MarkdownCode(report.Summary.RuntimePackageCount.ToString(CultureInfo.InvariantCulture))}");
        builder.AppendLine($"- findings: {MarkdownCode(report.Summary.FindingCount.ToString(CultureInfo.InvariantCulture))} total, {MarkdownCode(report.Summary.HardFailureCount.ToString(CultureInfo.InvariantCulture))} hard");
        if (report.Failure is not null)
        {
            builder.AppendLine($"- failure stage: {MarkdownCode(report.Failure.Stage)}");
            builder.AppendLine($"- failure type: {MarkdownCode(report.Failure.ExceptionType)}");
            builder.AppendLine($"- failure message: {MarkdownText(report.Failure.Message)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Packages");
        builder.AppendLine();
        builder.AppendLine("| Package | Version | Bytes | SHA-256 | Runtime | Tool | Symbols | lib | analyzers | tools | runtimes |");
        builder.AppendLine("| --- | --- | ---: | --- | --- | --- | --- | ---: | ---: | ---: | ---: |");

        foreach (var package in report.Packages)
        {
            builder.AppendLine(string.Join(" | ", [
                $"| {MarkdownCode(package.Id)}",
                MarkdownCode(package.Version),
                package.SizeBytes.ToString(CultureInfo.InvariantCulture),
                MarkdownCode(package.Sha256),
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
        builder.AppendLine("| Package | Version | Bytes | SHA-256 | PDBs | Entries |");
        builder.AppendLine("| --- | --- | ---: | --- | ---: | ---: |");

        foreach (var symbolPackage in report.SymbolPackages)
        {
            builder.AppendLine(
                $"| {MarkdownCode(symbolPackage.Id)} | {MarkdownCode(symbolPackage.Version)} | " +
                $"{symbolPackage.SizeBytes.ToString(CultureInfo.InvariantCulture)} | {MarkdownCode(symbolPackage.Sha256)} | " +
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
                    : $" {MarkdownCode(finding.TargetFramework)}";
                builder.AppendLine($"- {MarkdownCode(finding.Kind.ToString())} (hard: {MarkdownCode(finding.IsHardFailure.ToString())}) {MarkdownCode(finding.PackageId)}{target}: {MarkdownText(finding.Message)}");
            }
        }

        foreach (var package in report.Packages)
        {
            builder.AppendLine();
            builder.AppendLine($"## {MarkdownText(package.Id)}");
            builder.AppendLine();
            builder.AppendLine($"- description: {MarkdownCode(package.Metadata.Description ?? "missing")}");
            builder.AppendLine($"- repository: {MarkdownCode(package.Metadata.RepositoryUrl ?? "missing")}");
            builder.AppendLine($"- repository commit: {MarkdownCode(package.Metadata.RepositoryCommit ?? "missing")}");
            builder.AppendLine($"- license: {MarkdownCode(package.Metadata.License ?? "missing")}");
            builder.AppendLine($"- readme: {MarkdownCode(package.Metadata.Readme ?? "missing")}");
            builder.AppendLine();

            foreach (var group in package.DependencyGroups)
            {
                builder.AppendLine($"### {MarkdownText(group.TargetFramework)}");
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
                        : $", exclude {MarkdownCode(dependency.Exclude)}";
                    builder.AppendLine($"- {MarkdownCode(dependency.Id)} {MarkdownCode(dependency.Version)}{exclude}");
                }
            }
        }

        return builder.ToString();
    }

    private PackageInspectionPackageReport InspectPackage(
        string packagePath,
        IReadOnlyList<PackageInspectionSymbolPackageReport> symbolPackages)
    {
        var canonicalPath = Path.GetFullPath(packagePath);
        RejectReparsePointTraversal(canonicalPath, "package archive");
        using var stream = OpenPackageArchive(canonicalPath);
        var sizeBytes = stream.Length;
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        ValidateArchive(archive, canonicalPath);
        var entries = archive.Entries
            .Select(static entry => NormalizeEntryName(entry.FullName))
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var nuspec = ReadNuspec(archive, canonicalPath, out var nuspecPath);
        var metadata = ReadPackageMetadata(nuspec);
        ValidateNuspecPath(nuspecPath, metadata.Id, canonicalPath);
        var id = metadata.Id ?? Path.GetFileNameWithoutExtension(canonicalPath);
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
            canonicalPath,
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
            ReadManagedAssemblyInspections(archive, id))
        {
            SizeBytes = sizeBytes,
            Sha256 = sha256
        };
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

    private void AddVersionAlignmentFindings(
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

        if (versions.Length > 1)
        {
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

        if (options.ExpectedVersion is null)
            return;

        foreach (var package in expectedPackages.Where(package =>
                     !package.Version.Equals(options.ExpectedVersion, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.PackageVersionMismatch,
                package.Id,
                null,
                $"Package version '{package.Version}' does not match requested candidate version '{options.ExpectedVersion}'."));
        }
    }

    private void AddSymbolPackageFindings(
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
            if (options.ExpectedVersion is not null &&
                options.ExpectedPackageIds.Contains(symbolPackage.Id) &&
                !symbolPackage.Version.Equals(options.ExpectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.PackageVersionMismatch,
                    symbolPackage.Id,
                    null,
                    $"Symbol package version '{symbolPackage.Version}' does not match requested candidate version '{options.ExpectedVersion}'."));
            }

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

        AddRequiredSymbolMetadataFinding(symbolPackage, findings, "repository type", symbolPackage.Metadata.RepositoryType);
        AddRequiredSymbolMetadataFinding(symbolPackage, findings, "repository URL", symbolPackage.Metadata.RepositoryUrl);
        AddRequiredSymbolMetadataFinding(symbolPackage, findings, "repository commit", symbolPackage.Metadata.RepositoryCommit);
        AddExactSymbolMetadataFinding(symbolPackage, findings, "repository type", symbolPackage.Metadata.RepositoryType, "git");
        AddExactSymbolMetadataFinding(
            symbolPackage,
            findings,
            "repository URL",
            NormalizeRepositoryUrl(symbolPackage.Metadata.RepositoryUrl),
            PackageInspectionPolicy.RepositoryUrl);
        if (!string.IsNullOrWhiteSpace(symbolPackage.Metadata.RepositoryCommit) &&
            !IsFullGitObjectId(symbolPackage.Metadata.RepositoryCommit.Trim()))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.InvalidPackageMetadata,
                symbolPackage.Id,
                null,
                $"Symbol package nuspec repository commit '{symbolPackage.Metadata.RepositoryCommit}' is not a full Git object id."));
        }
    }

    private static void AddRequiredSymbolMetadataFinding(
        PackageInspectionSymbolPackageReport package,
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
            $"Symbol package nuspec is missing required {field} metadata."));
    }

    private static void AddExactSymbolMetadataFinding(
        PackageInspectionSymbolPackageReport package,
        ICollection<PackageInspectionFinding> findings,
        string field,
        string? actual,
        string expected)
    {
        if (string.IsNullOrWhiteSpace(actual) ||
            actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        findings.Add(new PackageInspectionFinding(
            PackageInspectionFindingKind.InvalidPackageMetadata,
            package.Id,
            null,
            $"Symbol package nuspec {field} is '{actual}'; expected '{expected}'."));
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
        if (!string.IsNullOrWhiteSpace(package.Metadata.RepositoryCommit) &&
            !IsFullGitObjectId(package.Metadata.RepositoryCommit.Trim()))
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.InvalidPackageMetadata,
                package.Id,
                null,
                $"Package nuspec repository commit '{package.Metadata.RepositoryCommit}' is not a full Git object id."));
        }

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
        var hardFailureCount = findings.Count(static finding => finding.IsHardFailure);
        return new PackageInspectionSummary(
            packages.Count,
            options.ExpectedPackageIds.Count,
            packages.Count(static package => package.IsRuntimePackage),
            findings.Count,
            hardFailureCount,
            hardFailureCount > 0);
    }

    internal bool IsHardFailure(PackageInspectionFinding finding) =>
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
                PackageInspectionFindingKind.InvalidManagedAssembly or
                PackageInspectionFindingKind.PackageArchiveChanged or
                PackageInspectionFindingKind.InspectionError => true,
            _ => true
        };

    private void WriteReportArtifacts(PackageInspectionReport report)
    {
        var reportDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(report.ReportDirectory));
        var expectedJsonPath = Path.Combine(reportDirectory, "report.json");
        var expectedMarkdownPath = Path.Combine(reportDirectory, "report.md");
        if (!PathEquals(report.Artifacts.JsonPath, expectedJsonPath) ||
            !PathEquals(report.Artifacts.MarkdownPath, expectedMarkdownPath))
        {
            throw new InvalidDataException("Package inspection artifact paths do not match the guarded report directory.");
        }

        if (!IsPathStrictlyWithin(reportDirectory, Path.Combine(options.RepositoryRoot, "artifacts")))
            throw new InvalidDataException("Package inspection report directory escaped the repository artifact root.");
        RejectReparsePointTraversal(reportDirectory, "report directory");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };
        var suffix = Guid.NewGuid().ToString("N");
        var temporaryJsonPath = Path.Combine(reportDirectory, $".report-{suffix}.json.tmp");
        var temporaryMarkdownPath = Path.Combine(reportDirectory, $".report-{suffix}.md.tmp");
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            File.WriteAllText(temporaryMarkdownPath, ToMarkdown(report), utf8NoBom);
            File.WriteAllText(temporaryJsonPath, JsonSerializer.Serialize(report, jsonOptions), utf8NoBom);
            RejectReparsePointTraversal(reportDirectory, "report directory");
            RejectReparsePointTraversal(expectedMarkdownPath, "Markdown report");
            File.Move(temporaryMarkdownPath, expectedMarkdownPath, overwrite: true);
            RejectReparsePointTraversal(reportDirectory, "report directory");
            RejectReparsePointTraversal(expectedJsonPath, "JSON report");
            File.Move(temporaryJsonPath, expectedJsonPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryMarkdownPath))
                File.Delete(temporaryMarkdownPath);
            if (File.Exists(temporaryJsonPath))
                File.Delete(temporaryJsonPath);
        }
    }

    private static XDocument ReadNuspec(
        ZipArchive archive,
        string packagePath,
        out string nuspecPath)
    {
        var entries = archive.Entries
            .Where(static entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (entries.Length != 1)
        {
            throw new InvalidDataException(
                $"Package '{packagePath}' must contain exactly one .nuspec file; found {entries.Length.ToString(CultureInfo.InvariantCulture)}.");
        }

        var entry = entries[0];
        if (!TryNormalizeArchivePath(entry.FullName, out nuspecPath, out var reason))
            throw new InvalidDataException($"Nuspec path '{entry.FullName}' is invalid: {reason}.");
        if (entry.Length > PackageInspectionPolicy.MaximumNuspecBytes)
        {
            throw new InvalidDataException(
                $"Nuspec '{entry.FullName}' exceeds the {PackageInspectionPolicy.MaximumNuspecBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = PackageInspectionPolicy.MaximumNuspecBytes
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

        var metadataElements = root.Elements()
            .Where(static element => element.Name.LocalName.Equals("metadata", StringComparison.Ordinal))
            .ToArray();
        if (metadataElements.Length != 1 || metadataElements[0].Name.Namespace != root.Name.Namespace)
            throw new InvalidDataException("Nuspec must contain exactly one direct metadata element in the package namespace.");

        return document;
    }

    private static void ValidateNuspecPath(string nuspecPath, string? packageId, string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return;
        var expectedPath = $"{packageId}.nuspec";
        if (!nuspecPath.Equals(expectedPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Package '{Path.GetFileName(packagePath)}' has nuspec path '{nuspecPath}'; expected exact root path '{expectedPath}'.");
        }
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
        var canonicalPath = Path.GetFullPath(symbolPackagePath);
        RejectReparsePointTraversal(canonicalPath, "symbol package archive");
        using var stream = OpenPackageArchive(canonicalPath);
        var sizeBytes = stream.Length;
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        ValidateArchive(archive, canonicalPath);
        var allFiles = archive.Entries
            .Select(static entry => NormalizeEntryName(entry.FullName))
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pdbFiles = allFiles
            .Where(static entry => entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var nuspec = ReadNuspec(archive, canonicalPath, out var nuspecPath);
        var metadata = ReadPackageMetadata(nuspec);
        ValidateNuspecPath(nuspecPath, metadata.Id, canonicalPath);
        var id = metadata.Id ?? Path.GetFileNameWithoutExtension(canonicalPath);
        var version = metadata.Version ?? "unknown";
        return new PackageInspectionSymbolPackageReport(
            id,
            version,
            canonicalPath,
            metadata,
            pdbFiles,
            allFiles,
            ReadBinaryPayloadMatches(archive, id))
        {
            SizeBytes = sizeBytes,
            Sha256 = sha256
        };
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
            EnsureEntryWithinManagedAssetLimit(entry);
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            CopyToBounded(stream, buffer, PackageInspectionPolicy.MaximumPrimaryManagedAssetBytes);
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
            EnsureEntryWithinManagedAssetLimit(entry);
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            CopyToBounded(entryStream, buffer, PackageInspectionPolicy.MaximumPrimaryManagedAssetBytes);
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

    private static PackageInspectionOptions NormalizeOptions(
        DevToolPaths paths,
        PackageInspectionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PackageDirectory);

        var repositoryRoot = NormalizeDirectory(options.RepositoryRoot, Environment.CurrentDirectory);
        var configuredRepositoryRoot = NormalizeDirectory(paths.RepositoryRoot, Environment.CurrentDirectory);
        if (!PathEquals(repositoryRoot, configuredRepositoryRoot))
        {
            throw new ArgumentException(
                $"Package inspection repository root '{repositoryRoot}' does not match the configured tooling root '{configuredRepositoryRoot}'.",
                nameof(options));
        }
        if (!Directory.Exists(repositoryRoot))
            throw new DirectoryNotFoundException($"Package inspection repository root '{repositoryRoot}' does not exist.");
        RejectReparsePointTraversal(repositoryRoot, "repository root");

        var packageDirectory = NormalizeDirectory(options.PackageDirectory, repositoryRoot);
        var expectedPackageIds = NormalizePackageIds(options.ExpectedPackageIds, "expected package", requireAny: true);
        var runtimePackageIds = NormalizePackageIds(options.RuntimePackageIds, "runtime package", requireAny: false);
        if (options.ExpectedVersion is not null &&
            (string.IsNullOrWhiteSpace(options.ExpectedVersion) ||
             !options.ExpectedVersion.Equals(options.ExpectedVersion.Trim(), StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Expected package version must be nonblank and contain no surrounding whitespace when supplied.",
                nameof(options));
        }
        var expectedVersion = options.ExpectedVersion;
        if (expectedVersion is not null && !IsValidPackageVersion(expectedVersion))
        {
            throw new ArgumentException(
                $"Package version '{options.ExpectedVersion}' is not a valid exact package version.",
                nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputFormat);
        var outputFormat = options.OutputFormat.Trim().ToLowerInvariant();
        if (outputFormat is not ("summary" or "json" or "markdown"))
        {
            throw new ArgumentException(
                $"Unsupported package report format '{options.OutputFormat}'. Expected summary, json, or markdown.",
                nameof(options));
        }

        var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? null
            : NormalizeDirectory(options.OutputDirectory, repositoryRoot);
        return options with
        {
            RepositoryRoot = repositoryRoot,
            PackageDirectory = packageDirectory,
            ExpectedPackageIds = expectedPackageIds,
            RuntimePackageIds = runtimePackageIds,
            ExpectedVersion = expectedVersion,
            OutputDirectory = outputDirectory,
            OutputFormat = outputFormat
        };
    }

    private static IReadOnlySet<string> NormalizePackageIds(
        IReadOnlySet<string> packageIds,
        string label,
        bool requireAny)
    {
        ArgumentNullException.ThrowIfNull(packageIds);
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in packageIds)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"The {label} id set contains a blank value.", nameof(packageIds));

            var trimmed = value.Trim();
            if (!IsValidPackageId(trimmed))
                throw new ArgumentException($"Package id '{value}' is not a valid exact package id.", nameof(packageIds));
            var canonical = PackageInspectionPolicy.PublicPackageIds.FirstOrDefault(id =>
                id.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            normalized.Add(canonical ?? trimmed);
        }

        if (requireAny && normalized.Count == 0)
            throw new ArgumentException($"At least one {label} id is required.", nameof(packageIds));

        return normalized;
    }

    private string PrepareReportDirectory(string repositoryRoot, string requestedDirectory)
    {
        var reportDirectory = NormalizeDirectory(requestedDirectory, repositoryRoot);
        ValidateReportDirectoryBoundary(repositoryRoot, options.PackageDirectory, reportDirectory);
        if (File.Exists(reportDirectory))
            throw new InvalidDataException($"Package inspection output '{reportDirectory}' is a file, not a directory.");

        Directory.CreateDirectory(reportDirectory);
        RejectReparsePointTraversal(reportDirectory, "report directory");
        ClearKnownReportArtifacts(reportDirectory);

        return reportDirectory;
    }

    private static void ValidateReportDirectoryBoundary(
        string repositoryRoot,
        string packageDirectory,
        string reportDirectory)
    {
        var artifactRoot = Path.Combine(repositoryRoot, "artifacts");
        if (!IsPathStrictlyWithin(reportDirectory, artifactRoot))
        {
            throw new InvalidDataException(
                $"Package inspection output '{reportDirectory}' must remain below repository artifact root '{artifactRoot}'.");
        }
        if (PathsOverlap(reportDirectory, packageDirectory))
        {
            throw new InvalidDataException(
                $"Package inspection output '{reportDirectory}' must not overlap package input '{packageDirectory}'.");
        }

        RejectReparsePointTraversal(reportDirectory, "report directory");
        if (File.Exists(reportDirectory))
            throw new InvalidDataException($"Package inspection output '{reportDirectory}' is a file, not a directory.");
    }

    private static void ClearKnownReportArtifacts(string reportDirectory)
    {
        RejectReparsePointTraversal(reportDirectory, "report directory");
        var entries = Directory.EnumerateFileSystemEntries(reportDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
        foreach (var entry in entries)
        {
            var fileName = Path.GetFileName(entry);
            if (fileName is not ("report.json" or "report.md") ||
                !File.Exists(entry) ||
                (File.GetAttributes(entry) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidDataException(
                    $"Package inspection output '{reportDirectory}' must be empty or contain only prior regular report.json/report.md files.");
            }
        }

        foreach (var entry in entries)
            File.Delete(entry);
    }

    private PackageInspectionInvocation CreateInvocation(string reportDirectory) =>
        new(
            Command: "package-report",
            RepositoryRoot: options.RepositoryRoot,
            PackageDirectory: options.PackageDirectory,
            ReportDirectory: reportDirectory,
            ExpectedVersion: options.ExpectedVersion,
            OutputFormat: options.OutputFormat,
            ExpectedPackageIds: Array.AsReadOnly(options.ExpectedPackageIds
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static id => id, StringComparer.Ordinal)
                .ToArray()),
            RuntimePackageIds: Array.AsReadOnly(options.RuntimePackageIds
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static id => id, StringComparer.Ordinal)
                .ToArray()),
            FailOnUnexpectedPackage: options.FailOnUnexpectedPackage,
            FailOnMissingSymbolPackage: options.FailOnMissingSymbolPackage,
            FailOnRuntimeRoslyn: options.FailOnRuntimeRoslyn,
            FailOnRuntimeRemotion: options.FailOnRuntimeRemotion,
            FailOnAnalyzerAssetLeak: options.FailOnAnalyzerAssetLeak);

    private static void ValidatePackageDirectory(string packageDirectory, string reportDirectory)
    {
        if (!Directory.Exists(packageDirectory))
            throw new DirectoryNotFoundException($"Package directory '{packageDirectory}' does not exist.");
        if (PathsOverlap(packageDirectory, reportDirectory))
            throw new InvalidDataException("Package input and report output directories must be disjoint.");

        RejectReparsePointTraversal(packageDirectory, "package directory");
        var attributes = File.GetAttributes(packageDirectory);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Package directory '{packageDirectory}' is not a regular directory.");
    }

    private static string[] EnumeratePackageArchives(string packageDirectory)
    {
        var paths = Directory.EnumerateFiles(packageDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(static path =>
                path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        if (paths.Length > PackageInspectionPolicy.MaximumPackageArchives)
        {
            throw new InvalidDataException(
                $"Package directory contains {paths.Length.ToString(CultureInfo.InvariantCulture)} archives, exceeding the {PackageInspectionPolicy.MaximumPackageArchives.ToString(CultureInfo.InvariantCulture)} archive inspection limit.");
        }
        var legacySymbolPackage = paths.FirstOrDefault(static path =>
            path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));
        if (legacySymbolPackage is not null)
        {
            throw new InvalidDataException(
                $"Legacy symbol archive '{Path.GetFileName(legacySymbolPackage)}' is not allowed; release candidates must use .snupkg symbol packages.");
        }

        return paths;
    }

    private static FileStream OpenPackageArchive(string packagePath)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("Package archive does not exist.", packagePath);
        var attributes = File.GetAttributes(packagePath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException($"Package archive '{packagePath}' is not a regular file.");

        var stream = new FileStream(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        if (stream.Length > PackageInspectionPolicy.MaximumPackageArchiveBytes)
        {
            stream.Dispose();
            throw new InvalidDataException(
                $"Package archive '{packagePath}' exceeds the {PackageInspectionPolicy.MaximumPackageArchiveBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
        }

        return stream;
    }

    private static void ValidateArchive(ZipArchive archive, string packagePath)
    {
        if (archive.Entries.Count > PackageInspectionPolicy.MaximumPackageEntryCount)
        {
            throw new InvalidDataException(
                $"Package '{packagePath}' contains {archive.Entries.Count.ToString(CultureInfo.InvariantCulture)} entries, exceeding the {PackageInspectionPolicy.MaximumPackageEntryCount.ToString(CultureInfo.InvariantCulture)} entry inspection limit.");
        }

        long aggregateLength = 0;
        var normalizedPaths = new List<string>(archive.Entries.Count);
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > PackageInspectionPolicy.MaximumAggregateUncompressedBytes - aggregateLength)
            {
                throw new InvalidDataException(
                    $"Package '{packagePath}' exceeds the {PackageInspectionPolicy.MaximumAggregateUncompressedBytes.ToString(CultureInfo.InvariantCulture)} byte aggregate uncompressed inspection limit.");
            }
            aggregateLength += entry.Length;

            if (!TryNormalizeArchivePath(entry.FullName, out var normalized, out var reason))
            {
                throw new InvalidDataException(
                    $"Package '{packagePath}' contains invalid archive entry '{entry.FullName}': {reason}.");
            }
            normalizedPaths.Add(normalized);
        }

        var duplicate = normalizedPaths
            .GroupBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Package '{packagePath}' contains duplicate normalized archive path '{duplicate.Key}'.");
        }
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
            reason = $"the path exceeds {MaximumArchivePathCharacters.ToString(CultureInfo.InvariantCulture)} characters";
            return false;
        }
        if (archivePath.Contains('\\'))
        {
            reason = "backslash separators are not allowed";
            return false;
        }
        if (archivePath.StartsWith("/", StringComparison.Ordinal) || archivePath.EndsWith("//", StringComparison.Ordinal))
        {
            reason = "rooted or empty path segments are not allowed";
            return false;
        }

        var withoutTrailingSlash = archivePath.EndsWith("/", StringComparison.Ordinal)
            ? archivePath[..^1]
            : archivePath;
        var segments = withoutTrailingSlash.Split('/');
        if (withoutTrailingSlash.Length == 0 || segments.Any(static segment => segment.Length == 0))
        {
            reason = "empty path segments are not allowed";
            return false;
        }
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Contains(':') || segment.Any(char.IsControl) ||
                segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                reason = "a segment is non-portable or contains traversal syntax";
                return false;
            }
        }

        normalizedPath = string.Join('/', segments.Select(static segment => segment.Normalize(NormalizationForm.FormC)));
        return true;
    }

    private static void EnsureEntryWithinManagedAssetLimit(ZipArchiveEntry entry)
    {
        if (entry.Length > PackageInspectionPolicy.MaximumPrimaryManagedAssetBytes)
        {
            throw new InvalidDataException(
                $"Managed package asset '{entry.FullName}' exceeds the {PackageInspectionPolicy.MaximumPrimaryManagedAssetBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
        }
    }

    private static void CopyToBounded(Stream source, Stream destination, int maximumBytes)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
                return;
            total = checked(total + read);
            if (total > maximumBytes)
                throw new InvalidDataException($"Package entry exceeds the {maximumBytes.ToString(CultureInfo.InvariantCulture)} byte inspection limit.");
            destination.Write(buffer, 0, read);
        }
    }

    private static void AddArchiveStabilityFindings(
        string packageDirectory,
        IReadOnlyList<string> initialArchivePaths,
        IReadOnlyList<PackageInspectionPackageReport> packages,
        IReadOnlyList<PackageInspectionSymbolPackageReport> symbolPackages,
        ICollection<PackageInspectionFinding> findings)
    {
        try
        {
            var finalArchivePaths = EnumeratePackageArchives(packageDirectory);
            if (!initialArchivePaths.SequenceEqual(finalArchivePaths, PathComparer))
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.PackageArchiveChanged,
                    "<package-directory>",
                    null,
                    "The package archive set changed while it was being inspected."));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and
                                          not AccessViolationException and
                                          not OperationCanceledException)
        {
            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.PackageArchiveChanged,
                "<package-directory>",
                null,
                $"The package archive set could not be re-enumerated: {exception.GetType().Name}."));
        }

        foreach (var archive in packages.Select(static package =>
                     (package.Id, package.PackagePath, package.SizeBytes, package.Sha256))
                 .Concat(symbolPackages.Select(static package =>
                     (package.Id, package.PackagePath, package.SizeBytes, package.Sha256))))
        {
            try
            {
                RejectReparsePointTraversal(archive.PackagePath, "package archive");
                using var stream = OpenPackageArchive(archive.PackagePath);
                var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (stream.Length == archive.SizeBytes && hash.Equals(archive.Sha256, StringComparison.Ordinal))
                    continue;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and
                                              not AccessViolationException and
                                              not OperationCanceledException)
            {
                findings.Add(new PackageInspectionFinding(
                    PackageInspectionFindingKind.PackageArchiveChanged,
                    archive.Id,
                    null,
                    $"Package archive '{Path.GetFileName(archive.PackagePath)}' could not be re-inspected after reading: {exception.GetType().Name}."));
                continue;
            }

            findings.Add(new PackageInspectionFinding(
                PackageInspectionFindingKind.PackageArchiveChanged,
                archive.Id,
                null,
                $"Package archive '{Path.GetFileName(archive.PackagePath)}' changed while it was being inspected."));
        }
    }

    private PackageInspectionCandidateIdentity CreateCandidateIdentity(
        IReadOnlyList<PackageInspectionPackageReport> packages,
        IReadOnlyList<PackageInspectionSymbolPackageReport> symbolPackages,
        IReadOnlyList<PackageInspectionFinding> findings)
    {
        var rows = packages
            .Select(static package => new CandidateArchiveRow(
                "nupkg", package.Id, package.Version, package.Sha256))
            .Concat(symbolPackages.Select(static package => new CandidateArchiveRow(
                "snupkg", package.Id, package.Version, package.Sha256)))
            .OrderBy(static row => row.Kind, StringComparer.Ordinal)
            .ThenBy(static row => row.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Id, StringComparer.Ordinal)
            .ThenBy(static row => row.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Version, StringComparer.Ordinal)
            .ThenBy(static row => row.Sha256, StringComparer.Ordinal)
            .ToArray();
        var builder = new StringBuilder();
        AppendIdentityValue(builder, CandidateAggregateFormat);
        foreach (var row in rows)
        {
            AppendIdentityValue(builder, row.Kind);
            AppendIdentityValue(builder, row.Id);
            AppendIdentityValue(builder, row.Version);
            AppendIdentityValue(builder, row.Sha256);
        }

        var aggregateSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
        var packageCoverageComplete = HasExactArchiveCoverage(
            packages.Select(static package => package.Id), options.ExpectedPackageIds);
        var symbolCoverageComplete = HasExactArchiveCoverage(
            symbolPackages.Select(static package => package.Id), options.ExpectedPackageIds);
        var allVersions = packages.Select(static package => package.Version)
            .Concat(symbolPackages.Select(static package => package.Version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actualVersion = allVersions.Length == 1 ? allVersions[0] : null;
        var versionConsistent = options.ExpectedVersion is not null &&
                                packageCoverageComplete &&
                                symbolCoverageComplete &&
                                rows.All(row => row.Version.Equals(options.ExpectedVersion, StringComparison.OrdinalIgnoreCase));

        var expectedPackages = packages.Where(package => options.ExpectedPackageIds.Contains(package.Id)).ToArray();
        var expectedSymbolPackages = symbolPackages.Where(package => options.ExpectedPackageIds.Contains(package.Id)).ToArray();
        var archiveCommits = expectedPackages.Select(static package => package.Metadata.RepositoryCommit?.Trim())
            .Concat(expectedSymbolPackages.Select(static package => package.Metadata.RepositoryCommit?.Trim()))
            .ToArray();
        var commits = archiveCommits
            .Where(static commit => !string.IsNullOrWhiteSpace(commit))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var repositoryCommit = commits.Length == 1 && IsFullGitObjectId(commits[0]) ? commits[0].ToLowerInvariant() : null;
        var repositoryCommitConsistent = packageCoverageComplete &&
                                         symbolCoverageComplete &&
                                         archiveCommits.Length == options.ExpectedPackageIds.Count * 2 &&
                                         commits.Length == 1 &&
                                         repositoryCommit is not null &&
                                         archiveCommits.All(commit =>
                                             commit?.Equals(repositoryCommit, StringComparison.OrdinalIgnoreCase) == true);
        var archiveIdentitiesComplete = packages.All(static package => package.SizeBytes >= 0 && IsSha256(package.Sha256)) &&
                                        symbolPackages.All(static package => package.SizeBytes >= 0 && IsSha256(package.Sha256));
        var archivesStable = findings.All(static finding => finding.Kind != PackageInspectionFindingKind.PackageArchiveChanged) &&
                             rows.Length > 0 &&
                             archiveIdentitiesComplete;
        return new PackageInspectionCandidateIdentity(
            aggregateSha256,
            actualVersion,
            versionConsistent,
            repositoryCommit,
            repositoryCommitConsistent,
            archivesStable);
    }

    internal static PackageInspectionRunnerEvidence EvaluateRunnerEvidence(
        TestRunSummaryRepositoryState start,
        TestRunSummaryRepositoryState end,
        TestRunSummaryRunnerAssembly entryAssembly,
        TestRunSummaryRunnerAssembly devToolsAssembly,
        string? candidateRepositoryCommit)
    {
        var stateChanged = !start.Captured ||
                           !end.Captured ||
                           start.Captured != end.Captured ||
                           !start.Commit.Equals(end.Commit, StringComparison.OrdinalIgnoreCase) ||
                           !start.Branch.Equals(end.Branch, StringComparison.Ordinal) ||
                           start.Dirty != end.Dirty ||
                           !start.StatusSha256.Equals(end.StatusSha256, StringComparison.OrdinalIgnoreCase);
        var assembliesMatch = start.Captured && end.Captured &&
                              entryAssembly.Name.Equals(ExpectedEntryAssemblyName, StringComparison.Ordinal) &&
                              devToolsAssembly.Name.Equals(ExpectedDevToolsAssemblyName, StringComparison.Ordinal) &&
                              entryAssembly.RepositoryCommitCaptured &&
                              devToolsAssembly.RepositoryCommitCaptured &&
                              entryAssembly.RepositoryCommit.Equals(start.Commit, StringComparison.OrdinalIgnoreCase) &&
                              devToolsAssembly.RepositoryCommit.Equals(start.Commit, StringComparison.OrdinalIgnoreCase);
        var assembliesBuiltClean = entryAssembly.RepositoryBuildState.Equals(CleanRepositoryBuildState, StringComparison.Ordinal) &&
                                   devToolsAssembly.RepositoryBuildState.Equals(CleanRepositoryBuildState, StringComparison.Ordinal);
        var candidateMatches = candidateRepositoryCommit is not null &&
                               start.Captured &&
                               candidateRepositoryCommit.Equals(start.Commit, StringComparison.OrdinalIgnoreCase);
        var valid = start.Captured && end.Captured &&
                    !start.Dirty && !end.Dirty && !stateChanged &&
                    assembliesMatch && assembliesBuiltClean && candidateMatches;
        return new PackageInspectionRunnerEvidence(
            start,
            end,
            entryAssembly,
            devToolsAssembly,
            stateChanged,
            assembliesMatch,
            assembliesBuiltClean,
            candidateMatches,
            valid);
    }

    internal static bool EvaluateValidForEvidence(
        PackageInspectionOutcome outcome,
        bool inspectionComplete,
        bool artifactsComplete,
        bool canonicalPolicy,
        bool packageDirectoryIsRepositoryArtifact,
        PackageInspectionCandidateIdentity candidate,
        PackageInspectionRunnerEvidence runner) =>
        outcome == PackageInspectionOutcome.Passed &&
        inspectionComplete &&
        artifactsComplete &&
        canonicalPolicy &&
        packageDirectoryIsRepositoryArtifact &&
        candidate.VersionConsistent &&
        candidate.RepositoryCommitConsistent &&
        candidate.ArchivesStable &&
        runner.ValidForEvidence;

    private static bool IsCanonicalReleasePolicy(PackageInspectionInvocation invocation) =>
        invocation.ExpectedVersion is not null &&
        HasExactSet(invocation.ExpectedPackageIds, PackageInspectionPolicy.PublicPackageIds) &&
        HasExactSet(invocation.RuntimePackageIds, PackageInspectionPolicy.RuntimePackageIds) &&
        invocation.FailOnUnexpectedPackage &&
        invocation.FailOnMissingSymbolPackage &&
        invocation.FailOnRuntimeRoslyn &&
        invocation.FailOnRuntimeRemotion &&
        invocation.FailOnAnalyzerAssetLeak;

    private static bool HasExactArchiveCoverage(IEnumerable<string> actualIds, IReadOnlySet<string> expectedIds)
    {
        var values = actualIds.ToArray();
        return values.Length == expectedIds.Count &&
               values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length &&
               values.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedIds);
    }

    private static bool HasExactSet(IEnumerable<string> actual, IEnumerable<string> expected) =>
        actual.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(expected.ToHashSet(StringComparer.OrdinalIgnoreCase));

    private static void AppendIdentityValue(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsFullGitObjectId(string value) =>
        value.Length is 40 or 64 && value.All(static character =>
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

    private static bool IsValidPackageId(string packageId) =>
        packageId.Length is > 0 and <= 100 &&
        packageId.All(static character =>
            character is >= '0' and <= '9' or
                >= 'A' and <= 'Z' or
                >= 'a' and <= 'z' or '.' or '-' or '_');

    private static bool IsAsciiNumericComponent(string value) =>
        value.Length > 0 &&
        value.All(static character => character is >= '0' and <= '9') &&
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsValidLabelSequence(string value) =>
        value.Split('.').All(static part =>
            part.Length > 0 &&
            part.All(static character =>
                character is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '-'));

    private static string NormalizeDirectory(string path, string baseDirectory)
    {
        var fullPath = Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, baseDirectory);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static bool PathsOverlap(string first, string second) =>
        IsPathWithin(first, second) || IsPathWithin(second, first);

    private static bool IsPathStrictlyWithin(string path, string root) =>
        IsPathWithin(path, root) && !PathEquals(path, root);

    private static bool IsPathWithin(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
        return !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathEquals(string first, string second) =>
        Path.GetFullPath(first).Equals(
            Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

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
                throw new InvalidDataException($"Package inspection {label} traverses reparse point '{current}'.");
        }
    }

    private sealed record CandidateArchiveRow(
        string Kind,
        string Id,
        string Version,
        string Sha256);

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
        return Path.Combine(
            artifactRoot,
            "package-report",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
    }

    private static string MarkdownCode(string value) =>
        $"<code>{MarkdownText(value)}</code>";

    private static string MarkdownText(string value)
    {
        var singleLine = new StringBuilder(value.Length);
        foreach (var character in value)
            singleLine.Append(character is '\r' or '\n' || char.IsControl(character) ? ' ' : character);

        return WebUtility.HtmlEncode(singleLine.ToString())
            .Replace("`", "&#96;", StringComparison.Ordinal)
            .Replace("|", "&#124;", StringComparison.Ordinal)
            .Replace("*", "&#42;", StringComparison.Ordinal)
            .Replace("_", "&#95;", StringComparison.Ordinal)
            .Replace("[", "&#91;", StringComparison.Ordinal)
            .Replace("]", "&#93;", StringComparison.Ordinal)
            .Replace("\\", "&#92;", StringComparison.Ordinal);
    }

}
