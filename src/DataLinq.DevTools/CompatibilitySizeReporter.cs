using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataLinq.DevTools;

public sealed class CompatibilitySizeReporter
{
    public const string SchemaVersion = "v0.9.compatibility-size-report.v6";

    private const string ExpectedEntryAssemblyName = "DataLinq.Dev.CLI";
    private const string ExpectedDevToolsAssemblyName = "DataLinq.DevTools";
    private const string RepositoryBuildStateMetadataName = "DataLinqRepositoryBuildState";
    private const string CleanRepositoryBuildState = "clean";

    private readonly DevToolPaths paths;
    private readonly CompatibilityReportOptions options;

    public CompatibilitySizeReporter(DevToolPaths paths, CompatibilityReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(options);
        var repositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RepositoryRoot));
        var pathsRepositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.RepositoryRoot));
        if (!repositoryRoot.Equals(pathsRepositoryRoot, PathComparison))
        {
            throw new ArgumentException(
                $"Compatibility options repository root '{repositoryRoot}' does not match DevToolPaths root '{pathsRepositoryRoot}'.",
                nameof(options));
        }
        if (options.LargestFileCount < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Largest-file count must be zero or greater.");
        if (options.TotalSizeWarningBytes < 0 ||
            options.SymbolExcludedSizeWarningBytes < 0 ||
            options.FileCountWarning < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Compatibility warning thresholds must be zero or greater.");
        }
        if (string.IsNullOrWhiteSpace(options.Configuration) ||
            !options.Configuration.Equals(options.Configuration.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Compatibility configuration must be nonblank without surrounding whitespace.", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.RuntimeIdentifier) ||
            !options.RuntimeIdentifier.Equals(options.RuntimeIdentifier.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Compatibility runtime identifier must be nonblank without surrounding whitespace.", nameof(options));
        }

        var outputFormat = string.IsNullOrWhiteSpace(options.OutputFormat)
            ? "summary"
            : options.OutputFormat.Trim().ToLowerInvariant();
        if (outputFormat is not ("summary" or "markdown" or "json"))
            throw new ArgumentException($"Unsupported compatibility output format '{options.OutputFormat}'.", nameof(options));

        this.paths = paths;
        this.options = options with
        {
            RepositoryRoot = repositoryRoot,
            TargetSet = CompatibilityTargetCatalog.NormalizeTargetSet(options.TargetSet),
            OutputDirectory = options.OutputDirectory is null
                ? null
                : NormalizeOutputDirectory(repositoryRoot, options.OutputDirectory),
            OutputFormat = outputFormat
        };
    }

    public static string NormalizeOutputDirectory(string repositoryRoot, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var canonicalRepositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var canonicalOutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Path.IsPathRooted(outputDirectory)
                ? outputDirectory
                : Path.Combine(canonicalRepositoryRoot, outputDirectory)));
        ValidateReportDirectoryBoundary(canonicalRepositoryRoot, canonicalOutputDirectory);
        return canonicalOutputDirectory;
    }

    public static void InvalidateExistingReportDirectory(string repositoryRoot, string outputDirectory)
        => InvalidateExistingReportDirectory(repositoryRoot, outputDirectory, packageDirectory: null);

    public static void InvalidateExistingReportDirectory(
        string repositoryRoot,
        string outputDirectory,
        string? packageDirectory)
    {
        var canonicalOutputDirectory = NormalizeOutputDirectory(repositoryRoot, outputDirectory);
        if (!string.IsNullOrWhiteSpace(packageDirectory))
        {
            var canonicalPackageDirectory = Path.GetFullPath(
                Path.IsPathRooted(packageDirectory)
                    ? packageDirectory
                    : Path.Combine(repositoryRoot, packageDirectory));
            if (PathsOverlap(canonicalOutputDirectory, canonicalPackageDirectory))
            {
                throw new InvalidDataException(
                    $"Compatibility report output '{canonicalOutputDirectory}' must not overlap package input '{canonicalPackageDirectory}'.");
            }
        }

        using var reportDirectoryLock = AcquireReportDirectoryLock(
            repositoryRoot,
            canonicalOutputDirectory);
        if (!Directory.Exists(canonicalOutputDirectory))
            return;

        ClearKnownReportArtifacts(canonicalOutputDirectory);
    }

    internal static FileStream AcquireReportDirectoryLock(
        string repositoryRoot,
        string outputDirectory)
    {
        var canonicalOutputDirectory = NormalizeOutputDirectory(repositoryRoot, outputDirectory);
        var lockRoot = GetReportLockRoot(repositoryRoot);
        RejectReparsePointTraversal(lockRoot, "compatibility report lock directory");
        Directory.CreateDirectory(lockRoot);
        RejectReparsePointTraversal(lockRoot, "compatibility report lock directory");

        var lockIdentity = OperatingSystem.IsWindows()
            ? canonicalOutputDirectory.ToUpperInvariant()
            : canonicalOutputDirectory;
        var lockName = $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockIdentity))).ToLowerInvariant()}.lock";
        var lockPath = Path.Combine(lockRoot, lockName);
        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Compatibility report output '{canonicalOutputDirectory}' is already owned by another writer.",
                exception);
        }
    }

    public CompatibilitySizeReport CreateReport()
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var dependencySource = ValidatePackageModeOptions(
            options.TargetSet,
            options.PackageDirectory,
            options.PackageVersion);

        if (options.CleanIntermediateOutputs && options.NoRestore)
        {
            throw new InvalidOperationException(
                "--clean-output cannot be combined with --no-restore because cleaning removes the target-owned restore assets.");
        }

        var requestedReportDirectory = options.OutputDirectory ?? CreateReportDirectoryPath(paths.ArtifactRoot);
        using var reportDirectoryLock = AcquireReportDirectoryLock(
            options.RepositoryRoot,
            requestedReportDirectory);

        var runnerAssemblies = ReadRunnerAssemblyState();
        var runnerStartState = ReadRunnerRepositoryState();

        CompatibilityPackageInput? packageInput = null;
        if (dependencySource == CompatibilityDependencySource.PackedPackages)
        {
            packageInput = CompatibilityPackageInputInspector.Inspect(
                ResolvePackageDirectory(options.RepositoryRoot, options.PackageDirectory!),
                options.PackageVersion!);
            RejectPackageDirectoryInsideArtifactRoot(packageInput.PackageDirectory, paths.ArtifactRoot);
        }

        var reportDirectory = PrepareReportDirectory(
            options.RepositoryRoot,
            requestedReportDirectory,
            packageInput?.PackageDirectory);

        paths.EnsureCreated();

        var packageBuildIdentity = packageInput is null
            ? null
            : CreatePackageBuildIdentity(packageInput);
        using var packageBuildLock = packageBuildIdentity is null
            ? null
            : AcquireBuildArtifactsLock(
                paths.ArtifactRoot,
                options.TargetSet,
                "package-context",
                packageBuildIdentity);
        if (packageBuildIdentity is not null && options.CleanIntermediateOutputs)
        {
            ResetPackageBuildRootForCleanEvidence(
                paths.ArtifactRoot,
                options.TargetSet,
                packageBuildIdentity);
        }

        var packageBuildContext = packageInput is null
            ? null
            : CreatePackageBuildContext(packageInput, packageBuildIdentity!);

        var expectedTargets = CompatibilityTargetCatalog.GetTargets(options.TargetSet);
        var targets = CompatibilityTargetCatalog.GetTargets(options.TargetSet, options.TargetSelectors);
        var selectedTargetIds = targets.Select(static target => target.Name).ToArray();
        var runner = new DotnetCommandRunner(paths, options.Profile);
        var targetReports = new List<CompatibilityTargetReport>();

        foreach (var target in targets)
        {
            CompatibilityTargetReport targetReport;
            try
            {
                targetReport = CreateTargetReport(
                    reportDirectory,
                    runner,
                    target,
                    dependencySource,
                    packageInput,
                    packageBuildContext);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and
                                              not AccessViolationException and
                                              not OperationCanceledException)
            {
                targetReport = CreateInfrastructureFailureTargetReport(
                    reportDirectory,
                    target,
                    exception,
                    packageBuildContext);
            }

            targetReports.Add(targetReport);

            if (targetReport.Publish.Status == CompatibilityCommandStatus.Failed && !options.ContinueOnPublishFailure)
                break;
        }

        for (var index = 0; index < targetReports.Count; index++)
            targetReports[index] = SanitizeTargetReport(targetReports[index]);

        var isFullTargetSet = targetReports
            .Select(static target => target.Name)
            .SequenceEqual(
                expectedTargets.Select(static target => target.Name),
                StringComparer.OrdinalIgnoreCase);

        var candidateStableDuringRun = false;
        CompatibilityReportFailure? failure = null;
        if (packageInput is not null)
        {
            try
            {
                var reinspectedPackageInput = CompatibilityPackageInputInspector.Inspect(
                    packageInput.PackageDirectory,
                    packageInput.Version);
                candidateStableDuringRun = PackageInputsMatch(packageInput, reinspectedPackageInput);
                if (!candidateStableDuringRun)
                {
                    failure = new CompatibilityReportFailure(
                        "reinspect-package-candidate",
                        nameof(InvalidDataException),
                        "The package candidate changed while compatibility targets were being evaluated.");
                }
            }
            catch (Exception exception) when (IsReportableException(exception))
            {
                failure = new CompatibilityReportFailure(
                    "reinspect-package-candidate",
                    exception.GetType().FullName ?? exception.GetType().Name,
                    TestRunSummaryReporter.SanitizeFailureMessage(exception.Message));
            }
        }

        var sdkVersion = ReadDotnetSdkVersion();
        var runnerEndState = ReadRunnerRepositoryState();
        var runnerEvidence = EvaluateRunnerEvidence(
            runnerStartState,
            runnerEndState,
            runnerAssemblies.EntryAssembly,
            runnerAssemblies.DevToolsAssembly);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var invocation = new CompatibilityReportInvocation(
            options.Profile,
            options.NoRestore,
            options.SkipSmoke,
            options.CleanIntermediateOutputs,
            options.UseReleaseThresholds,
            options.FailOnBannedPayload,
            options.FailOnThresholdWarnings,
            options.ContinueOnPublishFailure,
            options.LargestFileCount,
            options.TotalSizeWarningBytes,
            options.SymbolExcludedSizeWarningBytes,
            options.FileCountWarning)
        {
            TargetSet = options.TargetSet,
            TargetSelectors = options.TargetSelectors,
            Configuration = options.Configuration,
            RuntimeIdentifier = options.RuntimeIdentifier,
            DependencySource = dependencySource,
            PackageDirectory = packageInput?.PackageDirectory,
            PackageVersion = packageInput?.Version,
            ReportDirectory = reportDirectory,
            UsesExplicitOutput = options.OutputDirectory is not null,
            OutputFormat = options.OutputFormat,
            ReleaseEvidenceIntent = options.ReleaseEvidenceIntent
        };
        var summary = CreateSummary(
            targetReports,
            options.FailOnBannedPayload,
            options.FailOnThresholdWarnings,
            !runnerEvidence.ValidForEvidence);
        var isCompleteForInvocation = IsInvocationComplete(
            invocation,
            selectedTargetIds,
            targetReports,
            dependencySource);
        CompatibilityReportArtifacts artifacts;
        var artifactsComplete = false;
        try
        {
            artifacts = CreateArtifactManifest(
                options.RepositoryRoot,
                reportDirectory,
                targetReports,
                packageBuildContext?.NugetConfigPath);
            artifactsComplete = ArtifactsAreComplete(
                options.RepositoryRoot,
                targetReports,
                dependencySource,
                artifacts,
                packageBuildContext?.NugetConfigPath);
        }
        catch (Exception exception) when (IsReportableException(exception))
        {
            artifacts = new CompatibilityReportArtifacts(
                Path.Combine(reportDirectory, "report.json"),
                Path.Combine(reportDirectory, "report.md"),
                []);
            failure ??= new CompatibilityReportFailure(
                "inspect-report-artifacts",
                exception.GetType().FullName ?? exception.GetType().Name,
                TestRunSummaryReporter.SanitizeFailureMessage(exception.Message));
        }

        var isCanonicalReleaseInvocation = IsCanonicalReleaseInvocation(
            invocation,
            selectedTargetIds,
            packageInput);
        var candidateRepositoryCommit = packageInput?.RepositoryCommit;
        var candidateMatchesCheckout = packageInput is not null &&
                                       IsFullRepositoryCommit(candidateRepositoryCommit) &&
                                       candidateRepositoryCommit!.Equals(
                                           runnerEndState.Commit,
                                           StringComparison.OrdinalIgnoreCase);
        var packageSourceIsRepositoryArtifact = packageInput is not null &&
                                                IsPathStrictlyWithin(
                                                    packageInput.PackageDirectory,
                                                    Path.Combine(options.RepositoryRoot, "artifacts"));
        var outcome = DetermineOutcome(
            summary,
            isCompleteForInvocation,
            artifactsComplete,
            dependencySource,
            candidateStableDuringRun);
        var targetResultsValidForEvidence = TargetResultsAreValidForEvidence(
            targetReports,
            expectedTargets,
            dependencySource,
            options.RepositoryRoot,
            paths.ArtifactRoot,
            reportDirectory,
            packageInput);
        var validForEvidence = outcome == CompatibilityReportOutcome.Passed &&
                               isCanonicalReleaseInvocation &&
                               isFullTargetSet &&
                               targetResultsValidForEvidence &&
                               artifactsComplete &&
                               runnerEvidence.ValidForEvidence &&
                               candidateStableDuringRun &&
                               candidateMatchesCheckout &&
                               packageSourceIsRepositoryArtifact;
        var report = new CompatibilitySizeReport(
            SchemaVersion,
            completedAtUtc,
            options.RepositoryRoot,
            options.TargetSet,
            selectedTargetIds,
            expectedTargets.Count,
            isFullTargetSet,
            options.Configuration,
            options.RuntimeIdentifier,
            sdkVersion,
            reportDirectory,
            targetReports,
            summary)
        {
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationSeconds = Math.Round((completedAtUtc - startedAtUtc).TotalSeconds, 3),
            DependencySource = dependencySource,
            Invocation = invocation,
            PackageInput = packageInput,
            PackageNugetConfigPath = packageBuildContext?.NugetConfigPath,
            PackageCacheDirectory = packageBuildContext?.PackagesCacheDirectory,
            RunnerEntryAssembly = runnerAssemblies.EntryAssembly,
            RunnerDevToolsAssembly = runnerAssemblies.DevToolsAssembly,
            RunnerStartRepositoryCommit = runnerStartState.Commit,
            RunnerStartWorkingTreeDirty = runnerStartState.Dirty,
            RunnerStartStatusSha256 = runnerStartState.StatusSha256,
            RunnerRepositoryCommit = runnerEndState.Commit,
            RunnerWorkingTreeDirty = runnerEndState.Dirty,
            RunnerStatusSha256 = runnerEndState.StatusSha256,
            RunnerStateChangedDuringRun = runnerEvidence.ChangedDuringRun,
            RunnerAssemblyRevisionsMatchRepositoryCommit =
                runnerEvidence.AssemblyRevisionsMatchRepositoryCommit,
            RunnerAssembliesBuiltFromCleanRepositoryState =
                runnerEvidence.AssembliesBuiltFromCleanRepositoryState,
            RunnerStateValidForEvidence = runnerEvidence.ValidForEvidence,
            Outcome = outcome,
            OverallExitCode = ResolveExitCode(
                outcome,
                options.ReleaseEvidenceIntent,
                validForEvidence),
            IsCompleteForInvocation = isCompleteForInvocation,
            ArtifactsComplete = artifactsComplete,
            IsCanonicalReleaseInvocation = isCanonicalReleaseInvocation,
            CandidateStableDuringRun = candidateStableDuringRun,
            CandidateRepositoryCommit = candidateRepositoryCommit,
            CandidateMatchesCheckout = candidateMatchesCheckout,
            PackageDirectoryIsRepositoryArtifact = packageSourceIsRepositoryArtifact,
            TargetResultsValidForEvidence = targetResultsValidForEvidence,
            ReviewRequired = summary.DistinctWarningCount > 0,
            ValidForEvidence = validForEvidence,
            Artifacts = artifacts,
            Failure = failure
        };

        WriteReportArtifacts(report);
        return report;
    }

    private static CompatibilityTargetReport SanitizeTargetReport(CompatibilityTargetReport target)
    {
        var packageResolution = target.PackageResolution is null
            ? null
            : target.PackageResolution with
            {
                ResolvedPackages = Array.AsReadOnly(target.PackageResolution.ResolvedPackages
                    .Select(static package => package with
                    {
                        Source = SanitizeOptionalMessage(package.Source)
                    })
                    .ToArray()),
                Findings = Array.AsReadOnly(target.PackageResolution.Findings
                    .Select(static finding => finding with
                    {
                        Message = TestRunSummaryReporter.SanitizeFailureMessage(finding.Message)
                    })
                    .ToArray())
            };
        var warningSummary = target.WarningSummary with
        {
            Diagnostics = Array.AsReadOnly(target.WarningSummary.Diagnostics
                .Select(static diagnostic => diagnostic with
                {
                    Message = TestRunSummaryReporter.SanitizeFailureMessage(diagnostic.Message)
                })
                .ToArray())
        };

        return target with
        {
            Publish = SanitizeCommandReport(target.Publish),
            Smoke = SanitizeCommandReport(target.Smoke),
            Inspection = SanitizeCommandReport(target.Inspection),
            ThresholdWarnings = Array.AsReadOnly(target.ThresholdWarnings
                .Select(static finding => finding with
                {
                    Message = TestRunSummaryReporter.SanitizeFailureMessage(finding.Message)
                })
                .ToArray()),
            WarningSummary = warningSummary,
            PackageResolution = packageResolution
        };
    }

    private static CompatibilityCommandReport SanitizeCommandReport(CompatibilityCommandReport command) =>
        command with
        {
            Summary = SanitizeOptionalMessage(command.Summary),
            Browser = command.Browser is null
                ? null
                : command.Browser with
                {
                    FinalStatus = TestRunSummaryReporter.SanitizeFailureMessage(command.Browser.FinalStatus),
                    FinalStage = TestRunSummaryReporter.SanitizeFailureMessage(command.Browser.FinalStage),
                    WindowConsole = SanitizeMessages(command.Browser.WindowConsole),
                    PlaywrightConsole = SanitizeMessages(command.Browser.PlaywrightConsole),
                    PageErrors = SanitizeMessages(command.Browser.PageErrors)
                }
        };

    private static IReadOnlyList<string> SanitizeMessages(IReadOnlyList<string> messages) =>
        Array.AsReadOnly(messages
            .Select(static message => TestRunSummaryReporter.SanitizeFailureMessage(message))
            .ToArray());

    private static string? SanitizeOptionalMessage(string? message) =>
        message is null ? null : TestRunSummaryReporter.SanitizeFailureMessage(message);

    public static string ToMarkdown(CompatibilitySizeReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Compatibility Size Report");
        builder.AppendLine();
        builder.AppendLine($"Schema: `{report.SchemaVersion}` (revision `{report.SchemaRevision}`)");
        builder.AppendLine($"Generated UTC: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Started UTC: {report.StartedAtUtc:O}");
        builder.AppendLine($"Completed UTC: {report.CompletedAtUtc:O}");
        builder.AppendLine($"Duration seconds: `{report.DurationSeconds:0.###}`");
        builder.AppendLine($"Outcome: `{report.Outcome}` (exit `{report.OverallExitCode}`)");
        builder.AppendLine($"Complete for invocation: `{report.IsCompleteForInvocation}`");
        builder.AppendLine($"Artifacts complete: `{report.ArtifactsComplete}`");
        builder.AppendLine($"Canonical release invocation: `{report.IsCanonicalReleaseInvocation}`");
        builder.AppendLine($"Review required: `{report.ReviewRequired}`");
        builder.AppendLine($"Valid for release evidence: `{report.ValidForEvidence}`");
        builder.AppendLine($"Target set: `{report.TargetSet}`");
        builder.AppendLine($"Dependency source: `{report.DependencySource}`");
        if (report.Invocation is { } invocation)
        {
            builder.AppendLine($"Invocation command: `{invocation.Command}`");
            builder.AppendLine($"Invocation tooling profile: `{invocation.Profile}`");
            builder.AppendLine($"Invocation target set: `{invocation.TargetSet}`");
            builder.AppendLine($"Invocation target selectors: `{invocation.TargetSelectors ?? "default"}`");
            builder.AppendLine($"Invocation configuration: `{invocation.Configuration}`");
            builder.AppendLine($"Invocation runtime identifier: `{invocation.RuntimeIdentifier}`");
            builder.AppendLine($"Invocation report directory: `{invocation.ReportDirectory}`");
            builder.AppendLine($"Invocation uses explicit output: `{invocation.UsesExplicitOutput}`");
            builder.AppendLine($"Invocation output format: `{invocation.OutputFormat}`");
            builder.AppendLine($"Invocation release-evidence intent: `{invocation.ReleaseEvidenceIntent}`");
            builder.AppendLine($"Invocation clean intermediate outputs: `{invocation.CleanIntermediateOutputs}`");
            builder.AppendLine($"Invocation no restore: `{invocation.NoRestore}`");
            builder.AppendLine($"Invocation skip smoke: `{invocation.SkipSmoke}`");
            builder.AppendLine($"Invocation release thresholds: `{invocation.UseReleaseThresholds}`");
            builder.AppendLine($"Invocation fail on thresholds: `{invocation.FailOnThresholdWarnings}`");
            builder.AppendLine($"Invocation fail on banned payload: `{invocation.FailOnBannedPayload}`");
            builder.AppendLine($"Invocation continue on publish failure: `{invocation.ContinueOnPublishFailure}`");
            builder.AppendLine($"Invocation largest-file count: `{invocation.LargestFileCount}`");
            builder.AppendLine($"Invocation max total bytes: `{invocation.TotalSizeWarningBytes?.ToString() ?? "none"}`");
            builder.AppendLine($"Invocation max symbol-excluded bytes: `{invocation.SymbolExcludedSizeWarningBytes?.ToString() ?? "none"}`");
            builder.AppendLine($"Invocation max file count: `{invocation.FileCountWarning?.ToString() ?? "none"}`");
        }
        builder.AppendLine(
            $"Target coverage: `{report.Targets.Count}/{report.ExpectedTargetCount}` " +
            $"(`{(report.IsFullTargetSet ? "full" : "subset")}`)");
        builder.AppendLine($"Selected target ids: `{string.Join(", ", report.SelectedTargetIds)}`");
        builder.AppendLine($"Configuration: `{report.Configuration}`");
        builder.AppendLine($"Runtime identifier: `{report.RuntimeIdentifier}`");
        builder.AppendLine($"SDK: `{report.DotnetSdkVersion}`");
        AppendRunnerAssemblyIdentity(builder, "Runner entry assembly", report.RunnerEntryAssembly);
        AppendRunnerAssemblyIdentity(builder, "Runner DevTools assembly", report.RunnerDevToolsAssembly);
        builder.AppendLine($"Runner start repository commit: `{report.RunnerStartRepositoryCommit}`");
        builder.AppendLine($"Runner start working tree dirty: `{report.RunnerStartWorkingTreeDirty}`");
        builder.AppendLine($"Runner start status SHA-256: `{report.RunnerStartStatusSha256}`");
        builder.AppendLine($"Runner end repository commit: `{report.RunnerRepositoryCommit}`");
        builder.AppendLine($"Runner end working tree dirty: `{report.RunnerWorkingTreeDirty}`");
        builder.AppendLine($"Runner end status SHA-256: `{report.RunnerStatusSha256}`");
        builder.AppendLine($"Runner state changed during run: `{report.RunnerStateChangedDuringRun}`");
        builder.AppendLine(
            $"Runner assembly revisions match repository commit: " +
            $"`{report.RunnerAssemblyRevisionsMatchRepositoryCommit}`");
        builder.AppendLine(
            $"Runner assemblies built from clean repository state: " +
            $"`{report.RunnerAssembliesBuiltFromCleanRepositoryState}`");
        builder.AppendLine($"Runner state valid for evidence: `{report.RunnerStateValidForEvidence}`");
        if (report.PackageInput is { } packageInput)
        {
            builder.AppendLine($"Package directory: `{packageInput.PackageDirectory}`");
            builder.AppendLine($"Package version: `{packageInput.Version}`");
            builder.AppendLine($"Package aggregate identity: `{packageInput.AggregateIdentity}`");
            builder.AppendLine($"Package content aggregate SHA-256: `{packageInput.ContentAggregateSha256}`");
            builder.AppendLine($"Package repository commit: `{packageInput.RepositoryCommit ?? "unavailable"}`");
            builder.AppendLine($"Package NuGet config: `{report.PackageNugetConfigPath}`");
            builder.AppendLine($"Package cache: `{report.PackageCacheDirectory}`");
        }
        builder.AppendLine($"Candidate stable during run: `{report.CandidateStableDuringRun}`");
        builder.AppendLine($"Candidate repository commit: `{report.CandidateRepositoryCommit ?? "unavailable"}`");
        builder.AppendLine($"Candidate matches checkout: `{report.CandidateMatchesCheckout}`");
        builder.AppendLine($"Package directory is a repository artifact: `{report.PackageDirectoryIsRepositoryArtifact}`");
        builder.AppendLine($"Target results valid for evidence: `{report.TargetResultsValidForEvidence}`");
        if (report.Artifacts is { } artifacts)
        {
            builder.AppendLine($"Report JSON: `{artifacts.JsonPath}`");
            builder.AppendLine($"Report Markdown: `{artifacts.MarkdownPath}`");
            builder.AppendLine($"Referenced artifact count: `{artifacts.Files.Count}`");
        }
        builder.AppendLine($"Product publish failures: `{report.Summary.ProductPublishFailureCount}`");
        builder.AppendLine($"Product smoke failures: `{report.Summary.ProductSmokeFailureCount}`");
        builder.AppendLine($"Product inspection failures: `{report.Summary.ProductInspectionFailureCount}`");
        builder.AppendLine($"Environment failures: `{report.Summary.EnvironmentFailureCount}`");
        builder.AppendLine($"Unsupported observations: `{report.Summary.UnsupportedCount}`");
        builder.AppendLine($"Runner state failures: `{report.Summary.RunnerStateFailureCount}`");
        builder.AppendLine();
        if (report.Failure is { } failure)
        {
            builder.AppendLine("## Report Failure");
            builder.AppendLine();
            builder.AppendLine($"- Stage: `{EscapeTable(failure.Stage)}`");
            builder.AppendLine($"- Type: `{EscapeTable(failure.ExceptionType)}`");
            builder.AppendLine(
                $"- Message: {EscapeTable(failure.Message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal))}");
            builder.AppendLine();
        }
        if (report.PackageInput is { } candidate)
        {
            builder.AppendLine("## Package Inputs");
            builder.AppendLine();
            builder.AppendLine("| Package | Version | Size | SHA-256 | Repository commit |");
            builder.AppendLine("| --- | --- | ---: | --- | --- |");
            foreach (var package in candidate.Packages)
            {
                builder.AppendLine(
                    $"| `{EscapeTable(package.Id)}` | `{EscapeTable(package.Version)}` | {package.SizeBytes} | " +
                    $"`{package.Sha256}` | `{EscapeTable(package.RepositoryCommit ?? "missing")}` |");
            }
            builder.AppendLine();
        }

        if (report.Artifacts is { Files.Count: > 0 } reportArtifacts)
        {
            builder.AppendLine("## Referenced Artifacts");
            builder.AppendLine();
            builder.AppendLine("| Kind | Repository path | Size | SHA-256 |");
            builder.AppendLine("| --- | --- | ---: | --- |");
            foreach (var artifact in reportArtifacts.Files)
            {
                builder.AppendLine(
                    $"| `{EscapeTable(artifact.Kind)}` | `{EscapeTable(artifact.RepositoryRelativePath)}` | " +
                    $"{artifact.SizeBytes} | `{artifact.Sha256}` |");
            }
            builder.AppendLine();
        }

        builder.AppendLine("| Target | Graph | Publish | Smoke | Inspection | Files | Total | Symbol-excluded | .br | .gz | Banned | Warnings |");
        builder.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var target in report.Targets)
        {
            builder.AppendLine(string.Join(" | ", [
                $"| {EscapeTable(target.Name)}",
                target.RuntimeGraph.ToString(),
                FormatCommandStatus(target.Publish),
                FormatCommandStatus(target.Smoke),
                FormatCommandStatus(target.Inspection),
                target.Payload.FileCount.ToString(),
                CompatibilityPayloadInspector.FormatBytes(target.Payload.TotalBytes),
                CompatibilityPayloadInspector.FormatBytes(target.Payload.SymbolExcludedBytes),
                CompatibilityPayloadInspector.FormatBytes(target.BrotliAssets.TotalBytes),
                CompatibilityPayloadInspector.FormatBytes(target.GzipAssets.TotalBytes),
                target.BannedPayloads.Count.ToString(),
                $"{target.WarningSummary.DistinctWarningCount} distinct / {target.WarningSummary.TotalWarningCount} total |"
            ]));
        }

        foreach (var target in report.Targets)
        {
            builder.AppendLine();
            builder.AppendLine($"## {target.DisplayName}");
            builder.AppendLine();
            builder.AppendLine($"Publish directory: `{target.PublishDirectory}`");
            builder.AppendLine($"Mutable build scratch directory: `{target.BuildScratchDirectory}`");
            builder.AppendLine($"Runtime graph: `{target.RuntimeGraph}`");
            builder.AppendLine($"Publish log: `{target.Publish.RawLogPath ?? "-"}`");
            builder.AppendLine($"Publish binary log: `{target.Publish.BinaryLogPath ?? "-"}`");
            builder.AppendLine($"Publish executable: `{target.Publish.Executable ?? "-"}`");
            builder.AppendLine($"Publish arguments: `{string.Join(" ", target.Publish.Arguments)}`");
            builder.AppendLine($"Publish working directory: `{target.Publish.WorkingDirectory ?? "-"}`");
            builder.AppendLine($"Publish started UTC: `{target.Publish.StartedAtUtc?.ToString("O") ?? "-"}`");
            builder.AppendLine($"Publish completed UTC: `{target.Publish.CompletedAtUtc?.ToString("O") ?? "-"}`");
            builder.AppendLine($"Smoke log: `{target.Smoke.RawLogPath ?? "-"}`");
            builder.AppendLine($"Smoke executable: `{target.Smoke.Executable ?? "-"}`");
            builder.AppendLine($"Smoke working directory: `{target.Smoke.WorkingDirectory ?? "-"}`");
            builder.AppendLine($"Smoke started UTC: `{target.Smoke.StartedAtUtc?.ToString("O") ?? "-"}`");
            builder.AppendLine($"Smoke completed UTC: `{target.Smoke.CompletedAtUtc?.ToString("O") ?? "-"}`");
            builder.AppendLine($"Inspection log: `{target.Inspection.RawLogPath ?? "-"}`");

            if (target.PackageResolution is { } resolution)
            {
                builder.AppendLine($"Package provenance passed: `{resolution.Passed}`");
                builder.AppendLine($"Package assets file: `{resolution.AssetsPath}`");
                builder.AppendLine($"Package project libraries: `{string.Join(", ", resolution.ProjectLibraries)}`");
                foreach (var resolved in resolution.ResolvedPackages)
                {
                    builder.AppendLine(
                        $"Package resolution `{resolved.Id}`: version `{resolved.Version}`, " +
                        $"source match `{resolved.SourceMatchesPackageDirectory}`, hash match `{resolved.HashMatchesCandidate}`, " +
                        $"extracted files match `{resolved.ExtractedFilesMatchArchive}` " +
                        $"({resolved.VerifiedExtractedFileCount} verified)");
                }

                foreach (var finding in resolution.Findings)
                    builder.AppendLine(
                        $"Package provenance finding `{EscapeTable(finding.Code)}`: {MarkdownText(finding.Message)}");
            }

            if (target.Publish.FailureClassification != CompatibilityFailureClassification.None)
            {
                builder.AppendLine($"Publish failure disposition: `{target.Publish.FailureDisposition}`");
                builder.AppendLine($"Publish failure classification: `{target.Publish.FailureClassification}`");
            }

            if (target.Smoke.FailureClassification != CompatibilityFailureClassification.None)
            {
                builder.AppendLine($"Smoke failure disposition: `{target.Smoke.FailureDisposition}`");
                builder.AppendLine($"Smoke failure classification: `{target.Smoke.FailureClassification}`");
            }

            if (target.Inspection.FailureClassification != CompatibilityFailureClassification.None)
            {
                builder.AppendLine($"Inspection failure disposition: `{target.Inspection.FailureDisposition}`");
                builder.AppendLine($"Inspection failure classification: `{target.Inspection.FailureClassification}`");
            }

            if (target.Smoke.Browser is { } browser)
            {
                builder.AppendLine();
                builder.AppendLine("### Browser Smoke Telemetry");
                builder.AppendLine();
                builder.AppendLine($"- Contract present: `{browser.ContractPresent}`");
                builder.AppendLine($"- Final status: `{browser.FinalStatus}`");
                builder.AppendLine($"- Final stage: `{browser.FinalStage}`");
                builder.AppendLine($"- Window console entries: `{browser.WindowConsole.Count}`");
                builder.AppendLine($"- Playwright console entries: `{browser.PlaywrightConsole.Count}`");
                builder.AppendLine($"- Page errors: `{browser.PageErrors.Count}`");
                AppendTelemetryEntries(builder, "Window console", browser.WindowConsole);
                AppendTelemetryEntries(builder, "Playwright console", browser.PlaywrightConsole);
                AppendTelemetryEntries(builder, "Page errors", browser.PageErrors);
            }

            if (target.BannedPayloads.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Banned Payloads");
                builder.AppendLine();
                foreach (var finding in target.BannedPayloads)
                {
                    builder.AppendLine(
                        $"- `{finding.Rule}`: `{finding.RelativePath}` ({CompatibilityPayloadInspector.FormatBytes(finding.SizeBytes)})");
                }
            }

            if (target.ThresholdWarnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Threshold Warnings");
                builder.AppendLine();
                foreach (var finding in target.ThresholdWarnings)
                    builder.AppendLine($"- `{EscapeTable(finding.Metric)}`: {MarkdownText(finding.Message)}");
            }

            if (target.WarningSummary.Owners.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Warning Owners");
                builder.AppendLine();
                foreach (var owner in target.WarningSummary.Owners)
                {
                    builder.AppendLine(
                        $"- `{owner.Owner}`: {owner.DistinctWarningCount} distinct / {owner.TotalWarningCount} total");
                }
            }

            if (target.WarningSummary.Diagnostics.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Warning Diagnostics");
                builder.AppendLine();
                foreach (var diagnostic in target.WarningSummary.Diagnostics)
                {
                    var code = string.IsNullOrWhiteSpace(diagnostic.Code) ? "no-code" : diagnostic.Code;
                    builder.AppendLine(
                        $"- `{diagnostic.Owner}` `{EscapeTable(code)}` x{diagnostic.Count}: {MarkdownText(diagnostic.Message)}");
                }
            }

            if (target.LargestFiles.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Largest Files");
                builder.AppendLine();
                foreach (var file in target.LargestFiles)
                {
                    builder.AppendLine(
                        $"- `{file.RelativePath}` ({CompatibilityPayloadInspector.FormatBytes(file.SizeBytes)})");
                }
            }
        }

        return builder.ToString();
    }

    private CompatibilityTargetReport CreateTargetReport(
        string reportDirectory,
        DotnetCommandRunner runner,
        CompatibilityTargetDefinition target,
        CompatibilityDependencySource dependencySource,
        CompatibilityPackageInput? packageInput,
        CompatibilityPackageBuildContext? packageBuildContext)
    {
        var targetRoot = Path.Combine(reportDirectory, target.Name);
        var publishDirectory = Path.Combine(targetRoot, "publish");
        var buildScratchDirectory = CreateBuildScratchDirectory(
            paths.ArtifactRoot,
            options.TargetSet,
            target.Name,
            packageBuildContext?.BuildIdentity);
        ResetDirectory(
            targetRoot,
            reportDirectory,
            Path.Combine(paths.RepositoryRoot, "artifacts"));
        Directory.CreateDirectory(publishDirectory);

        var projectPath = ResolveRepositoryPath(target.ProjectRelativePath);
        DotnetCommandResult publishResult;
        CompatibilityCommandReport publishReport;
        var phaseExceptions = new List<Exception>();
        CompatibilityPackageResolutionReport? packageResolution = null;
        var packageProvenanceFailed = false;
        using (AcquireBuildArtifactsLock(
                   paths.ArtifactRoot,
                   options.TargetSet,
                   target.Name,
                   packageBuildContext?.BuildIdentity))
        {
            if (options.CleanIntermediateOutputs)
            {
                ResetDirectory(
                    buildScratchDirectory,
                    packageBuildContext?.RootDirectory ??
                    Path.Combine(paths.ArtifactRoot, "compat-size-build", options.TargetSet),
                    paths.ArtifactRoot);
            }

            publishResult = runner.Execute(
                DotnetCommandType.Publish,
                CreatePublishArguments(
                    target,
                    projectPath,
                    publishDirectory,
                    buildScratchDirectory,
                    options.Configuration,
                    options.RuntimeIdentifier,
                    options.NoRestore,
                    dependencySource,
                    packageInput?.Version,
                    packageBuildContext?.NugetConfigPath,
                    packageBuildContext?.PackagesCacheDirectory),
                artifactPrefix: $"compat-size-report-{target.Name}-publish",
                displayTarget: projectPath,
                generateBinaryLog: true,
                additionalEnvironmentVariables: packageBuildContext?.Environment);

            var publishCompletedAtUtc = DateTimeOffset.UtcNow;

            publishReport = new CompatibilityCommandReport(
                publishResult.ProcessResult.ExitCode == 0 ? CompatibilityCommandStatus.Succeeded : CompatibilityCommandStatus.Failed,
                publishResult.ProcessResult.ExitCode,
                publishResult.ProcessResult.Duration.TotalSeconds,
                publishResult.RawLogPath,
                CompatibilityWarningClassifier.ClassifyFailureDisposition(publishResult),
                CompatibilityWarningClassifier.ClassifyFailure(target, publishResult),
                publishResult.Analysis.FailureSummary)
            {
                Executable = "dotnet",
                Arguments = publishResult.Arguments,
                WorkingDirectory = paths.RepositoryRoot,
                StartedAtUtc = publishCompletedAtUtc - publishResult.ProcessResult.Duration,
                CompletedAtUtc = publishCompletedAtUtc,
                BinaryLogPath = publishResult.BinaryLogPath
            };

            if (packageInput is not null &&
                packageBuildContext is not null &&
                publishReport.Status == CompatibilityCommandStatus.Succeeded)
            {
                try
                {
                    RefuseReparsePoints(
                        paths.ArtifactRoot,
                        packageBuildContext.PackagesCacheDirectory);
                    RefuseReparsePointsRecursively(
                        packageBuildContext.PackagesCacheDirectory);
                    packageResolution = CompatibilityPackageRestoreAuditor.Audit(
                        target,
                        paths.RepositoryRoot,
                        options.RuntimeIdentifier,
                        buildScratchDirectory,
                        packageBuildContext.PackagesCacheDirectory,
                        packageBuildContext.NugetConfigPath,
                        packageInput);
                    packageProvenanceFailed = !packageResolution.Passed;
                    if (packageProvenanceFailed)
                    {
                        phaseExceptions.Add(new CompatibilityPackageProvenanceException(
                            string.Join(Environment.NewLine, packageResolution.Findings)));
                    }
                }
                catch (Exception exception) when (IsReportableException(exception))
                {
                    packageProvenanceFailed = true;
                    phaseExceptions.Add(new CompatibilityPackageProvenanceException(
                        $"Package provenance audit failed: {exception.Message}",
                        exception));
                }
            }
        }

        CompatibilityCommandReport smokeReport;
        if (packageProvenanceFailed)
        {
            smokeReport = CreatePackageProvenanceSkippedSmokeReport();
        }
        else
        {
            try
            {
                smokeReport = CreateSmokeReport(target, publishDirectory, publishReport.Status, targetRoot);
            }
            catch (Exception exception) when (IsReportableException(exception))
            {
                smokeReport = CreatePhaseExceptionReport(targetRoot, "smoke", exception);
            }
        }

        var inspection = EmptyPayloadInspection();
        IReadOnlyList<CompatibilityThresholdFinding> thresholdWarnings = [];
        var warningSummary = new CompatibilityWarningSummary(0, 0, [], []);
        CompatibilityCommandReport inspectionReport;
        var inspectionCompleted = false;
        var inspectionStartedAtUtc = DateTimeOffset.UtcNow;
        var inspectionStopwatch = Stopwatch.StartNew();

        try
        {
            inspection = CompatibilityPayloadInspector.Inspect(
                target,
                publishDirectory,
                options.LargestFileCount,
                options.TotalSizeWarningBytes,
                options.SymbolExcludedSizeWarningBytes,
                options.FileCountWarning);
            inspectionCompleted = true;
            thresholdWarnings = inspection.ThresholdWarnings;
        }
        catch (Exception exception) when (IsReportableException(exception))
        {
            phaseExceptions.Add(exception);
        }

        try
        {
            warningSummary = CompatibilityWarningClassifier.Summarize(target, publishResult.Analysis.Warnings);
        }
        catch (Exception exception) when (IsReportableException(exception))
        {
            phaseExceptions.Add(exception);
        }

        if (inspectionCompleted && options.UseReleaseThresholds)
        {
            try
            {
                thresholdWarnings = inspection.ThresholdWarnings
                    .Concat(CompatibilityReleaseThresholds.FindWarnings(
                        target,
                        publishDirectory,
                        inspection.Payload,
                        inspection.BrotliAssets))
                    .ToArray();
            }
            catch (Exception exception) when (IsReportableException(exception))
            {
                phaseExceptions.Add(exception);
            }
        }

        inspectionStopwatch.Stop();
        if (phaseExceptions.Count == 0)
        {
            inspectionReport = new CompatibilityCommandReport(
                CompatibilityCommandStatus.Succeeded,
                0,
                inspectionStopwatch.Elapsed.TotalSeconds,
                null,
                CompatibilityFailureDisposition.None,
                CompatibilityFailureClassification.None,
                "Payload inspection and report analysis completed.")
            {
                Executable = "DataLinq.DevTools.CompatibilityPayloadInspector",
                Arguments = [],
                WorkingDirectory = publishDirectory,
                StartedAtUtc = inspectionStartedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }
        else
        {
            var exception = phaseExceptions.Count == 1
                ? phaseExceptions[0]
                : new AggregateException("Multiple payload inspection or report-analysis failures occurred.", phaseExceptions);
            inspectionReport = CreatePhaseExceptionReport(targetRoot, "inspection", exception) with
            {
                Executable = "DataLinq.DevTools.CompatibilityPayloadInspector",
                Arguments = [],
                WorkingDirectory = publishDirectory,
                StartedAtUtc = inspectionStartedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow
            };
        }

        return new CompatibilityTargetReport(
            target.Name,
            target.Kind,
            target.RuntimeGraph,
            target.DisplayName,
            projectPath,
            publishDirectory,
            buildScratchDirectory,
            publishReport,
            smokeReport,
            inspectionReport,
            inspection.Payload,
            inspection.BannedPayloads,
            thresholdWarnings,
            warningSummary,
            inspection.LargestFiles,
            inspection.BrotliAssets,
            inspection.GzipAssets)
        {
            PackageResolution = packageResolution
        };
    }

    private CompatibilityTargetReport CreateInfrastructureFailureTargetReport(
        string reportDirectory,
        CompatibilityTargetDefinition target,
        Exception exception,
        CompatibilityPackageBuildContext? packageBuildContext)
    {
        var disposition = ClassifyExceptionDisposition(exception);
        var rawLogPath = WriteFailureLog(
            reportDirectory,
            $"{target.Name}-preparation-or-publish-failure.log",
            exception);
        var publish = new CompatibilityCommandReport(
            CompatibilityCommandStatus.Failed,
            null,
            null,
            rawLogPath,
            disposition,
            CompatibilityFailureClassification.Unknown,
            $"{exception.GetType().Name} while preparing or publishing target: {exception.Message}");
        var smoke = new CompatibilityCommandReport(
            CompatibilityCommandStatus.Skipped,
            null,
            null,
            null,
            CompatibilityFailureDisposition.None,
            CompatibilityFailureClassification.None,
            "Smoke skipped because target preparation or publish failed.");
        var inspection = new CompatibilityCommandReport(
            CompatibilityCommandStatus.Skipped,
            null,
            null,
            null,
            CompatibilityFailureDisposition.None,
            CompatibilityFailureClassification.None,
            "Inspection skipped because target preparation or publish failed.");

        return new CompatibilityTargetReport(
            target.Name,
            target.Kind,
            target.RuntimeGraph,
            target.DisplayName,
            ResolveRepositoryPath(target.ProjectRelativePath),
            Path.Combine(reportDirectory, target.Name, "publish"),
            CreateBuildScratchDirectory(
                paths.ArtifactRoot,
                options.TargetSet,
                target.Name,
                packageBuildContext?.BuildIdentity),
            publish,
            smoke,
            inspection,
            new CompatibilityPayloadSizeSummary(0, 0, 0),
            [],
            [],
            new CompatibilityWarningSummary(0, 0, [], []),
            [],
            new CompatibilityCompressedAssetSummary(".br", 0, 0),
            new CompatibilityCompressedAssetSummary(".gz", 0, 0));
    }

    internal static CompatibilityCommandReport CreatePackageProvenanceSkippedSmokeReport() =>
        new(
            CompatibilityCommandStatus.Skipped,
            null,
            null,
            null,
            CompatibilityFailureDisposition.None,
            CompatibilityFailureClassification.None,
            "Smoke skipped because package provenance validation failed.");

    internal static CompatibilityCommandReport CreatePhaseExceptionReport(
        string targetRoot,
        string phase,
        Exception exception)
    {
        var disposition = ClassifyExceptionDisposition(exception);
        return new CompatibilityCommandReport(
            CompatibilityCommandStatus.Failed,
            null,
            null,
            WriteFailureLog(targetRoot, $"{phase}-failure.log", exception),
            disposition,
            phase == "inspection"
                ? ContainsPackageProvenanceFailure(exception)
                    ? CompatibilityFailureClassification.PackageProvenance
                    : CompatibilityFailureClassification.PayloadInspection
                : disposition == CompatibilityFailureDisposition.Environment
                    ? CompatibilityFailureClassification.SdkOrWebAssemblyToolchain
                    : CompatibilityFailureClassification.ProductRegression,
            $"{exception.GetType().Name} during {phase}: {exception.Message}");
    }

    private static bool ContainsPackageProvenanceFailure(Exception exception) =>
        exception is CompatibilityPackageProvenanceException ||
        exception is AggregateException aggregate &&
        aggregate.Flatten().InnerExceptions.Any(static inner => inner is CompatibilityPackageProvenanceException);

    private static CompatibilityFailureDisposition ClassifyExceptionDisposition(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            return aggregateException.Flatten().InnerExceptions.All(IsEnvironmentException)
                ? CompatibilityFailureDisposition.Environment
                : CompatibilityFailureDisposition.Product;
        }

        return IsEnvironmentException(exception)
            ? CompatibilityFailureDisposition.Environment
            : CompatibilityFailureDisposition.Product;
    }

    private static bool IsEnvironmentException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;

    private static bool IsReportableException(Exception exception) =>
        exception is not OutOfMemoryException and
        not AccessViolationException and
        not OperationCanceledException;

    private static CompatibilityPayloadInspectionResult EmptyPayloadInspection() =>
        new(
            new CompatibilityPayloadSizeSummary(0, 0, 0),
            [],
            [],
            new CompatibilityCompressedAssetSummary(".br", 0, 0),
            new CompatibilityCompressedAssetSummary(".gz", 0, 0),
            []);

    private static string? WriteFailureLog(
        string directory,
        string fileName,
        Exception exception)
    {
        try
        {
            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, exception.ToString(), Encoding.UTF8);
            return path;
        }
        catch (Exception writeException) when (writeException is not OutOfMemoryException and
                                               not AccessViolationException and
                                               not OperationCanceledException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> CreatePublishArguments(
        CompatibilityTargetDefinition target,
        string projectPath,
        string publishDirectory,
        string buildScratchDirectory,
        string configuration,
        string runtimeIdentifier,
        bool noRestore,
        CompatibilityDependencySource dependencySource = CompatibilityDependencySource.ProjectReferences,
        string? packageVersion = null,
        string? nugetConfigPath = null,
        string? packagesCacheDirectory = null)
    {
        var arguments = new List<string>
        {
            "publish",
            projectPath,
            "-f",
            target.TargetFramework,
            "-c",
            configuration,
            "-v",
            "minimal",
            "-noAutoResponse",
            "--artifacts-path",
            buildScratchDirectory,
            $"-p:PublishDir={EnsureTrailingDirectorySeparator(publishDirectory)}",
            $"-p:DataLinqCompatibilityDependencySource={dependencySource}"
        };

        if (dependencySource == CompatibilityDependencySource.PackedPackages)
        {
            if (string.IsNullOrWhiteSpace(packageVersion) ||
                string.IsNullOrWhiteSpace(nugetConfigPath) ||
                string.IsNullOrWhiteSpace(packagesCacheDirectory))
            {
                throw new InvalidOperationException(
                    "PackedPackages publish arguments require an exact package version, NuGet config, and package cache.");
            }

            arguments.Add($"-p:DataLinqCandidateVersion={packageVersion}");
            arguments.Add($"-p:RestoreConfigFile={nugetConfigPath}");
            arguments.Add($"-p:RestorePackagesPath={packagesCacheDirectory}");
            arguments.Add($"-p:NuGetPackageRoot={packagesCacheDirectory}");
            arguments.Add($"-p:NuGetPackageFolders={packagesCacheDirectory}");
            arguments.Add("-p:RestoreDisablePackageSourceMapping=false");
        }
        else
        {
            arguments.Add("-p:DataLinqCandidateVersion=");
        }

        if (target.RequiresRuntimeIdentifier)
        {
            arguments.Add("-r");
            arguments.Add(runtimeIdentifier);
            arguments.Add("--self-contained");
            arguments.Add("true");
        }

        if (noRestore)
            arguments.Add("--no-restore");

        foreach (var property in target.PublishProperties)
            arguments.Add($"-p:{property}");

        return arguments;
    }

    private CompatibilityCommandReport CreateSmokeReport(
        CompatibilityTargetDefinition target,
        string publishDirectory,
        CompatibilityCommandStatus publishStatus,
        string targetRoot)
    {
        if (publishStatus != CompatibilityCommandStatus.Succeeded)
        {
            return new CompatibilityCommandReport(
                CompatibilityCommandStatus.Skipped,
                null,
                null,
                null,
                CompatibilityFailureDisposition.None,
                CompatibilityFailureClassification.None,
                "Smoke skipped because publish failed.");
        }

        if (options.SkipSmoke)
        {
            return new CompatibilityCommandReport(
                CompatibilityCommandStatus.Skipped,
                null,
                null,
                null,
                CompatibilityFailureDisposition.None,
                CompatibilityFailureClassification.None,
                "Smoke skipped by command option.");
        }

        if (target.IsWebAssembly)
            return BrowserSmokeRunner.Run(target, publishDirectory, targetRoot, paths);

        var executablePath = ResolvePublishedExecutable(target, publishDirectory);
        if (executablePath is null)
        {
            return new CompatibilityCommandReport(
                CompatibilityCommandStatus.Failed,
                null,
                null,
                null,
                CompatibilityFailureDisposition.Product,
                CompatibilityFailureClassification.Unknown,
                $"Could not find published executable '{target.ExecutableName}' in '{publishDirectory}'.");
        }

        ExternalCommandResult result;
        try
        {
            result = ExternalProcessRunner.Execute(
                executablePath,
                [],
                publishDirectory,
                paths.CreateEnvironment(options.Profile));
        }
        catch (Exception exception)
        {
            return new CompatibilityCommandReport(
                CompatibilityCommandStatus.Failed,
                null,
                null,
                null,
                CompatibilityFailureDisposition.Environment,
                CompatibilityFailureClassification.Unknown,
                $"Could not start published smoke executable: {exception.Message}");
        }
        var rawLogPath = WriteSmokeLog(target.Name, result);
        var completedAtUtc = DateTimeOffset.UtcNow;

        return new CompatibilityCommandReport(
            result.ExitCode == 0 ? CompatibilityCommandStatus.Succeeded : CompatibilityCommandStatus.Failed,
            result.ExitCode,
            result.Duration.TotalSeconds,
            rawLogPath,
            result.ExitCode == 0
                ? CompatibilityFailureDisposition.None
                : CompatibilityFailureDisposition.Product,
            result.ExitCode == 0 ? CompatibilityFailureClassification.None : CompatibilityFailureClassification.Unknown,
            CreateSmokeSummary(result))
        {
            Executable = executablePath,
            Arguments = [],
            WorkingDirectory = publishDirectory,
            StartedAtUtc = completedAtUtc - result.Duration,
            CompletedAtUtc = completedAtUtc
        };
    }

    private string? ResolvePublishedExecutable(
        CompatibilityTargetDefinition target,
        string publishDirectory)
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { $"{target.ExecutableName}.exe", target.ExecutableName }
            : new[] { target.ExecutableName, $"{target.ExecutableName}.exe" };

        foreach (var candidate in candidates.Select(candidate => Path.Combine(publishDirectory, candidate)))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private string WriteSmokeLog(string targetName, ExternalCommandResult result)
    {
        var path = Path.Combine(
            paths.ArtifactRoot,
            $"compat-size-report-{targetName}-smoke-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.log");
        File.WriteAllText(path, string.Concat(result.StandardOutput, result.StandardError), Encoding.UTF8);
        return path;
    }

    private string ReadDotnetSdkVersion()
    {
        try
        {
            var result = ExternalProcessRunner.Execute(
                "dotnet",
                ["--version"],
                paths.RepositoryRoot,
                paths.CreateEnvironment(options.Profile));

            return result.ExitCode == 0
                ? result.StandardOutput.Trim()
                : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    internal static CompatibilityDependencySource ValidatePackageModeOptions(
        string targetSet,
        string? packageDirectory,
        string? packageVersion)
    {
        targetSet = CompatibilityTargetCatalog.NormalizeTargetSet(targetSet);

        if (packageDirectory is not null && string.IsNullOrWhiteSpace(packageDirectory))
            throw new InvalidOperationException("--package-dir must not be blank when supplied.");
        if (packageVersion is not null && string.IsNullOrWhiteSpace(packageVersion))
            throw new InvalidOperationException("--version must not be blank when supplied.");

        var hasPackageDirectory = packageDirectory is not null;
        var hasPackageVersion = packageVersion is not null;
        if (hasPackageDirectory != hasPackageVersion)
        {
            throw new InvalidOperationException(
                "--package-dir and --version must be supplied together for package-backed compatibility evidence.");
        }

        if (!hasPackageDirectory)
            return CompatibilityDependencySource.ProjectReferences;

        if (targetSet != CompatibilityTargetCatalog.CurrentTargetSet)
        {
            throw new InvalidOperationException(
                $"Package-backed compatibility evidence is supported only for --target {CompatibilityTargetCatalog.CurrentTargetSet}; " +
                $"the historical {CompatibilityTargetCatalog.HistoricalTargetSet} graph remains project-reference evidence.");
        }

        return CompatibilityDependencySource.PackedPackages;
    }

    internal static string ResolvePackageDirectory(string repositoryRoot, string packageDirectory) =>
        Path.IsPathRooted(packageDirectory)
            ? Path.GetFullPath(packageDirectory)
            : Path.GetFullPath(Path.Combine(repositoryRoot, packageDirectory));

    private static string CreatePackageBuildIdentity(CompatibilityPackageInput packageInput) =>
        ValidateBuildIdentity($"packed-{packageInput.ScratchIdentity}")!;

    internal static string CreatePackageBuildRootDirectory(
        string artifactRoot,
        string targetSet,
        string buildIdentity)
    {
        targetSet = CompatibilityTargetCatalog.NormalizeTargetSet(targetSet);
        buildIdentity = ValidateBuildIdentity(buildIdentity)!;
        return Path.GetFullPath(Path.Combine(
            artifactRoot,
            "compat-size-build",
            targetSet,
            buildIdentity));
    }

    internal static void ResetPackageBuildRootForCleanEvidence(
        string artifactRoot,
        string targetSet,
        string buildIdentity)
    {
        targetSet = CompatibilityTargetCatalog.NormalizeTargetSet(targetSet);
        var packageRoot = CreatePackageBuildRootDirectory(artifactRoot, targetSet, buildIdentity);
        RefuseReparsePoints(artifactRoot, packageRoot);
        ResetDirectoryWithoutFollowingReparsePoints(
            packageRoot,
            Path.Combine(artifactRoot, "compat-size-build", targetSet),
            artifactRoot);
    }

    private CompatibilityPackageBuildContext CreatePackageBuildContext(
        CompatibilityPackageInput packageInput,
        string buildIdentity)
    {
        var rootDirectory = CreatePackageBuildRootDirectory(
            paths.ArtifactRoot,
            options.TargetSet,
            buildIdentity);
        var packagesCacheDirectory = Path.Combine(rootDirectory, ".nuget", "packages");
        var httpCacheDirectory = Path.Combine(rootDirectory, ".nuget", "http-cache");
        var tempDirectory = Path.Combine(rootDirectory, ".tmp");
        var nugetConfigPath = Path.Combine(rootDirectory, "NuGet.Config");

        RefuseReparsePoints(paths.ArtifactRoot, rootDirectory);
        Directory.CreateDirectory(rootDirectory);
        RefuseReparsePoints(paths.ArtifactRoot, rootDirectory);

        foreach (var directory in new[] { packagesCacheDirectory, httpCacheDirectory, tempDirectory })
        {
            RefuseReparsePoints(paths.ArtifactRoot, directory);
            if (string.Equals(directory, packagesCacheDirectory, StringComparison.Ordinal))
                RefuseReparsePointsRecursively(directory);
            Directory.CreateDirectory(directory);
            RefuseReparsePoints(paths.ArtifactRoot, directory);
        }

        RefuseReparsePoints(paths.ArtifactRoot, nugetConfigPath);
        PackageConsumerSmokeRunner.WriteNugetConfig(nugetConfigPath, packageInput.PackageDirectory);
        RefuseReparsePoints(paths.ArtifactRoot, nugetConfigPath);
        var environment = PackageConsumerSmokeRunner.CreateIsolatedEnvironment(
            rootDirectory,
            packagesCacheDirectory,
            httpCacheDirectory,
            tempDirectory);
        RefuseReparsePoints(paths.ArtifactRoot, packagesCacheDirectory);
        RefuseReparsePointsRecursively(packagesCacheDirectory);

        return new CompatibilityPackageBuildContext(
            buildIdentity,
            rootDirectory,
            packagesCacheDirectory,
            nugetConfigPath,
            environment);
    }

    private static void RejectPackageDirectoryInsideArtifactRoot(
        string packageDirectory,
        string artifactRoot)
    {
        var fullPackageDirectory = Path.GetFullPath(packageDirectory);
        var fullArtifactRoot = Path.GetFullPath(artifactRoot);
        if (IsPathInsideOrEqual(fullArtifactRoot, fullPackageDirectory) ||
            IsPathInsideOrEqual(fullPackageDirectory, fullArtifactRoot))
        {
            throw new InvalidOperationException(
                $"Compatibility package directory '{fullPackageDirectory}' must not overlap developer artifact root '{fullArtifactRoot}'.");
        }
    }

    private RunnerRepositoryState ReadRunnerRepositoryState()
    {
        try
        {
            var environment = paths.CreateEnvironment(options.Profile);
            var commit = ExternalProcessRunner.Execute(
                "git",
                ["rev-parse", "HEAD"],
                paths.RepositoryRoot,
                environment);
            var status = ExternalProcessRunner.Execute(
                "git",
                ["status", "--porcelain"],
                paths.RepositoryRoot,
                environment);
            var commitValue = commit.StandardOutput.Trim();
            if (commit.ExitCode != 0 ||
                status.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(commitValue))
            {
                return RunnerRepositoryState.Unknown;
            }

            var normalizedStatus = status.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal);
            var statusSha256 = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(normalizedStatus)))
                .ToLowerInvariant();
            return new RunnerRepositoryState(
                commitValue,
                !string.IsNullOrWhiteSpace(normalizedStatus),
                statusSha256,
                true);
        }
        catch
        {
            return RunnerRepositoryState.Unknown;
        }
    }

    internal static RunnerEvidenceEvaluation EvaluateRunnerEvidence(
        RunnerRepositoryState start,
        RunnerRepositoryState end,
        CompatibilityRunnerAssemblyIdentity entryAssembly,
        CompatibilityRunnerAssemblyIdentity devToolsAssembly)
    {
        var changed = !start.Captured ||
                      !end.Captured ||
                      !start.Commit.Equals(end.Commit, StringComparison.OrdinalIgnoreCase) ||
                      start.Dirty != end.Dirty ||
                      !start.StatusSha256.Equals(end.StatusSha256, StringComparison.Ordinal);
        var assemblyRevisionsMatchRepositoryCommit =
            start.Captured &&
            end.Captured &&
            AssemblyRevisionMatchesRepositoryCommit(
                entryAssembly,
                ExpectedEntryAssemblyName,
                start.Commit) &&
            AssemblyRevisionMatchesRepositoryCommit(
                devToolsAssembly,
                ExpectedDevToolsAssemblyName,
                start.Commit) &&
            entryAssembly.RepositoryCommit.Equals(
                end.Commit,
                StringComparison.OrdinalIgnoreCase) &&
            devToolsAssembly.RepositoryCommit.Equals(
                end.Commit,
                StringComparison.OrdinalIgnoreCase);
        var assembliesBuiltFromCleanRepositoryState =
            entryAssembly.RepositoryBuildState.Equals(
                CleanRepositoryBuildState,
                StringComparison.Ordinal) &&
            devToolsAssembly.RepositoryBuildState.Equals(
                CleanRepositoryBuildState,
                StringComparison.Ordinal);
        var valid = start.Captured &&
                    end.Captured &&
                    !start.Dirty &&
                    !end.Dirty &&
                    !changed &&
                    assemblyRevisionsMatchRepositoryCommit &&
                    assembliesBuiltFromCleanRepositoryState;
        return new RunnerEvidenceEvaluation(
            changed,
            assemblyRevisionsMatchRepositoryCommit,
            assembliesBuiltFromCleanRepositoryState,
            valid);
    }

    internal static string? ExtractRepositoryCommitFromInformationalVersion(
        string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return null;

        var metadataSeparator = informationalVersion.IndexOf('+');
        if (metadataSeparator < 0 || metadataSeparator == informationalVersion.Length - 1)
            return null;

        var metadata = informationalVersion[(metadataSeparator + 1)..];
        var finalSeparator = metadata.LastIndexOf('.');
        var candidate = finalSeparator < 0 ? metadata : metadata[(finalSeparator + 1)..];
        return candidate.Length is 40 or 64 && candidate.All(Uri.IsHexDigit)
            ? candidate.ToLowerInvariant()
            : null;
    }

    private static RunnerAssemblyState ReadRunnerAssemblyState() =>
        new(
            ReadRunnerAssemblyIdentity(Assembly.GetEntryAssembly()),
            ReadRunnerAssemblyIdentity(typeof(CompatibilitySizeReporter).Assembly));

    private static CompatibilityRunnerAssemblyIdentity ReadRunnerAssemblyIdentity(Assembly? assembly)
    {
        if (assembly is null)
            return UnknownRunnerAssemblyIdentity;

        var name = assembly.GetName().Name ?? "unknown";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        var repositoryCommit = ExtractRepositoryCommitFromInformationalVersion(informationalVersion);
        var repositoryBuildState = ReadRunnerRepositoryBuildState(assembly);
        return new CompatibilityRunnerAssemblyIdentity(
            name,
            string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion,
            repositoryCommit ?? "unknown",
            repositoryCommit is not null,
            repositoryBuildState);
    }

    private static string ReadRunnerRepositoryBuildState(Assembly assembly)
    {
        var values = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(static attribute =>
                attribute.Key.Equals(RepositoryBuildStateMetadataName, StringComparison.Ordinal))
            .Select(static attribute => attribute.Value)
            .ToArray();
        return values.Length switch
        {
            0 => "missing",
            1 when string.IsNullOrWhiteSpace(values[0]) => "invalid",
            1 => values[0] ?? "invalid",
            _ => "ambiguous"
        };
    }

    private static bool AssemblyRevisionMatchesRepositoryCommit(
        CompatibilityRunnerAssemblyIdentity assembly,
        string expectedName,
        string repositoryCommit) =>
        assembly.RepositoryCommitCaptured &&
        assembly.Name.Equals(expectedName, StringComparison.Ordinal) &&
        assembly.RepositoryCommit.Equals(repositoryCommit, StringComparison.OrdinalIgnoreCase);

    internal static bool PackageInputsMatch(
        CompatibilityPackageInput first,
        CompatibilityPackageInput second)
    {
        if (!PathEquals(first.PackageDirectory, second.PackageDirectory) ||
            !first.Version.Equals(second.Version, StringComparison.Ordinal) ||
            !first.AggregateIdentity.Equals(second.AggregateIdentity, StringComparison.Ordinal) ||
            !first.ContentAggregateSha256.Equals(second.ContentAggregateSha256, StringComparison.Ordinal) ||
            !string.Equals(first.RepositoryCommit, second.RepositoryCommit, StringComparison.OrdinalIgnoreCase) ||
            first.Packages.Count != second.Packages.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Packages.Count; index++)
        {
            var left = first.Packages[index];
            var right = second.Packages[index];
            if (!left.Id.Equals(right.Id, StringComparison.Ordinal) ||
                !left.Version.Equals(right.Version, StringComparison.Ordinal) ||
                !PathEquals(left.PackagePath, right.PackagePath) ||
                left.SizeBytes != right.SizeBytes ||
                !left.Sha256.Equals(right.Sha256, StringComparison.Ordinal) ||
                !string.Equals(left.RepositoryCommit, right.RepositoryCommit, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool CandidateInputStillMatches(CompatibilityPackageInput packageInput)
    {
        try
        {
            var current = CompatibilityPackageInputInspector.Inspect(
                packageInput.PackageDirectory,
                packageInput.Version);
            return PackageInputsMatch(packageInput, current);
        }
        catch (Exception exception) when (IsReportableException(exception))
        {
            return false;
        }
    }

    internal static CompatibilityReportOutcome DetermineOutcome(
        CompatibilityReportSummary summary,
        bool isCompleteForInvocation,
        bool artifactsComplete,
        CompatibilityDependencySource dependencySource,
        bool candidateStableDuringRun)
    {
        if (!isCompleteForInvocation ||
            !artifactsComplete ||
            dependencySource == CompatibilityDependencySource.PackedPackages && !candidateStableDuringRun)
        {
            return CompatibilityReportOutcome.Incomplete;
        }

        return summary.HasHardFailures
            ? CompatibilityReportOutcome.Failed
            : CompatibilityReportOutcome.Passed;
    }

    internal static int ResolveExitCode(
        CompatibilityReportOutcome outcome,
        bool releaseEvidenceIntent,
        bool validForEvidence) =>
        outcome == CompatibilityReportOutcome.Passed &&
        (!releaseEvidenceIntent || validForEvidence)
            ? 0
            : 1;

    internal static bool IsInvocationComplete(
        CompatibilityReportInvocation invocation,
        IReadOnlyList<string> selectedTargetIds,
        IReadOnlyList<CompatibilityTargetReport> targets,
        CompatibilityDependencySource dependencySource)
    {
        if (!invocation.Command.Equals("size-report", StringComparison.Ordinal) ||
            selectedTargetIds.Count == 0 ||
            selectedTargetIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedTargetIds.Count ||
            !selectedTargetIds.SequenceEqual(
                targets.Select(static target => target.Name),
                StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (!CommandRecordIsComplete(target.Publish, requireRawLog: true, requireBinaryLog: true) ||
                !CommandRecordIsComplete(target.Smoke, requireRawLog: true, requireBinaryLog: false) ||
                !CommandRecordIsComplete(target.Inspection, requireRawLog: false, requireBinaryLog: false))
            {
                return false;
            }

            if (target.Publish.Status == CompatibilityCommandStatus.Succeeded)
            {
                if (invocation.SkipSmoke && target.Smoke.Status != CompatibilityCommandStatus.Skipped)
                    return false;
                if (!invocation.SkipSmoke && target.Smoke.Status == CompatibilityCommandStatus.Skipped)
                    return false;
                if (target.Inspection.Status == CompatibilityCommandStatus.Skipped)
                    return false;
                if (dependencySource == CompatibilityDependencySource.PackedPackages &&
                    target.PackageResolution is null)
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool IsCanonicalReleaseInvocation(
        CompatibilityReportInvocation invocation,
        IReadOnlyList<string> selectedTargetIds,
        CompatibilityPackageInput? packageInput)
    {
        var expectedTargetIds = CompatibilityTargetCatalog
            .GetTargets(CompatibilityTargetCatalog.CurrentTargetSet)
            .Select(static target => target.Name)
            .ToArray();
        var expectedPackageIds = PackageInspectionPolicy.PublicPackageIds
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        var actualPackageIds = packageInput?.Packages
            .Select(static package => package.Id)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static id => id, StringComparer.Ordinal)
            .ToArray() ?? [];

        return invocation.Command.Equals("size-report", StringComparison.Ordinal) &&
               invocation.TargetSet.Equals(CompatibilityTargetCatalog.CurrentTargetSet, StringComparison.Ordinal) &&
               selectedTargetIds.SequenceEqual(expectedTargetIds, StringComparer.Ordinal) &&
               invocation.Configuration.Equals("Release", StringComparison.Ordinal) &&
               invocation.RuntimeIdentifier.Equals(
                   CompatibilityTargetCatalog.DefaultRuntimeIdentifier(),
                   StringComparison.Ordinal) &&
               invocation.Profile != ToolingProfile.Sandbox &&
               invocation.DependencySource == CompatibilityDependencySource.PackedPackages &&
               !invocation.NoRestore &&
               !invocation.SkipSmoke &&
               invocation.CleanIntermediateOutputs &&
               invocation.UseReleaseThresholds &&
               invocation.FailOnBannedPayload &&
               invocation.FailOnThresholdWarnings &&
               invocation.ContinueOnPublishFailure &&
               invocation.LargestFileCount == 15 &&
               invocation.TotalSizeWarningBytes is null &&
               invocation.SymbolExcludedSizeWarningBytes is null &&
               invocation.FileCountWarning is null &&
               !string.IsNullOrWhiteSpace(invocation.ReportDirectory) &&
               invocation.UsesExplicitOutput &&
               !string.IsNullOrWhiteSpace(invocation.PackageDirectory) &&
               !string.IsNullOrWhiteSpace(invocation.PackageVersion) &&
               packageInput is not null &&
               packageInput.Version.Equals(invocation.PackageVersion, StringComparison.Ordinal) &&
               PathEquals(packageInput.PackageDirectory, invocation.PackageDirectory) &&
               actualPackageIds.SequenceEqual(expectedPackageIds, StringComparer.Ordinal) &&
               IsSha256(packageInput.ContentAggregateSha256);
    }

    internal static bool TargetResultsAreValidForEvidence(
        IReadOnlyList<CompatibilityTargetReport> targets,
        IReadOnlyList<CompatibilityTargetDefinition> expectedTargets,
        CompatibilityDependencySource dependencySource,
        string? repositoryRoot = null,
        string? artifactRoot = null,
        string? reportDirectory = null,
        CompatibilityPackageInput? packageInput = null)
    {
        if (dependencySource != CompatibilityDependencySource.PackedPackages ||
            targets.Count != expectedTargets.Count)
        {
            return false;
        }

        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            var expected = expectedTargets[index];
            if (!target.Name.Equals(expected.Name, StringComparison.Ordinal) ||
                target.Kind != expected.Kind ||
                target.RuntimeGraph != expected.RuntimeGraph ||
                !target.DisplayName.Equals(expected.DisplayName, StringComparison.Ordinal) ||
                target.Publish.Status != CompatibilityCommandStatus.Succeeded ||
                target.Publish.ExitCode != 0 ||
                !CommandRecordIsComplete(target.Publish, requireRawLog: true, requireBinaryLog: true) ||
                target.Smoke.Status != CompatibilityCommandStatus.Succeeded ||
                target.Smoke.ExitCode != 0 ||
                !CommandRecordIsComplete(target.Smoke, requireRawLog: true, requireBinaryLog: false) ||
                target.Inspection.Status != CompatibilityCommandStatus.Succeeded ||
                target.Inspection.ExitCode != 0 ||
                !CommandRecordIsComplete(target.Inspection, requireRawLog: false, requireBinaryLog: false) ||
                target.Payload.FileCount <= 0 ||
                target.Payload.TotalBytes <= 0 ||
                target.Payload.SymbolExcludedBytes <= 0 ||
                target.BannedPayloads.Count != 0 ||
                target.ThresholdWarnings.Count != 0 ||
                target.PackageResolution is not { Passed: true } resolution ||
                resolution.Findings.Count != 0 ||
                resolution.ResolvedPackages.Count == 0 ||
                resolution.ResolvedPackages.Any(static package =>
                    !package.ExactVersion ||
                    !package.SourceMatchesPackageDirectory ||
                    !package.HashMatchesCandidate ||
                    !package.ExtractedFilesMatchArchive ||
                    package.VerifiedExtractedFileCount <= 0) ||
                target.WarningSummary.Owners.Any(static owner =>
                    owner.Owner != CompatibilityWarningOwner.ThirdPartyDependency))
            {
                return false;
            }

            if (repositoryRoot is not null &&
                artifactRoot is not null &&
                reportDirectory is not null &&
                packageInput is not null &&
                !TargetPathsAndCommandsMatch(
                    target,
                    expected,
                    repositoryRoot,
                    artifactRoot,
                    reportDirectory,
                    packageInput))
            {
                return false;
            }

            if (expected.IsWebAssembly)
            {
                if (target.Smoke.Browser is not { ContractPresent: true } browser ||
                    !browser.FinalStatus.Equals("passed", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(browser.FinalStage) ||
                    browser.PageErrors.Count != 0)
                {
                    return false;
                }
            }
            else if (target.Smoke.Browser is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TargetPathsAndCommandsMatch(
        CompatibilityTargetReport target,
        CompatibilityTargetDefinition expected,
        string repositoryRoot,
        string artifactRoot,
        string reportDirectory,
        CompatibilityPackageInput packageInput)
    {
        var normalizedProjectRelativePath = expected.ProjectRelativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var expectedProjectPath = Path.Combine(repositoryRoot, normalizedProjectRelativePath);
        var expectedPublishDirectory = Path.Combine(reportDirectory, expected.Name, "publish");
        var buildIdentity = $"packed-{packageInput.ScratchIdentity}";
        var expectedBuildScratchDirectory = CreateBuildScratchDirectory(
            artifactRoot,
            CompatibilityTargetCatalog.CurrentTargetSet,
            expected.Name,
            buildIdentity);
        if (string.IsNullOrWhiteSpace(target.Publish.WorkingDirectory) ||
            string.IsNullOrWhiteSpace(target.Smoke.WorkingDirectory) ||
            string.IsNullOrWhiteSpace(target.Inspection.WorkingDirectory) ||
            string.IsNullOrWhiteSpace(target.Publish.Executable) ||
            !PathEquals(target.ProjectPath, expectedProjectPath) ||
            !PathEquals(target.PublishDirectory, expectedPublishDirectory) ||
            !PathEquals(target.BuildScratchDirectory, expectedBuildScratchDirectory) ||
            !PathEquals(target.Publish.WorkingDirectory, repositoryRoot) ||
            !target.Publish.Executable!.Equals("dotnet", StringComparison.Ordinal) ||
            target.Publish.Arguments.Count < 3 ||
            !PathEquals(target.Smoke.WorkingDirectory, expectedPublishDirectory) ||
            !PathEquals(target.Inspection.WorkingDirectory, expectedPublishDirectory))
        {
            return false;
        }

        if (!PackageResolutionMatchesCandidate(target, packageInput))
            return false;

        var buildRoot = Path.GetDirectoryName(Path.GetDirectoryName(expectedBuildScratchDirectory));
        if (string.IsNullOrWhiteSpace(buildRoot))
            return false;
        var packageContextRoot = Path.Combine(buildRoot, buildIdentity);
        var nugetConfigPath = Path.Combine(packageContextRoot, "NuGet.Config");
        var packageCacheDirectory = Path.Combine(packageContextRoot, ".nuget", "packages");
        var expectedArguments = CreatePublishArguments(
            expected,
            expectedProjectPath,
            expectedPublishDirectory,
            expectedBuildScratchDirectory,
            "Release",
            CompatibilityTargetCatalog.DefaultRuntimeIdentifier(),
            noRestore: false,
            CompatibilityDependencySource.PackedPackages,
            packageInput.Version,
            nugetConfigPath,
            packageCacheDirectory);
        if (target.Publish.Arguments.Count != expectedArguments.Count + 3 ||
            !target.Publish.Arguments.Take(expectedArguments.Count).SequenceEqual(
                expectedArguments,
                StringComparer.Ordinal) ||
            !target.Publish.Arguments[expectedArguments.Count].Equals("-nologo", StringComparison.Ordinal) ||
            !target.Publish.Arguments[expectedArguments.Count + 1].Equals(
                "-p:NuGetAudit=false",
                StringComparison.Ordinal) ||
            !target.Publish.Arguments[expectedArguments.Count + 2].Equals(
                $"/bl:{target.Publish.BinaryLogPath}",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (expected.IsWebAssembly)
        {
            if (!target.Smoke.Executable!.Equals(
                    "DataLinq.DevTools.BrowserSmokeRunner",
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        else if (!IsExpectedSmokeExecutable(target.Smoke.Executable, expectedPublishDirectory, expected.ExecutableName))
        {
            return false;
        }

        return target.Smoke.Arguments.Count == 0 &&
               target.Inspection.Executable!.Equals(
                   "DataLinq.DevTools.CompatibilityPayloadInspector",
                   StringComparison.Ordinal) &&
               target.Inspection.Arguments.Count == 0;
    }

    private static bool PackageResolutionMatchesCandidate(
        CompatibilityTargetReport target,
        CompatibilityPackageInput packageInput)
    {
        if (target.PackageResolution is not { Passed: true } resolution)
            return false;
        var expectedIds = target.RuntimeGraph == CompatibilityRuntimeGraph.Memory
            ? new[] { "DataLinq", "DataLinq.Memory" }
            : new[] { "DataLinq", "DataLinq.SQLite" };
        if (resolution.ResolvedPackages.Count != expectedIds.Length ||
            !resolution.ResolvedPackages
                .Select(static package => package.Id)
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(
                    expectedIds.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var resolved in resolution.ResolvedPackages)
        {
            var candidate = packageInput.Packages.SingleOrDefault(package =>
                package.Id.Equals(resolved.Id, StringComparison.OrdinalIgnoreCase));
            if (candidate is null ||
                !resolved.Version.Equals(candidate.Version, StringComparison.Ordinal) ||
                !PathEquals(resolved.CandidatePackagePath, candidate.PackagePath) ||
                !resolved.CandidateSha256.Equals(candidate.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolved.CachedSha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExpectedSmokeExecutable(
        string? executable,
        string publishDirectory,
        string executableName)
    {
        if (string.IsNullOrWhiteSpace(executable))
            return false;
        var withoutExtension = Path.Combine(publishDirectory, executableName);
        var withExtension = Path.Combine(publishDirectory, $"{executableName}.exe");
        return PathEquals(executable, withoutExtension) || PathEquals(executable, withExtension);
    }

    private static bool CommandRecordIsComplete(
        CompatibilityCommandReport command,
        bool requireRawLog,
        bool requireBinaryLog)
    {
        if (command.Status is CompatibilityCommandStatus.Skipped or CompatibilityCommandStatus.NotApplicable)
            return !string.IsNullOrWhiteSpace(command.Summary);

        if (command.Status == CompatibilityCommandStatus.Unsupported)
            return !string.IsNullOrWhiteSpace(command.Summary);

        return command.Status is CompatibilityCommandStatus.Succeeded or CompatibilityCommandStatus.Failed &&
               command.DurationSeconds is >= 0 &&
               command.StartedAtUtc.HasValue &&
               command.CompletedAtUtc.HasValue &&
               command.StartedAtUtc <= command.CompletedAtUtc &&
               !string.IsNullOrWhiteSpace(command.Executable) &&
               !string.IsNullOrWhiteSpace(command.WorkingDirectory) &&
               (!requireRawLog || !string.IsNullOrWhiteSpace(command.RawLogPath)) &&
               (!requireBinaryLog || !string.IsNullOrWhiteSpace(command.BinaryLogPath));
    }

    private static CompatibilityReportArtifacts CreateArtifactManifest(
        string repositoryRoot,
        string reportDirectory,
        IReadOnlyList<CompatibilityTargetReport> targets,
        string? packageNugetConfigPath)
    {
        var candidates = new List<(string Kind, string Path)>();
        if (!string.IsNullOrWhiteSpace(packageNugetConfigPath))
            candidates.Add(("package-nuget-config", packageNugetConfigPath));

        foreach (var target in targets)
        {
            AddArtifactCandidate(candidates, $"{target.Name}-publish-log", target.Publish.RawLogPath);
            AddArtifactCandidate(candidates, $"{target.Name}-publish-binlog", target.Publish.BinaryLogPath);
            AddArtifactCandidate(candidates, $"{target.Name}-smoke-log", target.Smoke.RawLogPath);
            AddArtifactCandidate(candidates, $"{target.Name}-inspection-log", target.Inspection.RawLogPath);
        }

        var duplicatePath = candidates
            .GroupBy(static candidate => Path.GetFullPath(candidate.Path), PathComparer)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicatePath is not null)
        {
            throw new InvalidDataException(
                $"Compatibility artifact path '{duplicatePath.Key}' is referenced more than once.");
        }

        var files = candidates
            .Select(candidate => CreateArtifactReference(repositoryRoot, candidate.Kind, candidate.Path))
            .ToArray();
        return new CompatibilityReportArtifacts(
            Path.Combine(reportDirectory, "report.json"),
            Path.Combine(reportDirectory, "report.md"),
            Array.AsReadOnly(files));
    }

    private static void AddArtifactCandidate(
        ICollection<(string Kind, string Path)> candidates,
        string kind,
        string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            candidates.Add((kind, path));
    }

    private static CompatibilityReportArtifact CreateArtifactReference(
        string repositoryRoot,
        string kind,
        string path)
    {
        var canonicalPath = Path.GetFullPath(path);
        var artifactRoot = Path.Combine(repositoryRoot, "artifacts");
        if (!IsPathStrictlyWithin(canonicalPath, artifactRoot))
            throw new InvalidDataException($"Compatibility artifact '{canonicalPath}' escaped '{artifactRoot}'.");
        RejectReparsePointTraversal(canonicalPath, "report artifact");
        if (!File.Exists(canonicalPath))
            throw new FileNotFoundException("Compatibility report artifact does not exist.", canonicalPath);
        var attributes = File.GetAttributes(canonicalPath);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException($"Compatibility artifact '{canonicalPath}' must be a regular file.");

        using var stream = new FileStream(
            canonicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        var sizeBytes = stream.Length;
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        var relativePath = Path.GetRelativePath(repositoryRoot, canonicalPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return new CompatibilityReportArtifact(kind, canonicalPath, relativePath, sizeBytes, sha256);
    }

    private static bool ArtifactsAreComplete(
        string repositoryRoot,
        IReadOnlyList<CompatibilityTargetReport> targets,
        CompatibilityDependencySource dependencySource,
        CompatibilityReportArtifacts artifacts,
        string? packageNugetConfigPath)
    {
        var reportDirectory = Path.GetDirectoryName(artifacts.JsonPath);
        if (string.IsNullOrWhiteSpace(reportDirectory) ||
            !PathEquals(reportDirectory, Path.GetDirectoryName(artifacts.MarkdownPath) ?? string.Empty))
        {
            return false;
        }

        try
        {
            ValidateReportDirectoryBoundary(repositoryRoot, reportDirectory);
        }
        catch (Exception exception) when (IsReportableException(exception))
        {
            return false;
        }

        var referencedPaths = artifacts.Files
            .Select(static artifact => Path.GetFullPath(artifact.Path))
            .ToHashSet(PathComparer);
        if (referencedPaths.Count != artifacts.Files.Count || artifacts.Files.Count == 0)
            return false;

        if (dependencySource == CompatibilityDependencySource.PackedPackages &&
            (string.IsNullOrWhiteSpace(packageNugetConfigPath) ||
             !referencedPaths.Contains(Path.GetFullPath(packageNugetConfigPath))))
        {
            return false;
        }

        foreach (var target in targets)
        {
            if (!HasReferencedArtifact(target.Publish.RawLogPath, referencedPaths) ||
                !HasReferencedArtifact(target.Publish.BinaryLogPath, referencedPaths))
            {
                return false;
            }

            if (target.Smoke.Status is CompatibilityCommandStatus.Succeeded or CompatibilityCommandStatus.Failed &&
                !HasReferencedArtifact(target.Smoke.RawLogPath, referencedPaths))
            {
                return false;
            }
            if (target.Inspection.Status == CompatibilityCommandStatus.Failed &&
                !HasReferencedArtifact(target.Inspection.RawLogPath, referencedPaths))
            {
                return false;
            }
        }

        return artifacts.Files.All(static artifact =>
            artifact.SizeBytes >= 0 &&
            IsSha256(artifact.Sha256) &&
            !string.IsNullOrWhiteSpace(artifact.Kind) &&
            !string.IsNullOrWhiteSpace(artifact.RepositoryRelativePath));
    }

    private static bool HasReferencedArtifact(string? path, IReadOnlySet<string> referencedPaths) =>
        !string.IsNullOrWhiteSpace(path) && referencedPaths.Contains(Path.GetFullPath(path));

    private static bool ArtifactReferenceStillMatches(CompatibilityReportArtifact artifact)
    {
        try
        {
            RejectReparsePointTraversal(artifact.Path, "report artifact");
            if (!File.Exists(artifact.Path))
                return false;
            var attributes = File.GetAttributes(artifact.Path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                return false;

            using var stream = new FileStream(
                artifact.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.SequentialScan);
            return stream.Length == artifact.SizeBytes &&
                   Convert.ToHexString(SHA256.HashData(stream))
                       .Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (IsReportableException(exception))
        {
            return false;
        }
    }

    private static bool IsFullRepositoryCommit(string? value) =>
        value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private void WriteReportArtifacts(CompatibilitySizeReport report)
    {
        if (report.Artifacts is null)
            throw new InvalidDataException("Compatibility report artifact paths are missing.");
        var reportDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(report.ReportDirectory));
        var expectedJsonPath = Path.Combine(reportDirectory, "report.json");
        var expectedMarkdownPath = Path.Combine(reportDirectory, "report.md");
        if (!PathEquals(report.Artifacts.JsonPath, expectedJsonPath) ||
            !PathEquals(report.Artifacts.MarkdownPath, expectedMarkdownPath))
        {
            throw new InvalidDataException(
                "Compatibility report artifact paths do not match the guarded report directory.");
        }

        ValidateReportDirectoryBoundary(options.RepositoryRoot, reportDirectory);
        RejectReparsePointTraversal(reportDirectory, "compatibility report directory");
        if (!report.Artifacts.Files.All(ArtifactReferenceStillMatches))
            throw new InvalidDataException("A referenced compatibility artifact changed before report completion.");
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
            File.WriteAllText(
                temporaryJsonPath,
                JsonSerializer.Serialize(report, jsonOptions),
                utf8NoBom);
            ValidateReportDirectoryBoundary(options.RepositoryRoot, reportDirectory);
            RejectReparsePointTraversal(expectedMarkdownPath, "Markdown report");
            File.Move(temporaryMarkdownPath, expectedMarkdownPath, overwrite: true);
            ValidateReportDirectoryBoundary(options.RepositoryRoot, reportDirectory);
            if (!report.Artifacts.Files.All(ArtifactReferenceStillMatches))
                throw new InvalidDataException("A referenced compatibility artifact changed before JSON completion.");
            if (report.PackageInput is not null && !CandidateInputStillMatches(report.PackageInput))
                throw new InvalidDataException("The package candidate changed before JSON completion.");
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

    private string ResolveRepositoryPath(string relativePath)
    {
        var normalized = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.Combine(paths.RepositoryRoot, normalized);
    }

    public static CompatibilityReportSummary CreateSummary(
        IReadOnlyList<CompatibilityTargetReport> targets,
        bool failOnBannedPayload,
        bool failOnThresholdWarnings,
        bool runnerStateFailure = false)
    {
        var productPublishFailureCount = targets.Count(static target =>
            IsProductFailure(target.Publish));
        var productSmokeFailureCount = targets.Count(static target =>
            IsProductFailure(target.Smoke));
        var productInspectionFailureCount = targets.Count(static target =>
            IsProductFailure(target.Inspection));
        var environmentFailureCount = targets.Sum(static target =>
            CountEnvironmentFailure(target.Publish) +
            CountEnvironmentFailure(target.Smoke) +
            CountEnvironmentFailure(target.Inspection));
        var unsupportedCount = targets.Sum(static target =>
            CountUnsupported(target.Publish) +
            CountUnsupported(target.Smoke) +
            CountUnsupported(target.Inspection));
        var bannedPayloadCount = targets.Sum(static target => target.BannedPayloads.Count);
        var thresholdWarningCount = targets.Sum(static target => target.ThresholdWarnings.Count);
        var distinctWarningCount = targets.Sum(static target => target.WarningSummary.DistinctWarningCount);
        var hasHardFailures =
            productPublishFailureCount > 0 ||
            productSmokeFailureCount > 0 ||
            productInspectionFailureCount > 0 ||
            environmentFailureCount > 0 ||
            unsupportedCount > 0 ||
            runnerStateFailure ||
            failOnBannedPayload && bannedPayloadCount > 0 ||
            failOnThresholdWarnings && thresholdWarningCount > 0;

        return new CompatibilityReportSummary(
            targets.Count,
            productPublishFailureCount,
            productSmokeFailureCount,
            productInspectionFailureCount,
            environmentFailureCount,
            unsupportedCount,
            bannedPayloadCount,
            thresholdWarningCount,
            distinctWarningCount,
            hasHardFailures)
        {
            RunnerStateFailureCount = runnerStateFailure ? 1 : 0
        };
    }

    private static int CountEnvironmentFailure(CompatibilityCommandReport command) =>
        command.Status == CompatibilityCommandStatus.Failed &&
        command.FailureDisposition == CompatibilityFailureDisposition.Environment
            ? 1
            : 0;

    private static bool IsProductFailure(CompatibilityCommandReport command) =>
        command.Status == CompatibilityCommandStatus.Failed &&
        command.FailureDisposition is
            CompatibilityFailureDisposition.Product or
            CompatibilityFailureDisposition.None;

    private static int CountUnsupported(CompatibilityCommandReport command) =>
        command.Status == CompatibilityCommandStatus.Unsupported ||
        command.FailureDisposition == CompatibilityFailureDisposition.Unsupported
            ? 1
            : 0;

    internal static string CreateReportDirectory(string artifactRoot)
    {
        var reportDirectory = CreateReportDirectoryPath(artifactRoot);
        Directory.CreateDirectory(reportDirectory);
        return reportDirectory;
    }

    private static string CreateReportDirectoryPath(string artifactRoot) =>
        Path.Combine(
            artifactRoot,
            "compat-size-report",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");

    private string PrepareReportDirectory(
        string repositoryRoot,
        string requestedDirectory,
        string? packageDirectory)
    {
        var reportDirectory = NormalizeOutputDirectory(repositoryRoot, requestedDirectory);
        if (!string.IsNullOrWhiteSpace(packageDirectory) && PathsOverlap(reportDirectory, packageDirectory))
        {
            throw new InvalidDataException(
                $"Compatibility report output '{reportDirectory}' must not overlap package input '{packageDirectory}'.");
        }

        if (File.Exists(reportDirectory))
            throw new InvalidDataException($"Compatibility report output '{reportDirectory}' is a file, not a directory.");
        if (Directory.Exists(reportDirectory))
            ClearKnownReportArtifacts(reportDirectory);
        else
            Directory.CreateDirectory(reportDirectory);

        ValidateReportDirectoryBoundary(repositoryRoot, reportDirectory);
        return reportDirectory;
    }

    private static void ValidateReportDirectoryBoundary(
        string repositoryRoot,
        string reportDirectory)
    {
        var artifactRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            Path.Combine(repositoryRoot, "artifacts")));
        if (!IsPathStrictlyWithin(reportDirectory, artifactRoot))
        {
            throw new InvalidDataException(
                $"Compatibility report output '{reportDirectory}' must remain below repository artifact root '{artifactRoot}'.");
        }

        var mutableBuildRoot = Path.Combine(repositoryRoot, "artifacts", "dev", "compat-size-build");
        if (PathsOverlap(reportDirectory, mutableBuildRoot))
        {
            throw new InvalidDataException(
                $"Compatibility report output '{reportDirectory}' must not overlap mutable compatibility build root '{mutableBuildRoot}'.");
        }
        var defaultReportParent = Path.Combine(repositoryRoot, "artifacts", "dev", "compat-size-report");
        if (PathEquals(reportDirectory, defaultReportParent))
        {
            throw new InvalidDataException(
                $"Compatibility report output '{reportDirectory}' must be a unique child directory, not the shared report parent.");
        }
        var reportLockRoot = GetReportLockRoot(repositoryRoot);
        if (PathsOverlap(reportDirectory, reportLockRoot))
        {
            throw new InvalidDataException(
                $"Compatibility report output '{reportDirectory}' must not overlap report-writer lock root '{reportLockRoot}'.");
        }

        RejectReparsePointTraversal(artifactRoot, "repository artifact root");
        RejectReparsePointTraversal(reportDirectory, "compatibility report directory");
        if (File.Exists(reportDirectory))
        {
            throw new InvalidDataException(
                $"Compatibility report output '{reportDirectory}' is a file, not a directory.");
        }
    }

    private static string GetReportLockRoot(string repositoryRoot) =>
        Path.GetFullPath(Path.Combine(
            repositoryRoot,
            "artifacts",
            "dev",
            "compat-size-report",
            ".locks"));

    private static void ClearKnownReportArtifacts(string reportDirectory)
    {
        RejectReparsePointTraversal(reportDirectory, "compatibility report directory");
        DeleteKnownReportArtifact(Path.Combine(reportDirectory, "report.json"));
        DeleteKnownReportArtifact(Path.Combine(reportDirectory, "report.md"));

        var unexpectedEntries = Directory.EnumerateFileSystemEntries(
                reportDirectory,
                "*",
                SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();

        if (unexpectedEntries.Length > 0)
        {
            throw new InvalidDataException(
                $"Compatibility report output '{reportDirectory}' contains prior run content " +
                $"({string.Join(", ", unexpectedEntries)}); " +
                "the completion marker was invalidated, but the directory must otherwise be fresh.");
        }
    }

    private static void DeleteKnownReportArtifact(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;

        var attributes = File.GetAttributes(path);
        if (!File.Exists(path) ||
            (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException(
                $"Compatibility report artifact '{path}' must be a regular file before it can be invalidated.");
        }

        File.Delete(path);
    }

    private static bool IsPathStrictlyWithin(string path, string root)
    {
        var canonicalPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var relative = Path.GetRelativePath(canonicalRoot, canonicalPath);
        return !Path.IsPathRooted(relative) &&
               relative != "." &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathsOverlap(string first, string second) =>
        IsPathStrictlyWithin(first, second) ||
        IsPathStrictlyWithin(second, first) ||
        PathEquals(first, second);

    private static bool PathEquals(string first, string second) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first))
            .Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                PathComparison);

    private static void RejectReparsePointTraversal(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException(
                $"Could not determine the filesystem root for {label} '{fullPath}'.");
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
                throw new InvalidDataException(
                    $"Compatibility {label} traverses reparse point '{current}', which is not allowed for release evidence.");
            }
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string CreateBuildScratchDirectory(
        string artifactRoot,
        string targetSet,
        string targetName,
        string? buildIdentity = null)
    {
        targetSet = CompatibilityTargetCatalog.NormalizeTargetSet(targetSet);
        buildIdentity = ValidateBuildIdentity(buildIdentity);
        return Path.GetFullPath(buildIdentity is null
            ? Path.Combine(artifactRoot, "compat-size-build", targetSet, targetName)
            : Path.Combine(artifactRoot, "compat-size-build", targetSet, buildIdentity, targetName));
    }

    internal static FileStream AcquireBuildArtifactsLock(
        string artifactRoot,
        string targetSet,
        string targetName,
        string? buildIdentity = null)
    {
        targetSet = CompatibilityTargetCatalog.NormalizeTargetSet(targetSet);
        buildIdentity = ValidateBuildIdentity(buildIdentity);
        var lockDirectory = Path.Combine(artifactRoot, "compat-size-build", ".locks", targetSet);
        if (buildIdentity is not null)
            lockDirectory = Path.Combine(lockDirectory, buildIdentity);
        Directory.CreateDirectory(lockDirectory);
        var lockPath = Path.Combine(lockDirectory, $"{targetName}.lock");

        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Compatibility target '{targetSet}/{(buildIdentity is null ? "" : buildIdentity + "/")}{targetName}' " +
                "is already being published by another process.",
                exception);
        }
    }

    private static string? ValidateBuildIdentity(string? buildIdentity)
    {
        if (buildIdentity is null)
            return null;

        if (string.IsNullOrWhiteSpace(buildIdentity) ||
            buildIdentity is "." or ".." ||
            buildIdentity.Any(static character =>
                !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.')))
        {
            throw new InvalidOperationException($"Invalid compatibility build identity '{buildIdentity}'.");
        }

        return buildIdentity;
    }

    internal static void ResetDirectory(string targetDirectory, string allowedRoot, string trustedRoot)
    {
        var fullTarget = Path.GetFullPath(targetDirectory);
        var fullRoot = Path.GetFullPath(allowedRoot);
        var fullTrustedRoot = Path.GetFullPath(trustedRoot);

        if (!IsPathStrictlyInside(fullTrustedRoot, fullRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to trust allowed root '{fullRoot}' outside '{fullTrustedRoot}'.");
        }

        if (!IsPathStrictlyInside(fullRoot, fullTarget))
            throw new InvalidOperationException($"Refusing to clean '{fullTarget}' outside allowed root '{fullRoot}'.");

        RefuseReparsePoints(fullTrustedRoot, fullTarget);

        if (Directory.Exists(fullTarget))
            Directory.Delete(fullTarget, recursive: true);

        Directory.CreateDirectory(fullTarget);
        RefuseReparsePoints(fullTrustedRoot, fullTarget);
    }

    private static void ResetDirectoryWithoutFollowingReparsePoints(
        string targetDirectory,
        string allowedRoot,
        string trustedRoot)
    {
        var fullTarget = Path.GetFullPath(targetDirectory);
        var fullRoot = Path.GetFullPath(allowedRoot);
        var fullTrustedRoot = Path.GetFullPath(trustedRoot);

        if (!IsPathStrictlyInside(fullTrustedRoot, fullRoot))
        {
            throw new InvalidOperationException(
                $"Refusing to trust allowed root '{fullRoot}' outside '{fullTrustedRoot}'.");
        }

        if (!IsPathStrictlyInside(fullRoot, fullTarget))
            throw new InvalidOperationException($"Refusing to clean '{fullTarget}' outside allowed root '{fullRoot}'.");

        RefuseReparsePoints(fullTrustedRoot, fullTarget);
        if (Directory.Exists(fullTarget))
            DeleteDirectoryTreeWithoutFollowingReparsePoints(fullTarget);
        else if (File.Exists(fullTarget))
            throw new IOException($"Compatibility package context '{fullTarget}' must be a directory.");

        Directory.CreateDirectory(fullTarget);
        RefuseReparsePoints(fullTrustedRoot, fullTarget);
    }

    private static void DeleteDirectoryTreeWithoutFollowingReparsePoints(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory).ToArray())
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                    Directory.Delete(entry, recursive: false);
                else
                    File.Delete(entry);
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
                DeleteDirectoryTreeWithoutFollowingReparsePoints(entry);
            else
                File.Delete(entry);
        }

        Directory.Delete(directory, recursive: false);
    }

    private static void RefuseReparsePoints(string trustedRoot, string targetPath)
    {
        var relativePath = Path.GetRelativePath(trustedRoot, targetPath);
        var currentPath = trustedRoot;

        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);

            try
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"Refusing to clean '{targetPath}' through reparse point '{currentPath}'.");
                }
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
        }
    }

    internal static void RefuseReparsePointsRecursively(string rootDirectory)
    {
        var fullRoot = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(fullRoot))
        {
            if (File.Exists(fullRoot))
                throw new IOException($"Compatibility package context '{fullRoot}' must be a directory.");
            return;
        }

        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"Compatibility package context root '{fullRoot}' is a reparse point.");
        }

        var directories = new Stack<string>();
        directories.Push(fullRoot);
        while (directories.Count > 0)
        {
            var directory = directories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"Compatibility package context '{fullRoot}' contains reparse point '{entry}'.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                    directories.Push(entry);
            }
        }
    }

    private static string EnsureTrailingDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool IsPathInsideOrEqual(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return relativePath == "." ||
               (!relativePath.Equals("..", StringComparison.Ordinal) &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathRooted(relativePath));
    }

    private static bool IsPathStrictlyInside(string root, string path) =>
        IsPathInsideOrEqual(root, path) && Path.GetRelativePath(root, path) != ".";

    private static string CreateSmokeSummary(ExternalCommandResult result)
    {
        var firstLine = string.Concat(result.StandardOutput, result.StandardError)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return firstLine ?? $"Smoke exited with code {result.ExitCode}.";
    }

    private static string FormatCommandStatus(CompatibilityCommandReport command) =>
        command.Status switch
        {
            CompatibilityCommandStatus.Succeeded => "ok",
            CompatibilityCommandStatus.Failed =>
                $"failed ({command.FailureDisposition}/{command.FailureClassification})",
            CompatibilityCommandStatus.Skipped => "skipped",
            CompatibilityCommandStatus.NotApplicable => "n/a",
            CompatibilityCommandStatus.Unsupported => $"unsupported ({command.FailureClassification})",
            _ => command.Status.ToString()
        };

    private static void AppendRunnerAssemblyIdentity(
        StringBuilder builder,
        string label,
        CompatibilityRunnerAssemblyIdentity? assembly)
    {
        if (assembly is null)
        {
            builder.AppendLine($"{label}: `missing`");
            return;
        }

        builder.AppendLine($"{label}: `{assembly.Name}`");
        builder.AppendLine($"{label} informational version: `{assembly.InformationalVersion}`");
        builder.AppendLine($"{label} repository commit: `{assembly.RepositoryCommit}`");
        builder.AppendLine($"{label} repository commit captured: `{assembly.RepositoryCommitCaptured}`");
        builder.AppendLine($"{label} repository build state: `{assembly.RepositoryBuildState}`");
    }

    private static string EscapeTable(string value) => MarkdownText(value);

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

    private static void AppendTelemetryEntries(
        StringBuilder builder,
        string label,
        IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
            return;

        builder.AppendLine($"- {label}:");
        foreach (var entry in entries)
            builder.AppendLine($"  - <code>{MarkdownText(entry)}</code>");
    }

    internal sealed record RunnerRepositoryState(
        string Commit,
        bool Dirty,
        string StatusSha256,
        bool Captured)
    {
        public static RunnerRepositoryState Unknown { get; } = new("unknown", true, "unknown", false);
    }

    internal sealed record RunnerEvidenceEvaluation(
        bool ChangedDuringRun,
        bool AssemblyRevisionsMatchRepositoryCommit,
        bool AssembliesBuiltFromCleanRepositoryState,
        bool ValidForEvidence);

    private sealed record RunnerAssemblyState(
        CompatibilityRunnerAssemblyIdentity EntryAssembly,
        CompatibilityRunnerAssemblyIdentity DevToolsAssembly);

    private static CompatibilityRunnerAssemblyIdentity UnknownRunnerAssemblyIdentity { get; } =
        new("unknown", "unknown", "unknown", false, "missing");

    private sealed record CompatibilityPackageBuildContext(
        string BuildIdentity,
        string RootDirectory,
        string PackagesCacheDirectory,
        string NugetConfigPath,
        IReadOnlyDictionary<string, string?> Environment);

    internal sealed class CompatibilityPackageProvenanceException : Exception
    {
        public CompatibilityPackageProvenanceException(string message)
            : base(message)
        {
        }

        public CompatibilityPackageProvenanceException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
