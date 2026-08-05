using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataLinq.DevTools;

public sealed class CompatibilitySizeReporter
{
    public const string SchemaVersion = "v0.9.compatibility-size-report.v4";

    private const string ExpectedEntryAssemblyName = "DataLinq.Dev.CLI";
    private const string ExpectedDevToolsAssemblyName = "DataLinq.DevTools";

    private readonly DevToolPaths paths;
    private readonly CompatibilityReportOptions options;

    public CompatibilitySizeReporter(DevToolPaths paths, CompatibilityReportOptions options)
    {
        this.paths = paths;
        this.options = options with
        {
            TargetSet = CompatibilityTargetCatalog.NormalizeTargetSet(options.TargetSet)
        };
    }

    public CompatibilitySizeReport CreateReport()
    {
        var dependencySource = ValidatePackageModeOptions(
            options.TargetSet,
            options.PackageDirectory,
            options.PackageVersion);

        if (options.CleanIntermediateOutputs && options.NoRestore)
        {
            throw new InvalidOperationException(
                "--clean-output cannot be combined with --no-restore because cleaning removes the target-owned restore assets.");
        }

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

        var reportDirectory = CreateReportDirectory(paths.ArtifactRoot);
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

        var isFullTargetSet = targetReports
            .Select(static target => target.Name)
            .SequenceEqual(
                expectedTargets.Select(static target => target.Name),
                StringComparer.OrdinalIgnoreCase);

        var sdkVersion = ReadDotnetSdkVersion();
        var runnerEndState = ReadRunnerRepositoryState();
        var runnerEvidence = EvaluateRunnerEvidence(
            runnerStartState,
            runnerEndState,
            runnerAssemblies.EntryAssembly,
            runnerAssemblies.DevToolsAssembly);
        var report = new CompatibilitySizeReport(
            SchemaVersion,
            DateTimeOffset.UtcNow,
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
            CreateSummary(
                targetReports,
                options.FailOnBannedPayload,
                options.FailOnThresholdWarnings,
                !runnerEvidence.ValidForEvidence))
        {
            DependencySource = dependencySource,
            Invocation = new CompatibilityReportInvocation(
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
                options.FileCountWarning),
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
            RunnerStateValidForEvidence = runnerEvidence.ValidForEvidence
        };

        WriteReportArtifacts(report);
        return report;
    }

