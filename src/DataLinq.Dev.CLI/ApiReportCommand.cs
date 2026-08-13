using System;
using System.CommandLine;
using System.IO;
using DataLinq.DevTools;
using Spectre.Console;

namespace DataLinq.Dev.CLI;

internal static class ApiReportCommand
{
    public static Command Create(DevCliSettings settings)
    {
        var candidateDirectoryOption = new Option<string>("--candidate-dir")
        {
            Description = "Fresh directory containing the exact 0.9 candidate package set.",
            Required = true
        };
        var candidateVersionOption = new Option<string>("--candidate-version")
        {
            Description = "Exact candidate package version.",
            Required = true
        };
        var baselineDirectoryOption = new Option<string>("--baseline-dir")
        {
            Description = "Directory containing the exact locked baseline package set.",
            Required = true
        };
        var baselineVersionOption = new Option<string>("--baseline-version")
        {
            Description = "Exact baseline package version.",
            DefaultValueFactory = _ => "0.8.0"
        };
        var baselineLockOption = new Option<string>("--baseline-lock")
        {
            Description = "Tracked package hash and repository provenance lock.",
            DefaultValueFactory = _ => Path.Combine(
                "test-infra",
                "api-compatibility",
                "v0.8.0-packages.json")
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Fresh append-never report directory. Defaults under artifacts/dev/api-report."
        };
        var profileOption = CommandHelpers.ProfileOption();
        var formatOption = new Option<string>("--format")
        {
            Description = "Console output format: summary, markdown, or json.",
            DefaultValueFactory = _ => "summary"
        };

        var command = new Command(
            "api-report",
            "Compares exact public package assets against the locked 0.8 API baseline with pinned ApiCompat.");
        command.Options.Add(candidateDirectoryOption);
        command.Options.Add(candidateVersionOption);
        command.Options.Add(baselineDirectoryOption);
        command.Options.Add(baselineVersionOption);
        command.Options.Add(baselineLockOption);
        command.Options.Add(outputOption);
        command.Options.Add(profileOption);
        command.Options.Add(formatOption);

        command.SetAction(parseResult =>
        {
            var candidateVersion = RequireExactValue(
                parseResult.GetValue(candidateVersionOption),
                "--candidate-version");
            var baselineVersion = RequireExactValue(
                parseResult.GetValue(baselineVersionOption),
                "--baseline-version");
            var outputValue = parseResult.GetValue(outputOption);
            var outputDirectory = string.IsNullOrWhiteSpace(outputValue)
                ? CreateDefaultOutputDirectory(settings.Paths.ArtifactRoot)
                : ResolvePath(settings.RepositoryRoot, outputValue);
            var options = new ApiCompatibilityReportOptions(
                settings.RepositoryRoot,
                ResolvePath(settings.RepositoryRoot, parseResult.GetValue(candidateDirectoryOption)),
                candidateVersion,
                ResolvePath(settings.RepositoryRoot, parseResult.GetValue(baselineDirectoryOption)),
                baselineVersion,
                ResolvePath(settings.RepositoryRoot, parseResult.GetValue(baselineLockOption)),
                outputDirectory,
                CommandHelpers.ParseProfile(parseResult.GetValue(profileOption)));
            var report = new ApiCompatibilityReporter(settings.Paths, options).CreateReport();

            Render(report, parseResult.GetValue(formatOption));
            if (report.Summary.HasHardFailures)
                Environment.ExitCode = 1;
        });

        return command;
    }

    private static void Render(ApiCompatibilityReport report, string? format)
    {
        switch (format?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "summary":
                RenderSummary(report);
                break;
            case "markdown":
                Console.WriteLine(ApiCompatibilityReporter.ToMarkdown(report));
                break;
            case "json":
                Console.WriteLine(File.ReadAllText(Path.Combine(report.ReportDirectory, "report.json")));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported API report format '{format}'. Use summary, markdown, or json.");
        }
    }

    private static void RenderSummary(ApiCompatibilityReport report)
    {
        if (report.Summary.HasHardFailures)
            AnsiConsole.MarkupLine("[red]FAIL[/] public API compatibility");
        else if (report.Summary.RequiresReview)
            AnsiConsole.MarkupLine("[yellow]REVIEW[/] public API compatibility");
        else
            AnsiConsole.MarkupLine("[green]OK[/] public API compatibility");

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Evidence")
            .AddColumn(new TableColumn("Count").RightAligned());
        table.AddRow("Baseline packages", report.Summary.BaselinePackageCount.ToString());
        table.AddRow("Candidate packages", report.Summary.CandidatePackageCount.ToString());
        table.AddRow("API surfaces", report.Summary.SurfaceCount.ToString());
        table.AddRow("Comparisons", report.Summary.ComparisonCount.ToString());
        table.AddRow("Hard failures", report.Summary.HardFailureCount.ToString());
        table.AddRow("Review findings", report.Summary.ReviewCount.ToString());
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(
            $"[grey]Pinned ApiCompat:[/] {Markup.Escape(report.ApiCompatToolVersion ?? "not verified")}");
        AnsiConsole.MarkupLine(
            $"[grey]Report JSON:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.json"))}");
        AnsiConsole.MarkupLine(
            $"[grey]Report Markdown:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.md"))}");
    }

    private static string ResolvePath(string repositoryRoot, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("A required path option was blank.");
        return Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(repositoryRoot, value));
    }

    private static string RequireExactValue(string? value, string option)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            throw new InvalidOperationException($"{option} must be an exact nonblank value without surrounding whitespace.");
        return value;
    }

    private static string CreateDefaultOutputDirectory(string artifactRoot) =>
        Path.Combine(
            artifactRoot,
            "api-report",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
}
