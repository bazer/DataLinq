using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DataLinq.DevTools;
using Spectre.Console;

namespace DataLinq.Dev.CLI;

internal static class DoctorCommand
{
    public static Command Create(DevCliSettings settings)
    {
        var profileOption = CommandHelpers.ProfileOption();
        var command = new Command("doctor", "Diagnoses the local dotnet/NuGet execution environment.");
        command.Options.Add(profileOption);

        command.SetAction(parseResult =>
        {
            var profile = CommandHelpers.ParseProfile(parseResult.GetValue(profileOption));
            Execute(settings, profile);
        });

        return command;
    }

    private static void Execute(DevCliSettings settings, ToolingProfile profile)
    {
        settings.Paths.EnsureCreated();
        var runner = new DotnetCommandRunner(settings.Paths, profile);

        var version = runner.Execute(
            DotnetCommandType.Info,
            ["--version"],
            "doctor-dotnet-version",
            "dotnet --version",
            includeNuGetAuditProperty: false,
            includeOfflineRestoreProperty: false);
        var sdkList = runner.Execute(
            DotnetCommandType.Info,
            ["--list-sdks"],
            "doctor-dotnet-sdks",
            "dotnet --list-sdks",
            includeNuGetAuditProperty: false,
            includeOfflineRestoreProperty: false);
        var dotnetInfo = runner.Execute(
            DotnetCommandType.Info,
            ["--info"],
            "doctor-dotnet-info",
            "dotnet --info",
            includeNuGetAuditProperty: false,
            includeOfflineRestoreProperty: false);

        AnsiConsole.Write(new Rule("[yellow]Environment Doctor[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Check")
            .AddColumn("Value");

        table.AddRow("Profile", profile.ToCliValue());
        table.AddRow("Repo root", settings.RepositoryRoot);
        table.AddRow("Artifact root", settings.Paths.ArtifactRoot);
        table.AddRow("DOTNET_CLI_HOME", settings.Paths.DotnetCliHome);
        table.AddRow("APPDATA", settings.Paths.AppDataRoot);
        table.AddRow("LOCALAPPDATA", settings.Paths.LocalAppDataRoot);
        table.AddRow("HOME", settings.Paths.HomeRoot);
        table.AddRow("NUGET_PACKAGES", settings.Paths.NugetPackagesRoot);
        table.AddRow("NuGet.Config", settings.Paths.NugetConfigPath);
        table.AddRow("Writable paths", CheckWritablePaths(settings.Paths));
        table.AddRow("dotnet version", FormatCommandValue(version, GetFirstNonEmptyLine(version.ProcessResult.StandardOutput)));
        table.AddRow("SDK count", CountNonEmptyLines(sdkList.ProcessResult.StandardOutput).ToString());
        table.AddRow("Current SDK base path", FormatCommandValue(dotnetInfo, ParseDotnetInfoValue(dotnetInfo.ProcessResult.StandardOutput, "Base Path")));
        table.AddRow("Workload resolver", dotnetInfo.ProcessResult.ExitCode == 0 ? CheckWorkloadResolver(dotnetInfo.ProcessResult.StandardOutput) : "Unknown");
        table.AddRow("NuGet sources", string.Join(", ", ReadNugetSources(settings.Paths.NugetConfigPath)));
        table.AddRow("Cached package roots", CountTopLevelDirectories(settings.Paths.NugetPackagesRoot).ToString());

        AnsiConsole.Write(table);

        Console.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Artifacts:[/] {Markup.Escape(version.RawLogPath)}, {Markup.Escape(sdkList.RawLogPath)}, {Markup.Escape(dotnetInfo.RawLogPath)}");
    }

    private static string FormatCommandValue(DotnetCommandResult result, string? successValue)
    {
        if (result.ProcessResult.ExitCode == 0)
            return string.IsNullOrWhiteSpace(successValue) ? "<unknown>" : successValue;

        return $"FAILED: {result.Analysis.FailureSummary ?? "see artifact log"}";
    }

    private static string CheckWritablePaths(DevToolPaths paths)
    {
        var checks = new[]
        {
            paths.ArtifactRoot,
            paths.DotnetCliHome,
            paths.AppDataRoot,
            paths.LocalAppDataRoot,
            paths.HomeRoot,
            paths.NugetPackagesRoot
        };

        foreach (var path in checks)
        {
            try
            {
                Directory.CreateDirectory(path);
                var probePath = Path.Combine(path, $".probe-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probePath, "ok");
                File.Delete(probePath);
            }
            catch (Exception exception)
            {
                return $"FAILED: {Path.GetFileName(path)} ({exception.Message})";
            }
        }

        return "OK";
    }

    private static string CheckWorkloadResolver(string dotnetInfoOutput)
    {
        var basePath = ParseDotnetInfoValue(dotnetInfoOutput, "Base Path");
        if (string.IsNullOrWhiteSpace(basePath))
            return "Unknown";

        var resolverPath = Path.Combine(basePath.Trim(), "Sdks", "Microsoft.NET.SDK.WorkloadAutoImportPropsLocator", "Sdk");
        return Directory.Exists(resolverPath)
            ? "Present"
            : "Missing";
    }

    private static string? ParseDotnetInfoValue(string output, string key)
    {
        var prefix = $"{key}:";
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[(line.IndexOf(':') + 1)..].Trim();
        }

        return null;
    }

    private static string[] ReadNugetSources(string configPath)
    {
        try
        {
            var document = XDocument.Load(configPath);
            return document.Root?
                .Element("packageSources")?
                .Elements("add")
                .Select(static x => x.Attribute("value")?.Value)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .ToArray()
                ?? [];
        }
        catch
        {
            return ["<unreadable>"];
        }
    }

    private static int CountTopLevelDirectories(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateDirectories(path).Count()
            : 0;

    private static int CountNonEmptyLines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    private static string? GetFirstNonEmptyLine(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
