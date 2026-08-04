using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using DataLinq.DevTools;
using Spectre.Console;

namespace DataLinq.Dev.CLI;

internal static class SizeReportCommand
{
    public static Command Create(DevCliSettings settings)
    {
        var profileOption = CommandHelpers.ProfileOption();
        var targetOption = new Option<string>("--target")
        {
            Description = "Compatibility report target set: phase8c or v0.9.",
            DefaultValueFactory = _ => CompatibilityTargetCatalog.HistoricalTargetSet
        };
        var targetsOption = new Option<string?>("--targets")
        {
            Description = "Comma-separated exact target ids or mode/graph aliases: aot, trim, wasm, wasm-aot, sqlite, memory, or all."
        };
        var configurationOption = new Option<string>("--configuration")
        {
            Description = "Publish configuration.",
            DefaultValueFactory = _ => "Release"
        };
        var runtimeOption = new Option<string>("--runtime")
        {
            Description = "Runtime identifier for native publish targets.",
            DefaultValueFactory = _ => CompatibilityTargetCatalog.DefaultRuntimeIdentifier()
        };
        var topOption = new Option<int>("--top")
        {
            Description = "Number of largest files to include for each target.",
            DefaultValueFactory = _ => 15
        };
        var noRestoreOption = new Option<bool>("--no-restore")
        {
            Description = "Passes --no-restore to each publish."
        };
        var skipSmokeOption = new Option<bool>("--skip-smoke")
        {
            Description = "Skips executable smoke runs after publish."
        };
        var cleanOutputOption = new Option<bool>("--clean-output")
        {
            Description = "Deletes the selected projects' bin/obj output before publishing. Use this for fresh WebAssembly warning evidence."
        };
        var releaseThresholdsOption = new Option<bool>("--release-thresholds")
        {
            Description = "Applies compatibility payload guardrails where the selected runtime graph defines them."
        };
        var maxTotalSizeOption = new Option<double?>("--max-total-size-mb")
        {
            Description = "Advisory total payload size warning threshold in MiB."
        };
        var maxSymbolExcludedSizeOption = new Option<double?>("--max-symbol-excluded-size-mb")
        {
            Description = "Advisory symbol-excluded payload size warning threshold in MiB."
        };
        var maxFileCountOption = new Option<int?>("--max-file-count")
        {
            Description = "Advisory payload file-count warning threshold."
        };
        var failOnBannedPayloadOption = new Option<bool>("--fail-on-banned-payload")
        {
            Description = "Returns a non-zero exit code when target-specific banned runtime payloads are present."
        };
        var failOnThresholdsOption = new Option<bool>("--fail-on-threshold")
        {
            Description = "Returns a non-zero exit code when advisory size or file-count thresholds are exceeded."
        };
        var stopOnPublishFailureOption = new Option<bool>("--stop-on-publish-failure")
        {
            Description = "Stops after the first failed publish instead of collecting the remaining targets."
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Console output format: summary, markdown, or json.",
            DefaultValueFactory = _ => "summary"
        };

        var command = new Command("size-report", "Publishes constrained compatibility targets and reports payload size, warnings, smoke results, and banned files.");
        command.Options.Add(profileOption);
        command.Options.Add(targetOption);
        command.Options.Add(targetsOption);
        command.Options.Add(configurationOption);
        command.Options.Add(runtimeOption);
        command.Options.Add(topOption);
        command.Options.Add(noRestoreOption);
        command.Options.Add(skipSmokeOption);
        command.Options.Add(cleanOutputOption);
        command.Options.Add(releaseThresholdsOption);
        command.Options.Add(maxTotalSizeOption);
        command.Options.Add(maxSymbolExcludedSizeOption);
        command.Options.Add(maxFileCountOption);
        command.Options.Add(failOnBannedPayloadOption);
        command.Options.Add(failOnThresholdsOption);
        command.Options.Add(stopOnPublishFailureOption);
        command.Options.Add(formatOption);

        command.SetAction(parseResult =>
        {
            var profile = CommandHelpers.ParseProfile(parseResult.GetValue(profileOption));
            var targetSet = parseResult.GetValue(targetOption) ?? CompatibilityTargetCatalog.HistoricalTargetSet;
            var targetSelectors = parseResult.GetValue(targetsOption);
            var configuration = parseResult.GetValue(configurationOption) ?? "Release";
            var runtimeIdentifier = parseResult.GetValue(runtimeOption) ?? CompatibilityTargetCatalog.DefaultRuntimeIdentifier();
            var largestFileCount = parseResult.GetValue(topOption);
            if (largestFileCount < 0)
                throw new InvalidOperationException("--top must be zero or greater.");

            var options = new CompatibilityReportOptions(
                settings.RepositoryRoot,
                profile,
                targetSet,
                targetSelectors,
                configuration,
                runtimeIdentifier,
                largestFileCount,
                parseResult.GetValue(noRestoreOption),
                parseResult.GetValue(skipSmokeOption),
                MegabytesToBytes(parseResult.GetValue(maxTotalSizeOption)),
                MegabytesToBytes(parseResult.GetValue(maxSymbolExcludedSizeOption)),
                parseResult.GetValue(maxFileCountOption),
                parseResult.GetValue(failOnBannedPayloadOption),
                parseResult.GetValue(failOnThresholdsOption),
                !parseResult.GetValue(stopOnPublishFailureOption),
                parseResult.GetValue(cleanOutputOption),
                parseResult.GetValue(releaseThresholdsOption));

            var reporter = new CompatibilitySizeReporter(settings.Paths, options);
            var report = reporter.CreateReport();

            Render(report, parseResult.GetValue(formatOption));

            if (report.Summary.HasHardFailures)
                Environment.ExitCode = 1;
        });

        return command;
    }

