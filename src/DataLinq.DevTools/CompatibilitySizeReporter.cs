using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataLinq.DevTools;

public sealed class CompatibilitySizeReporter
{
    private const string SchemaVersion = "phase8c.compatibility-size-report.v1";

    private readonly DevToolPaths paths;
    private readonly CompatibilityReportOptions options;

    public CompatibilitySizeReporter(DevToolPaths paths, CompatibilityReportOptions options)
    {
        this.paths = paths;
        this.options = options;
    }

    public CompatibilitySizeReport CreateReport()
    {
        paths.EnsureCreated();

        var reportDirectory = CreateReportDirectory(paths.ArtifactRoot);
        var targets = CompatibilityTargetCatalog.GetTargets(options.TargetSet, options.Targets);
        var runner = new DotnetCommandRunner(paths, options.Profile);
        var targetReports = new List<CompatibilityTargetReport>();

        foreach (var target in targets)
        {
            var targetReport = CreateTargetReport(reportDirectory, runner, target);
            targetReports.Add(targetReport);

            if (targetReport.Publish.Status == CompatibilityCommandStatus.Failed && !options.ContinueOnPublishFailure)
                break;
        }

        var sdkVersion = ReadDotnetSdkVersion();
        var report = new CompatibilitySizeReport(
            SchemaVersion,
            DateTimeOffset.UtcNow,
            options.RepositoryRoot,
            options.TargetSet,
            options.Configuration,
            options.RuntimeIdentifier,
            sdkVersion,
            reportDirectory,
            targetReports,
            CreateSummary(targetReports));

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
        builder.AppendLine($"Configuration: `{report.Configuration}`");
        builder.AppendLine($"Runtime identifier: `{report.RuntimeIdentifier}`");
        builder.AppendLine($"SDK: `{report.DotnetSdkVersion}`");
        builder.AppendLine();
        builder.AppendLine("| Target | Publish | Smoke | Files | Total | Symbol-excluded | .br | .gz | Banned | Warnings |");
        builder.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var target in report.Targets)
        {
            builder.AppendLine(string.Join(" | ", [
                $"| {EscapeTable(target.Name)}",
                FormatCommandStatus(target.Publish),
                FormatCommandStatus(target.Smoke),
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
            builder.AppendLine($"Publish log: `{target.Publish.RawLogPath ?? "-"}`");
            builder.AppendLine($"Smoke log: `{target.Smoke.RawLogPath ?? "-"}`");

            if (target.Publish.FailureClassification != CompatibilityFailureClassification.None)
                builder.AppendLine($"Publish failure classification: `{target.Publish.FailureClassification}`");

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
        ResetDirectory(targetRoot, reportDirectory);
        Directory.CreateDirectory(publishDirectory);

        var projectPath = ResolveRepositoryPath(target.ProjectRelativePath);
        if (options.CleanIntermediateOutputs)
            ResetProjectOutputDirectories(projectPath);

        var publishResult = runner.Execute(
            DotnetCommandType.Publish,
            CreatePublishArguments(target, projectPath, publishDirectory),
            artifactPrefix: $"compat-size-report-{target.Name}-publish",
            displayTarget: projectPath,
            generateBinaryLog: true);

        var publishReport = new CompatibilityCommandReport(
            publishResult.ProcessResult.ExitCode == 0 ? CompatibilityCommandStatus.Succeeded : CompatibilityCommandStatus.Failed,
            publishResult.ProcessResult.ExitCode,
            publishResult.ProcessResult.Duration.TotalSeconds,
            publishResult.RawLogPath,
            CompatibilityWarningClassifier.ClassifyFailure(target, publishResult),
            publishResult.Analysis.FailureSummary);

        var smokeReport = CreateSmokeReport(target, publishDirectory, publishReport.Status, targetRoot);
        var inspection = CompatibilityPayloadInspector.Inspect(
            publishDirectory,
            options.LargestFileCount,
            options.TotalSizeWarningBytes,
            options.SymbolExcludedSizeWarningBytes,
            options.FileCountWarning);
        var thresholdWarnings = options.UseReleaseThresholds
            ? inspection.ThresholdWarnings
                .Concat(CompatibilityReleaseThresholds.FindWarnings(
                    target,
                    publishDirectory,
                    inspection.Payload,
                    inspection.BrotliAssets))
                .ToArray()
            : inspection.ThresholdWarnings;
        var warningSummary = CompatibilityWarningClassifier.Summarize(target, publishResult.Analysis.Warnings);

        return new CompatibilityTargetReport(
            target.Name,
            target.Kind,
            target.DisplayName,
            projectPath,
            publishDirectory,
            publishReport,
            smokeReport,
            inspection.Payload,
            inspection.BannedPayloads,
            thresholdWarnings,
            warningSummary,
            inspection.LargestFiles,
            inspection.BrotliAssets,
            inspection.GzipAssets);
    }

    private IReadOnlyList<string> CreatePublishArguments(
        CompatibilityTargetDefinition target,
        string projectPath,
        string publishDirectory)
    {
        var arguments = new List<string>
        {
            "publish",
            projectPath,
            "-f",
            target.TargetFramework,
            "-c",
            options.Configuration,
            "-v",
            "minimal",
            $"-p:PublishDir={EnsureTrailingDirectorySeparator(publishDirectory)}"
        };

        if (target.RequiresRuntimeIdentifier)
        {
            arguments.Add("-r");
            arguments.Add(options.RuntimeIdentifier);
            arguments.Add("--self-contained");
            arguments.Add("true");
        }

        if (options.NoRestore)
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
                CompatibilityFailureClassification.Unknown,
                $"Could not find published executable '{target.ExecutableName}' in '{publishDirectory}'.");
        }

        var result = ExternalProcessRunner.Execute(
            executablePath,
            [],
            publishDirectory,
            paths.CreateEnvironment(options.Profile));
        var rawLogPath = WriteSmokeLog(target.Name, result);

        return new CompatibilityCommandReport(
            result.ExitCode == 0 ? CompatibilityCommandStatus.Succeeded : CompatibilityCommandStatus.Failed,
            result.ExitCode,
            result.Duration.TotalSeconds,
            rawLogPath,
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

    private CompatibilityReportSummary CreateSummary(IReadOnlyList<CompatibilityTargetReport> targets)
    {
        var publishFailureCount = targets.Count(static target => target.Publish.Status == CompatibilityCommandStatus.Failed);
        var smokeFailureCount = targets.Count(static target => target.Smoke.Status == CompatibilityCommandStatus.Failed);
        var bannedPayloadCount = targets.Sum(static target => target.BannedPayloads.Count);
        var thresholdWarningCount = targets.Sum(static target => target.ThresholdWarnings.Count);
        var distinctWarningCount = targets.Sum(static target => target.WarningSummary.DistinctWarningCount);
        var hasHardFailures =
            publishFailureCount > 0 ||
            smokeFailureCount > 0 ||
            options.FailOnBannedPayload && bannedPayloadCount > 0 ||
            options.FailOnThresholdWarnings && thresholdWarningCount > 0;

        return new CompatibilityReportSummary(
            targets.Count,
            publishFailureCount,
            smokeFailureCount,
            bannedPayloadCount,
            thresholdWarningCount,
            distinctWarningCount,
            hasHardFailures);
    }

    private static string CreateReportDirectory(string artifactRoot)
    {
        var reportDirectory = Path.Combine(
            artifactRoot,
            "compat-size-report",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(reportDirectory);
        return reportDirectory;
    }

    private static void ResetDirectory(string targetDirectory, string allowedRoot)
    {
        var fullTarget = Path.GetFullPath(targetDirectory);
        var fullRoot = Path.GetFullPath(allowedRoot);

        if (!IsPathInsideOrEqual(fullRoot, fullTarget))
            throw new InvalidOperationException($"Refusing to clean '{fullTarget}' outside report root '{fullRoot}'.");

        if (Directory.Exists(fullTarget))
            Directory.Delete(fullTarget, recursive: true);

        Directory.CreateDirectory(fullTarget);
    }

    private void ResetProjectOutputDirectories(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))
            ?? throw new InvalidOperationException($"Could not resolve project directory for '{projectPath}'.");
        var repositoryRoot = Path.GetFullPath(paths.RepositoryRoot);

        if (!IsPathInsideOrEqual(repositoryRoot, projectDirectory))
            throw new InvalidOperationException($"Refusing to clean project outputs outside repository root: '{projectDirectory}'.");

        foreach (var outputDirectory in new[]
        {
            Path.Combine(projectDirectory, "bin"),
            Path.Combine(projectDirectory, "obj")
        })
        {
            var fullOutputDirectory = Path.GetFullPath(outputDirectory);
            if (!IsPathInsideOrEqual(repositoryRoot, fullOutputDirectory))
                throw new InvalidOperationException($"Refusing to clean output directory outside repository root: '{fullOutputDirectory}'.");

            if (Directory.Exists(fullOutputDirectory))
                Directory.Delete(fullOutputDirectory, recursive: true);
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
            CompatibilityCommandStatus.Failed => $"failed ({command.FailureClassification})",
            CompatibilityCommandStatus.Skipped => "skipped",
            CompatibilityCommandStatus.NotApplicable => "n/a",
            CompatibilityCommandStatus.Unsupported => $"unsupported ({command.FailureClassification})",
            _ => command.Status.ToString()
        };

    private static string EscapeTable(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);
}
