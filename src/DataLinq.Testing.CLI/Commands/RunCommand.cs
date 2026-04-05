using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Spectre.Console;

namespace DataLinq.Testing.CLI;

internal static class RunCommand
{
    public static Command Create(TestInfraOrchestrator orchestrator, TestInfraCliSettings settings)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();
        var interactiveOption = CommandHelpers.InteractiveOption();
        var projectOption = new Option<string>("--project")
        {
            Description = "Path to the test project to run.",
            DefaultValueFactory = _ => Path.Combine("src", "DataLinq.Tests.Compliance", "DataLinq.Tests.Compliance.csproj")
        };
        var configurationOption = new Option<string>("--configuration")
        {
            Description = "Build configuration.",
            DefaultValueFactory = _ => "Debug"
        };
        var buildOption = new Option<bool>("--build")
        {
            Description = "Builds the test project before running it."
        };
        var batchSizeOption = new Option<int>("--batch-size")
        {
            Description = "How many targets to include in each batch.",
            DefaultValueFactory = _ => 2
        };
        var tearDownOption = new Option<bool>("--tear-down")
        {
            Description = "Stops the provisioned server targets after the run completes."
        };

        var command = new Command("run", "Runs the compliance suite against the selected targets, optionally in batches.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);
        command.Options.Add(interactiveOption);
        command.Options.Add(projectOption);
        command.Options.Add(configurationOption);
        command.Options.Add(buildOption);
        command.Options.Add(batchSizeOption);
        command.Options.Add(tearDownOption);

        command.SetAction(parseResult =>
        {
            if (parseResult.GetValue(interactiveOption))
            {
                InteractiveCliRunner.RunTests(orchestrator, settings);
                return;
            }

            var batchSize = parseResult.GetValue(batchSizeOption);
            if (batchSize < 1 || batchSize > 32)
                throw new InvalidOperationException("'--batch-size' must be between 1 and 32.");

            var selection = TargetSelectionResolver.Resolve(
                parseResult.GetValue(aliasOption),
                parseResult.GetValue(targetsOption),
                defaultAlias: "latest");

            RunSelection(
                orchestrator,
                settings,
                selection,
                parseResult.GetValue(projectOption) ?? throw new InvalidOperationException("A test project path is required."),
                parseResult.GetValue(configurationOption) ?? throw new InvalidOperationException("A build configuration is required."),
                parseResult.GetValue(buildOption),
                batchSize,
                parseResult.GetValue(tearDownOption));
        });

        return command;
    }

    public static void Execute(
        TestInfraOrchestrator orchestrator,
        TestInfraCliSettings settings,
        CliTargetSelection selection,
        string projectPath,
        string configuration,
        bool buildProject,
        int batchSize,
        bool tearDown)
    {
        RunSelection(orchestrator, settings, selection, projectPath, configuration, buildProject, batchSize, tearDown);
    }

    private static void RunSelection(
        TestInfraOrchestrator orchestrator,
        TestInfraCliSettings settings,
        CliTargetSelection selection,
        string projectPath,
        string configuration,
        bool buildProject,
        int batchSize,
        bool tearDown)
    {
        var repositoryRoot = settings.RepositoryRoot;
        var fullProjectPath = Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.Combine(repositoryRoot, projectPath);

        if (!File.Exists(fullProjectPath))
            throw new FileNotFoundException($"The requested test project was not found: '{fullProjectPath}'.", fullProjectPath);

        if (buildProject)
            BuildProject(fullProjectPath, configuration, repositoryRoot);

        var batches = CreateBatches(selection.Targets.ToArray(), batchSize)
            .Select(batchTargets => new CliTargetSelection(selection.AliasName, batchTargets))
            .ToArray();

        var results = new List<BatchResult>();
        try
        {
            for (var index = 0; index < batches.Length; index++)
            {
                var batch = batches[index];

                Console.WriteLine();
                Console.WriteLine($"=== Running target batch [{string.Join(", ", batch.Targets.Select(x => x.Id))}] ===");

                orchestrator.Up(batch, recreate: false);

                var start = Stopwatch.StartNew();
                var result = ExecuteTestRun(fullProjectPath, configuration, repositoryRoot, batch);
                start.Stop();

                WriteProcessOutput(result);
                results.Add(new BatchResult(
                    Index: index + 1,
                    Targets: string.Join(", ", batch.Targets.Select(x => x.Id)),
                    ExitCode: result.ExitCode,
                    DurationSeconds: Math.Round(start.Elapsed.TotalSeconds, 1),
                    Total: ParseSummaryCount(result.StandardOutput, "total"),
                    Succeeded: ParseSummaryCount(result.StandardOutput, "succeeded"),
                    Failed: ParseSummaryCount(result.StandardOutput, "failed"),
                    Skipped: ParseSummaryCount(result.StandardOutput, "skipped")));

                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"Compliance suite failed for target batch [{string.Join(", ", batch.Targets.Select(x => x.Id))}].");
            }

            orchestrator.PersistState(selection);
        }
        finally
        {
            if (tearDown)
                orchestrator.Down(remove: false, selection: null);
        }