    private static void Render(CompatibilitySizeReport report, string? format)
    {
        switch (format?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "summary":
                RenderSummary(report);
                break;
            case "markdown":
                Console.WriteLine(CompatibilitySizeReporter.ToMarkdown(report));
                break;
            case "json":
                Console.WriteLine(File.ReadAllText(Path.Combine(report.ReportDirectory, "report.json")));
                break;
            default:
                throw new InvalidOperationException($"Unsupported size report format '{format}'. Use summary, markdown, or json.");
        }
    }

    private static void RenderSummary(CompatibilitySizeReport report)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Target")
            .AddColumn("Graph")
            .AddColumn("Publish")
            .AddColumn("Smoke")
            .AddColumn("Inspection")
            .AddColumn(new TableColumn("Files").RightAligned())
            .AddColumn(new TableColumn("Total").RightAligned())
            .AddColumn(new TableColumn("No Symbols").RightAligned())
            .AddColumn(new TableColumn(".br").RightAligned())
            .AddColumn(new TableColumn(".gz").RightAligned())
            .AddColumn(new TableColumn("Banned").RightAligned())
            .AddColumn(new TableColumn("Warnings").RightAligned());

        foreach (var target in report.Targets)
        {
            table.AddRow(
                Markup.Escape(target.Name),
                Markup.Escape(target.RuntimeGraph.ToString()),
                FormatStatus(target.Publish),
                FormatStatus(target.Smoke),
                FormatStatus(target.Inspection),
                target.Payload.FileCount.ToString(CultureInfo.InvariantCulture),
                CompatibilityPayloadInspector.FormatBytes(target.Payload.TotalBytes),
                CompatibilityPayloadInspector.FormatBytes(target.Payload.SymbolExcludedBytes),
                CompatibilityPayloadInspector.FormatBytes(target.BrotliAssets.TotalBytes),
                CompatibilityPayloadInspector.FormatBytes(target.GzipAssets.TotalBytes),
                target.BannedPayloads.Count.ToString(CultureInfo.InvariantCulture),
                target.WarningSummary.DistinctWarningCount.ToString(CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]Report JSON:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.json"))}");
        AnsiConsole.MarkupLine($"[grey]Report Markdown:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.md"))}");
        AnsiConsole.MarkupLine(
            $"[grey]Target coverage:[/] {report.Targets.Count}/{report.ExpectedTargetCount} " +
            $"({(report.IsFullTargetSet ? "full" : "subset")})");
        AnsiConsole.MarkupLine(
            $"[grey]Failures:[/] product publish={report.Summary.ProductPublishFailureCount}, " +
            $"product smoke={report.Summary.ProductSmokeFailureCount}, " +
            $"product inspection={report.Summary.ProductInspectionFailureCount}, " +
            $"environment={report.Summary.EnvironmentFailureCount}, unsupported={report.Summary.UnsupportedCount}");

        if (report.Summary.BannedPayloadCount > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Banned payload findings:[/] {report.Summary.BannedPayloadCount} " +
                "(advisory unless --fail-on-banned-payload is used)");
        }

        if (report.Summary.ThresholdWarningCount > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Threshold warnings:[/] {report.Summary.ThresholdWarningCount} " +
                "(advisory unless --fail-on-threshold is used)");
        }
    }

    private static string FormatStatus(CompatibilityCommandReport command) =>
        command.Status switch
        {
            CompatibilityCommandStatus.Succeeded => "[green]ok[/]",
            CompatibilityCommandStatus.Failed =>
                $"[red]failed[/] ({Markup.Escape(command.FailureDisposition.ToString())}/{Markup.Escape(command.FailureClassification.ToString())})",
            CompatibilityCommandStatus.Skipped => "[yellow]skipped[/]",
            CompatibilityCommandStatus.NotApplicable => "[grey]n/a[/]",
            CompatibilityCommandStatus.Unsupported => $"[yellow]unsupported[/] ({Markup.Escape(command.FailureClassification.ToString())})",
            _ => Markup.Escape(command.Status.ToString())
        };

    private static long? MegabytesToBytes(double? megabytes) =>
        megabytes.HasValue
            ? checked((long)(megabytes.Value * 1024d * 1024d))
            : null;
}
