using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using DataLinq.DevTools;
using Spectre.Console;

namespace DataLinq.Testing.CLI;

internal static class RunCommand
{
    private static readonly Regex AnsiEscapePattern = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.CultureInvariant);

    public static Command Create(TestInfraOrchestrator orchestrator, TestInfraCliSettings settings)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();
        var suiteOption = CommandHelpers.SuiteOption();
        var interactiveOption = CommandHelpers.InteractiveOption();
        var parallelSuitesOption = CommandHelpers.ParallelSuitesOption();
        var outputOption = CommandHelpers.OutputOption();
        var profileOption = CommandHelpers.ProfileOption();
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
        var summaryJsonOption = new Option<string?>("--summary-json")
        {
            Description = "Optional path to write a machine-readable run summary JSON file."
        };

        var command = new Command("run", "Runs the selected test suite or suites.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);
        command.Options.Add(suiteOption);
        command.Options.Add(interactiveOption);
        command.Options.Add(parallelSuitesOption);
        command.Options.Add(outputOption);
        command.Options.Add(profileOption);
        command.Options.Add(projectOption);
        command.Options.Add(configurationOption);
        command.Options.Add(buildOption);
        command.Options.Add(batchSizeOption);
        command.Options.Add(tearDownOption);
        command.Options.Add(summaryJsonOption);

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

            var exitCode = ExecuteSafely(() => RunSelection(
                orchestrator,
                settings,
                selection,
                parseResult.GetValue(suiteOption) ?? TestCliSuiteCatalog.AllSuites,
                parseResult.GetValue(projectOption),
                parseResult.GetValue(configurationOption) ?? throw new InvalidOperationException("A build configuration is required."),
                parseResult.GetValue(buildOption),
                batchSize,
                parseResult.GetValue(parallelSuitesOption),
                parseResult.GetValue(tearDownOption),
                parseResult.GetValue(summaryJsonOption),
                CommandHelpers.ParseOutputMode(parseResult.GetValue(outputOption)),
                CommandHelpers.ParseProfile(parseResult.GetValue(profileOption))));

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
        bool parallelSuites,
        bool tearDown,
        string? summaryJsonPath,
        TestCliOutputMode outputMode,
        ToolingProfile profile)
    {
        var exitCode = ExecuteSafely(() => RunSelection(orchestrator, settings, selection, suiteName, projectPathOverride, configuration, buildProject, batchSize, parallelSuites, tearDown, summaryJsonPath, outputMode, profile));
        if (exitCode != 0)
            Environment.ExitCode = exitCode;
    }

    private static int ExecuteSafely(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
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
        bool parallelSuites,
        bool tearDown,
        string? summaryJsonPath,
        TestCliOutputMode outputMode,
        ToolingProfile profile)
    {
        var repositoryRoot = settings.RepositoryRoot;
        var suites = ResolveSuites(suiteName, projectPathOverride);

        if (buildProject)
        {
            foreach (var suite in suites)
                BuildProject(ResolveProjectPath(repositoryRoot, suite.ProjectPath), configuration, settings, outputMode, profile);
        }

        var results = new List<RunResult>();
        var overallExitCode = 0;
        var usedTargets = false;
        var resultLock = new object();
        try
        {
            if (parallelSuites)
            {
                var suiteTasks = suites
                    .Select(suite => Task.Run(() =>
                    {
                        var result = ExecuteSuiteRun(
                            suite,
                            selection,
                            settings,
                            repositoryRoot,
                            configuration,
                            batchSize,
                            orchestrator,
                            outputMode,
                            profile,
                            usedTargetsRef: value =>
                            {
                                lock (resultLock)
                                    usedTargets = usedTargets || value;
                            });

                        lock (resultLock)
                        {
                            results.AddRange(result.Results);
                            if (result.ExitCode != 0)
                                overallExitCode = result.ExitCode;
                        }
                    }))
                    .ToArray();

                try
                {
                    Task.WhenAll(suiteTasks).GetAwaiter().GetResult();
                }
                catch
                {
                    foreach (var task in suiteTasks.Where(static x => x.IsFaulted))
                        task.GetAwaiter().GetResult();

                    throw;
                }
            }
            else
            {
                foreach (var suite in suites)
                {
                    var result = ExecuteSuiteRun(
                        suite,
                        selection,
                        settings,
                        repositoryRoot,
                        configuration,
                        batchSize,
                        orchestrator,
                        outputMode,
                        profile,
                        usedTargetsRef: value => usedTargets = usedTargets || value);

                    results.AddRange(result.Results);
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

        var orderedResults = OrderResults(results);
        if (ShouldRenderSummary(outputMode, orderedResults, overallExitCode))
            RenderSummary(orderedResults);
        if (ShouldRenderFailedTests(outputMode, orderedResults))
            RenderFailedTests(orderedResults);
        WriteSummaryJson(summaryJsonPath, orderedResults, overallExitCode);
        return overallExitCode;
    }

    private static SuiteRunResult ExecuteSuiteRun(
        TestCliSuite suite,
        CliTargetSelection selection,
        TestInfraCliSettings settings,
        string repositoryRoot,
        string configuration,
        int batchSize,
        TestInfraOrchestrator orchestrator,
        TestCliOutputMode outputMode,
        ToolingProfile profile,
        Action<bool>? usedTargetsRef)
    {
        var projectPath = ResolveProjectPath(repositoryRoot, suite.ProjectPath);
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"The requested test project was not found: '{projectPath}'.", projectPath);

        var runResults = new List<RunResult>();
        var exitCode = 0;

        if (suite.UsesTargetBatches)
        {
            usedTargetsRef?.Invoke(true);
            var suiteTargets = suite.IncludeSqliteTargets
                ? selection.Targets.ToArray()
                : selection.Targets.Where(static x => !TestTargetCatalog.IsSQLiteTarget(x.Id)).ToArray();

            if (suiteTargets.Length == 0)
                return new SuiteRunResult(exitCode, runResults);

            var batches = CreateBatches(suiteTargets, batchSize)
                .Select(batchTargets => new CliTargetSelection(selection.AliasName, batchTargets))
                .ToArray();

            for (var index = 0; index < batches.Length; index++)
            {
                var batch = batches[index];
                var suppressInfraOutput = outputMode is TestCliOutputMode.Quiet or TestCliOutputMode.Failures;
                if (!suppressInfraOutput)
                {
                    ConsoleSync.Run(() =>
                    {
                        Console.WriteLine();
                        Console.WriteLine($"=== Running suite [{suite.Name}] target batch [{string.Join(", ", batch.Targets.Select(x => x.Id))}] ===");
                    });
                }

                using var mutedScope = suppressInfraOutput ? ConsoleSync.PushMuted() : null;
                orchestrator.Up(batch, recreate: false);

                var start = Stopwatch.StartNew();
                var result = ExecuteTestRun(projectPath, configuration, settings, batch, suite.Name, batchIndex: index + 1, profile);
                start.Stop();

                var runResult = CreateRunResult(
                    suite.Name,
                    index + 1,
                    string.Join(", ", batch.Targets.Select(x => x.Id)),
                    start.Elapsed,
                    result);
                RenderTestRunOutcome(runResult, outputMode);
                runResults.Add(runResult);

                if (result.ProcessResult.ExitCode != 0)
                    exitCode = result.ProcessResult.ExitCode;
            }
        }
        else
        {
            if (outputMode is TestCliOutputMode.Summary or TestCliOutputMode.Raw)
            {
                ConsoleSync.Run(() =>
                {
                    Console.WriteLine();
                    Console.WriteLine($"=== Running suite [{suite.Name}] ===");
                });
            }

            var start = Stopwatch.StartNew();
            var result = ExecuteTestRun(projectPath, configuration, settings, selection: null, suite.Name, batchIndex: null, profile);
            start.Stop();

            var runResult = CreateRunResult(
                suite.Name,
                batchIndex: null,
                targets: "-",
                start.Elapsed,
                result);
            RenderTestRunOutcome(runResult, outputMode);
            runResults.Add(runResult);

            if (result.ProcessResult.ExitCode != 0)
                exitCode = result.ProcessResult.ExitCode;
        }

        return new SuiteRunResult(exitCode, runResults);
    }

    private static void BuildProject(string projectPath, string configuration, TestInfraCliSettings settings, TestCliOutputMode outputMode, ToolingProfile profile)
    {
        var arguments = new List<string>
        {
            "build",
            projectPath,
            "-c", configuration,
            "-nologo",
            "-v", outputMode == TestCliOutputMode.Raw ? "minimal" : "q",
            "-p:NuGetAudit=false"
        };

        if (profile.IsOffline())
            arguments.Add("-p:RestoreIgnoreFailedSources=true");

        var result = ExecuteDotnet(arguments, settings, profile, "build-" + Path.GetFileNameWithoutExtension(projectPath));
        RenderBuildOutcome(projectPath, result, outputMode);

        if (result.ProcessResult.ExitCode != 0)
            throw new InvalidOperationException($"Failed to build '{projectPath}'.");
    }

    private static LoggedCommandResult ExecuteTestRun(
        string projectPath,
        string configuration,
        TestInfraCliSettings settings,
        CliTargetSelection? selection,
        string suiteName,
        int? batchIndex,
        ToolingProfile profile)
    {
        var environmentVariables = new Dictionary<string, string?>(
            settings.ToolPaths.CreateEnvironment(profile),
            StringComparer.OrdinalIgnoreCase);

        if (selection is not null)
        {
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.ProviderSetEnvironmentVariable] = "targets";
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetIdsEnvironmentVariable] = string.Join(",", selection.Targets.Select(x => x.Id));
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetAliasEnvironmentVariable] = null;
        }

        return ExecuteDotnet(
            ["run", "--project", projectPath, "-c", configuration, "--no-build"],
            settings,
            profile,
            CreateRunArtifactPrefix(suiteName, batchIndex, selection),
            environmentVariables);
    }

    private static LoggedCommandResult ExecuteDotnet(
        IReadOnlyList<string> arguments,
        TestInfraCliSettings settings,
        ToolingProfile profile,
        string artifactPrefix,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        settings.ToolPaths.EnsureCreated();
        var mergedEnvironmentVariables = new Dictionary<string, string?>(
            settings.ToolPaths.CreateEnvironment(profile),
            StringComparer.OrdinalIgnoreCase);

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
                mergedEnvironmentVariables[pair.Key] = pair.Value;
        }

        var processResult = ExternalProcessRunner.Execute(
            "dotnet",
            arguments,
            settings.RepositoryRoot,
            mergedEnvironmentVariables);
        var logPath = WriteRawLog(settings, artifactPrefix, processResult);
        return new LoggedCommandResult(processResult, logPath);
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
        ConsoleSync.Run(() =>
        {
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                Console.WriteLine(result.StandardOutput.TrimEnd());

            if (!string.IsNullOrWhiteSpace(result.StandardError))
                Console.Error.WriteLine(result.StandardError.TrimEnd());
        });
    }

    private static string WriteRawLog(TestInfraCliSettings settings, string artifactPrefix, ExternalCommandResult result)
    {
        var directory = Path.Combine(settings.ArtifactRoot, "cli-logs");
        Directory.CreateDirectory(directory);

        var safePrefix = string.Concat(artifactPrefix.Select(static character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        var path = Path.Combine(directory, $"{safePrefix}-{timestamp}.log");
        File.WriteAllText(path, string.Concat(result.StandardOutput, result.StandardError));
        return path;
    }

    private static void RenderBuildOutcome(string projectPath, LoggedCommandResult result, TestCliOutputMode outputMode)
    {
        var projectName = Path.GetFileName(projectPath);

        if (outputMode == TestCliOutputMode.Raw)
        {
            Console.WriteLine($"Building '{projectPath}'...");
            WriteProcessOutput(result.ProcessResult);
            WriteLogPath(result.LogPath);
            return;
        }

        if (result.ProcessResult.ExitCode == 0)
        {
            Console.WriteLine($"OK build {projectName} ({result.ProcessResult.Duration.TotalSeconds:0.0}s)");

            if (outputMode == TestCliOutputMode.Summary)
                WriteLogPath(result.LogPath);

            return;
        }

        Console.WriteLine($"FAIL build {projectName} ({result.ProcessResult.Duration.TotalSeconds:0.0}s)");
        var analysis = DotnetOutputAnalyzer.Analyze(DotnetCommandType.Build, result.ProcessResult);
        if (!string.IsNullOrWhiteSpace(analysis.FailureSummary))
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(analysis.FailureSummary)}[/]");

        if (analysis.Errors.Count > 0)
            WriteDiagnostics("Errors", analysis.Errors);
        else
            WriteFailureDetails(ExtractFailureLines(string.Concat(result.ProcessResult.StandardOutput, Environment.NewLine, result.ProcessResult.StandardError)));

        WriteLogPath(result.LogPath);
    }

    private static void RenderTestRunOutcome(RunResult result, TestCliOutputMode outputMode)
    {
        if (outputMode == TestCliOutputMode.Raw)
        {
            WriteProcessOutput(result.ProcessResult);
            WriteLogPath(result.LogPath);
            return;
        }

        var batchLabel = result.BatchIndex.HasValue
            ? $" batch {result.BatchIndex.Value}"
            : string.Empty;
        var targetLabel = result.Targets == "-"
            ? string.Empty
            : $" [{result.Targets}]";

        if (result.ExitCode == 0)
        {
            Console.WriteLine($"OK suite {result.Suite}{batchLabel}{targetLabel} ({FormatSucceededCount(result)}, {result.DurationSeconds:0.0}s)");

            if (outputMode == TestCliOutputMode.Summary)
            {
                WriteDetailBlock("Summary", "yellow", ExtractSummaryLines(result.ProcessResult.StandardOutput));
                WriteLogPath(result.LogPath);
            }

            return;
        }

        Console.WriteLine($"FAIL suite {result.Suite}{batchLabel}{targetLabel} ({result.DurationSeconds:0.0}s)");

        var failureLines = result.FailedTests.Count > 0
            ? result.FailedTests.Select(static failedTest => $"{failedTest.FormattedName}: {failedTest.Message ?? "failed"}").ToArray()
            : ExtractFailureLines(string.Concat(result.ProcessResult.StandardOutput, Environment.NewLine, result.ProcessResult.StandardError));

        WriteDetailBlock("Failures", "red", failureLines);
        WriteLogPath(result.LogPath);
    }

    private static void WriteDiagnostics(string title, IReadOnlyList<DotnetDiagnostic> diagnostics)
    {
        Console.WriteLine();
        AnsiConsole.Write(new Rule($"[yellow]{title}[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Code")
            .AddColumn("Message")
            .AddColumn("Projects")
            .AddColumn("Count");

        foreach (var diagnostic in diagnostics)
        {
            var projects = diagnostic.Projects.Count switch
            {
                0 => "-",
                <= 2 => string.Join(", ", diagnostic.Projects.Select(static project => Path.GetFileName(project))),
                _ => $"{string.Join(", ", diagnostic.Projects.Take(2).Select(static project => Path.GetFileName(project)))}, +{diagnostic.Projects.Count - 2} more"
            };

            table.AddRow(
                Markup.Escape(diagnostic.Code ?? "-"),
                Markup.Escape(diagnostic.Message),
                Markup.Escape(projects),
                Markup.Escape(diagnostic.Count.ToString()));
        }

        AnsiConsole.Write(table);
    }

    private static void WriteFailureDetails(IEnumerable<string> lines) =>
        WriteDetailBlock("Failures", "red", lines);

    private static void WriteDetailBlock(string title, string color, IEnumerable<string> lines)
    {
        var details = lines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (details.Length == 0)
            return;

        Console.WriteLine();
        AnsiConsole.Write(new Rule($"[{color}]{title}[/]"));
        foreach (var line in details)
            Console.WriteLine(line);
    }

    private static void WriteLogPath(string logPath)
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Raw log:[/] {Markup.Escape(logPath)}");
    }

    private static string[] ExtractSummaryLines(string output) =>
        SanitizeConsoleOutput(output)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line =>
                line.StartsWith("HTML test report written to:", StringComparison.Ordinal) ||
                line.StartsWith("In process file artifacts produced:", StringComparison.Ordinal) ||
                line.StartsWith("-", StringComparison.Ordinal) ||
                line.StartsWith("Test run summary:", StringComparison.Ordinal) ||
                line.StartsWith("total:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("failed:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("succeeded:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("skipped:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("duration:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static string[] ExtractFailureLines(string output) =>
        SanitizeConsoleOutput(output)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static line =>
                line.StartsWith("failed ", StringComparison.Ordinal) ||
                line.StartsWith("Unhandled exception", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Error output:", StringComparison.Ordinal) ||
                line.StartsWith("Exit code:", StringComparison.Ordinal) ||
                line.StartsWith("Unknown option", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Test run summary:", StringComparison.Ordinal) ||
                line.Contains("UnauthorizedAccessException", StringComparison.Ordinal) ||
                line.Contains("NamedPipeClient.ConnectAsync", StringComparison.Ordinal) ||
                line.Contains("Det går inte att hitta filen", StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToArray();

    private static bool ShouldRenderSummary(TestCliOutputMode outputMode, IReadOnlyList<RunResult> results, int overallExitCode) =>
        outputMode == TestCliOutputMode.Summary ||
        results.Count > 1 ||
        overallExitCode != 0;

    private static bool ShouldRenderFailedTests(TestCliOutputMode outputMode, IReadOnlyList<RunResult> results) =>
        outputMode is TestCliOutputMode.Summary or TestCliOutputMode.Failures &&
        results.Any(static result => result.FailedTests.Count > 0);

    private static string FormatSucceededCount(RunResult result)
    {
        if (result.Succeeded.HasValue && result.Total.HasValue)
            return $"{result.Succeeded.Value}/{result.Total.Value} passed";

        if (result.Succeeded.HasValue)
            return $"{result.Succeeded.Value} passed";

        return "passed";
    }

    private static string CreateRunArtifactPrefix(string suiteName, int? batchIndex, CliTargetSelection? selection)
    {
        var segments = new List<string> { "run", suiteName };
        if (batchIndex.HasValue)
            segments.Add($"batch{batchIndex.Value}");
        if (selection is not null && selection.Targets.Count > 0)
            segments.Add(string.Join("-", selection.Targets.Select(static target => target.Id)));

        return string.Join("-", segments);
    }

    private static void RenderSummary(IReadOnlyList<RunResult> results)
    {
        ConsoleSync.Run(() =>
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
        });
    }

    private static void RenderFailedTests(IReadOnlyList<RunResult> results)
    {
        var failedBatches = results
            .Where(x => x.FailedTests.Count > 0)
            .ToArray();

        if (failedBatches.Length == 0)
            return;

        ConsoleSync.Run(() =>
        {
            Console.WriteLine();
            AnsiConsole.Write(new Rule("[red]Failed Tests[/]"));

            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Suite")
                .AddColumn("Test")
                .AddColumn("Target")
                .AddColumn("Message");

            foreach (var row in failedBatches
                         .SelectMany(batch => batch.FailedTests.Select(failedTest => new
                         {
                             batch.Suite,
                             FailedTest = failedTest
                         }))
                         .OrderBy(x => x.FailedTest.FormattedName, StringComparer.Ordinal)
                         .ThenBy(x => x.FailedTest.Target ?? string.Empty, StringComparer.Ordinal))
            {
                table.AddRow(
                    new Text(row.Suite),
                    new Text(row.FailedTest.FormattedName),
                    new Text(row.FailedTest.Target ?? "-"),
                    new Text(ShortenFailureMessage(row.FailedTest.Message ?? "-")));
            }

            AnsiConsole.Write(table);
        });
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

    private static RunResult CreateRunResult(string suite, int? batchIndex, string targets, TimeSpan elapsed, LoggedCommandResult result) =>
        new(
            Suite: suite,
            BatchIndex: batchIndex,
            Targets: targets,
            ExitCode: result.ProcessResult.ExitCode,
            DurationSeconds: Math.Round(elapsed.TotalSeconds, 1),
            Total: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "total"),
            Succeeded: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "succeeded"),
            Failed: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "failed"),
            Skipped: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "skipped"),
            FailedTests: ParseFailedTests(SanitizeConsoleOutput(result.ProcessResult.StandardOutput)),
            ProcessResult: result.ProcessResult,
            LogPath: result.LogPath);

    private static string SanitizeConsoleOutput(string output) =>
        string.IsNullOrEmpty(output)
            ? output
            : AnsiEscapePattern.Replace(output, string.Empty);

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

    private static IReadOnlyList<FailedTestResult> ParseFailedTests(string output)
    {
        var failedTests = new List<FailedTestResult>();
        var lines = output.Replace("\r\n", "\n").Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (!trimmed.StartsWith("failed ", StringComparison.Ordinal))
                continue;

            var header = trimmed["failed ".Length..];
            var detailLines = new List<string>();

            for (var detailIndex = index + 1; detailIndex < lines.Length; detailIndex++)
            {
                var detail = lines[detailIndex];
                var detailTrimmed = detail.Trim();

                if (detailTrimmed.StartsWith("failed ", StringComparison.Ordinal) ||
                    detailTrimmed.StartsWith("Test run summary:", StringComparison.Ordinal))
                {
                    index = detailIndex - 1;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(detail))
                    detailLines.Add(detailTrimmed);

                if (detailIndex == lines.Length - 1)
                    index = detailIndex;
            }

            failedTests.Add(ParseFailedTest(header, detailLines));
        }

        return failedTests;
    }

    private static FailedTestResult ParseFailedTest(string header, IReadOnlyList<string> detailLines)
    {
        var target = ExtractTarget(header);
        var testName = ExtractTestName(header);
        var className = ExtractClassName(detailLines, testName);
        var message = ExtractFailureMessage(detailLines);

        return new FailedTestResult(testName, className, target, message);
    }

    private static string ExtractTestName(string header)
    {
        var argumentListIndex = header.IndexOf('(');
        if (argumentListIndex > 0)
            return header[..argumentListIndex].TrimEnd();

        return header.Trim();
    }

    private static string? ExtractTarget(string header)
    {
        var match = Regex.Match(header, @"TestProviderDescriptor\s*\{\s*Name\s*=\s*(?<target>[^,}]+)", RegexOptions.CultureInvariant);
        if (match.Success)
            return match.Groups["target"].Value.Trim();

        return null;
    }

    private static string? ExtractFailureMessage(IReadOnlyList<string> detailLines)
    {
        foreach (var line in detailLines)
        {
            var testFailureIndex = line.IndexOf("[Test Failure] ", StringComparison.Ordinal);
            if (testFailureIndex >= 0)
                return line[(testFailureIndex + "[Test Failure] ".Length)..].Trim();
        }

        foreach (var line in detailLines)
        {
            if (line.StartsWith("at ", StringComparison.Ordinal))
                continue;

            if (line.Length > 0)
                return line.Trim();
        }

        return null;
    }

    private static string? ExtractClassName(IReadOnlyList<string> detailLines, string testName)
    {
        var escapedTestName = Regex.Escape(testName);
        var pattern = $@"\bat\s+(?<qualified>[A-Za-z0-9_\.]+)\.{escapedTestName}\s*\(";

        foreach (var line in detailLines)
        {
            var match = Regex.Match(line, pattern, RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            var qualifiedType = match.Groups["qualified"].Value;
            var lastDot = qualifiedType.LastIndexOf('.');
            return lastDot >= 0 ? qualifiedType[(lastDot + 1)..] : qualifiedType;
        }

        return null;
    }

    private static string ShortenFailureMessage(string message)
    {
        const int maxLength = 140;
        return message.Length <= maxLength
            ? message
            : $"{message[..(maxLength - 1)].TrimEnd()}…";
    }

    private static string FormatNullableCount(int? value) => value?.ToString() ?? "-";

    private static void WriteSummaryJson(string? summaryJsonPath, IReadOnlyList<RunResult> results, int overallExitCode)
    {
        if (string.IsNullOrWhiteSpace(summaryJsonPath))
            return;

        var resolvedPath = Path.GetFullPath(summaryJsonPath);
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new RunSummaryPayload(
            OverallExitCode: overallExitCode,
            Total: SumCounts(results.Select(static x => x.Total)),
            Passed: SumCounts(results.Select(static x => x.Succeeded)),
            Failed: SumCounts(results.Select(static x => x.Failed)),
            Skipped: SumCounts(results.Select(static x => x.Skipped)),
            Results: results.Select(static x => new RunSummaryEntryPayload(
                x.Suite,
                x.BatchIndex,
                x.Targets,
                x.ExitCode,
                x.DurationSeconds,
                x.Total,
                x.Succeeded,
                x.Failed,
                x.Skipped)).ToArray());

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(resolvedPath, json);
    }

    private static int? SumCounts(IEnumerable<int?> values)
    {
        var knownValues = values.Where(static x => x.HasValue).Select(static x => x!.Value).ToArray();
        return knownValues.Length == 0 ? null : knownValues.Sum();
    }

    private static IReadOnlyList<RunResult> OrderResults(IEnumerable<RunResult> results) =>
        results
            .OrderBy(x => GetSuiteOrder(x.Suite))
            .ThenBy(x => x.BatchIndex ?? 0)
            .ThenBy(x => x.Targets, StringComparer.Ordinal)
            .ToArray();

    private static int GetSuiteOrder(string suiteName)
    {
        for (var index = 0; index < TestCliSuiteCatalog.Suites.Count; index++)
        {
            if (string.Equals(TestCliSuiteCatalog.Suites[index].Name, suiteName, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        return int.MaxValue;
    }

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
        IReadOnlyList<FailedTestResult> FailedTests,
        ExternalCommandResult ProcessResult,
        string LogPath);

    private sealed record FailedTestResult(
        string TestName,
        string? ClassName,
        string? Target,
        string? Message)
    {
        public string FormattedName => string.IsNullOrWhiteSpace(ClassName)
            ? TestName
            : $"{ClassName}.{TestName}";
    }

    private sealed record SuiteRunResult(
        int ExitCode,
        IReadOnlyList<RunResult> Results);

    private sealed record LoggedCommandResult(
        ExternalCommandResult ProcessResult,
        string LogPath);

    private sealed record RunSummaryPayload(
        int OverallExitCode,
        int? Total,
        int? Passed,
        int? Failed,
        int? Skipped,
        IReadOnlyList<RunSummaryEntryPayload> Results);

    private sealed record RunSummaryEntryPayload(
        string Suite,
        int? BatchIndex,
        string Targets,
        int ExitCode,
        double DurationSeconds,
        int? Total,
        int? Passed,
        int? Failed,
        int? Skipped);
}
