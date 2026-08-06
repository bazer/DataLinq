using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataLinq.DevTools;

public sealed class ApiCompatibilityReporter
{
    public const string SchemaVersion = "v0.9.api-compatibility-report.v1";

    private const string ExpectedEntryAssemblyName = "DataLinq.Dev.CLI";
    private const string ExpectedDevToolsAssemblyName = "DataLinq.DevTools";
    private const string RepositoryBuildStateMetadataName = "DataLinqRepositoryBuildState";
    private const string CleanRepositoryBuildState = "clean";

    private static readonly string[] BaselinePackageIds =
    [
        PackageInspectionPolicy.CorePackageId,
        PackageInspectionPolicy.SQLitePackageId,
        PackageInspectionPolicy.MySqlPackageId,
        PackageInspectionPolicy.ToolsPackageId,
        PackageInspectionPolicy.CliPackageId
    ];

    private static readonly string[] CandidatePackageIds =
        PackageInspectionPolicy.PublicPackageIds.ToArray();

    private static readonly string[] LibraryComparisonPackageIds =
    [
        PackageInspectionPolicy.CorePackageId,
        PackageInspectionPolicy.SQLitePackageId,
        PackageInspectionPolicy.MySqlPackageId,
        PackageInspectionPolicy.ToolsPackageId
    ];

    private readonly DevToolPaths paths;
    private readonly ApiCompatibilityReportOptions options;
    private readonly IApiCompatProcessRunner? processRunner;

    public ApiCompatibilityReporter(DevToolPaths paths, ApiCompatibilityReportOptions options)
        : this(paths, options, processRunner: null)
    {
    }

