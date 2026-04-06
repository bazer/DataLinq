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
        var suiteOption = CommandHelpers.SuiteOption();
        var interactiveOption = CommandHelpers.InteractiveOption();
        var projectOption = new Option<string?>("--project")
        {
            Description = "Optional project path override for a single-suite run."
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

        var command = new Command("run", "Runs the selected test suite or suites.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);
        command.Options.Add(suiteOption);
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

            var exitCode = RunSelection(
                orchestrator,
                settings,
                selection,
                parseResult.GetValue(suiteOption) ?? TestCliSuiteCatalog.AllSuites,
                parseResult.GetValue(projectOption),
                parseResult.GetValue(configurationOption) ?? throw new InvalidOperationException("A build configuration is required."),
                parseResult.GetValue(buildOption),
                batchSize,
                parseResult.GetValue(tearDownOption));

            if (exitCode != 0)
                Environment.ExitCode = exitCode;
        });

        return command;
    }

    public static void Execute(
        TestInfraOrchestrator orchestrator,
        TestInfraCliSettings settings,
        CliTargetSelection selection,
        string suiteName,
        string? projectPathOverride,
        string configuration,
        bool buildProject,
        int batchSize,
        bool tearDown)
    {
        var exitCode = RunSelection(orchestrator, settings, selection, suiteName, projectPathOverride, configuration, buildProject, batchSize, tearDown);
        if (exitCode != 0)
            Environment.ExitCode = exitCode;
    }

    private static int RunSelection(
        TestInfraOrchestrator orchestrator,
        TestInfraCliSettings settings,
        CliTargetSelection selection,
        string suiteName,
        string? projectPathOverride,
        string configuration,
        bool buildProject,
        int batchSize,
        bool tearDown)
    {
        var repositoryRoot = settings.RepositoryRoot;
        var suites = ResolveSuites(suiteName, projectPathOverride);

        if (buildProject)
        {
            foreach (var suite in suites)
                BuildProject(ResolveProjectPath(repositoryRoot, suite.ProjectPath), configuration, repositoryRoot);
        }

        var results = new List<RunResult>();
        var overallExitCode = 0;
        var usedTargets = false;
        try
        {
            foreach (var suite in suites)
            {
                var projectPath = ResolveProjectPath(repositoryRoot, suite.ProjectPath);
                if (!File.Exists(projectPath))
                    throw new FileNotFoundException($"The requested test project was not found: '{projectPath}'.", projectPath);

                if (suite.UsesTargetBatches)
                {
                    usedTargets = true;
                    var suiteTargets = suite.IncludeSqliteTargets
                        ? selection.Targets.ToArray()
                        : selection.Targets.Where(static x => !TestTargetCatalog.IsSQLiteTarget(x.Id)).ToArray();

                    if (suiteTargets.Length == 0)
                        continue;

                    var batches = CreateBatches(suiteTargets, batchSize)
                        .Select(batchTargets => new CliTargetSelection(selection.AliasName, batchTargets))
                        .ToArray();

                    for (var index = 0; index < batches.Length; index++)
                    {
                        var batch = batches[index];

                        Console.WriteLine();
                        Console.WriteLine($"=== Running suite [{suite.Name}] target batch [{string.Join(", ", batch.Targets.Select(x => x.Id))}] ===");

                        orchestrator.Up(batch, recreate: false);

                        var start = Stopwatch.StartNew();
                        var result = ExecuteTestRun(projectPath, configuration, repositoryRoot, batch);
                        start.Stop();

                        WriteProcessOutput(result);
                        results.Add(CreateRunResult(
                            suite.Name,
                            index + 1,
                            string.Join(", ", batch.Targets.Select(x => x.Id)),
                            start.Elapsed,
                            result));

                        if (result.ExitCode != 0)
                            overallExitCode = result.ExitCode;
                    }
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine($"=== Running suite [{suite.Name}] ===");

                    var start = Stopwatch.StartNew();
                    var result = ExecuteTestRun(projectPath, configuration, repositoryRoot, selection: null);
                    start.Stop();

                    WriteProcessOutput(result);
                    results.Add(CreateRunResult(
                        suite.Name,
                        batchIndex: null,
                        targets: "-",
                        start.Elapsed,
                        result));

                    if (result.ExitCode != 0)
                        overallExitCode = result.ExitCode;
                }
            }

            if (usedTargets)
                orchestrator.PersistState(selection);
        }
        finally
        {
            if (tearDown && usedTargets)
                orchestrator.Down(remove: false, selection: null);
        }

        RenderSummary(results);
        RenderFailedTests(results);
        return overallExitCode;
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

    private static ExternalCommandResult ExecuteTestRun(string projectPath, string configuration, string workingDirectory, CliTargetSelection? selection)
    {
        var environmentVariables = new Dictionary<string, string?>
        {
            ["DOTNET_CLI_HOME"] = Path.Combine(workingDirectory, ".dotnet"),
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_NOLOGO"] = "1"
        };

        if (selection is not null)
        {
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.ProviderSetEnvironmentVariable] = "targets";
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetIdsEnvironmentVariable] = string.Join(",", selection.Targets.Select(x => x.Id));
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetAliasEnvironmentVariable] = null;
        }

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

    private static void RenderSummary(IReadOnlyList<RunResult> results)
    {
        Console.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Run Summary[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Suite")
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
                result.Suite,
                result.BatchIndex?.ToString() ?? "-",
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

    private static void RenderFailedTests(IReadOnlyList<RunResult> results)
    {
        var failedBatches = results
            .Where(x => x.FailedTests.Count > 0)
            .ToArray();

        if (failedBatches.Length == 0)
            return;

        Console.WriteLine();
        AnsiConsole.Write(new Rule("[red]Failed Tests[/]"));

        foreach (var batch in failedBatches)
        {
            var label = batch.BatchIndex.HasValue
                ? $"Suite {batch.Suite}, batch {batch.BatchIndex}"
                : $"Suite {batch.Suite}";
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(label)}[/]: {Markup.Escape(batch.Targets)}");

            foreach (var failedTest in batch.FailedTests)
                AnsiConsole.MarkupLine($"  - {Markup.Escape(failedTest)}");
        }
    }

    private static IReadOnlyList<TestCliSuite> ResolveSuites(string suiteName, string? projectPathOverride)
    {
        if (string.IsNullOrWhiteSpace(projectPathOverride))
            return TestCliSuiteCatalog.Resolve(suiteName);

        if (string.Equals(suiteName, TestCliSuiteCatalog.AllSuites, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("'--project' cannot be combined with '--suite all'. Choose a single suite or omit '--project'.");

        var suite = TestCliSuiteCatalog.GetSuite(suiteName);
        return [suite with { ProjectPath = projectPathOverride }];
    }

    private static string ResolveProjectPath(string repositoryRoot, string projectPath) =>
        Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.Combine(repositoryRoot, projectPath);

    private static RunResult CreateRunResult(string suite, int? batchIndex, string targets, TimeSpan elapsed, ExternalCommandResult result) =>
        new(
            Suite: suite,
            BatchIndex: batchIndex,
            Targets: targets,
            ExitCode: result.ExitCode,
            DurationSeconds: Math.Round(elapsed.TotalSeconds, 1),
            Total: ParseSummaryCount(result.StandardOutput, "total"),
            Succeeded: ParseSummaryCount(result.StandardOutput, "succeeded"),
            Failed: ParseSummaryCount(result.StandardOutput, "failed"),
            Skipped: ParseSummaryCount(result.StandardOutput, "skipped"),
            FailedTests: ParseFailedTests(result.StandardOutput));

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

    private static IReadOnlyList<string> ParseFailedTests(string output)
    {
        var failedTests = new List<string>();
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("failed ", StringComparison.Ordinal))
                continue;

            failedTests.Add(trimmed["failed ".Length..]);
        }

        return failedTests;
    }

    private static string FormatNullableCount(int? value) => value?.ToString() ?? "-";

    private sealed record RunResult(
        string Suite,
        int? BatchIndex,
        string Targets,
        int ExitCode,
        double DurationSeconds,
        int? Total,
        int? Succeeded,
        int? Failed,
        int? Skipped,
        IReadOnlyList<string> FailedTests);
}
