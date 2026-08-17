using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DataLinq.DevTools;
using MySqlConnector;
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
        var planOption = new Option<string?>("--plan")
        {
            Description = "Runs a named feedback contract: focused, smoke, quick, latest, or full. Provider --alias/--targets remain an independent override."
        };
        var interactiveOption = CommandHelpers.InteractiveOption();
        var parallelSuitesOption = CommandHelpers.ParallelSuitesOption();
        var outputOption = CommandHelpers.OutputOption();
        var profileOption = CommandHelpers.ProfileOption();
        var projectOption = new Option<string?>("--project")
        {
            Description = "Optional project path override for a single-suite run."
        };
        var filterOption = new Option<string?>("--filter")
        {
            Description = "Optional TUnit tree-node filter expression. Forwarded to the test host as --treenode-filter."
        };
        filterOption.Aliases.Add("--treenode-filter");
        var configurationOption = new Option<string>("--configuration")
        {
            Description = "Build configuration.",
            DefaultValueFactory = _ => "Debug"
        };
        var buildOption = new Option<bool>("--build")
        {
            Description = "Explicitly builds each distinct test project once before running it (the default unless --no-build is used)."
        };
        var noBuildOption = new Option<bool>("--no-build")
        {
            Description = "Runs existing test host outputs directly and fails if they are missing, ambiguous, or stale."
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
        command.Options.Add(planOption);
        command.Options.Add(interactiveOption);
        command.Options.Add(parallelSuitesOption);
        command.Options.Add(outputOption);
        command.Options.Add(profileOption);
        command.Options.Add(projectOption);
        command.Options.Add(filterOption);
        command.Options.Add(configurationOption);
        command.Options.Add(buildOption);
        command.Options.Add(noBuildOption);
        command.Options.Add(batchSizeOption);
        command.Options.Add(tearDownOption);
        command.Options.Add(summaryJsonOption);

        command.SetAction(parseResult => CommandHelpers.ExecuteSafely(() =>
        {
            var requestedPlanName = parseResult.GetValue(planOption);
            var requestedPlan = string.IsNullOrWhiteSpace(requestedPlanName)
                ? null
                : TestCliRunPlanCatalog.GetPlan(requestedPlanName);
            var requestedSummaryPath = parseResult.GetValue(summaryJsonOption);
            if (requestedPlan is not null && string.IsNullOrWhiteSpace(requestedSummaryPath))
                requestedSummaryPath = TestCliRunPlanCatalog.GetLastSummaryPath(settings.RepositoryRoot, requestedPlan.Name);

            if (parseResult.GetValue(interactiveOption))
            {
                if (requestedPlan is not null)
                    throw new InvalidOperationException("'--interactive' cannot be combined with '--plan'.");
                if (!string.IsNullOrWhiteSpace(requestedSummaryPath))
                    throw new InvalidOperationException("'--interactive' cannot be combined with '--summary-json'.");
                InteractiveCliRunner.RunTests(orchestrator, settings);
                return;
            }

            var batchSize = parseResult.GetValue(batchSizeOption);
            if (batchSize < 1 || batchSize > 32)
                throw new InvalidOperationException("'--batch-size' must be between 1 and 32.");
            if (parseResult.GetValue(buildOption) && parseResult.GetValue(noBuildOption))
                throw new InvalidOperationException("'--build' and '--no-build' cannot be combined.");
            var buildProjects = !parseResult.GetValue(noBuildOption);

            var suiteName = parseResult.GetValue(suiteOption) ?? TestCliSuiteCatalog.AllSuites;
            var filter = parseResult.GetValue(filterOption);
            var projectPath = parseResult.GetValue(projectOption);
            ValidatePlanOverrides(requestedPlan, suiteName, filter, projectPath);

            var selection = ResolveTargetSelection(
                requestedPlan,
                parseResult.GetValue(aliasOption),
                parseResult.GetValue(targetsOption));
            ValidatePlanSelection(requestedPlan, selection);

            var exitCode = ExecuteSafely(() => RunSelection(
                orchestrator,
                settings,
                selection,
                suiteName,
                projectPath,
                filter,
                parseResult.GetValue(configurationOption) ?? throw new InvalidOperationException("A build configuration is required."),
                buildProjects,
                batchSize,
                parseResult.GetValue(parallelSuitesOption),
                parseResult.GetValue(tearDownOption),
                requestedSummaryPath,
                CommandHelpers.ParseOutputMode(parseResult.GetValue(outputOption)),
                CommandHelpers.ParseProfile(parseResult.GetValue(profileOption)),
                requestedPlan));

            if (exitCode != 0)
                Environment.ExitCode = exitCode;
        }));

        return command;
    }

    public static void Execute(
        TestInfraOrchestrator orchestrator,
        TestInfraCliSettings settings,
        CliTargetSelection selection,
        string suiteName,
        string? projectPathOverride,
        string? filter,
        string configuration,
        bool buildProject,
        int batchSize,
        bool parallelSuites,
        bool tearDown,
        string? summaryJsonPath,
        TestCliOutputMode outputMode,
        ToolingProfile profile,
        TestCliRunPlan? plan = null)
    {
        var exitCode = ExecuteSafely(() => RunSelection(orchestrator, settings, selection, suiteName, projectPathOverride, filter, configuration, buildProject, batchSize, parallelSuites, tearDown, summaryJsonPath, outputMode, profile, plan));
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
            Console.Error.WriteLine(TestRunSummaryReporter.SanitizeFailureMessage(exception.Message));
            return 1;
        }
    }

    private static void ValidatePlanOverrides(
        TestCliRunPlan? plan,
        string suiteName,
        string? filter,
        string? projectPath)
    {
        if (plan is null)
            return;

        if (!string.IsNullOrWhiteSpace(projectPath))
            throw new InvalidOperationException("'--project' cannot be combined with '--plan'. Use a named suite from the plan contract.");

        if (plan.RequiresExplicitSelection)
        {
            if (string.Equals(suiteName, TestCliSuiteCatalog.AllSuites, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The focused plan requires an explicit '--suite'.");
            if (string.IsNullOrWhiteSpace(filter))
                throw new InvalidOperationException("The focused plan requires an explicit TUnit '--filter'.");
            return;
        }

        if (!string.Equals(suiteName, TestCliSuiteCatalog.AllSuites, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The '{plan.Name}' plan owns its suite selection; do not combine it with '--suite'. Use '--plan focused' for an ad hoc suite.");
        if (!string.IsNullOrWhiteSpace(filter))
            throw new InvalidOperationException($"The '{plan.Name}' plan owns its filters; do not combine it with '--filter'. Use '--plan focused' for an ad hoc filter.");
    }

    private static CliTargetSelection ResolveTargetSelection(
        TestCliRunPlan? plan,
        string? aliasName,
        string? targetList)
    {
        if (plan is null)
            return TargetSelectionResolver.Resolve(aliasName, targetList, defaultAlias: TestTargetCatalog.LatestAlias);

        if (!string.IsNullOrWhiteSpace(aliasName) || !string.IsNullOrWhiteSpace(targetList))
            return TargetSelectionResolver.Resolve(aliasName, targetList);

        return plan.DefaultTargetAlias is not null
            ? TargetSelectionResolver.ResolveAlias(plan.DefaultTargetAlias)
            : TargetSelectionResolver.ResolveTargets(plan.DefaultTargetIds);
    }

    private static void ValidatePlanSelection(TestCliRunPlan? plan, CliTargetSelection selection)
    {
        if (plan?.Name is not (TestCliRunPlanCatalog.SmokePlan or TestCliRunPlanCatalog.QuickPlan))
            return;

        var serverTargets = selection.Targets.Where(static target => target.UsesPodman).Select(static target => target.Id).ToArray();
        if (serverTargets.Length > 0)
            throw new InvalidOperationException($"The '{plan.Name}' plan is a no-Podman contract and cannot use server targets: {string.Join(", ", serverTargets)}.");
    }

    private static int RunSelection(
        TestInfraOrchestrator orchestrator,
        TestInfraCliSettings settings,
        CliTargetSelection selection,
        string suiteName,
        string? projectPathOverride,
        string? filter,
        string configuration,
        bool buildProject,
        int batchSize,
        bool parallelSuites,
        bool tearDown,
        string? summaryJsonPath,
        TestCliOutputMode outputMode,
        ToolingProfile profile,
        TestCliRunPlan? plan)
    {
        var repositoryRoot = settings.RepositoryRoot;
        var summaryRequested = !string.IsNullOrWhiteSpace(summaryJsonPath);
        if (summaryRequested)
            TestRunSummaryReporter.InvalidateExistingReport(repositoryRoot, summaryJsonPath!);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var runId = CreateRunId(startedAtUtc);
        var runArtifactRoot = Path.Combine(repositoryRoot, "artifacts", "test-results", runId);
        Directory.CreateDirectory(runArtifactRoot);
        var repositoryStart = summaryRequested
            ? TestRunSummaryReporter.CaptureRepositoryState(repositoryRoot)
            : null;
        var runnerAssemblies = summaryRequested
            ? TestRunSummaryReporter.CaptureRunnerAssemblies()
            : default;
        var builds = new List<TestRunSummaryBuild>();
        var results = new List<RunResult>();
        var expectedResults = new List<TestRunSummaryExpectedResult>();
        var overallExitCode = 0;
        var teardownDurationSeconds = 0d;
        var usedTargets = false;
        var resultLock = new object();
        var failureStage = "resolve-suites";
        TestRunSummaryFailure? failure = null;
        TestRunSummaryFailure? teardownFailure = null;
        TestRunSummaryInvocation? invocation = null;
        TestRunSummaryReport? summaryReport = null;
        Exception? executionException = null;

        try
        {
            var suites = ResolveSuites(suiteName, projectPathOverride, plan);
            var invocationFilter = FormatInvocationFilter(suites, filter);
            invocation = summaryRequested
                ? CreateSummaryInvocation(
                    repositoryRoot,
                    selection,
                    suites,
                    suiteName,
                    projectPathOverride,
                    invocationFilter,
                    configuration,
                    buildProject,
                    batchSize,
                    parallelSuites,
                    tearDown,
                    outputMode,
                    profile,
                    plan?.Name)
                : null;
            expectedResults.AddRange(CreateExpectedResults(suites, selection, repositoryRoot, batchSize));
            var projectPaths = suites
                .Select(suite => ResolveProjectPath(repositoryRoot, suite.ProjectPath))
                .Distinct(PathComparer)
                .ToArray();

            if (buildProject)
            {
                foreach (var projectPath in projectPaths)
                {
                    failureStage = $"build:{Path.GetFileNameWithoutExtension(projectPath)}";
                    var build = BuildProject(projectPath, configuration, settings, outputMode, profile, runArtifactRoot);
                    builds.Add(CreateSummaryBuild(projectPath, build));
                    if (build.ProcessResult.ExitCode == 0)
                        continue;

                    overallExitCode = build.ProcessResult.ExitCode;
                    throw new InvalidOperationException($"Failed to build '{projectPath}'.");
                }
            }

            failureStage = "resolve-test-hosts";
            var testHostPaths = projectPaths.ToDictionary(
                static projectPath => projectPath,
                projectPath => TestHostResolver.Resolve(
                    repositoryRoot,
                    projectPath,
                    configuration,
                    requireCurrentOutput: true).HostPath,
                PathComparer);

            failureStage = "run-suites";
            Exception? suiteExecutionException = null;
            try
            {
                if (parallelSuites)
                {
                    var suiteTasks = suites
                        .Select(suite => Task.Run(() =>
                        {
                            var exitCode = ExecuteSuiteRun(
                                suite,
                                selection,
                                settings,
                                repositoryRoot,
                                suite.Filter ?? filter,
                                batchSize,
                                orchestrator,
                                outputMode,
                                profile,
                                runArtifactRoot,
                                testHostPaths,
                                completedResultRef: completedResult =>
                                {
                                    lock (resultLock)
                                        results.Add(completedResult);
                                },
                                usedTargetsRef: value =>
                                {
                                    lock (resultLock)
                                        usedTargets = usedTargets || value;
                                });

                            lock (resultLock)
                            {
                                if (exitCode != 0)
                                    overallExitCode = exitCode;
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
                        var exitCode = ExecuteSuiteRun(
                            suite,
                            selection,
                            settings,
                            repositoryRoot,
                            suite.Filter ?? filter,
                            batchSize,
                            orchestrator,
                            outputMode,
                            profile,
                            runArtifactRoot,
                            testHostPaths,
                            completedResultRef: results.Add,
                            usedTargetsRef: value => usedTargets = usedTargets || value);

                        if (exitCode != 0)
                            overallExitCode = exitCode;
                    }
                }
            }
            catch (Exception exception)
            {
                suiteExecutionException = exception;
            }

            if (tearDown && usedTargets)
            {
                var teardownStopwatch = Stopwatch.StartNew();
                try
                {
                    orchestrator.Down(remove: false, selection: null);
                }
                catch (Exception exception)
                {
                    if (suiteExecutionException is null)
                    {
                        failureStage = "tear-down";
                        throw;
                    }

                    teardownFailure = new TestRunSummaryFailure(
                        "tear-down",
                        exception.GetType().FullName ?? exception.GetType().Name,
                        TestRunSummaryReporter.SanitizeFailureMessage(
                            exception.Message,
                            settings.AdminPassword,
                            settings.ApplicationPassword));
                }
                finally
                {
                    teardownStopwatch.Stop();
                    teardownDurationSeconds = teardownStopwatch.Elapsed.TotalSeconds;
                }
            }

            if (suiteExecutionException is not null)
                ExceptionDispatchInfo.Capture(suiteExecutionException).Throw();

            var orderedResults = OrderResults(results);
            if (ShouldRenderSummary(outputMode, orderedResults, overallExitCode))
                RenderSummary(orderedResults);
            if (ShouldRenderFailedTests(outputMode, orderedResults))
                RenderFailedTests(orderedResults);
        }
        catch (Exception exception)
        {
            overallExitCode = 1;
            failure = new TestRunSummaryFailure(
                failureStage,
                exception.GetType().FullName ?? exception.GetType().Name,
                TestRunSummaryReporter.SanitizeFailureMessage(
                    exception.Message,
                    settings.AdminPassword,
                    settings.ApplicationPassword));
            executionException = new InvalidOperationException(failure.Message, exception);
        }
        finally
        {
            if (summaryRequested)
            {
                var repositoryEnd = TestRunSummaryReporter.CaptureRepositoryState(repositoryRoot);
                var orderedResults = OrderResults(results);
                try
                {
                    summaryReport = WriteSummaryJson(
                        summaryJsonPath!,
                        runId,
                        startedAtUtc,
                        invocation ?? CreateFallbackSummaryInvocation(
                            repositoryRoot,
                            selection,
                            suiteName,
                            projectPathOverride,
                            filter,
                            configuration,
                            buildProject,
                            batchSize,
                            parallelSuites,
                            tearDown,
                            outputMode,
                            profile,
                            plan?.Name),
                        repositoryStart!,
                        repositoryEnd,
                        runnerAssemblies.EntryAssembly,
                        runnerAssemblies.DevToolsAssembly,
                        expectedResults,
                        builds,
                        orderedResults,
                        overallExitCode,
                        failure,
                        teardownFailure,
                        teardownDurationSeconds);
                }
                catch (Exception reportException) when (executionException is not null)
                {
                    Console.Error.WriteLine(
                        $"Additionally failed to write test summary JSON: {TestRunSummaryReporter.SanitizeFailureMessage(reportException.Message)}");
                }
            }
        }

        if (executionException is not null)
            ExceptionDispatchInfo.Capture(executionException).Throw();

        return summaryReport?.OverallExitCode ?? overallExitCode;
    }

    private static int ExecuteSuiteRun(
        TestCliSuite suite,
        CliTargetSelection selection,
        TestInfraCliSettings settings,
        string repositoryRoot,
        string? filter,
        int batchSize,
        TestInfraOrchestrator orchestrator,
        TestCliOutputMode outputMode,
        ToolingProfile profile,
        string runArtifactRoot,
        IReadOnlyDictionary<string, string> testHostPaths,
        Action<RunResult> completedResultRef,
        Action<bool>? usedTargetsRef)
    {
        var projectPath = ResolveProjectPath(repositoryRoot, suite.ProjectPath);
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"The requested test project was not found: '{projectPath}'.", projectPath);
        if (!testHostPaths.TryGetValue(projectPath, out var testHostPath))
            throw new InvalidOperationException($"No resolved test host exists for '{projectPath}'.");

        var exitCode = 0;

        if (suite.UsesTargetBatches)
        {
            usedTargetsRef?.Invoke(true);
            var suiteTargets = suite.IncludeSqliteTargets
                ? selection.Targets.ToArray()
                : selection.Targets.Where(static x => !TestTargetCatalog.IsSQLiteTarget(x.Id)).ToArray();

            if (suiteTargets.Length == 0)
                return exitCode;

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
                var infrastructureStopwatch = Stopwatch.StartNew();
                orchestrator.Up(batch, recreate: false);
                infrastructureStopwatch.Stop();

                var artifacts = CreateRunArtifactPaths(runArtifactRoot, suite.Name, index + 1, batch);
                var result = ExecuteTestRun(testHostPath, filter, settings, batch, artifacts, profile);

                var runResult = CreateRunResult(
                    suite.Name,
                    projectPath,
                    index + 1,
                    batch.Targets.Select(static target => target.Id).ToArray(),
                    infrastructureStopwatch.Elapsed,
                    result);
                completedResultRef(runResult);
                RenderTestRunOutcome(runResult, outputMode);

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

            var artifacts = CreateRunArtifactPaths(runArtifactRoot, suite.Name, batchIndex: null, selection: null);
            var result = ExecuteTestRun(testHostPath, filter, settings, selection: null, artifacts, profile);

            var runResult = CreateRunResult(
                suite.Name,
                projectPath,
                batchIndex: null,
                targetIds: Array.Empty<string>(),
                infrastructureElapsed: TimeSpan.Zero,
                result);
            completedResultRef(runResult);
            RenderTestRunOutcome(runResult, outputMode);

            if (result.ProcessResult.ExitCode != 0)
                exitCode = result.ProcessResult.ExitCode;
        }

        return exitCode;
    }

    private static LoggedCommandResult BuildProject(
        string projectPath,
        string configuration,
        TestInfraCliSettings settings,
        TestCliOutputMode outputMode,
        ToolingProfile profile,
        string runArtifactRoot)
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

        var logPath = Path.Combine(
            runArtifactRoot,
            "build",
            $"{SanitizeArtifactSegment(Path.GetFileNameWithoutExtension(projectPath))}.log");
        var result = ExecuteDotnet(arguments, settings, profile, logPath);
        RenderBuildOutcome(projectPath, result, outputMode);
        return result;
    }

    private static LoggedCommandResult ExecuteTestRun(
        string testHostPath,
        string? filter,
        TestInfraCliSettings settings,
        CliTargetSelection? selection,
        RunArtifactPaths artifacts,
        ToolingProfile profile)
    {
        var environmentVariables = new Dictionary<string, string?>(
            settings.ToolPaths.CreateEnvironment(profile),
            StringComparer.OrdinalIgnoreCase);
        environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.FixtureTelemetryReportPathEnvironmentVariable] =
            artifacts.FixtureTelemetryReportPath;

        if (selection is not null)
        {
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.ProviderSetEnvironmentVariable] = "targets";
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetIdsEnvironmentVariable] = string.Join(",", selection.Targets.Select(x => x.Id));
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetAliasEnvironmentVariable] = null;
        }
        else
        {
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.ProviderSetEnvironmentVariable] = null;
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetIdsEnvironmentVariable] = null;
            environmentVariables[DataLinq.Testing.PodmanTestEnvironmentSettings.TargetAliasEnvironmentVariable] = null;
        }

        var arguments = new List<string>
        {
            "exec",
            testHostPath
        };
        arguments.AddRange([
            "--results-directory", artifacts.Directory,
            "--report-html-filename", artifacts.HtmlReportPath,
            "--report-trx",
            "--report-trx-filename", Path.GetFileName(artifacts.TrxReportPath),
            "--no-progress"
        ]);

        if (!string.IsNullOrWhiteSpace(filter))
            arguments.AddRange(["--treenode-filter", filter]);

        var result = ExecuteDotnet(
            arguments,
            settings,
            profile,
            artifacts.LogPath,
            environmentVariables,
            selection);
        CompleteFixtureTelemetryReport(artifacts.FixtureTelemetryReportPath, settings, selection);
        var configuredMaximumParallelTests = ResolveConfiguredMaximumParallelTests(environmentVariables);
        return result with
        {
            TestArtifacts = artifacts,
            Performance = TestRunTrxReader.Read(
                artifacts.TrxReportPath,
                result.ProcessResult.Duration.TotalSeconds,
                configuredMaximumParallelTests)
        };
    }

    private static void CompleteFixtureTelemetryReport(
        string reportPath,
        TestInfraCliSettings settings,
        CliTargetSelection? selection)
    {
        if (selection is null || selection.ServerTargets.Count == 0 || !File.Exists(reportPath))
            return;

        try
        {
            var report = JsonNode.Parse(File.ReadAllText(reportPath))?.AsObject();
            if (report is null)
                return;

            var environment = DataLinq.Testing.PodmanTestEnvironmentSettings.FromEnvironment(settings.RepositoryRoot);
            var samples = new JsonArray();
            foreach (var target in selection.ServerTargets)
            {
                var (connections, threadsConnected) = ReadServerStatusAfterTestHostExit(environment, target);
                samples.Add(new JsonObject
                {
                    ["Target"] = target.Id,
                    ["ServerConnectionsAfterTestHostExit"] = connections,
                    ["ServerThreadsConnectedAfterTestHostExit"] = threadsConnected
                });
            }

            report["SchemaVersion"] = "v0.9.fixture-telemetry.v2";
            report["PostProcessServerStatus"] = samples;
            File.WriteAllText(
                reportPath,
                report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Could not append post-process server status to fixture telemetry report '{reportPath}': {exception.Message}");
        }
    }

    private static (long? Connections, long? ThreadsConnected) ReadServerStatusAfterTestHostExit(
        DataLinq.Testing.PodmanTestEnvironmentSettings environment,
        DataLinq.Testing.DatabaseServerTarget target)
    {
        using var connection = new MySqlConnection(environment.CreateAdminConnectionString(target));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SHOW GLOBAL STATUS WHERE Variable_name IN ('Connections', 'Threads_connected');";
        using var reader = command.ExecuteReader();
        long? connections = null;
        long? threadsConnected = null;
        while (reader.Read())
        {
            var name = reader.GetString(0);
            if (!long.TryParse(reader.GetString(1), out var value))
                continue;

            if (string.Equals(name, "Connections", StringComparison.OrdinalIgnoreCase))
                connections = value;
            else if (string.Equals(name, "Threads_connected", StringComparison.OrdinalIgnoreCase))
                threadsConnected = value;
        }

        return (connections, threadsConnected);
    }

    private static LoggedCommandResult ExecuteDotnet(
        IReadOnlyList<string> arguments,
        TestInfraCliSettings settings,
        ToolingProfile profile,
        string logPath,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CliTargetSelection? targetSelection = null)
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

        var startedAtUtc = DateTimeOffset.UtcNow;
        var processResult = ExternalProcessRunner.Execute(
            "dotnet",
            arguments,
            settings.RepositoryRoot,
            mergedEnvironmentVariables);
        var completedAtUtc = DateTimeOffset.UtcNow;
        WriteRawLog(logPath, processResult);
        return new LoggedCommandResult(
            processResult,
            logPath,
            "dotnet",
            arguments.ToArray(),
            settings.RepositoryRoot,
            CaptureCommandEnvironment(settings, mergedEnvironmentVariables, targetSelection),
            startedAtUtc,
            completedAtUtc);
    }

    private static TestRunSummaryCommandEnvironment CaptureCommandEnvironment(
        TestInfraCliSettings settings,
        IReadOnlyDictionary<string, string?> environmentVariables,
        CliTargetSelection? targetSelection)
    {
        var hostOverride = ResolveEnvironmentValue(
            environmentVariables,
            DataLinq.Testing.PodmanTestEnvironmentSettings.HostEnvironmentVariable);
        var usesDatabaseHost = targetSelection?.ServerTargets.Count > 0;
        var resolvedHost = usesDatabaseHost
            ? hostOverride ?? new TestInfraRuntimeStateStore(settings.StatePath).Load()?.Host
            : null;
        string? host = null;
        var hostCaptured = usesDatabaseHost && TryNormalizeDatabaseHost(resolvedHost, out host);
        var providerSet = ResolveEnvironmentValue(
            environmentVariables,
            DataLinq.Testing.PodmanTestEnvironmentSettings.ProviderSetEnvironmentVariable);
        var targetAlias = ResolveEnvironmentValue(
            environmentVariables,
            DataLinq.Testing.PodmanTestEnvironmentSettings.TargetAliasEnvironmentVariable);
        var targetIdsValue = ResolveEnvironmentValue(
            environmentVariables,
            DataLinq.Testing.PodmanTestEnvironmentSettings.TargetIdsEnvironmentVariable);
        var parsedTargetIds = string.IsNullOrWhiteSpace(targetIdsValue)
            ? Array.Empty<string>()
            : targetIdsValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var targetIds = parsedTargetIds
            .Where(id => TestCliCatalog.Targets.Any(target =>
                string.Equals(target.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (targetIds.Length != parsedTargetIds.Length)
            providerSet = null;

        return new TestRunSummaryCommandEnvironment(
            usesDatabaseHost,
            hostCaptured,
            host,
            string.Equals(providerSet, "targets", StringComparison.OrdinalIgnoreCase),
            targetAlias is null,
            targetIds);
    }

    private static string? ResolveEnvironmentValue(
        IReadOnlyDictionary<string, string?> environmentVariables,
        string key) =>
        environmentVariables.TryGetValue(key, out var value)
            ? NormalizeEnvironmentValue(value)
            : NormalizeEnvironmentValue(Environment.GetEnvironmentVariable(key));

    private static string? NormalizeEnvironmentValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryNormalizeDatabaseHost(string? value, out string? host)
    {
        host = null;
        var candidate = NormalizeEnvironmentValue(value);
        if (candidate is null)
            return false;
        if (string.Equals(candidate, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            host = "localhost";
            return true;
        }
        if (!IPAddress.TryParse(candidate, out var address))
            return false;

        host = address.ToString();
        return true;
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

    private static void WriteRawLog(string logPath, ExternalCommandResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        File.WriteAllText(logPath, string.Concat(result.StandardOutput, result.StandardError));
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

    private static string CreateRunId(DateTimeOffset startedAtUtc) =>
        $"{startedAtUtc.UtcDateTime:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";

    private static RunArtifactPaths CreateRunArtifactPaths(
        string runArtifactRoot,
        string suiteName,
        int? batchIndex,
        CliTargetSelection? selection)
    {
        var rowName = batchIndex.HasValue
            ? $"batch-{batchIndex.Value:00}-{string.Join("-", selection!.Targets.Select(static target => target.Id))}"
            : "targetless";
        var directory = Path.Combine(
            runArtifactRoot,
            SanitizeArtifactSegment(suiteName),
            SanitizeArtifactSegment(rowName));
        Directory.CreateDirectory(directory);
        return new RunArtifactPaths(
            directory,
            Path.Combine(directory, "raw.log"),
            Path.Combine(directory, "report.html"),
            Path.Combine(directory, "report.trx"),
            Path.Combine(directory, "fixture-metrics.json"));
    }

    private static string SanitizeArtifactSegment(string value)
    {
        var sanitized = new string(value
            .Select(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    private static int? ResolveConfiguredMaximumParallelTests(
        IReadOnlyDictionary<string, string?> environmentVariables)
    {
        const string key = "TUNIT_MAX_PARALLEL_TESTS";
        var value = ResolveEnvironmentValue(environmentVariables, key);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
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

    private static IReadOnlyList<TestCliSuite> ResolveSuites(
        string suiteName,
        string? projectPathOverride,
        TestCliRunPlan? plan)
    {
        if (plan is not null && !plan.RequiresExplicitSelection)
            return TestCliRunPlanCatalog.ResolveSuites(plan);

        if (string.IsNullOrWhiteSpace(projectPathOverride))
            return TestCliSuiteCatalog.Resolve(suiteName);

        if (string.Equals(suiteName, TestCliSuiteCatalog.AllSuites, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("'--project' cannot be combined with '--suite all'. Choose a single suite or omit '--project'.");

        var suite = TestCliSuiteCatalog.GetSuite(suiteName);
        return [suite with { ProjectPath = projectPathOverride }];
    }

    private static string? FormatInvocationFilter(IReadOnlyList<TestCliSuite> suites, string? requestedFilter)
    {
        if (!string.IsNullOrWhiteSpace(requestedFilter))
            return requestedFilter;

        var suiteFilters = suites
            .Where(static suite => !string.IsNullOrWhiteSpace(suite.Filter))
            .Select(static suite => $"{suite.Name}={suite.Filter}")
            .ToArray();
        return suiteFilters.Length == 0 ? null : string.Join(";", suiteFilters);
    }

    private static string ResolveProjectPath(string repositoryRoot, string projectPath) =>
        Path.IsPathRooted(projectPath)
            ? projectPath
            : Path.Combine(repositoryRoot, projectPath);

    private static RunResult CreateRunResult(
        string suite,
        string projectPath,
        int? batchIndex,
        IReadOnlyList<string> targetIds,
        TimeSpan infrastructureElapsed,
        LoggedCommandResult result) =>
        new(
            Suite: suite,
            ProjectPath: projectPath,
            BatchIndex: batchIndex,
            TargetIds: targetIds,
            ExitCode: result.ProcessResult.ExitCode,
            DurationSeconds: Math.Round(result.ProcessResult.Duration.TotalSeconds, 3),
            InfrastructureSetupDurationSeconds: Math.Round(infrastructureElapsed.TotalSeconds, 3),
            Total: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "total"),
            Succeeded: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "succeeded"),
            Failed: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "failed"),
            Skipped: ParseSummaryCount(SanitizeConsoleOutput(result.ProcessResult.StandardOutput), "skipped"),
            FailedTests: ParseFailedTests(SanitizeConsoleOutput(result.ProcessResult.StandardOutput)),
            TestArtifacts: result.TestArtifacts ?? throw new InvalidOperationException("A test run did not declare its report artifacts."),
            Performance: result.Performance ?? TestRunTrxReader.Unavailable("Test performance capture was not attempted."),
            ProcessResult: result.ProcessResult,
            LogPath: result.LogPath,
            Executable: result.Executable,
            Arguments: result.Arguments,
            WorkingDirectory: result.WorkingDirectory,
            Environment: result.Environment,
            StartedAtUtc: result.StartedAtUtc,
            CompletedAtUtc: result.CompletedAtUtc);

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

    private static TestRunSummaryInvocation CreateSummaryInvocation(
        string repositoryRoot,
        CliTargetSelection selection,
        IReadOnlyList<TestCliSuite> suites,
        string suiteName,
        string? projectPathOverride,
        string? filter,
        string configuration,
        bool buildProject,
        int batchSize,
        bool parallelSuites,
        bool tearDown,
        TestCliOutputMode outputMode,
        ToolingProfile profile,
        string? planName)
    {
        var resolvedSuites = suites
            .Select(suite => new TestRunSummarySuite(
                suite.Name,
                ResolveProjectPath(repositoryRoot, suite.ProjectPath),
                suite.UsesTargetBatches,
                suite.IncludeSqliteTargets,
                suite.Filter))
            .ToArray();
        var selectedTargets = selection.Targets
            .Select(static target => new TestRunSummaryTarget(
                target.Id,
                target.DisplayName,
                target.Category,
                target.UsesPodman,
                target.ServerTarget?.HostPort))
            .ToArray();
        var resolvedSuiteNames = resolvedSuites
            .Select(static suite => suite.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allSuiteNames = TestCliSuiteCatalog.Suites
            .Select(static suite => suite.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedTargetIds = selectedTargets
            .Select(static target => target.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allTargetIds = TestCliCatalog.Targets
            .Select(static target => target.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new TestRunSummaryInvocation(
            Command: "run",
            RepositoryRoot: repositoryRoot,
            Alias: selection.AliasName,
            SelectedTargets: selectedTargets,
            ResolvedSuites: resolvedSuites,
            SafeEnvironment: CreateSummarySafeEnvironment(),
            IncludesAllSuites: resolvedSuiteNames.SetEquals(allSuiteNames),
            IncludesAllTargets: selectedTargetIds.SetEquals(allTargetIds),
            IsUnfiltered: string.IsNullOrWhiteSpace(filter),
            Suite: suiteName,
            ProjectPath: projectPathOverride,
            Filter: filter,
            Configuration: configuration,
            BuildProject: buildProject,
            BatchSize: batchSize,
            ParallelSuites: parallelSuites,
            TearDown: tearDown,
            OutputMode: outputMode.ToString(),
            Profile: profile,
            Plan: planName);
    }

    private static TestRunSummaryInvocation CreateFallbackSummaryInvocation(
        string repositoryRoot,
        CliTargetSelection selection,
        string suiteName,
        string? projectPathOverride,
        string? filter,
        string configuration,
        bool buildProject,
        int batchSize,
        bool parallelSuites,
        bool tearDown,
        TestCliOutputMode outputMode,
        ToolingProfile profile,
        string? planName) =>
        new(
            Command: "run",
            RepositoryRoot: repositoryRoot,
            Alias: selection.AliasName,
            SelectedTargets: selection.Targets
                .Select(static target => new TestRunSummaryTarget(
                    target.Id,
                    target.DisplayName,
                    target.Category,
                    target.UsesPodman,
                    target.ServerTarget?.HostPort))
                .ToArray(),
            ResolvedSuites: Array.Empty<TestRunSummarySuite>(),
            SafeEnvironment: CreateSummarySafeEnvironment(),
            IncludesAllSuites: false,
            IncludesAllTargets: false,
            IsUnfiltered: string.IsNullOrWhiteSpace(filter),
            Suite: suiteName,
            ProjectPath: projectPathOverride,
            Filter: filter,
            Configuration: configuration,
            BuildProject: buildProject,
            BatchSize: batchSize,
            ParallelSuites: parallelSuites,
            TearDown: tearDown,
            OutputMode: outputMode.ToString(),
            Profile: profile,
            Plan: planName);

    private static TestRunSummarySafeEnvironment CreateSummarySafeEnvironment()
    {
        var rawHost = NormalizeEnvironmentValue(Environment.GetEnvironmentVariable(
            DataLinq.Testing.PodmanTestEnvironmentSettings.HostEnvironmentVariable));
        var present = rawHost is not null;
        string? host = null;
        var valid = !present || TryNormalizeDatabaseHost(rawHost, out host);
        return new TestRunSummarySafeEnvironment(
            DatabaseHostOverridePresent: present,
            DatabaseHostOverrideValid: valid,
            DatabaseHostOverride: valid ? host : null,
            ProviderSetForTargetBatches: "targets",
            ClearsTargetAliasForTargetBatches: true);
    }

    private static IReadOnlyList<TestRunSummaryExpectedResult> CreateExpectedResults(
        IReadOnlyList<TestCliSuite> suites,
        CliTargetSelection selection,
        string repositoryRoot,
        int batchSize)
    {
        var expected = new List<TestRunSummaryExpectedResult>();
        foreach (var suite in suites)
        {
            var projectPath = ResolveProjectPath(repositoryRoot, suite.ProjectPath);
            if (!suite.UsesTargetBatches)
            {
                expected.Add(new TestRunSummaryExpectedResult(
                    suite.Name,
                    projectPath,
                    BatchIndex: null,
                    TargetIds: Array.Empty<string>()));
                continue;
            }

            var suiteTargets = suite.IncludeSqliteTargets
                ? selection.Targets.ToArray()
                : selection.Targets.Where(static target => !TestTargetCatalog.IsSQLiteTarget(target.Id)).ToArray();
            var batches = CreateBatches(suiteTargets, batchSize);
            for (var index = 0; index < batches.Count; index++)
            {
                expected.Add(new TestRunSummaryExpectedResult(
                    suite.Name,
                    projectPath,
                    BatchIndex: index + 1,
                    TargetIds: batches[index].Select(static target => target.Id).ToArray()));
            }
        }

        return expected;
    }

    private static TestRunSummaryBuild CreateSummaryBuild(string projectPath, LoggedCommandResult build) =>
        new(
            projectPath,
            build.Executable,
            build.Arguments,
            build.WorkingDirectory,
            build.StartedAtUtc,
            build.CompletedAtUtc,
            Math.Round(build.ProcessResult.Duration.TotalSeconds, 3),
            build.ProcessResult.ExitCode,
            build.LogPath);

    private static TestRunSummaryReport WriteSummaryJson(
        string summaryJsonPath,
        string runId,
        DateTimeOffset startedAtUtc,
        TestRunSummaryInvocation invocation,
        TestRunSummaryRepositoryState repositoryStart,
        TestRunSummaryRepositoryState repositoryEnd,
        TestRunSummaryRunnerAssembly entryAssembly,
        TestRunSummaryRunnerAssembly devToolsAssembly,
        IReadOnlyList<TestRunSummaryExpectedResult> expectedResults,
        IReadOnlyList<TestRunSummaryBuild> builds,
        IReadOnlyList<RunResult> results,
        int overallExitCode,
        TestRunSummaryFailure? failure,
        TestRunSummaryFailure? teardownFailure,
        double teardownDurationSeconds)
    {
        var summaryResults = results.Select(static result => new TestRunSummaryResult(
            result.Suite,
            result.ProjectPath,
            result.BatchIndex,
            result.Targets,
            result.TargetIds,
            TestRunSummaryOutcome.Incomplete,
            result.Executable,
            result.Arguments,
            result.WorkingDirectory,
            result.Environment,
            result.StartedAtUtc,
            result.CompletedAtUtc,
            result.DurationSeconds,
            result.ExitCode,
            result.Total,
            result.Succeeded,
            result.Failed,
            result.Skipped,
            new[]
                {
                    result.LogPath,
                    result.TestArtifacts.HtmlReportPath,
                    result.TestArtifacts.TrxReportPath
                }
                .Concat(File.Exists(result.TestArtifacts.FixtureTelemetryReportPath)
                    ? [result.TestArtifacts.FixtureTelemetryReportPath]
                    : Array.Empty<string>())
                .ToArray(),
            result.LogPath,
            result.TestArtifacts.HtmlReportPath,
            result.TestArtifacts.TrxReportPath,
            result.InfrastructureSetupDurationSeconds,
            result.Performance)).ToArray();
        var report = TestRunSummaryReporter.Create(new TestRunSummaryReportInput(
            RunId: runId,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: DateTimeOffset.UtcNow,
            Invocation: invocation,
            ReportPath: summaryJsonPath,
            RepositoryStart: repositoryStart,
            RepositoryEnd: repositoryEnd,
            EntryAssembly: entryAssembly,
            DevToolsAssembly: devToolsAssembly,
            OverallExitCode: overallExitCode,
            Total: SumCounts(results.Select(static result => result.Total)),
            Passed: SumCounts(results.Select(static result => result.Succeeded)),
            Failed: SumCounts(results.Select(static result => result.Failed)),
            Skipped: SumCounts(results.Select(static result => result.Skipped)),
            ExpectedResults: expectedResults,
            Builds: builds,
            Results: summaryResults,
            Failure: failure,
            TeardownFailure: teardownFailure,
            TeardownDurationSeconds: teardownDurationSeconds));
        var resolvedExitCode = TestRunSummaryReporter.ResolveExitCode(report, overallExitCode);
        if (resolvedExitCode != report.OverallExitCode)
            report = report with { OverallExitCode = resolvedExitCode };
        TestRunSummaryReporter.Write(report);
        return report;
    }

    private static int? SumCounts(IEnumerable<int?> values)
    {
        var counts = values.ToArray();
        return counts.Length == 0 || counts.Any(static count => count is null)
            ? null
            : counts.Sum(static count => count!.Value);
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

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record RunResult(
        string Suite,
        string ProjectPath,
        int? BatchIndex,
        IReadOnlyList<string> TargetIds,
        int ExitCode,
        double DurationSeconds,
        double InfrastructureSetupDurationSeconds,
        int? Total,
        int? Succeeded,
        int? Failed,
        int? Skipped,
        IReadOnlyList<FailedTestResult> FailedTests,
        RunArtifactPaths TestArtifacts,
        TestRunSummaryPerformance Performance,
        ExternalCommandResult ProcessResult,
        string LogPath,
        string Executable,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        TestRunSummaryCommandEnvironment Environment,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc)
    {
        public string Targets => TargetIds.Count == 0
            ? "-"
            : string.Join(", ", TargetIds);
    }

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

    private sealed record LoggedCommandResult(
        ExternalCommandResult ProcessResult,
        string LogPath,
        string Executable,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        TestRunSummaryCommandEnvironment Environment,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        RunArtifactPaths? TestArtifacts = null,
        TestRunSummaryPerformance? Performance = null);

    private sealed record RunArtifactPaths(
        string Directory,
        string LogPath,
        string HtmlReportPath,
        string TrxReportPath,
        string FixtureTelemetryReportPath);

}