    internal ApiCompatibilityReporter(
        DevToolPaths paths,
        ApiCompatibilityReportOptions options,
        IApiCompatProcessRunner? processRunner)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.processRunner = processRunner;
    }

    public ApiCompatibilityReport CreateReport()
    {
        var normalized = NormalizeAndValidateOptions(options);
        PrepareReportDirectory(
            normalized.OutputDirectory,
            normalized.BaselinePackageDirectory,
            normalized.CandidatePackageDirectory);

        var rawDirectory = Path.Combine(normalized.OutputDirectory, "raw");
        var surfacesDirectory = Path.Combine(normalized.OutputDirectory, "surfaces");
        var extractedDirectory = Path.Combine(normalized.OutputDirectory, "extracted");
        var inputsDirectory = Path.Combine(normalized.OutputDirectory, "inputs");
        Directory.CreateDirectory(rawDirectory);
        Directory.CreateDirectory(surfacesDirectory);
        Directory.CreateDirectory(extractedDirectory);
        Directory.CreateDirectory(inputsDirectory);

        var findings = new List<ApiCompatibilityFinding>();
        var executions = new List<ApiCompatibilityToolExecutionReport>();
        var surfaces = new List<ApiCompatibilitySurfaceReport>();
        var comparisons = new List<ApiCompatibilityComparisonReport>();
        var runnerAssemblies = ReadRunnerAssemblyState();
        var runnerStart = ReadRepositoryState(normalized.RepositoryRoot, normalized.Profile);

        ApiCompatibilityBaselineLockReport? baselineLock = null;
        ApiPackageSetInspection? baselinePackages = null;
        ApiPackageSetInspection? candidatePackages = null;
        EvidencePackageInput? baselineInput = null;
        EvidencePackageInput? candidateInput = null;
        var baselineLockMatchesCheckout = false;
        string? toolVersion = null;

        try
        {
            baselineLock = ApiCompatibilityBaselineLock.Load(
                normalized.BaselineLockPath,
                normalized.BaselineVersion,
                BaselinePackageIds);
            baselineLockMatchesCheckout = VerifyBaselineLockPolicy(
                normalized.RepositoryRoot,
                normalized.Profile,
                baselineLock.LockPath);
            baselineLock = baselineLock with
            {
                CanonicalTrackedPolicy = baselineLockMatchesCheckout
            };
        }
        catch (Exception exception) when (IsReportable(exception))
        {
            AddError(findings, "baseline-lock-invalid", "baseline", exception.Message);
        }

        if (baselineLock is not null)
        {
            try
            {
                var inspectionOptions = new ApiPackageSetInspectionOptions(
                        normalized.BaselinePackageDirectory,
                        normalized.BaselineVersion,
                        BaselinePackageIds,
                        baselineLock.PackageSha256,
                        baselineLock.RepositoryCommit,
                        baselineLock.RepositoryUrl);
                var sourcePackages = ApiPackageSetInspector.Inspect(inspectionOptions);
                baselineInput = MaterializePackageInput(
                    sourcePackages,
                    Path.Combine(inputsDirectory, "baseline"),
                    inspectionOptions with { PackageDirectory = Path.Combine(inputsDirectory, "baseline") });
                baselinePackages = baselineInput.Inspection;
            }
            catch (Exception exception) when (IsReportable(exception))
            {
                AddError(findings, "baseline-package-set-invalid", "baseline", exception.Message);
            }
        }

        try
        {
            var inspectionOptions = new ApiPackageSetInspectionOptions(
                    normalized.CandidatePackageDirectory,
                    normalized.CandidateVersion,
                    CandidatePackageIds,
                    ExpectedRepositoryUrl: baselineLock?.RepositoryUrl);
            var sourcePackages = ApiPackageSetInspector.Inspect(inspectionOptions);
            candidateInput = MaterializePackageInput(
                sourcePackages,
                Path.Combine(inputsDirectory, "candidate"),
                inspectionOptions with { PackageDirectory = Path.Combine(inputsDirectory, "candidate") });
            candidatePackages = candidateInput.Inspection;
        }
        catch (Exception exception) when (IsReportable(exception))
        {
            AddError(findings, "candidate-package-set-invalid", "candidate", exception.Message);
        }

        using var baselineInputLease = baselineInput;
        using var candidateInputLease = candidateInput;

        if (baselinePackages is not null)
            CaptureSurfaces("baseline", baselinePackages, surfacesDirectory, surfaces, findings);
        if (candidatePackages is not null)
            CaptureSurfaces("candidate", candidatePackages, surfacesDirectory, surfaces, findings);

        ApiCompatToolRunner? toolRunner = null;
        try
        {
            toolRunner = new ApiCompatToolRunner(
                paths,
                normalized.Profile,
                Path.Combine(normalized.RepositoryRoot, ".config", "dotnet-tools.json"),
                rawDirectory,
                processRunner);
            var versionExecution = toolRunner.VerifyTool();
            executions.Add(ToExecutionReport(versionExecution));
            if (versionExecution.Succeeded)
            {
                toolVersion = toolRunner.ToolVersion;
            }
            else
            {
                AddError(
                    findings,
                    "apicompat-tool-invalid",
                    "tool",
                    versionExecution.Failure ?? "Pinned ApiCompat tool verification failed.");
                toolRunner = null;
            }
        }
        catch (Exception exception) when (IsReportable(exception))
        {
            AddError(findings, "apicompat-tool-invalid", "tool", exception.Message);
            toolRunner = null;
        }

        if (toolRunner is not null && baselinePackages is not null && candidatePackages is not null)
        {
            foreach (var packageId in LibraryComparisonPackageIds)
            {
                try
                {
                    comparisons.Add(ComparePackage(
                        toolRunner,
                        packageId,
                        baselinePackages,
                        candidatePackages,
                        executions,
                        findings));
                }
                catch (Exception exception) when (IsReportable(exception))
                {
                    AddError(findings, "comparison-exception", packageId, exception.Message);
                    comparisons.Add(CreateFailedComparison(
                        packageId,
                        ApiCompatibilityComparisonKind.PackageBaseline));
                }
            }

            try
            {
                comparisons.AddRange(CompareCliAssemblies(
                    toolRunner,
                    baselinePackages,
                    candidatePackages,
                    extractedDirectory,
                    executions,
                    findings));
            }
            catch (Exception exception) when (IsReportable(exception))
            {
                AddError(findings, "comparison-exception", PackageInspectionPolicy.CliPackageId, exception.Message);
                comparisons.Add(CreateFailedComparison(
                    PackageInspectionPolicy.CliPackageId,
                    ApiCompatibilityComparisonKind.ToolAssemblyBaseline));
            }

            try
            {
                comparisons.Add(ValidateNewMemoryPackage(
                    toolRunner,
                    candidatePackages,
                    surfaces,
                    executions,
                    findings));
            }
            catch (Exception exception) when (IsReportable(exception))
            {
                AddError(findings, "comparison-exception", PackageInspectionPolicy.MemoryPackageId, exception.Message);
                comparisons.Add(CreateFailedComparison(
                    PackageInspectionPolicy.MemoryPackageId,
                    ApiCompatibilityComparisonKind.NewPackage));
            }
        }

        var baselineTagMatchesLock = baselineLock is not null &&
                                     VerifyBaselineTag(
                                         normalized.RepositoryRoot,
                                         normalized.Profile,
                                         baselineLock);
        var candidateMatchesCheckout = candidatePackages is not null &&
                                       runnerStart.Captured &&
                                       candidatePackages.RepositoryCommit.Equals(
                                           runnerStart.Commit,
                                           StringComparison.OrdinalIgnoreCase);
        VerifyPackageInputUnchanged(
            "baseline",
            baselinePackages,
            baselineInput?.InspectionOptions,
            findings);
        VerifyPackageInputUnchanged(
            "candidate",
            candidatePackages,
            candidateInput?.InspectionOptions,
            findings);
        var runnerEnd = ReadRepositoryState(normalized.RepositoryRoot, normalized.Profile);
        var runnerEvidence = EvaluateRunnerEvidence(
            runnerStart,
            runnerEnd,
            runnerAssemblies,
            candidateMatchesCheckout,
            baselineTagMatchesLock,
            baselineLockMatchesCheckout);
        AddRunnerEvidenceFindings(runnerEvidence, findings);

        var orderedFindings = findings
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static finding => finding.TargetFramework, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Fingerprint, StringComparer.Ordinal)
            .ToArray();
        var summary = CreateSummary(
            baselinePackages,
            candidatePackages,
            surfaces,
            comparisons,
            orderedFindings);
        var report = new ApiCompatibilityReport(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            new ApiCompatibilityReportInvocation(
                normalized.RepositoryRoot,
                normalized.CandidatePackageDirectory,
                normalized.CandidateVersion,
                normalized.BaselinePackageDirectory,
                normalized.BaselineVersion,
                normalized.BaselineLockPath,
                normalized.Profile,
                Array.AsReadOnly(BaselinePackageIds.ToArray()),
                Array.AsReadOnly(CandidatePackageIds.ToArray())),
            normalized.OutputDirectory,
            baselineLock,
            baselinePackages,
            candidatePackages,
            baselinePackages is null ? null : CreateAggregateIdentity(baselinePackages),
            candidatePackages is null ? null : CreateAggregateIdentity(candidatePackages),
            toolVersion,
            runnerEvidence,
            Array.AsReadOnly(executions.ToArray()),
            Array.AsReadOnly(surfaces
                .OrderBy(static surface => surface.Side, StringComparer.Ordinal)
                .ThenBy(static surface => surface.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static surface => surface.TargetFramework, StringComparer.Ordinal)
                .ToArray()),
            Array.AsReadOnly(comparisons.ToArray()),
            Array.AsReadOnly(orderedFindings),
            summary);

        WriteReportArtifacts(report);
        return report;
    }

    public static string ToMarkdown(ApiCompatibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine("# Public API Compatibility Report");
        builder.AppendLine();
        builder.AppendLine($"Generated UTC: `{report.GeneratedAtUtc:O}`");
        builder.AppendLine($"Baseline: {Code(report.Invocation.BaselineVersion)} from {Code(report.Invocation.BaselinePackageDirectory)}");
        builder.AppendLine($"Candidate: {Code(report.Invocation.CandidateVersion)} from {Code(report.Invocation.CandidatePackageDirectory)}");
        builder.AppendLine($"Outcome: **{(report.Summary.HasHardFailures ? "failed" : report.Summary.RequiresReview ? "review required" : "passed")}**");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- baseline packages: {report.Summary.BaselinePackageCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- candidate packages: {report.Summary.CandidatePackageCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- captured API surfaces: {report.Summary.SurfaceCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- comparisons: {report.Summary.ComparisonCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- compatibility/source-sensitive breaks: {report.Summary.CompatibilityBreakCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- current-package framework mismatches: {report.Summary.FrameworkMismatchCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- compatible/additive changes for review: {report.Summary.CompatibleChangeCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- new-package surfaces for review: {report.Summary.NewPackageSurfaceCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- hard failures: {report.Summary.HardFailureCount.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine("## Evidence identity");
        builder.AppendLine();
        builder.AppendLine($"- pinned ApiCompat: {Code(report.ApiCompatToolVersion ?? "not verified")}");
        builder.AppendLine($"- baseline aggregate: {Code(report.BaselineAggregateIdentity ?? "unavailable")}");
        builder.AppendLine($"- candidate aggregate: {Code(report.CandidateAggregateIdentity ?? "unavailable")}");
        builder.AppendLine($"- baseline source: {Code(report.BaselineLock?.PackageSource ?? "unavailable")}");
        builder.AppendLine($"- baseline lock SHA-256: {Code(report.BaselineLock?.LockSha256 ?? "unavailable")}");
        builder.AppendLine($"- canonical tracked baseline policy: `{report.BaselineLock?.CanonicalTrackedPolicy.ToString() ?? "unavailable"}`");
        builder.AppendLine($"- baseline provenance: {Text(report.BaselineLock?.ProvenanceNote ?? "unavailable")}");
        builder.AppendLine($"- runner start: {Code(report.Runner.Start.Commit)} on {Code(report.Runner.Start.Branch)}, dirty `{report.Runner.Start.Dirty}`");
        builder.AppendLine($"- runner end: {Code(report.Runner.End.Commit)} on {Code(report.Runner.End.Branch)}, dirty `{report.Runner.End.Dirty}`");
        builder.AppendLine($"- runner evidence valid: `{report.Runner.ValidForEvidence}`");
        builder.AppendLine();
        builder.AppendLine("## Comparisons");
        builder.AppendLine();
        builder.AppendLine("| Package | Scope | TFM | Changes | Hard | Review | Result |");
        builder.AppendLine("| --- | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var comparison in report.Comparisons)
        {
            builder.AppendLine(
                $"| {Code(comparison.PackageId)} | {Code(comparison.Kind.ToString())} | {Code(comparison.TargetFramework ?? "package")} | " +
                $"{comparison.ChangeCount.ToString(CultureInfo.InvariantCulture)} | " +
                $"{comparison.HardFailureCount.ToString(CultureInfo.InvariantCulture)} | " +
                $"{comparison.ReviewCount.ToString(CultureInfo.InvariantCulture)} | " +
                $"{(comparison.Succeeded ? "passed" : "failed")} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Tool executions");
        builder.AppendLine();
        builder.AppendLine("| Name | Result | Exit | Diagnostics | Standard output | Standard error | Suppressions |");
        builder.AppendLine("| --- | --- | ---: | ---: | --- | --- | --- |");
        foreach (var execution in report.ToolExecutions)
        {
            builder.AppendLine(
                $"| {Code(execution.Name)} | {(execution.Succeeded ? "passed" : "failed")} | " +
                $"{execution.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-"} | " +
                $"{execution.DiagnosticCount.ToString(CultureInfo.InvariantCulture)} | " +
                $"{Code(execution.StandardOutputPath)} | {Code(execution.StandardErrorPath)} | " +
                $"{Code(execution.SuppressionPath ?? "-")} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Public API surfaces");
        builder.AppendLine();
        builder.AppendLine("| Side | Package | TFM | API lines | API SHA-256 | Snapshot |");
        builder.AppendLine("| --- | --- | --- | ---: | --- | --- |");
        foreach (var surface in report.Surfaces)
        {
            builder.AppendLine(
                $"| {Code(surface.Side)} | {Code(surface.PackageId)} | {Code(surface.TargetFramework)} | " +
                $"{surface.ApiLineCount.ToString(CultureInfo.InvariantCulture)} | {Code(surface.ApiSha256)} | {Code(surface.SnapshotPath)} |");
        }

        if (report.Findings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();
            foreach (var finding in report.Findings)
            {
                var framework = finding.TargetFramework is null ? string.Empty : $" {Code(finding.TargetFramework)}";
                var diagnostic = finding.DiagnosticId is null
                    ? string.Empty
                    : $" Diagnostic {Code(finding.DiagnosticId)}, target {Code(finding.Target ?? "-")}, " +
                      $"left {Code(finding.Left ?? "-")}, right {Code(finding.Right ?? "-")}, " +
                      $"fingerprint {Code(finding.Fingerprint ?? "-")}.";
                builder.AppendLine(
                    $"- **{finding.Severity}** {Code(finding.Code)} {Code(finding.PackageId)}{framework}: " +
                    Text(finding.Message.Replace("\r", " ", StringComparison.Ordinal)
                        .Replace("\n", " ", StringComparison.Ordinal)) + diagnostic);
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Boundary");
        builder.AppendLine();
        builder.AppendLine(
            "This report checks managed binary/API shape in exact package assets. It does not prove behavioral compatibility, generated-source compatibility, wire-format stability, or successful consumer execution.");
        return builder.ToString();
    }

    internal static IReadOnlyList<ApiCompatibilityFinding> ClassifyDiagnostics(
        string packageId,
        string? targetFramework,
        ApiCompatibilityComparisonKind comparisonKind,
        IReadOnlyList<ApiCompatSuppressionDiagnostic> normalDiagnostics,
        IReadOnlyList<ApiCompatSuppressionDiagnostic> strictDiagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(normalDiagnostics);
        ArgumentNullException.ThrowIfNull(strictDiagnostics);

        var findings = new List<ApiCompatibilityFinding>();
        var normalIdentities = normalDiagnostics
            .Select(static diagnostic => DiagnosticIdentity.From(diagnostic))
            .ToHashSet();

        foreach (var diagnostic in normalDiagnostics)
        {
            var isBaselineDiagnostic =
                comparisonKind == ApiCompatibilityComparisonKind.ToolAssemblyBaseline ||
                comparisonKind == ApiCompatibilityComparisonKind.PackageBaseline &&
                diagnostic.IsBaselineSuppression == true;
            if (isBaselineDiagnostic)
            {
                var sourceSensitive = diagnostic.DiagnosticId.Equals("CP0017", StringComparison.Ordinal);
                findings.Add(ToDiagnosticFinding(
                    ApiCompatibilityFindingSeverity.Error,
                    sourceSensitive ? "source-sensitive-break" : "compatibility-break",
                    packageId,
                    targetFramework,
                    comparisonKind,
                    sourceSensitive ? ApiCompatibilityChangeKind.SourceSensitiveBreak : ApiCompatibilityChangeKind.CompatibilityBreak,
                    diagnostic,
                    sourceSensitive
                        ? "The candidate changes a baseline method parameter name, which can break named-argument source consumers."
                        : "The candidate is incompatible with the locked baseline API."));
            }
            else
            {
                findings.Add(ToDiagnosticFinding(
                    ApiCompatibilityFindingSeverity.Error,
                    "current-framework-mismatch",
                    packageId,
                    targetFramework,
                    comparisonKind,
                    ApiCompatibilityChangeKind.CurrentPackageFrameworkMismatch,
                    diagnostic,
                    "The candidate package exposes inconsistent API across its current target-framework assets."));
            }
        }

        foreach (var diagnostic in strictDiagnostics.Where(diagnostic =>
                     !normalIdentities.Contains(DiagnosticIdentity.From(diagnostic))))
        {
            var isBaselineDiagnostic =
                comparisonKind == ApiCompatibilityComparisonKind.ToolAssemblyBaseline ||
                comparisonKind == ApiCompatibilityComparisonKind.PackageBaseline &&
                diagnostic.IsBaselineSuppression == true;
            if (isBaselineDiagnostic)
            {
                findings.Add(ToDiagnosticFinding(
                    ApiCompatibilityFindingSeverity.Review,
                    "compatible-api-change",
                    packageId,
                    targetFramework,
                    comparisonKind,
                    ApiCompatibilityChangeKind.CompatibleApiChange,
                    diagnostic,
                    "Strict baseline comparison found an additive or otherwise compatible API change that requires release review."));
            }
            else
            {
                findings.Add(ToDiagnosticFinding(
                    ApiCompatibilityFindingSeverity.Error,
                    "current-framework-mismatch",
                    packageId,
                    targetFramework,
                    comparisonKind,
                    ApiCompatibilityChangeKind.CurrentPackageFrameworkMismatch,
                    diagnostic,
                    "Strict comparison found an inconsistent API across current target-framework assets."));
            }
        }

        return Array.AsReadOnly(findings.ToArray());
    }

    private ApiCompatibilityComparisonReport ComparePackage(
        ApiCompatToolRunner runner,
        string packageId,
        ApiPackageSetInspection baseline,
        ApiPackageSetInspection candidate,
        ICollection<ApiCompatibilityToolExecutionReport> executions,
        ICollection<ApiCompatibilityFinding> findings)
    {
        var baselinePackage = FindPackage(baseline, packageId);
        var candidatePackage = FindPackage(candidate, packageId);
        var stem = ToEvidenceName(packageId);
        var normal = runner.ComparePackages(
            $"{stem}-baseline",
            baselinePackage.PackagePath,
            candidatePackage.PackagePath,
            strictBaseline: false);
        executions.Add(ToExecutionReport(normal));
        var strict = runner.ComparePackages(
            $"{stem}-strict",
            baselinePackage.PackagePath,
            candidatePackage.PackagePath,
            strictBaseline: true);
        executions.Add(ToExecutionReport(strict));

        var local = new List<ApiCompatibilityFinding>();
        AddExecutionFailure(normal, packageId, null, local);
        AddExecutionFailure(strict, packageId, null, local);
        if (normal.Succeeded && strict.Succeeded)
            local.AddRange(ClassifyDiagnostics(
                packageId,
                null,
                ApiCompatibilityComparisonKind.PackageBaseline,
                normal.Diagnostics,
                strict.Diagnostics));
        foreach (var finding in local)
            findings.Add(finding);

        return CreateComparison(
            packageId,
            ApiCompatibilityComparisonKind.PackageBaseline,
            null,
            normal.Name,
            strict.Name,
            normal.Diagnostics,
            strict.Diagnostics,
            local);
    }

    private IReadOnlyList<ApiCompatibilityComparisonReport> CompareCliAssemblies(
        ApiCompatToolRunner runner,
        ApiPackageSetInspection baseline,
        ApiPackageSetInspection candidate,
        string extractedDirectory,
        ICollection<ApiCompatibilityToolExecutionReport> executions,
        ICollection<ApiCompatibilityFinding> findings)
    {
        var baselinePackage = FindPackage(baseline, PackageInspectionPolicy.CliPackageId);
        var candidatePackage = FindPackage(candidate, PackageInspectionPolicy.CliPackageId);
        var result = new List<ApiCompatibilityComparisonReport>();
        var candidateAssemblies = new Dictionary<string, (string Assembly, string ReferenceDirectory)>(StringComparer.Ordinal);

        foreach (var baselineAsset in baselinePackage.PrimaryAssets.OrderBy(static asset => asset.TargetFramework, StringComparer.Ordinal))
        {
            var candidateAsset = candidatePackage.PrimaryAssets.Single(asset =>
                asset.TargetFramework.Equals(baselineAsset.TargetFramework, StringComparison.Ordinal));
            var framework = baselineAsset.TargetFramework;
            var baselineRoot = Path.Combine(extractedDirectory, "baseline", "datalinq-cli", framework);
            var candidateRoot = Path.Combine(extractedDirectory, "candidate", "datalinq-cli", framework);
            ExtractToolDirectory(baselinePackage.PackagePath, framework, baselineRoot);
            ExtractToolDirectory(candidatePackage.PackagePath, framework, candidateRoot);
            var baselineAssembly = ResolveExtractedAsset(baselineRoot, baselineAsset.ArchivePath, framework);
            var candidateAssembly = ResolveExtractedAsset(candidateRoot, candidateAsset.ArchivePath, framework);
            candidateAssemblies.Add(framework, (candidateAssembly, candidateRoot));
            var evidenceFramework = framework.Replace('.', '-');
            var normal = runner.CompareAssemblies(
                $"datalinq-cli-{evidenceFramework}-baseline",
                baselineAssembly,
                candidateAssembly,
                strict: false,
                baselineRoot,
                candidateRoot);
            executions.Add(ToExecutionReport(normal));
            var strict = runner.CompareAssemblies(
                $"datalinq-cli-{evidenceFramework}-strict",
                baselineAssembly,
                candidateAssembly,
                strict: true,
                baselineRoot,
                candidateRoot);
            executions.Add(ToExecutionReport(strict));

            var local = new List<ApiCompatibilityFinding>();
            AddExecutionFailure(normal, PackageInspectionPolicy.CliPackageId, framework, local);
            AddExecutionFailure(strict, PackageInspectionPolicy.CliPackageId, framework, local);
            if (normal.Succeeded && strict.Succeeded)
            {
                local.AddRange(ClassifyDiagnostics(
                    PackageInspectionPolicy.CliPackageId,
                    framework,
                    ApiCompatibilityComparisonKind.ToolAssemblyBaseline,
                    normal.Diagnostics,
                    strict.Diagnostics));
            }

            foreach (var finding in local)
                findings.Add(finding);
            result.Add(CreateComparison(
                PackageInspectionPolicy.CliPackageId,
                ApiCompatibilityComparisonKind.ToolAssemblyBaseline,
                framework,
                normal.Name,
                strict.Name,
                normal.Diagnostics,
                strict.Diagnostics,
                local));
        }

        var canonicalFramework = PackageInspectionPolicy.PublicTargetFrameworks[0];
        var canonical = candidateAssemblies[canonicalFramework];
        foreach (var framework in PackageInspectionPolicy.PublicTargetFrameworks.Skip(1))
        {
            var other = candidateAssemblies[framework];
            var canonicalName = canonicalFramework.Replace('.', '-');
            var otherName = framework.Replace('.', '-');
            var forward = runner.CompareAssemblies(
                $"datalinq-cli-current-{canonicalName}-to-{otherName}",
                canonical.Assembly,
                other.Assembly,
                strict: false,
                canonical.ReferenceDirectory,
                other.ReferenceDirectory);
            executions.Add(ToExecutionReport(forward));
            var reverse = runner.CompareAssemblies(
                $"datalinq-cli-current-{otherName}-to-{canonicalName}",
                other.Assembly,
                canonical.Assembly,
                strict: false,
                other.ReferenceDirectory,
                canonical.ReferenceDirectory);
            executions.Add(ToExecutionReport(reverse));

            var local = new List<ApiCompatibilityFinding>();
            AddExecutionFailure(forward, PackageInspectionPolicy.CliPackageId, $"{canonicalFramework}->{framework}", local);
            AddExecutionFailure(reverse, PackageInspectionPolicy.CliPackageId, $"{framework}->{canonicalFramework}", local);
            if (forward.Succeeded)
            {
                local.AddRange(ClassifyDiagnostics(
                    PackageInspectionPolicy.CliPackageId,
                    $"{canonicalFramework}->{framework}",
                    ApiCompatibilityComparisonKind.CurrentFramework,
                    forward.Diagnostics,
                    forward.Diagnostics));
            }
            if (reverse.Succeeded)
            {
                local.AddRange(ClassifyDiagnostics(
                    PackageInspectionPolicy.CliPackageId,
                    $"{framework}->{canonicalFramework}",
                    ApiCompatibilityComparisonKind.CurrentFramework,
                    reverse.Diagnostics,
                    reverse.Diagnostics));
            }

            foreach (var finding in local)
                findings.Add(finding);
            var hardFailureCount = local.Count(static finding =>
                finding.Severity == ApiCompatibilityFindingSeverity.Error);
            result.Add(new ApiCompatibilityComparisonReport(
                PackageInspectionPolicy.CliPackageId,
                ApiCompatibilityComparisonKind.CurrentFramework,
                $"{canonicalFramework}<->{framework}",
                forward.Name,
                reverse.Name,
                forward.Diagnostics.Count + reverse.Diagnostics.Count,
                hardFailureCount,
                0,
                hardFailureCount == 0));
        }

        return Array.AsReadOnly(result.ToArray());
    }

    private ApiCompatibilityComparisonReport ValidateNewMemoryPackage(
        ApiCompatToolRunner runner,
        ApiPackageSetInspection candidate,
        IReadOnlyList<ApiCompatibilitySurfaceReport> surfaces,
        ICollection<ApiCompatibilityToolExecutionReport> executions,
        ICollection<ApiCompatibilityFinding> findings)
    {
        const string packageId = PackageInspectionPolicy.MemoryPackageId;
        var package = FindPackage(candidate, packageId);
        var execution = runner.ValidatePackage("datalinq-memory-current", package.PackagePath);
        executions.Add(ToExecutionReport(execution));
        var local = new List<ApiCompatibilityFinding>();
        AddExecutionFailure(execution, packageId, null, local);
        if (execution.Succeeded)
            local.AddRange(ClassifyDiagnostics(
                packageId,
                null,
                ApiCompatibilityComparisonKind.NewPackage,
                execution.Diagnostics,
                execution.Diagnostics));

        foreach (var surface in surfaces.Where(surface =>
                     surface.Side.Equals("candidate", StringComparison.Ordinal) &&
                     surface.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)))
        {
            local.Add(new ApiCompatibilityFinding(
                ApiCompatibilityFindingSeverity.Review,
                "new-package-surface",
                packageId,
                surface.TargetFramework,
                $"DataLinq.Memory is new in 0.9; review its first public API snapshot '{surface.SnapshotPath}'.",
                ApiCompatibilityChangeKind.NewPackageSurface));
        }

        foreach (var finding in local)
            findings.Add(finding);
        return new ApiCompatibilityComparisonReport(
            packageId,
            ApiCompatibilityComparisonKind.NewPackage,
            null,
            execution.Name,
            null,
            execution.Diagnostics.Count + local.Count(static finding =>
                finding.ChangeKind == ApiCompatibilityChangeKind.NewPackageSurface),
            local.Count(static finding => finding.Severity == ApiCompatibilityFindingSeverity.Error),
            local.Count(static finding => finding.Severity == ApiCompatibilityFindingSeverity.Review),
            execution.Succeeded && local.All(static finding => finding.Severity != ApiCompatibilityFindingSeverity.Error));
    }

    private static EvidencePackageInput MaterializePackageInput(
        ApiPackageSetInspection source,
        string destinationDirectory,
        ApiPackageSetInspectionOptions copiedInspectionOptions)
    {
        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
            throw new InvalidOperationException($"Evidence input directory '{destinationDirectory}' already exists.");
        Directory.CreateDirectory(destinationDirectory);

        foreach (var package in source.Packages)
        {
            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(package.PackagePath));
            RefuseExistingFile(destinationPath);
            using var input = new FileStream(
                package.PackagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan);
            using var output = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.SequentialScan);
            input.CopyTo(output);
        }

        var copied = ApiPackageSetInspector.Inspect(copiedInspectionOptions);
        if (!CreateAggregateIdentity(source).Equals(
                CreateAggregateIdentity(copied),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Package bytes changed while materializing evidence input '{destinationDirectory}'.");
        }

        return EvidencePackageInput.Create(copied, copiedInspectionOptions);
    }

    private static void VerifyPackageInputUnchanged(
        string side,
        ApiPackageSetInspection? original,
        ApiPackageSetInspectionOptions? inspectionOptions,
        ICollection<ApiCompatibilityFinding> findings)
    {
        if (original is null || inspectionOptions is null)
            return;

        try
        {
            var final = ApiPackageSetInspector.Inspect(inspectionOptions);
            if (!CreateAggregateIdentity(original).Equals(
                    CreateAggregateIdentity(final),
                    StringComparison.Ordinal))
            {
                AddError(
                    findings,
                    "package-input-drift",
                    side,
                    $"The evidence-owned {side} package bytes changed during API comparison.");
            }
        }
        catch (Exception exception) when (IsReportable(exception))
        {
            AddError(
                findings,
                "package-input-drift",
                side,
                $"Could not revalidate the evidence-owned {side} package bytes: {exception.Message}");
        }
    }

    private static void CaptureSurfaces(
        string side,
        ApiPackageSetInspection packageSet,
        string surfacesDirectory,
        ICollection<ApiCompatibilitySurfaceReport> surfaces,
        ICollection<ApiCompatibilityFinding> findings)
    {
        foreach (var package in packageSet.Packages)
        {
            foreach (var asset in package.PrimaryAssets)
            {
                try
                {
                    using var packageStream = new FileStream(
                        package.PackagePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 4096,
                        FileOptions.SequentialScan);
                    var snapshot = PublicApiSnapshotter.SnapshotPackageAsset(packageStream, asset.ArchivePath);
                    var snapshotDirectory = Path.Combine(surfacesDirectory, side, ToEvidenceName(package.Id));
                    Directory.CreateDirectory(snapshotDirectory);
                    var snapshotPath = Path.Combine(snapshotDirectory, $"{asset.TargetFramework}.txt");
                    RefuseExistingFile(snapshotPath);
                    File.WriteAllText(snapshotPath, snapshot.CanonicalText, new UTF8Encoding(false));
                    surfaces.Add(new ApiCompatibilitySurfaceReport(
                        side,
                        package.Id,
                        package.Version,
                        asset.TargetFramework,
                        asset.ArchivePath,
                        snapshot.AssemblyIdentity,
                        snapshot.ModuleVersionId,
                        snapshot.FileSha256,
                        snapshot.SemanticApiSha256,
                        snapshot.ApiLines.Count,
                        snapshotPath));
                }
                catch (Exception exception) when (IsReportable(exception))
                {
                    AddError(
                        findings,
                        "api-snapshot-failed",
                        package.Id,
                        $"Could not snapshot {side} asset '{asset.ArchivePath}': {exception.Message}",
                        asset.TargetFramework);
                }
            }
        }
    }

    private static ApiCompatibilityComparisonReport CreateComparison(
        string packageId,
        ApiCompatibilityComparisonKind kind,
        string? targetFramework,
        string normalExecution,
        string strictExecution,
        IReadOnlyList<ApiCompatSuppressionDiagnostic> normalDiagnostics,
        IReadOnlyList<ApiCompatSuppressionDiagnostic> strictDiagnostics,
        IReadOnlyCollection<ApiCompatibilityFinding> localFindings)
    {
        var diagnosticCount = normalDiagnostics
            .Concat(strictDiagnostics)
            .Select(static diagnostic => DiagnosticIdentity.From(diagnostic))
            .Distinct()
            .Count();
        var hardFailureCount = localFindings.Count(static finding =>
            finding.Severity == ApiCompatibilityFindingSeverity.Error);
        return new ApiCompatibilityComparisonReport(
            packageId,
            kind,
            targetFramework,
            normalExecution,
            strictExecution,
            diagnosticCount,
            hardFailureCount,
            localFindings.Count(static finding => finding.Severity == ApiCompatibilityFindingSeverity.Review),
            hardFailureCount == 0);
    }

    private static ApiCompatibilityComparisonReport CreateFailedComparison(
        string packageId,
        ApiCompatibilityComparisonKind kind,
        string? targetFramework = null) =>
        new(
            packageId,
            kind,
            targetFramework,
            null,
            null,
            0,
            1,
            0,
            false);

    private static void AddExecutionFailure(
        ApiCompatToolExecution execution,
        string packageId,
        string? targetFramework,
        ICollection<ApiCompatibilityFinding> findings)
    {
        if (execution.Succeeded)
            return;
        findings.Add(new ApiCompatibilityFinding(
            ApiCompatibilityFindingSeverity.Error,
            "apicompat-execution-failed",
            packageId,
            targetFramework,
            $"ApiCompat execution '{execution.Name}' failed: {execution.Failure ?? "unknown failure"}"));
    }

    private static ApiCompatibilityFinding ToDiagnosticFinding(
        ApiCompatibilityFindingSeverity severity,
        string code,
        string packageId,
        string? targetFramework,
        ApiCompatibilityComparisonKind comparisonKind,
        ApiCompatibilityChangeKind changeKind,
        ApiCompatSuppressionDiagnostic diagnostic,
        string message) =>
        new(
            severity,
            code,
            packageId,
            targetFramework,
            message,
            changeKind,
            diagnostic.DiagnosticId,
            diagnostic.Target,
            diagnostic.Left,
            diagnostic.Right,
            comparisonKind is ApiCompatibilityComparisonKind.ToolAssemblyBaseline or
                ApiCompatibilityComparisonKind.CurrentFramework
                ? CreateAssemblyFindingFingerprint(code, packageId, targetFramework, diagnostic)
                : diagnostic.Fingerprint);

    private static string CreateAssemblyFindingFingerprint(
        string code,
        string packageId,
        string? targetFramework,
        ApiCompatSuppressionDiagnostic diagnostic)
    {
        var builder = new StringBuilder("DataLinq assembly ApiCompat finding v1\n");
        AppendHashValue(builder, code);
        AppendHashValue(builder, packageId);
        AppendHashValue(builder, targetFramework ?? string.Empty);
        AppendHashValue(builder, diagnostic.DiagnosticId);
        AppendHashValue(builder, diagnostic.Target);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static ApiPackageArchiveInspection FindPackage(ApiPackageSetInspection set, string packageId) =>
        set.Packages.Single(package => package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

    private static ApiCompatibilityToolExecutionReport ToExecutionReport(ApiCompatToolExecution execution) =>
        new(
            execution.Name,
            execution.Arguments,
            execution.WorkingDirectory,
            execution.ExitCode,
            execution.DurationSeconds,
            execution.StandardOutputPath,
            execution.StandardErrorPath,
            execution.SuppressionPath,
            execution.Diagnostics.Count,
            execution.Succeeded,
            execution.Failure);

    private static string CreateAggregateIdentity(ApiPackageSetInspection packageSet)
    {
        var builder = new StringBuilder("DataLinq API package set v1\n");
        foreach (var package in packageSet.Packages
                     .OrderBy(static package => package.Id, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static package => package.Id, StringComparer.Ordinal))
        {
            AppendHashValue(builder, package.Id);
            AppendHashValue(builder, package.Version);
            AppendHashValue(builder, package.Sha256);
            AppendHashValue(builder, package.RepositoryUrl);
            AppendHashValue(builder, package.RepositoryCommit);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void AppendHashValue(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');

    private static void ExtractToolDirectory(string packagePath, string targetFramework, string destination)
    {
        if (Directory.Exists(destination))
            throw new InvalidOperationException($"Extracted tool directory '{destination}' already exists.");
        Directory.CreateDirectory(destination);
        var prefix = $"tools/{targetFramework}/any/";
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries.Where(entry =>
                     entry.FullName.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal) &&
                     !entry.FullName.EndsWith("/", StringComparison.Ordinal)))
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var relative = normalized[prefix.Length..];
            if (string.IsNullOrWhiteSpace(relative) ||
                relative.Split('/').Any(segment => segment is "" or "." or ".."))
            {
                throw new InvalidDataException($"Unsafe CLI tool archive path '{entry.FullName}'.");
            }

            var outputPath = Path.GetFullPath(Path.Combine(
                destination,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsPathStrictlyInside(destination, outputPath))
                throw new InvalidDataException($"CLI tool archive path '{entry.FullName}' escapes its evidence directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            RefuseExistingFile(outputPath);
            using var input = entry.Open();
            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static string ResolveExtractedAsset(
        string extractedRoot,
        string archivePath,
        string targetFramework)
    {
        var prefix = $"tools/{targetFramework}/any/";
        if (!archivePath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"CLI primary asset '{archivePath}' does not use expected prefix '{prefix}'.");
        var result = Path.Combine(
            extractedRoot,
            archivePath[prefix.Length..].Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(result))
            throw new FileNotFoundException($"Extracted CLI primary asset '{result}' does not exist.", result);
        return result;
    }

    private ApiCompatibilityRepositoryState ReadRepositoryState(
        string repositoryRoot,
        ToolingProfile profile)
    {
        try
        {
            var environment = paths.CreateEnvironment(profile);
            var commit = ExternalProcessRunner.Execute("git", ["rev-parse", "HEAD"], repositoryRoot, environment);
            var branch = ExternalProcessRunner.Execute("git", ["branch", "--show-current"], repositoryRoot, environment);
            var status = ExternalProcessRunner.Execute(
                "git",
                ["--no-optional-locks", "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=none"],
                repositoryRoot,
                environment);
            var commitValue = commit.StandardOutput.Trim();
            if (commit.ExitCode != 0 || branch.ExitCode != 0 || status.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(commitValue))
            {
                return UnknownRepositoryState;
            }

            var normalizedStatus = status.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
            return new ApiCompatibilityRepositoryState(
                commitValue,
                string.IsNullOrWhiteSpace(branch.StandardOutput) ? "(detached)" : branch.StandardOutput.Trim(),
                !string.IsNullOrWhiteSpace(normalizedStatus),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedStatus))).ToLowerInvariant(),
                true);
        }
        catch
        {
            return UnknownRepositoryState;
        }
    }

    private bool VerifyBaselineTag(
        string repositoryRoot,
        ToolingProfile profile,
        ApiCompatibilityBaselineLockReport baselineLock)
    {
        try
        {
            var reference = $"refs/tags/{baselineLock.RepositoryTag}";
            var environment = paths.CreateEnvironment(profile);
            var objectType = ExternalProcessRunner.Execute(
                "git",
                ["cat-file", "-t", reference],
                repositoryRoot,
                environment);
            var commit = ExternalProcessRunner.Execute(
                "git",
                ["rev-parse", $"{reference}^{{commit}}"],
                repositoryRoot,
                environment);
            return objectType.ExitCode == 0 &&
                   commit.ExitCode == 0 &&
                   objectType.StandardOutput.Trim().Equals(
                       baselineLock.RepositoryTagObjectType,
                       StringComparison.Ordinal) &&
                   commit.StandardOutput.Trim().Equals(
                       baselineLock.RepositoryCommit,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private bool VerifyBaselineLockPolicy(
        string repositoryRoot,
        ToolingProfile profile,
        string lockPath)
    {
        var relativePath = Path.Combine(
            "test-infra",
            "api-compatibility",
            "v0.8.0-packages.json");
        var expectedPath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!Path.GetFullPath(lockPath).Equals(expectedPath, pathComparison) ||
            (File.GetAttributes(expectedPath) & FileAttributes.ReparsePoint) != 0)
        {
            return false;
        }

        try
        {
            var environment = paths.CreateEnvironment(profile);
            var tracked = ExternalProcessRunner.Execute(
                "git",
                ["ls-files", "--error-unmatch", "--", relativePath.Replace('\\', '/')],
                repositoryRoot,
                environment);
            var unchanged = ExternalProcessRunner.Execute(
                "git",
                ["diff", "--quiet", "HEAD", "--", relativePath.Replace('\\', '/')],
                repositoryRoot,
                environment);
            return tracked.ExitCode == 0 && unchanged.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static RunnerAssemblyState ReadRunnerAssemblyState() =>
        new(
            ReadRunnerAssembly(Assembly.GetEntryAssembly()),
            ReadRunnerAssembly(typeof(ApiCompatibilityReporter).Assembly));

    private static ApiCompatibilityRunnerAssembly ReadRunnerAssembly(Assembly? assembly)
    {
        if (assembly is null)
            return UnknownRunnerAssembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var repositoryCommit = CompatibilitySizeReporter
            .ExtractRepositoryCommitFromInformationalVersion(informationalVersion);
        var buildStateValues = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static attribute => attribute.Key.Equals(RepositoryBuildStateMetadataName, StringComparison.Ordinal))
            .Select(static attribute => attribute.Value)
            .ToArray();
        var buildState = buildStateValues.Length switch
        {
            0 => "missing",
            1 when string.IsNullOrWhiteSpace(buildStateValues[0]) => "invalid",
            1 => buildStateValues[0] ?? "invalid",
            _ => "ambiguous"
        };
        return new ApiCompatibilityRunnerAssembly(
            assembly.GetName().Name ?? "unknown",
            string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion,
            repositoryCommit ?? "unknown",
            repositoryCommit is not null,
            buildState);
    }

    private static ApiCompatibilityRunnerEvidence EvaluateRunnerEvidence(
        ApiCompatibilityRepositoryState start,
        ApiCompatibilityRepositoryState end,
        RunnerAssemblyState assemblies,
        bool candidateMatchesCheckout,
        bool baselineTagMatchesLock,
        bool baselineLockMatchesCheckout)
    {
        var stateChanged = !start.Captured ||
                           !end.Captured ||
                           !start.Commit.Equals(end.Commit, StringComparison.OrdinalIgnoreCase) ||
                           !start.Branch.Equals(end.Branch, StringComparison.Ordinal) ||
                           start.Dirty != end.Dirty ||
                           !start.StatusSha256.Equals(end.StatusSha256, StringComparison.Ordinal);
        var assembliesMatch = start.Captured &&
                              end.Captured &&
                              AssemblyMatches(assemblies.Entry, ExpectedEntryAssemblyName, start.Commit) &&
                              AssemblyMatches(assemblies.DevTools, ExpectedDevToolsAssemblyName, start.Commit) &&
                              assemblies.Entry.RepositoryCommit.Equals(end.Commit, StringComparison.OrdinalIgnoreCase) &&
                              assemblies.DevTools.RepositoryCommit.Equals(end.Commit, StringComparison.OrdinalIgnoreCase);
        var assembliesClean = assemblies.Entry.RepositoryBuildState.Equals(CleanRepositoryBuildState, StringComparison.Ordinal) &&
                              assemblies.DevTools.RepositoryBuildState.Equals(CleanRepositoryBuildState, StringComparison.Ordinal);
        var valid = start.Captured &&
                    end.Captured &&
                    !start.Dirty &&
                    !end.Dirty &&
                    !stateChanged &&
                    assembliesMatch &&
                    assembliesClean &&
                    candidateMatchesCheckout &&
                    baselineTagMatchesLock &&
                    baselineLockMatchesCheckout;
        return new ApiCompatibilityRunnerEvidence(
            start,
            end,
            assemblies.Entry,
            assemblies.DevTools,
            stateChanged,
            assembliesMatch,
            assembliesClean,
            candidateMatchesCheckout,
            baselineTagMatchesLock,
            baselineLockMatchesCheckout,
            valid);
    }

    private static bool AssemblyMatches(
        ApiCompatibilityRunnerAssembly assembly,
        string expectedName,
        string commit) =>
        assembly.Name.Equals(expectedName, StringComparison.Ordinal) &&
        assembly.RepositoryCommitCaptured &&
        assembly.RepositoryCommit.Equals(commit, StringComparison.OrdinalIgnoreCase);

    private static void AddRunnerEvidenceFindings(
        ApiCompatibilityRunnerEvidence runner,
        ICollection<ApiCompatibilityFinding> findings)
    {
        if (!runner.Start.Captured || !runner.End.Captured)
            AddError(findings, "runner-repository-state-unavailable", "runner", "Git repository state could not be captured at both report boundaries.");
        if (runner.Start.Dirty || runner.End.Dirty)
            AddError(findings, "runner-working-tree-dirty", "runner", "Authoritative API evidence requires a clean working tree at both report boundaries.");
        if (runner.StateChangedDuringRun)
            AddError(findings, "runner-state-changed", "runner", "Repository commit, branch, or status changed while API evidence was being generated.");
        if (!runner.AssembliesMatchCheckout)
            AddError(findings, "runner-assembly-stale", "runner", "The Dev CLI and DevTools assemblies were not built from the checked-out commit.");
        if (!runner.AssembliesBuiltFromCleanState)
            AddError(findings, "runner-assembly-dirty-build", "runner", "The Dev CLI or DevTools assembly was built while the repository was dirty.");
        if (!runner.CandidateMatchesCheckout)
            AddError(findings, "candidate-checkout-mismatch", "candidate", "The candidate packages do not identify the checked-out commit.");
        if (!runner.BaselineTagMatchesLock)
            AddError(findings, "baseline-tag-mismatch", "baseline", "The locked baseline tag does not resolve to the locked commit and object type.");
        if (!runner.BaselineLockMatchesCheckout)
            AddError(findings, "baseline-lock-policy-mismatch", "baseline", "Authoritative evidence requires the canonical tracked baseline lock unchanged from the checkout.");
    }

    private static ApiCompatibilityReportSummary CreateSummary(
        ApiPackageSetInspection? baseline,
        ApiPackageSetInspection? candidate,
        IReadOnlyCollection<ApiCompatibilitySurfaceReport> surfaces,
        IReadOnlyCollection<ApiCompatibilityComparisonReport> comparisons,
        IReadOnlyCollection<ApiCompatibilityFinding> findings)
    {
        var hardFailures = findings.Count(static finding => finding.Severity == ApiCompatibilityFindingSeverity.Error);
        var reviewCount = findings.Count(static finding => finding.Severity == ApiCompatibilityFindingSeverity.Review);
        return new ApiCompatibilityReportSummary(
            baseline?.Packages.Count ?? 0,
            candidate?.Packages.Count ?? 0,
            surfaces.Count,
            comparisons.Count,
            findings.Count,
            reviewCount,
            hardFailures,
            findings.Count(static finding => finding.ChangeKind is
                ApiCompatibilityChangeKind.CompatibilityBreak or ApiCompatibilityChangeKind.SourceSensitiveBreak),
            findings.Count(static finding => finding.ChangeKind == ApiCompatibilityChangeKind.CurrentPackageFrameworkMismatch),
            findings.Count(static finding => finding.ChangeKind == ApiCompatibilityChangeKind.CompatibleApiChange),
            findings.Count(static finding => finding.ChangeKind == ApiCompatibilityChangeKind.NewPackageSurface),
            hardFailures > 0,
            reviewCount > 0);
    }

    private static void WriteReportArtifacts(ApiCompatibilityReport report)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        File.WriteAllText(
            Path.Combine(report.ReportDirectory, "report.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(report.ReportDirectory, "report.md"),
            ToMarkdown(report),
            new UTF8Encoding(false));
    }

    private static ApiCompatibilityReportOptions NormalizeAndValidateOptions(ApiCompatibilityReportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CandidatePackageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CandidateVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BaselinePackageDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BaselineVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.BaselineLockPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputDirectory);
        if (options.CandidateVersion != options.CandidateVersion.Trim() ||
            options.BaselineVersion != options.BaselineVersion.Trim())
        {
            throw new ArgumentException("API report versions must be exact and have no surrounding whitespace.");
        }

        var normalized = options with
        {
            RepositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RepositoryRoot)),
            CandidatePackageDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.CandidatePackageDirectory)),
            BaselinePackageDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.BaselinePackageDirectory)),
            BaselineLockPath = Path.GetFullPath(options.BaselineLockPath),
            OutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.OutputDirectory))
        };
        if (!Directory.Exists(normalized.RepositoryRoot))
            throw new DirectoryNotFoundException($"Repository root '{normalized.RepositoryRoot}' does not exist.");
        if (!File.Exists(normalized.BaselineLockPath))
            throw new FileNotFoundException($"API baseline lock '{normalized.BaselineLockPath}' does not exist.", normalized.BaselineLockPath);
        return normalized;
    }

    private static void PrepareReportDirectory(string output, string baselinePackages, string candidatePackages)
    {
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException($"API report output '{output}' already exists; evidence output must be fresh.");
        if (IsPathInsideOrEqual(baselinePackages, output) || IsPathInsideOrEqual(output, baselinePackages) ||
            IsPathInsideOrEqual(candidatePackages, output) || IsPathInsideOrEqual(output, candidatePackages))
        {
            throw new InvalidOperationException("API report output must not overlap the baseline or candidate package directory.");
        }

        RejectExistingReparseAncestors(output);
        Directory.CreateDirectory(output);
    }

    private static void RejectExistingReparseAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"API report path traverses reparse point '{current}'.");
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent.Equals(current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }

    private static bool IsPathInsideOrEqual(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative == "." ||
               (!relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relative));
    }

    private static bool IsPathStrictlyInside(string root, string path) =>
        IsPathInsideOrEqual(root, path) && Path.GetRelativePath(root, path) != ".";

    private static void RefuseExistingFile(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            throw new InvalidOperationException($"Evidence path '{path}' already exists.");
    }

    private static string ToEvidenceName(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
            builder.Append(character is >= 'a' and <= 'z' or >= '0' and <= '9' ? character : '-');
        return builder.ToString().Trim('-');
    }

    private static string Code(string value) =>
        $"<code>{System.Net.WebUtility.HtmlEncode(value)}</code>";

    private static string Text(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static bool IsReportable(Exception exception) =>
        exception is not OutOfMemoryException and
        not AccessViolationException and
        not OperationCanceledException;

    private static void AddError(
        ICollection<ApiCompatibilityFinding> findings,
        string code,
        string packageId,
        string message,
        string? targetFramework = null) =>
        findings.Add(new ApiCompatibilityFinding(
            ApiCompatibilityFindingSeverity.Error,
            code,
            packageId,
            targetFramework,
            message));

    private static ApiCompatibilityRepositoryState UnknownRepositoryState { get; } =
        new("unknown", "unknown", true, "unknown", false);

    private static ApiCompatibilityRunnerAssembly UnknownRunnerAssembly { get; } =
        new("unknown", "unknown", "unknown", false, "missing");

    private sealed record RunnerAssemblyState(
        ApiCompatibilityRunnerAssembly Entry,
        ApiCompatibilityRunnerAssembly DevTools);

    private sealed class EvidencePackageInput : IDisposable
    {
        private readonly IReadOnlyList<FileStream> leases;

        private EvidencePackageInput(
            ApiPackageSetInspection inspection,
            ApiPackageSetInspectionOptions inspectionOptions,
            IReadOnlyList<FileStream> leases)
        {
            Inspection = inspection;
            InspectionOptions = inspectionOptions;
            this.leases = leases;
        }

        public ApiPackageSetInspection Inspection { get; }

        public ApiPackageSetInspectionOptions InspectionOptions { get; }

        public static EvidencePackageInput Create(
            ApiPackageSetInspection inspection,
            ApiPackageSetInspectionOptions inspectionOptions)
        {
            var streams = new List<FileStream>();
            try
            {
                foreach (var package in inspection.Packages)
                {
                    streams.Add(new FileStream(
                        package.PackagePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 4096,
                        FileOptions.SequentialScan));
                }

                return new EvidencePackageInput(
                    inspection,
                    inspectionOptions,
                    Array.AsReadOnly(streams.ToArray()));
            }
            catch
            {
                foreach (var stream in streams)
                    stream.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    private readonly record struct DiagnosticIdentity(
        string DiagnosticId,
        string Target,
        string Left,
        string Right,
        bool IsBaselineSuppression)
    {
        public static DiagnosticIdentity From(ApiCompatSuppressionDiagnostic diagnostic) =>
            new(
                diagnostic.DiagnosticId,
                diagnostic.Target,
                diagnostic.Left,
                diagnostic.Right,
                diagnostic.IsBaselineSuppression == true);
    }
}
