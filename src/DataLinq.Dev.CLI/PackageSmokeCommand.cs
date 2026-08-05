using System;
using System.CommandLine;
using System.IO;
using DataLinq.DevTools;
using Spectre.Console;

namespace DataLinq.Dev.CLI;

internal static class PackageSmokeCommand
{
    public static Command Create(DevCliSettings settings)
    {
        var packageDirOption = new Option<string>("--package-dir")
        {
            Description = "Fresh local package directory to consume.",
            Required = true
        };
        var versionOption = new Option<string>("--version")
        {
            Description = "Exact package candidate version to restore and execute.",
            Required = true
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Fresh report and isolated consumer-work directory. Defaults under artifacts/dev/package-smoke."
        };
        var profileOption = CommandHelpers.ProfileOption();
        var formatOption = new Option<string>("--format")
        {
            Description = "Console output format: summary, markdown, or json.",
            DefaultValueFactory = _ => "summary"
        };

        var command = new Command(
            "package-smoke",
            "Restores, builds, and executes a package-only Memory and SQLite consumer against an exact local candidate.");
        command.Options.Add(packageDirOption);
        command.Options.Add(versionOption);
        command.Options.Add(outputOption);
        command.Options.Add(profileOption);
        command.Options.Add(formatOption);

        command.SetAction(parseResult =>
        {
            var packageDirectory = ResolvePath(
                settings.RepositoryRoot,
                parseResult.GetValue(packageDirOption));
            var version = parseResult.GetValue(versionOption);
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("--version must specify an exact package candidate version.");

            var outputValue = parseResult.GetValue(outputOption);
            var outputDirectory = string.IsNullOrWhiteSpace(outputValue)
                ? CreateDefaultOutputDirectory(settings.Paths.ArtifactRoot)
                : ResolvePath(settings.RepositoryRoot, outputValue);
            var profile = CommandHelpers.ParseProfile(parseResult.GetValue(profileOption));
            var options = new PackageConsumerSmokeOptions(
                settings.RepositoryRoot,
                packageDirectory,
                outputDirectory,
                version,
                profile);
            var runner = new PackageConsumerSmokeRunner(settings.Paths, options);
            var report = runner.CreateReport();

            Render(report, parseResult.GetValue(formatOption));
            return report.Summary.HasHardFailures ? 1 : 0;
        });

        return command;
    }

    private static void Render(PackageConsumerSmokeReport report, string? format)
    {
        switch (format?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "summary":
                RenderSummary(report);
                break;
            case "markdown":
                Console.WriteLine(PackageConsumerSmokeRunner.ToMarkdown(report));
                break;
            case "json":
                Console.WriteLine(File.ReadAllText(Path.Combine(report.ReportDirectory, "report.json")));
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported package smoke format '{format}'. Use summary, markdown, or json.");
        }
    }

    private static void RenderSummary(PackageConsumerSmokeReport report)
    {
        if (report.Summary.HasHardFailures)
        {
            AnsiConsole.MarkupLine("[red]FAIL[/] package smoke");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]OK[/] package smoke");
        }

        AnsiConsole.MarkupLine(
            $"[grey]Report JSON:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.json"))}");
        AnsiConsole.MarkupLine(
            $"[grey]Report Markdown:[/] {Markup.Escape(Path.Combine(report.ReportDirectory, "report.md"))}");
    }

    private static string ResolvePath(string repositoryRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("A non-empty path is required.");

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(repositoryRoot, path));
    }

    private static string CreateDefaultOutputDirectory(string artifactRoot) =>
        Path.Combine(
            artifactRoot,
            "package-smoke",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
}