    public static string ToMarkdown(CompatibilitySizeReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Compatibility Size Report");
        builder.AppendLine();
        builder.AppendLine($"Generated UTC: {report.GeneratedAtUtc:O}");
        builder.AppendLine($"Target set: `{report.TargetSet}`");
        builder.AppendLine($"Dependency source: `{report.DependencySource}`");
        if (report.Invocation is { } invocation)
        {
            builder.AppendLine($"Invocation tooling profile: `{invocation.Profile}`");
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
        builder.AppendLine($"Runner state valid for evidence: `{report.RunnerStateValidForEvidence}`");
        if (report.PackageInput is { } packageInput)
        {
            builder.AppendLine($"Package directory: `{packageInput.PackageDirectory}`");
            builder.AppendLine($"Package version: `{packageInput.Version}`");
            builder.AppendLine($"Package aggregate identity: `{packageInput.AggregateIdentity}`");
            builder.AppendLine($"Package NuGet config: `{report.PackageNugetConfigPath}`");
            builder.AppendLine($"Package cache: `{report.PackageCacheDirectory}`");
        }
        builder.AppendLine($"Product publish failures: `{report.Summary.ProductPublishFailureCount}`");
        builder.AppendLine($"Product smoke failures: `{report.Summary.ProductSmokeFailureCount}`");
        builder.AppendLine($"Product inspection failures: `{report.Summary.ProductInspectionFailureCount}`");
        builder.AppendLine($"Environment failures: `{report.Summary.EnvironmentFailureCount}`");
        builder.AppendLine($"Unsupported observations: `{report.Summary.UnsupportedCount}`");
        builder.AppendLine($"Runner state failures: `{report.Summary.RunnerStateFailureCount}`");
        builder.AppendLine();
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
            builder.AppendLine($"Smoke log: `{target.Smoke.RawLogPath ?? "-"}`");
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
                    builder.AppendLine($"Package provenance finding `{finding.Code}`: {finding.Message}");
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
                    builder.AppendLine($"- `{finding.Metric}`: {finding.Message}");
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
                        $"- `{diagnostic.Owner}` `{code}` x{diagnostic.Count}: {diagnostic.Message}");
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
        ResetDirectory(targetRoot, reportDirectory, paths.ArtifactRoot);
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

            publishReport = new CompatibilityCommandReport(
                publishResult.ProcessResult.ExitCode == 0 ? CompatibilityCommandStatus.Succeeded : CompatibilityCommandStatus.Failed,
                publishResult.ProcessResult.ExitCode,
                publishResult.ProcessResult.Duration.TotalSeconds,
                publishResult.RawLogPath,
                CompatibilityWarningClassifier.ClassifyFailureDisposition(publishResult),
                CompatibilityWarningClassifier.ClassifyFailure(target, publishResult),
                publishResult.Analysis.FailureSummary);

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
                "Payload inspection and report analysis completed.");
        }
        else
        {
            var exception = phaseExceptions.Count == 1
                ? phaseExceptions[0]
                : new AggregateException("Multiple payload inspection or report-analysis failures occurred.", phaseExceptions);
            inspectionReport = CreatePhaseExceptionReport(targetRoot, "inspection", exception);
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

        return new CompatibilityCommandReport(
            result.ExitCode == 0 ? CompatibilityCommandStatus.Succeeded : CompatibilityCommandStatus.Failed,
            result.ExitCode,
            result.Duration.TotalSeconds,
            rawLogPath,
            result.ExitCode == 0
                ? CompatibilityFailureDisposition.None
                : CompatibilityFailureDisposition.Product,
            result.ExitCode == 0 ? CompatibilityFailureClassification.None : CompatibilityFailureClassification.Unknown,
            CreateSmokeSummary(result));
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
        var valid = start.Captured &&
                    end.Captured &&
                    !start.Dirty &&
                    !end.Dirty &&
                    !changed &&
                    assemblyRevisionsMatchRepositoryCommit;
        return new RunnerEvidenceEvaluation(
            changed,
            assemblyRevisionsMatchRepositoryCommit,
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
        return new CompatibilityRunnerAssemblyIdentity(
            name,
            string.IsNullOrWhiteSpace(informationalVersion) ? "unknown" : informationalVersion,
            repositoryCommit ?? "unknown",
            repositoryCommit is not null);
    }

    private static bool AssemblyRevisionMatchesRepositoryCommit(
        CompatibilityRunnerAssemblyIdentity assembly,
        string expectedName,
        string repositoryCommit) =>
        assembly.RepositoryCommitCaptured &&
        assembly.Name.Equals(expectedName, StringComparison.Ordinal) &&
        assembly.RepositoryCommit.Equals(repositoryCommit, StringComparison.OrdinalIgnoreCase);

    private void WriteReportArtifacts(CompatibilitySizeReport report)
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
        var reportDirectory = Path.Combine(
            artifactRoot,
            "compat-size-report",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(reportDirectory);
        return reportDirectory;
    }

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
    }

    private static string EscapeTable(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private static void AppendTelemetryEntries(
        StringBuilder builder,
        string label,
        IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
            return;

        builder.AppendLine($"- {label}:");
        foreach (var entry in entries)
        {
            var singleLine = entry
                .Replace('`', '\'')
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            builder.AppendLine($"  - `{singleLine}`");
        }
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
        bool ValidForEvidence);

    private sealed record RunnerAssemblyState(
        CompatibilityRunnerAssemblyIdentity EntryAssembly,
        CompatibilityRunnerAssemblyIdentity DevToolsAssembly);

    private static CompatibilityRunnerAssemblyIdentity UnknownRunnerAssemblyIdentity { get; } =
        new("unknown", "unknown", "unknown", false);

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
