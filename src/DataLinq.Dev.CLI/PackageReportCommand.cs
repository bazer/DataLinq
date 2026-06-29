using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using DataLinq.DevTools;
using Spectre.Console;

namespace DataLinq.Dev.CLI;

internal static class PackageReportCommand
{
    private static readonly string[] PublicPackageIds =
    [
        "DataLinq",
        "DataLinq.SQLite",
        "DataLinq.MySql",
        "DataLinq.CLI",
        "DataLinq.Tools"
    ];

    private static readonly string[] RuntimePackageIds =
    [
        "DataLinq",
        "DataLinq.SQLite",
        "DataLinq.MySql"
    ];

    public static Command Create(DevCliSettings settings)
    {
        var packageDirOption = new Option<string>("--package-dir")
        {
            Description = "Directory containing .nupkg and .snupkg files to inspect.",
            DefaultValueFactory = _ => "nupkg"
        };
        var expectedPackagesOption = new Option<string>("--expected-packages")
        {
            Description = "Comma-separated expected package ids, or 'public'.",
            DefaultValueFactory = _ => "public"
        };
        var runtimePackagesOption = new Option<string>("--runtime-packages")
        {
            Description = "Comma-separated runtime package ids, or 'runtime'.",
            DefaultValueFactory = _ => "runtime"
        };
        var allowUnexpectedPackagesOption = new Option<bool>("--allow-unexpected-packages")
        {
            Description = "Do not fail when package ids outside the expected set are present."
        };
        var allowMissingSymbolsOption = new Option<bool>("--allow-missing-symbols")
        {
            Description = "Do not fail when a .nupkg has no matching .snupkg."
        };
        var allowRuntimeRoslynOption = new Option<bool>("--allow-runtime-roslyn")
        {
            Description = "Do not fail when runtime packages reference or contain Microsoft.CodeAnalysis payloads."
        };
        var allowRuntimeRemotionOption = new Option<bool>("--allow-runtime-remotion")
        {
            Description = "Do not fail when runtime packages reference or contain Remotion payloads."
        };
        var allowAnalyzerLeaksOption = new Option<bool>("--allow-analyzer-leaks")
        {
            Description = "Do not fail when analyzer payloads are missing or outside analyzer assets."
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "Console output format: summary, markdown, or json.",
            DefaultValueFactory = _ => "summary"
        };

        var command = new Command("package-report", "Inspects packed NuGet output for runtime dependency groups, analyzer placement, symbols, and unexpected package ids.");
        command.Options.Add(packageDirOption);
        command.Options.Add(expectedPackagesOption);
        command.Options.Add(runtimePackagesOption);
        command.Options.Add(allowUnexpectedPackagesOption);
        command.Options.Add(allowMissingSymbolsOption);
        command.Options.Add(allowRuntimeRoslynOption);
        command.Options.Add(allowRuntimeRemotionOption);
        command.Options.Add(allowAnalyzerLeaksOption);
        command.Options.Add(formatOption);

        command.SetAction(parseResult =>
        {
            var packageDirectory = ResolvePackageDirectory(
                settings.RepositoryRoot,
                parseResult.GetValue(packageDirOption));
            var options = new PackageInspectionOptions(
                settings.RepositoryRoot,
                packageDirectory,
                ParsePackageIds(parseResult.GetValue(expectedPackagesOption), PublicPackageIds, "public"),
                ParsePackageIds(parseResult.GetValue(runtimePackagesOption), RuntimePackageIds, "runtime"),
                !parseResult.GetValue(allowUnexpectedPackagesOption),
                !parseResult.GetValue(allowMissingSymbolsOption),
                !parseResult.GetValue(allowRuntimeRoslynOption),
                !parseResult.GetValue(allowRuntimeRemotionOption),
                !parseResult.GetValue(allowAnalyzerLeaksOption));

            var inspector = new PackageInspector(settings.Paths, options);
            var report = inspector.CreateReport();

            Render(report, parseResult.GetValue(formatOption));

            if (report.Summary.HasHardFailures)
                Environment.ExitCode = 1;
        });

        return command;
    }

    private static void Render(PackageInspectionReport report, string? format)
    {
        switch (format?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "summary":
                RenderSummary(report);
                break;
            case "markdown":
                Console.WriteLine(PackageInspector.ToMarkdown(report));
                break;
            case "json":
                Console.WriteLine(File.ReadAllText(Path.Combine(report.ReportDirectory, "report.json")));
                break;
            default:
                throw new InvalidOperationException($"Unsupported package report format '{format}'. Use summary, markdown, or json.");
        }
    }

    private static void RenderSummary(PackageInspectionReport report)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Package")
            .AddColumn("Version")
            .AddColumn("Runtime")
            .AddColumn("Tool")
            .AddColumn("Symbols")
            .AddColumn(new TableColumn("lib").RightAligned())
            .AddColumn(new TableColumn("analyzers").RightAligned())
            .AddColumn(new TableColumn("tools").RightAligned())
            .AddColumn(new TableColumn("runtimes").RightAligned());

        foreach (var package in report.Packages)
        {
            table.AddRow(
                Markup.Escape(package.Id),
                Markup.Escape(package.Version),
                package.IsRuntimePackage ? "[green]yes[/]" : "[grey]no[/]",
                package.IsDotnetTool ? "[green]yes[/]" : "[grey]no[/]",
                package.SymbolPackagePath is null ? "[red]missing[/]" : "[green]yes[/]",
                package.Assets.LibFileCount.ToString(CultureInfo.InvariantCulture),
                package.Assets.AnalyzerFileCount.ToString(CultureInfo.InvariantCulture),
                package.Assets.ToolFileCount.ToString(CultureInfo.InvariantCulture),
                package.Assets.RuntimeFileCount.ToString(CultureInfo.InvariantCulture));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]Report JSON:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.json"))}");
        AnsiConsole.MarkupLine($"[grey]Report Markdown:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.md"))}");

        if (report.Findings.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]Findings:[/] {report.Summary.FindingCount} total, {report.Summary.HardFailureCount} hard");

            foreach (var finding in report.Findings.Take(10))
            {
                AnsiConsole.MarkupLine(
                    $"  [yellow]{Markup.Escape(finding.Kind.ToString())}[/] " +
                    $"{Markup.Escape(finding.PackageId)}: {Markup.Escape(finding.Message)}");
            }
        }
    }

    private static string ResolvePackageDirectory(string repositoryRoot, string? packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory))
            packageDirectory = "nupkg";

        return Path.IsPathRooted(packageDirectory)
            ? Path.GetFullPath(packageDirectory)
            : Path.GetFullPath(Path.Combine(repositoryRoot, packageDirectory));
    }

    private static IReadOnlySet<string> ParsePackageIds(string? value, IReadOnlyList<string> preset, string presetName)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, presetName, StringComparison.OrdinalIgnoreCase))
            return preset.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ids = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ids.Count == 0)
            throw new InvalidOperationException($"No package ids were parsed from '{value}'.");

        return ids;
    }
}
