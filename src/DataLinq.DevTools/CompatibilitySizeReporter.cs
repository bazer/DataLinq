using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataLinq.DevTools;

public sealed class CompatibilitySizeReporter
{
    public const string SchemaVersion = "v0.9.compatibility-size-report.v2";

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
        if (options.CleanIntermediateOutputs && options.NoRestore)
        {
            throw new InvalidOperationException(
                "--clean-output cannot be combined with --no-restore because cleaning removes the target-owned restore assets.");
        }

        paths.EnsureCreated();

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
                targetReport = CreateTargetReport(reportDirectory, runner, target);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and
                                              not AccessViolationException and
                                              not OperationCanceledException)
            {
                targetReport = CreateInfrastructureFailureTargetReport(reportDirectory, target, exception);
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
                options.FailOnThresholdWarnings));

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
        builder.AppendLine(
            $"Target coverage: `{report.Targets.Count}/{report.ExpectedTargetCount}` " +
            $"(`{(report.IsFullTargetSet ? "full" : "subset")}`)");
        builder.AppendLine($"Selected target ids: `{string.Join(", ", report.SelectedTargetIds)}`");
        builder.AppendLine($"Configuration: `{report.Configuration}`");
        builder.AppendLine($"Runtime identifier: `{report.RuntimeIdentifier}`");
        builder.AppendLine($"SDK: `{report.DotnetSdkVersion}`");
        builder.AppendLine($"Product publish failures: `{report.Summary.ProductPublishFailureCount}`");
        builder.AppendLine($"Product smoke failures: `{report.Summary.ProductSmokeFailureCount}`");
        builder.AppendLine($"Product inspection failures: `{report.Summary.ProductInspectionFailureCount}`");
        builder.AppendLine($"Environment failures: `{report.Summary.EnvironmentFailureCount}`");
        builder.AppendLine($"Unsupported observations: `{report.Summary.UnsupportedCount}`");
        builder.AppendLine();
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
        CompatibilityTargetDefinition target)
    {
        var targetRoot = Path.Combine(reportDirectory, target.Name);
        var publishDirectory = Path.Combine(targetRoot, "publish");
        var buildScratchDirectory = CreateBuildScratchDirectory(
            paths.ArtifactRoot,
            options.TargetSet,
            target.Name);
        ResetDirectory(targetRoot, reportDirectory, paths.ArtifactRoot);
        Directory.CreateDirectory(publishDirectory);

        var projectPath = ResolveRepositoryPath(target.ProjectRelativePath);
        DotnetCommandResult publishResult;
        using (AcquireBuildArtifactsLock(paths.ArtifactRoot, options.TargetSet, target.Name))
        {
            if (options.CleanIntermediateOutputs)
            {
                ResetDirectory(
                    buildScratchDirectory,
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
                    options.NoRestore),
                artifactPrefix: $"compat-size-report-{target.Name}-publish",
                displayTarget: projectPath,
                generateBinaryLog: true);
        }

        var publishReport = new CompatibilityCommandReport(
            publishResult.ProcessResult.ExitCode == 0 ? CompatibilityCommandStatus.Succeeded : CompatibilityCommandStatus.Failed,
            publishResult.ProcessResult.ExitCode,
            publishResult.ProcessResult.Duration.TotalSeconds,
            publishResult.RawLogPath,
            CompatibilityWarningClassifier.ClassifyFailureDisposition(publishResult),
            CompatibilityWarningClassifier.ClassifyFailure(target, publishResult),
            publishResult.Analysis.FailureSummary);

        CompatibilityCommandReport smokeReport;
        try
        {
            smokeReport = CreateSmokeReport(target, publishDirectory, publishReport.Status, targetRoot);
        }
        catch (Exception exception) when (IsReportableException(exception))
        {
            smokeReport = CreatePhaseExceptionReport(targetRoot, "smoke", exception);
        }

        var inspection = EmptyPayloadInspection();
        IReadOnlyList<CompatibilityThresholdFinding> thresholdWarnings = [];
        var warningSummary = new CompatibilityWarningSummary(0, 0, [], []);
        CompatibilityCommandReport inspectionReport;
        var inspectionCompleted = false;
        var phaseExceptions = new List<Exception>();
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
            inspection.GzipAssets);
    }

    private CompatibilityTargetReport CreateInfrastructureFailureTargetReport(
        string reportDirectory,
        CompatibilityTargetDefinition target,
        Exception exception)
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
            CreateBuildScratchDirectory(paths.ArtifactRoot, options.TargetSet, target.Name),
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

    private static CompatibilityCommandReport CreatePhaseExceptionReport(
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
                ? CompatibilityFailureClassification.PayloadInspection
                : disposition == CompatibilityFailureDisposition.Environment
                    ? CompatibilityFailureClassification.SdkOrWebAssemblyToolchain
                    : CompatibilityFailureClassification.ProductRegression,
            $"{exception.GetType().Name} during {phase}: {exception.Message}");
    }

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
        bool noRestore)
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
            "--artifacts-path",
            buildScratchDirectory,
            $"-p:PublishDir={EnsureTrailingDirectorySeparator(publishDirectory)}"
        };

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
        bool failOnThresholdWarnings)
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
            hasHardFailures);
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
        string targetName)
    {
        targetSet = CompatibilityTargetCatalog.NormalizeTargetSet(targetSet);
        return Path.GetFullPath(Path.Combine(artifactRoot, "compat-size-build", targetSet, targetName));
    }

    internal static FileStream AcquireBuildArtifactsLock(
        string artifactRoot,
        string targetSet,
        string targetName)
    {
        targetSet = CompatibilityTargetCatalog.NormalizeTargetSet(targetSet);
        var lockDirectory = Path.Combine(artifactRoot, "compat-size-build", ".locks", targetSet);
        Directory.CreateDirectory(lockDirectory);
        var lockPath = Path.Combine(lockDirectory, $"{targetName}.lock");

        try
        {
            return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new IOException(
                $"Compatibility target '{targetSet}/{targetName}' is already being published by another process.",
                exception);
        }
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
}