        RenderSummary(results);
    }

    private static void BuildProject(string projectPath, string configuration, string workingDirectory)
    {
        Console.WriteLine($"Building '{projectPath}'...");
        var result = ExecuteDotnet(
            ["build", projectPath, "-c", configuration],
            workingDirectory);

        WriteProcessOutput(result);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to build '{projectPath}'.");
    }

    private static ExternalCommandResult ExecuteTestRun(string projectPath, string configuration, string workingDirectory, CliTargetSelection selection)
    {
        var environmentVariables = new Dictionary<string, string?>
        {
            ["DOTNET_CLI_HOME"] = Path.Combine(workingDirectory, ".dotnet"),
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            [DataLinq.Testing.PodmanTestEnvironmentSettings.ProviderSetEnvironmentVariable] = "targets",
            [DataLinq.Testing.PodmanTestEnvironmentSettings.TargetIdsEnvironmentVariable] = string.Join(",", selection.Targets.Select(x => x.Id)),
            [DataLinq.Testing.PodmanTestEnvironmentSettings.TargetAliasEnvironmentVariable] = null
        };

        return ExecuteDotnet(
            ["run", "--project", projectPath, "-c", configuration, "--no-build"],
            workingDirectory,
            environmentVariables);
    }

    private static ExternalCommandResult ExecuteDotnet(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        return ExternalProcessRunner.Execute(
            "dotnet",
            arguments,
            workingDirectory,
            environmentVariables);
    }

    private static List<TestCliTarget[]> CreateBatches(TestCliTarget[] targets, int batchSize)
    {
        var batches = new List<TestCliTarget[]>();
        for (var index = 0; index < targets.Length; index += batchSize)
        {
            var count = Math.Min(batchSize, targets.Length - index);
            batches.Add(targets[index..(index + count)]);
        }

        return batches;
    }

    private static void WriteProcessOutput(ExternalCommandResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            Console.WriteLine(result.StandardOutput.TrimEnd());

        if (!string.IsNullOrWhiteSpace(result.StandardError))
            Console.Error.WriteLine(result.StandardError.TrimEnd());
    }

    private static void RenderSummary(IReadOnlyList<BatchResult> results)
    {
        Console.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Run Summary[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Batch")
            .AddColumn("Targets")
            .AddColumn("Exit")
            .AddColumn("Total")
            .AddColumn("Passed")
            .AddColumn("Failed")
            .AddColumn("Skipped")
            .AddColumn("Seconds");

        foreach (var result in results)
        {
            table.AddRow(
                result.Index.ToString(),
                result.Targets,
                result.ExitCode.ToString(),
                FormatNullableCount(result.Total),
                FormatNullableCount(result.Succeeded),
                FormatNullableCount(result.Failed),
                FormatNullableCount(result.Skipped),
                result.DurationSeconds.ToString("0.0"));
        }

        AnsiConsole.Write(table);
    }

    private static int? ParseSummaryCount(string output, string label)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Reverse())
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith($"{label}:", StringComparison.OrdinalIgnoreCase))
                continue;

            var value = trimmed[(label.Length + 1)..].Trim();
            if (int.TryParse(value, out var parsed))
                return parsed;
        }

        return null;
    }

    private static string FormatNullableCount(int? value) => value?.ToString() ?? "-";

    private sealed record BatchResult(
        int Index,
        string Targets,
        int ExitCode,
        double DurationSeconds,
        int? Total,
        int? Succeeded,
        int? Failed,
        int? Skipped);
}
