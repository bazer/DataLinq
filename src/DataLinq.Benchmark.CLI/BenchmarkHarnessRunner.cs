using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Runtime.InteropServices;
using DataLinq.DevTools;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DataLinq.Benchmark.CLI;

internal sealed class BenchmarkHarnessRunner
{
    private static readonly string[] WarningPatterns =
    [
        "The minimum observed iteration time is",
        "MultimodalDistribution",
        "ZeroMeasurement",
        "EnvironmentVariable",
        "NoWorkloadResult"
    ];
    private static readonly Regex BenchmarkDotNetVersionPattern = new(
        @"\bBenchmarkDotNet v(?<version>[0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)\b",
        RegexOptions.CultureInvariant);
    internal const string Phase2WatchCategory = "phase2-watch";
    internal const string Phase3QueryHotPathCategory = "phase3-query-hotpath";
    internal const string Phase10KeyFoundationCategory = "phase10-key-foundation";
    internal const string Phase11CacheInvalidationCategory = "phase11-cache-invalidation";
    internal const string Phase12CacheMemoryCategory = "phase12-cache-memory";
    internal const string V09QueryBackendCategory = "v0.9-query-backend";
    internal const string V09MemoryReadCategory = "v0.9-memory-read";
    internal const string AllocationRegressionCategory = "allocation-regression";
    internal const string AllocationStagesCategory = "allocation-stages";
    internal const string MacroReadWriteCategory = "macro-readwrite";
    internal const string MacroBulkCategory = "macro-bulk";
    private const string BenchmarkProfileEnvironmentVariable = "DATALINQ_BENCHMARK_PROFILE";
    private const string BenchmarkRunIdEnvironmentVariable = "DATALINQ_BENCHMARK_RUN_ID";
    private const string BenchmarkArtifactsDirectoryEnvironmentVariable = "DATALINQ_BENCHMARK_ARTIFACTS_DIR";
    private const string BenchmarkResultsDirectoryEnvironmentVariable = "DATALINQ_BENCHMARK_RESULTS_DIR";

    private readonly BenchmarkCliSettings settings;

    public BenchmarkHarnessRunner(BenchmarkCliSettings settings)
    {
        this.settings = settings;
    }

    public int List(bool noBuild, bool verbose, IReadOnlyList<string> additionalArgs)
    {
        settings.EnsureDirectories();

        if (!noBuild)
            RestoreAndBuild(verbose);
        else
            Console.WriteLine("Skipping restore/build.");

        Console.WriteLine("Listing benchmarks...");
        var arguments = new List<string>
        {
            settings.BenchmarkAssemblyPath,
            "--artifacts",
            settings.ArtifactsRoot,
            "--list",
            "Flat"
        };

        arguments.AddRange(additionalArgs);

        var result = ExecuteDotnet(arguments);
        var logPath = WriteLog("benchmark-list", result);

        WriteStandardOutput(result, alwaysWrite: true);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Benchmark list failed. Full log: {logPath}");

        Console.WriteLine($"Benchmark list written to {logPath}");
        return 0;
    }

    public int Run(
        string filter,
        string profile,
        bool noBuild,
        bool keepFiles,
        bool verbose,
        bool phase2Watch,
        bool phase3QueryHotPath,
        bool phase10KeyFoundation,
        bool phase11CacheInvalidation,
        bool phase12CacheMemory,
        bool v09QueryBackend,
        bool v09MemoryRead,
        bool allocationRegression,
        bool allocationStages,
        string? historyJsonPath,
        string? baselinePath,
        string? comparisonJsonPath,
        double warningThresholdPercent,
        bool releaseEvidenceIntent,
        IReadOnlyList<string> additionalArgs)
    {
        settings.EnsureDirectories();
        var paths = BenchmarkEvidenceReporter.NormalizePaths(
            settings.RepositoryRoot,
            historyJsonPath,
            baselinePath,
            comparisonJsonPath,
            releaseEvidenceIntent);
        BenchmarkEvidenceReporter.InvalidateRequestedOutputs(settings.RepositoryRoot, paths);
        BenchmarkEvidenceReporter.ValidatePathDependencies(paths, releaseEvidenceIntent);
        BenchmarkEvidenceReporter.ValidateThreshold(warningThresholdPercent);
        var selectedCategory = ResolveSelectedCategory(
            phase2Watch,
            phase3QueryHotPath,
            phase10KeyFoundation,
            phase11CacheInvalidation,
            phase12CacheMemory,
            v09QueryBackend,
            v09MemoryRead,
            allocationRegression,
            allocationStages);
        if (!IsSupportedProfile(profile))
            throw new InvalidOperationException("The benchmark profile must be 'default', 'heavy', or 'smoke'.");
        var normalizedProfile = profile.Trim().ToLowerInvariant();
        var normalizedFilter = filter.Trim();
        if (normalizedFilter.Length is 0 or > 1024 ||
            !string.Equals(
                TestRunSummaryReporter.SanitizeFailureMessage(normalizedFilter),
                normalizedFilter,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The benchmark filter must be nonempty, bounded, and free of credential-shaped values.");
        }
        if (paths.BaselinePath is not null)
            BenchmarkEvidenceReporter.ValidateBaselinePath(settings.RepositoryRoot, paths.BaselinePath);
        var providerIds = BenchmarkEvidenceReporter.ResolveConfiguredProviderIds(
            selectedCategory,
            Environment.GetEnvironmentVariable("DATALINQ_BENCHMARK_PROVIDERS"));
        var expectedJob = BenchmarkEvidenceReporter.ResolveExpectedJob(normalizedProfile);
        var safeAdditionalArgs = SanitizeArguments(additionalArgs, out var argumentsRedacted);
        if (argumentsRedacted)
        {
            throw new InvalidDataException(
                "BenchmarkDotNet pass-through arguments must not contain credential-shaped values.");
        }
        var runId = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");
        var runDirectory = BenchmarkEvidenceReporter.PrepareRunDirectory(settings.RepositoryRoot, runId);
        var resultsDirectory = Path.Combine(runDirectory, "results");
        var startedAtUtc = DateTime.UtcNow;
        var repositoryStart = TestRunSummaryReporter.CaptureRepositoryState(settings.RepositoryRoot);
        var benchmarkTargetStart = settings.UsesExternalBenchmarkTarget
            ? TestRunSummaryReporter.CaptureRepositoryState(settings.BenchmarkTargetRepositoryRoot)
            : repositoryStart;
        var commands = new List<BenchmarkCommandRecord>();
        var warnings = new List<BenchmarkWarning>();
        SummaryResult? summaryResult = null;
        var processorIdentifier = ResolveProcessorIdentifier();
        var benchmarkDotNetVersion = ResolveBenchmarkDotNetVersion(result: null);
        var stage = "restore";
        var invocation = new BenchmarkInvocation(
            "run",
            Path.GetFullPath(settings.RepositoryRoot),
            Path.GetFullPath(settings.BenchmarkProjectPath),
            Path.GetFullPath(settings.BenchmarkAssemblyPath),
            runDirectory,
            normalizedProfile,
            expectedJob,
            normalizedFilter,
            selectedCategory,
            providerIds,
            noBuild,
            keepFiles,
            verbose,
            safeAdditionalArgs,
            argumentsRedacted,
            paths.HistoryJsonPath,
            paths.BaselinePath,
            paths.ComparisonJsonPath,
            warningThresholdPercent,
            releaseEvidenceIntent)
        {
            BenchmarkTargetRepositoryRoot = settings.UsesExternalBenchmarkTarget
                ? Path.GetFullPath(settings.BenchmarkTargetRepositoryRoot)
                : null
        };

        try
        {
            if (!noBuild)
                RestoreAndBuild(verbose, runDirectory, providerIds, commands);
            else
                Console.WriteLine("Skipping restore/build.");

            stage = "benchmark";
            var arguments = new List<string>
            {
                settings.BenchmarkAssemblyPath,
                "--artifacts",
                runDirectory,
                "--filter",
                normalizedFilter,
                "--join",
                "--disableLogFile"
            };
            if (keepFiles)
                arguments.Add("--keepFiles");
            arguments.AddRange(GetBenchmarkCategoryArguments(selectedCategory));
            arguments.AddRange(additionalArgs);

            var benchmarkEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [BenchmarkProfileEnvironmentVariable] = normalizedProfile,
                [BenchmarkRunIdEnvironmentVariable] = runId,
                [BenchmarkArtifactsDirectoryEnvironmentVariable] = runDirectory,
                [BenchmarkResultsDirectoryEnvironmentVariable] = resultsDirectory,
                ["DATALINQ_BENCHMARK_PROVIDERS"] = string.Equals(
                    selectedCategory,
                    V09MemoryReadCategory,
                    StringComparison.Ordinal)
                    ? null
                    : string.Join(',', providerIds),
                ["RestoreIgnoreFailedSources"] = "true",
                ["NuGetAudit"] = "false"
            };
            Console.WriteLine("Running benchmarks...");
            var executed = ExecuteRecordedDotnet(
                "benchmark",
                arguments,
                runDirectory,
                verbose,
                benchmarkEnvironment,
                normalizedProfile,
                runId,
                resultsDirectory,
                providerIds);
            commands.Add(executed.Command);
            benchmarkDotNetVersion = ResolveBenchmarkDotNetVersion(executed.Result) ?? benchmarkDotNetVersion;
            WriteStandardOutput(executed.Result, verbose || executed.Result.ExitCode != 0);
            if (executed.Result.ExitCode != 0)
                throw new InvalidOperationException($"Benchmark run failed. Full log: {executed.Command.LogPath}");
            EnsureNoKnownConfigurationErrors(executed.Result, executed.Command.LogPath);

            warnings.AddRange(ExtractWarnings(executed.Result));
            WriteWarnings(warnings);
            stage = "summary";
            summaryResult = WriteSummary(
                runDirectory,
                runId,
                executed.Command.LogPath,
                normalizedProfile,
                normalizedFilter,
                processorIdentifier,
                benchmarkDotNetVersion,
                selectedCategory,
                benchmarkTargetStart);
            var completedAtUtc = DateTime.UtcNow;
            var runnerEvidence = CaptureRunnerEvidence(repositoryStart, benchmarkTargetStart);
            var historyArtifact = BenchmarkEvidenceReporter.CreateHistory(new BenchmarkHistoryCreationInput(
                runId,
                startedAtUtc,
                completedAtUtc,
                CreateRunMetadata(
                    normalizedProfile,
                    normalizedFilter,
                    benchmarkTargetStart,
                    processorIdentifier,
                    benchmarkDotNetVersion),
                invocation,
                summaryResult.HistoryRows,
                commands,
                warnings,
                null,
                CreateArtifactPaths(invocation, commands, summaryResult),
                runnerEvidence));

            if (paths.HistoryJsonPath is not null)
                BenchmarkEvidenceReporter.WriteHistory(settings.RepositoryRoot, paths.HistoryJsonPath, historyArtifact);

            BenchmarkComparisonArtifact? comparisonArtifact = null;
            if (paths.BaselinePath is not null)
            {
                stage = "comparison";
                BenchmarkHistoryReadResult? baseline = null;
                BenchmarkHistoryReadResult? candidate = null;
                try
                {
                    baseline = BenchmarkEvidenceReporter.ReadHistory(settings.RepositoryRoot, paths.BaselinePath);
                    candidate = BenchmarkEvidenceReporter.ReadHistory(settings.RepositoryRoot, paths.HistoryJsonPath!);
                    comparisonArtifact = BenchmarkEvidenceReporter.CreateComparison(
                        settings.RepositoryRoot,
                        baseline,
                        candidate,
                        paths.ComparisonJsonPath,
                        warningThresholdPercent,
                        releaseEvidenceIntent);
                }
                catch (Exception exception)
                {
                    comparisonArtifact = BenchmarkEvidenceReporter.CreateErrorComparison(
                        new BenchmarkComparisonInvocation(
                            paths.BaselinePath,
                            paths.HistoryJsonPath!,
                            paths.ComparisonJsonPath,
                            warningThresholdPercent,
                            releaseEvidenceIntent),
                        baseline,
                        candidate,
                        exception);
                }

                RenderComparison(comparisonArtifact);
                if (paths.ComparisonJsonPath is not null)
                    BenchmarkEvidenceReporter.WriteComparison(settings.RepositoryRoot, paths.ComparisonJsonPath, comparisonArtifact);
            }

            WriteArtifacts(summaryResult, paths.HistoryJsonPath, paths.ComparisonJsonPath);
            if (ShouldFailRun(historyArtifact, comparisonArtifact, releaseEvidenceIntent))
            {
                return 1;
            }
            return 0;
        }
        catch (Exception exception)
        {
            var completedAtUtc = DateTime.UtcNow;
            var runnerEvidence = CaptureRunnerEvidence(repositoryStart, benchmarkTargetStart);
            var failure = new BenchmarkFailure(
                stage,
                exception.GetType().FullName ?? exception.GetType().Name,
                TestRunSummaryReporter.SanitizeFailureMessage(exception.Message));
            try
            {
                BenchmarkEvidenceReporter.InvalidateRequestedOutputs(settings.RepositoryRoot, paths);
            }
            catch
            {
                // Never follow an output path that became unsafe. The nonzero process exit
                // remains authoritative when the safe completion marker cannot be removed.
            }
            if (paths.HistoryJsonPath is not null)
            {
                try
                {
                    var historyArtifact = BenchmarkEvidenceReporter.CreateHistory(new BenchmarkHistoryCreationInput(
                        runId,
                        startedAtUtc,
                        completedAtUtc,
                        CreateRunMetadata(
                            normalizedProfile,
                            normalizedFilter,
                            benchmarkTargetStart,
                            processorIdentifier,
                            benchmarkDotNetVersion),
                        invocation,
                        summaryResult?.HistoryRows ?? [],
                        commands,
                        warnings,
                        failure,
                        CreateArtifactPaths(invocation, commands, summaryResult),
                        runnerEvidence));
                    BenchmarkEvidenceReporter.WriteHistory(settings.RepositoryRoot, paths.HistoryJsonPath, historyArtifact);
                }
                catch
                {
                    // The stale completion marker was invalidated before execution. A writer
                    // failure must not obscure the original benchmark failure.
                }
            }
            if (paths.ComparisonJsonPath is not null)
            {
                try
                {
                    var errorArtifact = BenchmarkEvidenceReporter.CreateErrorComparison(
                        new BenchmarkComparisonInvocation(
                            paths.BaselinePath ?? string.Empty,
                            paths.HistoryJsonPath ?? string.Empty,
                            paths.ComparisonJsonPath,
                            warningThresholdPercent,
                            releaseEvidenceIntent),
                        null,
                        null,
                        exception);
                    BenchmarkEvidenceReporter.WriteComparison(settings.RepositoryRoot, paths.ComparisonJsonPath, errorArtifact);
                }
                catch
                {
                    // No stale comparison survives, and the command remains failed.
                }
            }
            Console.Error.WriteLine(failure.Message);
            return 1;
        }
    }

    private void RestoreAndBuild(bool verbose) =>
        RestoreAndBuild(verbose, settings.ArtifactsRoot, [], new List<BenchmarkCommandRecord>());

    private void RestoreAndBuild(
        bool verbose,
        string logDirectory,
        IReadOnlyList<string> providerIds,
        ICollection<BenchmarkCommandRecord> commands)
    {
        Console.WriteLine("Restoring benchmark harness...");
        var restoreArguments = new[]
        {
            "restore",
            settings.BenchmarkProjectPath,
            "-nologo",
            "-v",
            verbose ? "minimal" : "q",
            "-p:NuGetAudit=false"
        };

        var restore = ExecuteRecordedDotnet(
            "restore",
            restoreArguments,
            logDirectory,
            verbose,
            null,
            null,
            null,
            null,
            providerIds);
        commands.Add(restore.Command);
        WriteStandardOutput(restore.Result, verbose || restore.Result.ExitCode != 0);

        if (restore.Result.ExitCode != 0)
            throw new InvalidOperationException("Benchmark harness restore failed.");

        Console.WriteLine("Building benchmark harness...");
        var buildArguments = new List<string>
        {
            "build",
            settings.BenchmarkProjectPath,
            "--no-restore",
            "-c",
            "Release",
            "-f",
            "net8.0",
            "-nologo",
            "-v",
            verbose ? "minimal" : "q",
            "-p:NuGetAudit=false"
        };
        if (settings.UsesExternalBenchmarkTarget)
        {
            buildArguments.Add($"-p:CustomAfterMicrosoftCommonTargets={Path.Combine(
                settings.RepositoryRoot,
                "src",
                "DataLinq.Benchmark.CLI",
                "BenchmarkTargetProvenance.targets")}");
            buildArguments.Add($"-p:DataLinqBenchmarkTargetRepositoryRoot={settings.BenchmarkTargetRepositoryRoot}");
            buildArguments.Add($"-p:DataLinqBenchmarkCompatibilitySource={Path.Combine(
                settings.RepositoryRoot,
                "src",
                "DataLinq.Benchmark.CLI",
                "HistoricalBenchmarkConfig.cs.txt")}");
        }

        var build = ExecuteRecordedDotnet(
            "build",
            buildArguments,
            logDirectory,
            verbose,
            null,
            null,
            null,
            null,
            providerIds);
        commands.Add(build.Command);
        WriteStandardOutput(build.Result, verbose || build.Result.ExitCode != 0);

        if (build.Result.ExitCode != 0)
            throw new InvalidOperationException("Benchmark harness build failed.");
    }

    private ExternalCommandResult ExecuteDotnet(
        IReadOnlyList<string> arguments,
        bool verbose = false,
        IReadOnlyDictionary<string, string?>? additionalEnvironmentVariables = null)
    {
        if (verbose)
            Console.WriteLine($"Command: dotnet {string.Join(" ", arguments.Select(QuoteArgument))}");

        var environmentVariables = new Dictionary<string, string?>(settings.CreateProcessEnvironment(), StringComparer.OrdinalIgnoreCase);
        if (additionalEnvironmentVariables is not null)
        {
            foreach (var pair in additionalEnvironmentVariables)
                environmentVariables[pair.Key] = pair.Value;
        }

        var workingDirectory = IsBenchmarkAssemblyInvocation(arguments)
            ? Path.GetDirectoryName(settings.BenchmarkProjectPath) ?? settings.RepositoryRoot
            : settings.RepositoryRoot;

        return ExternalProcessRunner.Execute(
            "dotnet",
            arguments,
            workingDirectory,
            environmentVariables);
    }

    private RecordedCommandResult ExecuteRecordedDotnet(
        string stage,
        IReadOnlyList<string> arguments,
        string logDirectory,
        bool verbose,
        IReadOnlyDictionary<string, string?>? additionalEnvironmentVariables,
        string? profile,
        string? runId,
        string? resultsDirectory,
        IReadOnlyList<string> providerIds)
    {
        var startedAtUtc = DateTime.UtcNow;
        var result = ExecuteDotnet(arguments, verbose, additionalEnvironmentVariables);
        var completedAtUtc = DateTime.UtcNow;
        var logPath = WriteLog(logDirectory, $"benchmark-{stage}", result);
        var safeArguments = SanitizeArguments(arguments, out _);
        var command = new BenchmarkCommandRecord(
            stage,
            "dotnet",
            safeArguments,
            GetDotnetWorkingDirectory(arguments),
            startedAtUtc,
            completedAtUtc,
            Math.Max(0d, result.Duration.TotalSeconds),
            result.ExitCode,
            logPath,
            new BenchmarkCommandEnvironment(
                profile,
                runId,
                runId is null ? null : logDirectory,
                resultsDirectory,
                providerIds.ToArray()));
        return new RecordedCommandResult(result, command);
    }

    private string GetDotnetWorkingDirectory(IReadOnlyList<string> arguments) =>
        IsBenchmarkAssemblyInvocation(arguments)
            ? Path.GetDirectoryName(settings.BenchmarkProjectPath) ?? settings.RepositoryRoot
            : settings.RepositoryRoot;

    private bool IsBenchmarkAssemblyInvocation(IReadOnlyList<string> arguments)
        => arguments.Count > 0 &&
           string.Equals(
               Path.GetFullPath(arguments[0]),
               Path.GetFullPath(settings.BenchmarkAssemblyPath),
               StringComparison.OrdinalIgnoreCase);

    private string WriteLog(string prefix, ExternalCommandResult result)
        => WriteLog(settings.ArtifactsRoot, prefix, result);

    private static string WriteLog(string directory, string prefix, ExternalCommandResult result)
    {
        Directory.CreateDirectory(directory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var logPath = Path.Combine(directory, $"{prefix}-{timestamp}-{Guid.NewGuid():N}.log");
        var content = string.Concat(result.StandardOutput, result.StandardError);
        File.WriteAllText(logPath, content);
        return logPath;
    }

    private void WriteStandardOutput(ExternalCommandResult result, bool alwaysWrite)
    {
        if (!alwaysWrite)
            return;

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            Console.WriteLine(result.StandardOutput.TrimEnd());

        if (!string.IsNullOrWhiteSpace(result.StandardError))
            Console.Error.WriteLine(result.StandardError.TrimEnd());
    }

    private static IReadOnlyList<BenchmarkWarning> ExtractWarnings(ExternalCommandResult result)
    {
        var outputLines = string.Concat(result.StandardOutput, Environment.NewLine, result.StandardError)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var warnings = outputLines
            .Where(line =>
                !string.Equals(line.Trim(), "// * Warnings *", StringComparison.OrdinalIgnoreCase) &&
                (WarningPatterns.Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase)) ||
                 line.Contains("warning", StringComparison.OrdinalIgnoreCase)))
            .Select(static line => TestRunSummaryReporter.SanitizeFailureMessage(line.Trim()))
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .Select(static line => new BenchmarkWarning("BenchmarkDotNet", line))
            .ToArray();

        return warnings;
    }

    private static void WriteWarnings(IReadOnlyList<BenchmarkWarning> warnings)
    {
        if (warnings.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Benchmark warnings:");
        foreach (var warning in warnings)
            Console.WriteLine($"  {warning.Message}");
    }

    private static void EnsureNoKnownConfigurationErrors(ExternalCommandResult result, string logPath)
    {
        var combinedOutput = string.Concat(result.StandardOutput, Environment.NewLine, result.StandardError);

        if (combinedOutput.Contains("The provided base job", StringComparison.OrdinalIgnoreCase) &&
            combinedOutput.Contains("is invalid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Benchmark run used an invalid BenchmarkDotNet job selection. Full log: {logPath}");
        }
    }

    private SummaryResult WriteSummary(
        string runDirectory,
        string runId,
        string logPath,
        string profile,
        string filter,
        string? processorIdentifier,
        string? benchmarkDotNetVersion,
        string? selectedCategory,
        TestRunSummaryRepositoryState benchmarkTargetState)
    {
        var resultsDirectory = Path.Combine(runDirectory, "results");
        if (!Directory.Exists(resultsDirectory))
            throw new InvalidOperationException($"Benchmark run did not produce a results directory. Full log: {logPath}");

        var csvPaths = Directory.GetFiles(resultsDirectory, "*-report.csv")
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (csvPaths.Length == 0)
            throw new InvalidOperationException($"Benchmark run did not produce a CSV summary. Full log: {logPath}");
        var markdownPaths = Directory.GetFiles(resultsDirectory, "*-report-github.md")
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (markdownPaths.Length != csvPaths.Length)
        {
            throw new InvalidOperationException(
                $"Benchmark run produced {csvPaths.Length} CSV report(s) but {markdownPaths.Length} Markdown report(s). Full log: {logPath}");
        }
        var telemetryPaths = Directory.GetFiles(resultsDirectory, $"{runId}-*-telemetry.json")
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var rowsByReport = csvPaths
            .Select(path => FilterRowsForProfile(ParseSummaryRows(path), profile))
            .ToArray();
        if (rowsByReport.Any(static rows => rows.Length == 0))
        {
            throw new InvalidOperationException(
                $"At least one current benchmark CSV did not contain rows for the requested '{profile}' job. Full log: {logPath}");
        }
        var rows = rowsByReport.SelectMany(static reportRows => reportRows).ToArray();
        var telemetryDeltas = LoadTelemetryDeltas(resultsDirectory, runId);

        if (rows.Length == 0)
            throw new InvalidOperationException(
                $"Benchmark summaries in '{resultsDirectory}' did not contain rows for the requested '{profile}' job. Full log: {logPath}");

        var mergedRows = BuildMergedSummaryRows(rows, telemetryDeltas);
        var measuredRows = mergedRows
            .Where(static row => row.MeanMicroseconds.HasValue)
            .ToArray();

        if (measuredRows.Length == 0)
        {
            throw new InvalidOperationException(
                $"Benchmark summary set '{resultsDirectory}' only contains invalid measurements. " +
                $"Raw measurements: {FormatInvalidMeasurements(mergedRows)}. Full log: {logPath}");
        }

        Console.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Summary[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Method")
            .AddColumn("Provider")
            .AddColumn(new TableColumn("Mean").RightAligned())
            .AddColumn(new TableColumn("Error").RightAligned())
            .AddColumn(new TableColumn("Noise").RightAligned())
            .AddColumn(new TableColumn("Allocated").RightAligned())
            .AddColumn("Telemetry");

        var fastestMean = measuredRows.Length > 0
            ? measuredRows.Min(static row => row.MeanMicroseconds!.Value)
            : (double?)null;
        var slowestMean = measuredRows.Length > 1
            ? measuredRows.Max(static row => row.MeanMicroseconds!.Value)
            : (double?)null;

        foreach (var row in mergedRows)
        {
            table.AddRow(
                new Text(FormatMethodLabel(row.Method)),
                new Text(FormatProviderLabel(row.ProviderName)),
                CreateMeanCell(row, fastestMean, slowestMean),
                CreateErrorCell(row),
                CreateNoiseCell(row),
                new Text(row.Allocated),
                new Text(FormatTelemetry(row.TelemetryDelta)));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Mean: green = fastest, red = slowest. Error/Noise: yellow > 10% of mean, red > 20%.[/]");
        AnsiConsole.MarkupLine("[grey]Telemetry deltas are per operation: Q=entity/scalar, Tx=starts/commits/rollbacks, Mut=inserts/updates/deletes with affected rows, Row=hits/misses/stores, Rel=hits/loads, Inv=ops rows/tables work precise/fallback, Mem=Memory construction/seed/read/cache work.[/]");
        var artifact = CreateSummaryArtifact(
            runId,
            profile,
            filter,
            mergedRows,
            processorIdentifier,
            benchmarkDotNetVersion,
            selectedCategory,
            benchmarkTargetState);
        var jsonPath = WriteSummaryArtifact(resultsDirectory, artifact);
        var historyRows = artifact.Rows.Select(static row => new BenchmarkHistoryArtifactRow(
            row.Method,
            row.ProviderName,
            row.Category,
            row.MeanMicroseconds,
            row.ErrorMicroseconds,
            row.MedianMicroseconds,
            row.StdDevMicroseconds,
            row.MinMicroseconds,
            row.MaxMicroseconds,
            row.AllocatedBytes,
            row.NoisePercent,
            row.UncertaintyPercent,
            row.StdDevPercent,
            row.OperationsPerInvoke,
            row.TrackingGroup,
            row.TelemetryDelta)
        {
            Job = row.Job,
            Runtime = row.Runtime,
            Jit = row.Jit,
            Platform = row.Platform,
            Toolchain = row.Toolchain
        }).ToArray();
        return new SummaryResult(
            jsonPath,
            artifact,
            csvPaths,
            markdownPaths,
            telemetryPaths,
            historyRows);
    }

    private static void WriteArtifacts(
        SummaryResult summary,
        string? historyJsonPath,
        string? comparisonJsonPath)
    {
        Console.WriteLine();
        Console.WriteLine("Artifacts:");
        foreach (var markdownPath in summary.MarkdownPaths)
            Console.WriteLine($"  Markdown: {markdownPath}");
        foreach (var csvPath in summary.CsvPaths)
            Console.WriteLine($"  CSV: {csvPath}");
        foreach (var telemetryPath in summary.TelemetryPaths)
            Console.WriteLine($"  Telemetry: {telemetryPath}");
        Console.WriteLine($"  Summary JSON: {summary.JsonPath}");

        if (!string.IsNullOrWhiteSpace(historyJsonPath))
            Console.WriteLine($"  History JSON: {historyJsonPath}");

        if (!string.IsNullOrWhiteSpace(comparisonJsonPath))
            Console.WriteLine($"  Comparison JSON: {comparisonJsonPath}");
    }

    private static BenchmarkSummaryRow[] ParseSummaryRows(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
            throw new InvalidDataException($"Benchmark CSV '{csvPath}' does not contain a header and measurement row.");

        var delimiter = DetectCsvDelimiter(lines[0]);
        var headers = SplitCsvLine(lines[0], delimiter);
        var methodIndex = Array.IndexOf(headers, "Method");
        var jobIndex = Array.IndexOf(headers, "Job");
        var runtimeIndex = Array.IndexOf(headers, "Runtime");
        var jitIndex = Array.IndexOf(headers, "Jit");
        var platformIndex = Array.IndexOf(headers, "Platform");
        var toolchainIndex = Array.IndexOf(headers, "Toolchain");
        var providerIndex = Array.IndexOf(headers, "ProviderName");
        var meanIndex = Array.IndexOf(headers, "Mean");
        var errorIndex = Array.IndexOf(headers, "Error");
        var medianIndex = Array.IndexOf(headers, "Median");
        var stdDevIndex = Array.IndexOf(headers, "StdDev");
        var minIndex = Array.IndexOf(headers, "Min");
        var maxIndex = Array.IndexOf(headers, "Max");
        var allocatedIndex = Array.IndexOf(headers, "Allocated");

        var requiredIndexes = new[]
        {
            methodIndex,
            jobIndex,
            runtimeIndex,
            jitIndex,
            platformIndex,
            toolchainIndex,
            providerIndex,
            meanIndex,
            errorIndex,
            allocatedIndex
        };
        if (requiredIndexes.Any(static index => index < 0))
        {
            throw new InvalidDataException(
                $"Benchmark CSV '{csvPath}' is missing one or more required identity or measurement columns.");
        }
        var maximumRequiredIndex = requiredIndexes.Max();
        var requiredHeaders = new[]
        {
            "Method", "Job", "Runtime", "Jit", "Platform", "Toolchain",
            "ProviderName", "Mean", "Error", "Allocated"
        };
        if (requiredHeaders.Any(name => headers.Count(header =>
                string.Equals(header, name, StringComparison.Ordinal)) != 1))
        {
            throw new InvalidDataException(
                $"Benchmark CSV '{csvPath}' contains duplicate required columns.");
        }

        var rows = new List<BenchmarkSummaryRow>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var columns = SplitCsvLine(line, delimiter);
            if (columns.Length <= maximumRequiredIndex)
            {
                throw new InvalidDataException(
                    $"Benchmark CSV '{csvPath}' contains a truncated measurement row.");
            }

            rows.Add(new BenchmarkSummaryRow(
                Method: NormalizeCell(columns[methodIndex]),
                Job: NormalizeCell(columns[jobIndex]),
                Runtime: NormalizeCell(columns[runtimeIndex]),
                Jit: NormalizeCell(columns[jitIndex]),
                Platform: NormalizeCell(columns[platformIndex]),
                Toolchain: NormalizeCell(columns[toolchainIndex]),
                ProviderName: NormalizeCell(columns[providerIndex]),
                Mean: NormalizeCell(columns[meanIndex]),
                Error: NormalizeCell(columns[errorIndex]),
                Allocated: NormalizeCell(columns[allocatedIndex]),
                MeanMicroseconds: TryParseDurationInMicroseconds(columns[meanIndex]),
                ErrorMicroseconds: TryParseDurationInMicroseconds(columns[errorIndex]),
                MedianMicroseconds: TryParseOptionalDuration(columns, medianIndex),
                StdDevMicroseconds: TryParseOptionalDuration(columns, stdDevIndex),
                MinMicroseconds: TryParseOptionalDuration(columns, minIndex),
                MaxMicroseconds: TryParseOptionalDuration(columns, maxIndex),
                AllocatedBytes: TryParseAllocatedBytes(columns[allocatedIndex])));
        }

        if (rows.Count == 0)
            throw new InvalidDataException($"Benchmark CSV '{csvPath}' contains no measurement rows.");
        return rows.ToArray();
    }

    private static BenchmarkSummaryRow[] FilterRowsForProfile(BenchmarkSummaryRow[] rows, string profile)
    {
        var expectedJob = BenchmarkEvidenceReporter.ResolveExpectedJob(profile);
        return rows
            .Where(row => string.Equals(row.Job, expectedJob, StringComparison.Ordinal))
            .ToArray();
    }

    private static bool IsSupportedProfile(string profile)
        => string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(profile, "heavy", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(profile, "smoke", StringComparison.OrdinalIgnoreCase);

    private static char DetectCsvDelimiter(string headerLine)
    {
        var candidates = new[] { ';', ',', '\t' };
        var bestDelimiter = ';';
        var bestScore = int.MinValue;
        var bestColumnCount = 0;

        foreach (var delimiter in candidates)
        {
            var columns = SplitCsvLine(headerLine, delimiter);
            var score =
                (columns.Contains("Method", StringComparer.Ordinal) ? 4 : 0) +
                (columns.Contains("ProviderName", StringComparer.Ordinal) ? 4 : 0) +
                (columns.Contains("Mean", StringComparer.Ordinal) ? 2 : 0) +
                (columns.Contains("Error", StringComparer.Ordinal) ? 2 : 0);

            if (score > bestScore || (score == bestScore && columns.Length > bestColumnCount))
            {
                bestDelimiter = delimiter;
                bestScore = score;
                bestColumnCount = columns.Length;
            }
        }

        return bestDelimiter;
    }

    private static string[] SplitCsvLine(string line, char delimiter)
    {
        var columns = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];

            if (character == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                insideQuotes = !insideQuotes;
                continue;
            }

            if (character == delimiter && !insideQuotes)
            {
                columns.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (insideQuotes)
            throw new InvalidDataException("Benchmark CSV contains an unterminated quoted field.");
        columns.Add(current.ToString());
        return columns.ToArray();
    }

    private static string NormalizeCell(string value) =>
        value.Trim().Trim('\'', '"');

    private static double? TryParseOptionalDuration(IReadOnlyList<string> columns, int index) =>
        index >= 0 && columns.Count > index
            ? TryParseDurationInMicroseconds(columns[index])
            : null;

    private static Dictionary<(string Method, string ProviderName), BenchmarkTelemetryDeltaArtifact> LoadTelemetryDeltas(string resultsDirectory, string runId)
    {
        var deltas = new Dictionary<(string Method, string ProviderName), BenchmarkTelemetryDeltaArtifact>();

        foreach (var filePath in Directory.GetFiles(resultsDirectory, $"{runId}-*-telemetry.json"))
        {
            var artifact = JsonSerializer.Deserialize<BenchmarkTelemetryDeltaArtifact>(File.ReadAllText(filePath));
            if (artifact is null ||
                string.IsNullOrWhiteSpace(artifact.Method) ||
                string.IsNullOrWhiteSpace(artifact.ProviderName) ||
                artifact.OperationsPerInvoke <= 0)
            {
                throw new InvalidDataException($"Benchmark telemetry '{filePath}' is incomplete.");
            }

            if (!deltas.TryAdd((artifact.Method, artifact.ProviderName), artifact))
            {
                throw new InvalidDataException(
                    $"Benchmark telemetry contains a duplicate row for '{artifact.Method}/{artifact.ProviderName}'.");
            }
        }

        return deltas;
    }

    private static MergedBenchmarkSummaryRow[] BuildMergedSummaryRows(
        BenchmarkSummaryRow[] rows,
        IReadOnlyDictionary<(string Method, string ProviderName), BenchmarkTelemetryDeltaArtifact> telemetryDeltas)
        => rows
            .Select(row =>
            {
                telemetryDeltas.TryGetValue((row.Method, row.ProviderName), out var delta);
                return new MergedBenchmarkSummaryRow(row, delta);
            })
            .ToArray();

    private BenchmarkSummaryArtifact CreateSummaryArtifact(
        string runId,
        string profile,
        string filter,
        IReadOnlyList<MergedBenchmarkSummaryRow> rows,
        string? processorIdentifier,
        string? benchmarkDotNetVersion,
        string? selectedCategory,
        TestRunSummaryRepositoryState benchmarkTargetState)
    {
        return new BenchmarkSummaryArtifact(
            RunId: runId,
            GeneratedAtUtc: DateTime.UtcNow,
            Metadata: CreateRunMetadata(
                profile,
                filter,
                benchmarkTargetState,
                processorIdentifier,
                benchmarkDotNetVersion),
            Rows: rows.Select(row => new BenchmarkSummaryArtifactRow(
                Method: row.Method,
                ProviderName: row.ProviderName,
                Category: GetScenarioCategory(row.Method),
                Job: row.Job,
                Runtime: row.Runtime,
                Jit: row.Jit,
                Platform: row.Platform,
                Toolchain: row.Toolchain,
                Mean: row.Mean,
                Error: row.Error,
                Allocated: row.Allocated,
                MeanMicroseconds: row.MeanMicroseconds,
                ErrorMicroseconds: row.ErrorMicroseconds,
                MedianMicroseconds: row.MedianMicroseconds,
                StdDevMicroseconds: row.StdDevMicroseconds,
                MinMicroseconds: row.MinMicroseconds,
                MaxMicroseconds: row.MaxMicroseconds,
                AllocatedBytes: row.AllocatedBytes,
                NoisePercent: GetRelativeError(row.MeanMicroseconds, row.ErrorMicroseconds) is double relativeError ? relativeError * 100d : null,
                UncertaintyPercent: GetRelativeError(row.MeanMicroseconds, row.ErrorMicroseconds) is double uncertainty ? uncertainty * 100d : null,
                StdDevPercent: GetRelativeError(row.MeanMicroseconds, row.StdDevMicroseconds) is double stdDevRelative ? stdDevRelative * 100d : null,
                OperationsPerInvoke: row.TelemetryDelta?.OperationsPerInvoke,
                TrackingGroup: GetTrackingGroup(row.Method, selectedCategory),
                TelemetryDelta: row.TelemetryDelta)).ToArray());
    }

    private static string WriteSummaryArtifact(string resultsDirectory, BenchmarkSummaryArtifact artifact)
    {
        var jsonPath = Path.Combine(resultsDirectory, $"{artifact.RunId}-summary.json");
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(jsonPath, json);
        return jsonPath;
    }

    private BenchmarkRunMetadata CreateRunMetadata(
        string profile,
        string filter,
        string? processorIdentifier,
        string? benchmarkDotNetVersion)
    {
        var gitContext = ResolveGitContext();
        return CreateRunMetadata(
            profile,
            filter,
            new TestRunSummaryRepositoryState(
                !string.IsNullOrWhiteSpace(gitContext.Commit),
                gitContext.Commit ?? "unknown",
                gitContext.Branch ?? "unknown",
                true,
                "unknown"),
            processorIdentifier,
            benchmarkDotNetVersion);
    }

    private static BenchmarkRunMetadata CreateRunMetadata(
        string profile,
        string filter,
        TestRunSummaryRepositoryState repositoryState,
        string? processorIdentifier,
        string? benchmarkDotNetVersion)
    {
        return new BenchmarkRunMetadata(
            Repository: SafeMetadataValue(Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")),
            Branch: SafeMetadataValue(repositoryState.Branch),
            Commit: SafeMetadataValue(repositoryState.Commit),
            Workflow: SafeMetadataValue(Environment.GetEnvironmentVariable("GITHUB_WORKFLOW")),
            RunId: SafeMetadataValue(Environment.GetEnvironmentVariable("GITHUB_RUN_ID")),
            RunNumber: SafeMetadataValue(Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER")),
            EventName: SafeMetadataValue(Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME")),
            RunnerOs: SafeIdentityValue(PreferNonblank(
                Environment.GetEnvironmentVariable("RUNNER_OS"),
                RuntimeInformation.OSDescription)),
            RunnerArchitecture: SafeIdentityValue(PreferNonblank(
                Environment.GetEnvironmentVariable("RUNNER_ARCH"),
                RuntimeInformation.ProcessArchitecture.ToString())),
            Profile: profile,
            Filter: filter)
        {
            RuntimeDescription = SafeIdentityValue(RuntimeInformation.FrameworkDescription),
            ProcessorCount = Environment.ProcessorCount,
            ProcessorIdentifier = SafeIdentityValue(processorIdentifier),
            BenchmarkDotNetVersion = SafeIdentityValue(benchmarkDotNetVersion)
        };
    }

    private static string? SafeMetadataValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : TestRunSummaryReporter.SanitizeFailureMessage(value.Trim());

    private static string PreferNonblank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string? SafeIdentityValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length > 512)
            return null;
        var sanitized = TestRunSummaryReporter.SanitizeFailureMessage(trimmed);
        return string.Equals(trimmed, sanitized, StringComparison.Ordinal) ? trimmed : null;
    }

    private static string? ResolveProcessorIdentifier()
    {
        var configured = SafeIdentityValue(Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"));
        if (configured is not null)
            return configured;

        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/cpuinfo"))
            return null;
        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                var separator = line.IndexOf(':');
                if (separator <= 0)
                    continue;
                var key = line[..separator].Trim();
                if (key is not ("model name" or "Hardware" or "cpu model"))
                    continue;
                var value = SafeIdentityValue(line[(separator + 1)..]);
                if (value is not null)
                    return value;
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private string? ResolveBenchmarkDotNetVersion(ExternalCommandResult? result)
    {
        if (result is not null)
        {
            var output = string.Concat(result.StandardOutput, Environment.NewLine, result.StandardError);
            var match = BenchmarkDotNetVersionPattern.Match(output);
            if (match.Success)
            {
                var reported = SafeIdentityValue(match.Groups["version"].Value);
                if (reported is not null)
                    return reported;
            }
        }

        try
        {
            var dependencyPath = Path.Combine(
                Path.GetDirectoryName(settings.BenchmarkAssemblyPath)!,
                "BenchmarkDotNet.dll");
            return File.Exists(dependencyPath)
                ? SafeIdentityValue(AssemblyName.GetAssemblyName(dependencyPath).Version?.ToString())
                : null;
        }
        catch
        {
            return null;
        }
    }

    private GitContext ResolveGitContext()
    {
        try
        {
            var gitDirectory = Path.Combine(settings.RepositoryRoot, ".git");

            var headPath = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(headPath))
                return new GitContext(null, null);

            var headContent = File.ReadAllText(headPath).Trim();
            if (!headContent.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
                return new GitContext(null, headContent);

            var reference = headContent["ref:".Length..].Trim();
            var branch = reference.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)
                ? reference["refs/heads/".Length..]
                : reference;

            var refPath = Path.Combine(gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(refPath))
                return new GitContext(branch, File.ReadAllText(refPath).Trim());

            var packedRefsPath = Path.Combine(gitDirectory, "packed-refs");
            if (File.Exists(packedRefsPath))
            {
                var packedRefLine = File.ReadLines(packedRefsPath)
                    .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#') && !line.StartsWith('^'))
                    .Select(static line => line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
                    .FirstOrDefault(parts => parts.Length == 2 && string.Equals(parts[1], reference, StringComparison.Ordinal));

                if (packedRefLine is not null)
                    return new GitContext(branch, packedRefLine[0]);
            }

            return new GitContext(branch, null);
        }
        catch
        {
            return new GitContext(null, null);
        }
    }

    private static void RenderComparison(BenchmarkComparisonArtifact comparisonArtifact)
    {
        Console.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Comparison[/]"));

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Method")
            .AddColumn("Provider")
            .AddColumn(new TableColumn("Mean Δ").RightAligned())
            .AddColumn(new TableColumn("Alloc Δ").RightAligned())
            .AddColumn("Status");

        foreach (var row in comparisonArtifact.Rows)
        {
            table.AddRow(
                new Text(FormatMethodLabel(row.Method)),
                new Text(FormatProviderLabel(row.ProviderName)),
                CreateChangeCell(row.MeanDeltaPercent),
                CreateChangeCell(row.AllocatedDeltaPercent),
                CreateStatusCell(row.Status));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine(string.Create(
            CultureInfo.InvariantCulture,
            $"[grey]Latency warnings require non-noisy >= {comparisonArtifact.WarningThresholdPercent:0.#}% regression; allocation warnings are never suppressed by timing noise. Outcome: {comparisonArtifact.Outcome}.[/]"));
    }

    internal static bool AreBenchmarkProfilesCompatible(string? baselineProfile, string? candidateProfile) =>
        string.Equals(
            NormalizeBenchmarkProfile(baselineProfile),
            NormalizeBenchmarkProfile(candidateProfile),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeBenchmarkProfile(string? profile) =>
        string.IsNullOrWhiteSpace(profile) ? "default" : profile.Trim();

    private BenchmarkRunnerEvidence CaptureRunnerEvidence(
        TestRunSummaryRepositoryState start,
        TestRunSummaryRepositoryState benchmarkTargetStart)
    {
        var end = TestRunSummaryReporter.CaptureRepositoryState(settings.RepositoryRoot);
        var benchmarkTargetEnd = settings.UsesExternalBenchmarkTarget
            ? TestRunSummaryReporter.CaptureRepositoryState(settings.BenchmarkTargetRepositoryRoot)
            : end;
        var assemblies = TestRunSummaryReporter.CaptureRunnerAssemblies();
        var benchmarkAssembly = BenchmarkEvidenceReporter.CaptureAssemblyEvidence(settings.BenchmarkAssemblyPath);
        return BenchmarkEvidenceReporter.EvaluateRunnerEvidence(
            start,
            end,
            assemblies.EntryAssembly,
            assemblies.DevToolsAssembly,
            benchmarkAssembly,
            benchmarkTargetStart,
            benchmarkTargetEnd);
    }

    private BenchmarkArtifactPaths CreateArtifactPaths(
        BenchmarkInvocation invocation,
        IReadOnlyList<BenchmarkCommandRecord> commands,
        SummaryResult? summary)
    {
        var candidates = new List<(string Kind, string Path)>();
        candidates.AddRange(commands.Select(static command => ($"{command.Stage}-log", command.LogPath)));
        if (summary is not null)
        {
            candidates.Add(("summary-json", summary.JsonPath));
            candidates.AddRange(summary.CsvPaths.Select(static path => ("benchmarkdotnet-csv", path)));
            candidates.AddRange(summary.MarkdownPaths.Select(static path => ("benchmarkdotnet-markdown", path)));
            candidates.AddRange(summary.TelemetryPaths.Select(static path => ("telemetry-json", path)));
        }

        var files = candidates
            .Where(static candidate => File.Exists(candidate.Path))
            .GroupBy(static candidate => Path.GetFullPath(candidate.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => BenchmarkEvidenceReporter.CreateArtifactReference(
                settings.RepositoryRoot,
                group.First().Kind,
                group.Key))
            .OrderBy(static artifact => artifact.RepositoryRelativePath, StringComparer.Ordinal)
            .ToArray();
        return new BenchmarkArtifactPaths(
            invocation.HistoryJsonPath,
            invocation.ComparisonJsonPath,
            files);
    }

    private static IReadOnlyList<string> SanitizeArguments(
        IReadOnlyList<string> arguments,
        out bool redacted)
    {
        var safe = new string[arguments.Count];
        redacted = false;
        for (var index = 0; index < arguments.Count; index++)
        {
            safe[index] = TestRunSummaryReporter.SanitizeFailureMessage(arguments[index]);
            if (!string.Equals(safe[index], arguments[index], StringComparison.Ordinal))
                redacted = true;
        }
        return safe;
    }

    internal static string? ResolveSelectedCategory(
        bool phase2Watch,
        bool phase3QueryHotPath,
        bool phase10KeyFoundation,
        bool phase11CacheInvalidation,
        bool phase12CacheMemory,
        bool v09QueryBackend,
        bool v09MemoryRead,
        bool allocationRegression,
        bool allocationStages)
    {
        var selectedCategories = new[]
        {
            (Selected: phase2Watch, Category: Phase2WatchCategory),
            (Selected: phase3QueryHotPath, Category: Phase3QueryHotPathCategory),
            (Selected: phase10KeyFoundation, Category: Phase10KeyFoundationCategory),
            (Selected: phase11CacheInvalidation, Category: Phase11CacheInvalidationCategory),
            (Selected: phase12CacheMemory, Category: Phase12CacheMemoryCategory),
            (Selected: v09QueryBackend, Category: V09QueryBackendCategory),
            (Selected: v09MemoryRead, Category: V09MemoryReadCategory),
            (Selected: allocationRegression, Category: AllocationRegressionCategory),
            (Selected: allocationStages, Category: AllocationStagesCategory)
        }
        .Where(static selection => selection.Selected)
        .Select(static selection => selection.Category)
        .ToArray();

        if (selectedCategories.Length > 1)
        {
            throw new InvalidOperationException(
                "Benchmark category options '--phase2-watch', '--phase3-query-hotpath', '--phase10-key-foundation', '--phase11-cache-invalidation', '--phase12-cache-memory', '--v09-query-backend', '--v09-memory-read', '--allocation-regression', and '--allocation-stages' cannot be combined.");
        }

        return selectedCategories.SingleOrDefault();
    }

    internal static string? GetTrackingGroup(string? method)
        => method switch
        {
            "Provider initialization" => Phase2WatchCategory,
            "Startup primary-key fetch" => Phase2WatchCategory,
            "Warm primary-key fetch" => Phase2WatchCategory,
            "Repeated non-PK equality fetch" => Phase3QueryHotPathCategory,
            "Repeated IN predicate fetch" => Phase3QueryHotPathCategory,
            "Repeated scalar Any" => Phase3QueryHotPathCategory,
            "Warm generated static Get" => Phase10KeyFoundationCategory,
            "Warm relation traversal" => Phase10KeyFoundationCategory,
            "Scalar row-cache add/get/remove" => Phase10KeyFoundationCategory,
            "Invalidate one employee row" => Phase11CacheInvalidationCategory,
            "Invalidate many employee rows" => Phase11CacheInvalidationCategory,
            "Invalidate employee table" => Phase11CacheInvalidationCategory,
            "Invalidate database" => Phase11CacheInvalidationCategory,
            "Warm PK with cache estimate" => Phase12CacheMemoryCategory,
            "Warm relation with cache estimate" => Phase12CacheMemoryCategory,
            "Large relation index preload" => Phase12CacheMemoryCategory,
            "Composite dynamic key workload" => Phase12CacheMemoryCategory,
            "High-cardinality strings baseline" => Phase12CacheMemoryCategory,
            "High-cardinality strings bounded pool" => Phase12CacheMemoryCategory,
            "Low-cardinality strings baseline" => Phase12CacheMemoryCategory,
            "Low-cardinality strings bounded pool" => Phase12CacheMemoryCategory,
            "Expression parse/structural template" => V09QueryBackendCategory,
            "Expression parse/template/initial bind" => V09QueryBackendCategory,
            "Template freeze/validation" => V09QueryBackendCategory,
            "Invocation bind scalar/local sequence" => V09QueryBackendCategory,
            "SQL request/capability preparation" => V09QueryBackendCategory,
            "SQL adapter scalar Any" => V09QueryBackendCategory,
            "Canonical provider-row decoding" => AllocationStagesCategory,
            "Provider-row model materialization" => AllocationStagesCategory,
            "Mutation state-change capture" => AllocationStagesCategory,
            "Mutation execution preflight" => AllocationStagesCategory,
            "Memory database construction" => V09MemoryReadCategory,
            "Memory construct and seed" => V09MemoryReadCategory,
            "Memory primary-key hit" => V09MemoryReadCategory,
            "Memory primary-key miss" => V09MemoryReadCategory,
            "Memory scalar scan" => V09MemoryReadCategory,
            "Memory filter order page" => V09MemoryReadCategory,
            "Memory repeated entity identity" => V09MemoryReadCategory,
            "Memory direct-Guid equality count" => V09MemoryReadCategory,
            "Memory typed-ID equality count" => V09MemoryReadCategory,
            _ => null
        };

    internal static string? GetTrackingGroup(string? method, string? selectedCategory)
    {
        if (string.Equals(selectedCategory, AllocationRegressionCategory, StringComparison.Ordinal) &&
            method is "Provider initialization" or
                "Startup primary-key fetch" or
                "CRUD workflow small" or
                "CRUD workflow batch" or
                "Update employees" or
                "Cold primary-key fetch" or
                "Warm primary-key fetch" or
                "Cold relation traversal" or
                "Warm relation traversal")
        {
            return AllocationRegressionCategory;
        }

        return GetTrackingGroup(method);
    }

    internal static IReadOnlyList<string> GetBenchmarkCategoryArguments(string? selectedCategory) =>
        string.Equals(selectedCategory, AllocationRegressionCategory, StringComparison.Ordinal)
            ? ["--anyCategories", "stable", MacroReadWriteCategory, MacroBulkCategory]
            : selectedCategory is null
                ? []
                : ["--anyCategories", selectedCategory];

    internal static bool ShouldFailRun(
        BenchmarkHistoryArtifact history,
        BenchmarkComparisonArtifact? comparison,
        bool releaseEvidenceIntent) =>
        history.OverallExitCode != 0 ||
        comparison is not null && comparison.OverallExitCode != 0 ||
        releaseEvidenceIntent && !history.ValidForEvidence;

    internal static string GetScenarioCategory(string method)
        => method switch
        {
            "Provider initialization" => "startup",
            "Startup primary-key fetch" => "startup",
            "Cold primary-key fetch" => "read-hotpath",
            "Warm primary-key fetch" => "read-hotpath",
            "Warm generated static Get" => "read-hotpath",
            "Repeated non-PK equality fetch" => "read-hotpath",
            "Repeated IN predicate fetch" => "read-hotpath",
            "Repeated scalar Any" => "read-hotpath",
            "Cold relation traversal" => "relation-traversal",
            "Warm relation traversal" => "relation-traversal",
            "Scalar row-cache add/get/remove" => "cache-hotpath",
            "Warm PK with cache estimate" => "cache-memory",
            "Warm relation with cache estimate" => "cache-memory",
            "Large relation index preload" => "cache-memory",
            "Composite dynamic key workload" => "cache-memory",
            "High-cardinality strings baseline" => "cache-memory",
            "High-cardinality strings bounded pool" => "cache-memory",
            "Low-cardinality strings baseline" => "cache-memory",
            "Low-cardinality strings bounded pool" => "cache-memory",
            "Expression parse/structural template" => "query-planning",
            "Expression parse/template/initial bind" => "query-planning",
            "Template freeze/validation" => "query-planning",
            "Invocation bind scalar/local sequence" => "query-binding",
            "SQL request/capability preparation" => "sql-adapter",
            "SQL adapter scalar Any" => "sql-adapter",
            "Canonical provider-row decoding" => "row-decoding",
            "Provider-row model materialization" => "row-materialization",
            "Mutation state-change capture" => "mutation-capture",
            "Mutation execution preflight" => "mutation-preflight",
            "Memory database construction" => "memory-startup",
            "Memory construct and seed" => "memory-seed",
            "Memory primary-key hit" => "memory-primary-key",
            "Memory primary-key miss" => "memory-primary-key",
            "Memory scalar scan" => "memory-query",
            "Memory filter order page" => "memory-query",
            "Memory repeated entity identity" => "memory-identity",
            "Memory direct-Guid equality count" => "memory-conversion",
            "Memory typed-ID equality count" => "memory-conversion",
            "Invalidate one employee row" => "cache-invalidation",
            "Invalidate many employee rows" => "cache-invalidation",
            "Invalidate employee table" => "cache-invalidation",
            "Invalidate database" => "cache-invalidation",
            "Insert employees" => "mutation",
            "Update employees" => "mutation",
            "Delete employees" => "mutation",
            "CRUD workflow small" => MacroReadWriteCategory,
            "CRUD workflow" => MacroBulkCategory,
            "CRUD workflow batch" => MacroBulkCategory,
            _ => "other"
        };

    private static IRenderable CreateMeanCell(MergedBenchmarkSummaryRow row, double? fastestMean, double? slowestMean)
    {
        if (!row.MeanMicroseconds.HasValue)
            return new Text(row.Mean);

        if (fastestMean.HasValue && AreClose(row.MeanMicroseconds.Value, fastestMean.Value))
            return CreateMarkupCell(row.Mean, "green");

        if (slowestMean.HasValue && AreClose(row.MeanMicroseconds.Value, slowestMean.Value))
            return CreateMarkupCell(row.Mean, "red");

        return new Text(row.Mean);
    }

    private static IRenderable CreateErrorCell(MergedBenchmarkSummaryRow row)
    {
        if (!row.MeanMicroseconds.HasValue || !row.ErrorMicroseconds.HasValue || row.MeanMicroseconds.Value <= 0)
            return new Text(row.Error);

        var relativeError = GetRelativeError(row.MeanMicroseconds, row.ErrorMicroseconds);
        return relativeError switch
        {
            >= 0.20 => CreateMarkupCell(row.Error, "red"),
            >= 0.10 => CreateMarkupCell(row.Error, "yellow"),
            _ => new Text(row.Error)
        };
    }

    private static IRenderable CreateNoiseCell(MergedBenchmarkSummaryRow row)
    {
        var relativeError = GetRelativeError(row.MeanMicroseconds, row.ErrorMicroseconds);
        if (!relativeError.HasValue)
            return new Text("-");

        var noise = string.Create(
            CultureInfo.InvariantCulture,
            $"{relativeError.Value * 100d:0.0}%");

        return relativeError.Value switch
        {
            >= 0.20 => CreateMarkupCell(noise, "red"),
            >= 0.10 => CreateMarkupCell(noise, "yellow"),
            _ => new Text(noise)
        };
    }

    private static IRenderable CreateChangeCell(double? deltaPercent)
    {
        if (!deltaPercent.HasValue)
            return new Text("-");

        var text = string.Create(CultureInfo.InvariantCulture, $"{deltaPercent.Value:+0.0;-0.0;0.0}%");
        return deltaPercent.Value switch
        {
            >= 10d => CreateMarkupCell(text, "red"),
            <= -10d => CreateMarkupCell(text, "green"),
            _ => new Text(text)
        };
    }

    private static IRenderable CreateStatusCell(string status)
        => status switch
        {
            "warning" => CreateMarkupCell("warning", "red"),
            "noisy" => CreateMarkupCell("noisy", "yellow"),
            "improved" => CreateMarkupCell("improved", "green"),
            "missing-baseline" => CreateMarkupCell("missing baseline", "grey"),
            "missing-candidate" => CreateMarkupCell("missing candidate", "grey"),
            "profile-mismatch" => CreateMarkupCell("profile", "grey"),
            "scope-mismatch" => CreateMarkupCell("scope", "grey"),
            "invalid" => CreateMarkupCell("invalid", "red"),
            _ => new Text(status)
        };

    private static Markup CreateMarkupCell(string value, string style) =>
        new($"[{style}]{Markup.Escape(value)}[/]");

    private static bool AreClose(double left, double right) =>
        Math.Abs(left - right) < 0.0001d;

    private static double? GetRelativeError(double? meanMicroseconds, double? errorMicroseconds)
    {
        if (!meanMicroseconds.HasValue || !errorMicroseconds.HasValue || meanMicroseconds.Value <= 0)
            return null;

        return errorMicroseconds.Value / meanMicroseconds.Value;
    }

    private static string FormatQueries(BenchmarkTelemetryDeltaArtifact? artifact)
        => artifact is null
            ? "-"
            : $"{FormatMetric(artifact.EntityQueriesPerOperation)}/{FormatMetric(artifact.ScalarQueriesPerOperation)}";

    private static string FormatTransactions(BenchmarkTelemetryDeltaArtifact artifact)
        => $"{FormatMetric(artifact.TransactionStartsPerOperation)}/{FormatMetric(artifact.TransactionCommitsPerOperation)}/{FormatMetric(artifact.TransactionRollbacksPerOperation)}";

    private static string FormatMutations(BenchmarkTelemetryDeltaArtifact artifact)
        => $"{FormatMetric(artifact.MutationInsertsPerOperation)}/{FormatMetric(artifact.MutationUpdatesPerOperation)}/{FormatMetric(artifact.MutationDeletesPerOperation)} rows {FormatMetric(artifact.MutationAffectedRowsPerOperation)}";

    private static string FormatMethodLabel(string method)
        => method switch
        {
            "Provider initialization" => "Provider init",
            "Startup primary-key fetch" => "Startup PK",
            "CRUD workflow small" => "CRUD small",
            "CRUD workflow" => "CRUD flow",
            "CRUD workflow batch" => "CRUD batch",
            "Insert employees" => "Insert",
            "Update employees" => "Update",
            "Warm relation traversal" => "Warm rel",
            "Cold relation traversal" => "Cold rel",
            "Warm primary-key fetch" => "Warm PK",
            "Cold primary-key fetch" => "Cold PK",
            "Invalidate one employee row" => "Inv row",
            "Invalidate many employee rows" => "Inv rows",
            "Invalidate employee table" => "Inv table",
            "Invalidate database" => "Inv DB",
            _ => method
        };

    private static string FormatProviderLabel(string providerName)
        => providerName switch
        {
            "sqlite-memory" => "memory",
            "sqlite-file" => "file",
            _ => providerName
        };

    private static string FormatRowMetrics(BenchmarkTelemetryDeltaArtifact? artifact)
        => artifact is null
            ? "-"
            : $"{FormatMetric(artifact.RowCacheHitsPerOperation)}/{FormatMetric(artifact.RowCacheMissesPerOperation)}/{FormatMetric(artifact.RowCacheStoresPerOperation)}";

    private static string FormatRelations(BenchmarkTelemetryDeltaArtifact? artifact)
        => artifact is null
            ? "-"
            : $"{FormatMetric(artifact.RelationHitsPerOperation)}/{FormatMetric(artifact.RelationLoadsPerOperation)}";

    private static string FormatTelemetry(BenchmarkTelemetryDeltaArtifact? artifact)
    {
        if (artifact is null)
            return "-";

        var parts = new List<string>();

        if (HasSignal(artifact.EntityQueriesPerOperation, artifact.ScalarQueriesPerOperation))
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Q {FormatQueries(artifact)}"));

        if (HasSignal(
            artifact.TransactionStartsPerOperation,
            artifact.TransactionCommitsPerOperation,
            artifact.TransactionRollbacksPerOperation))
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Tx {FormatTransactions(artifact)}"));
        }

        if (HasSignal(
            artifact.MutationInsertsPerOperation,
            artifact.MutationUpdatesPerOperation,
            artifact.MutationDeletesPerOperation,
            artifact.MutationAffectedRowsPerOperation))
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Mut {FormatMutations(artifact)}"));
        }

        if (HasSignal(
            artifact.RowCacheHitsPerOperation,
            artifact.RowCacheMissesPerOperation,
            artifact.RowCacheStoresPerOperation))
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Row {FormatRowMetrics(artifact)}"));
        }

        if (HasSignal(artifact.DatabaseRowsPerOperation))
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"DB {FormatMetric(artifact.DatabaseRowsPerOperation)}"));

        if (HasSignal(artifact.MaterializationsPerOperation))
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Mat {FormatMetric(artifact.MaterializationsPerOperation)}"));

        if (HasSignal(artifact.RelationHitsPerOperation, artifact.RelationLoadsPerOperation))
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Rel {FormatRelations(artifact)}"));

        if (HasSignal(
            artifact.CacheInvalidationOperationsPerOperation,
            artifact.CacheInvalidationRowsRemovedPerOperation,
            artifact.CacheInvalidationTablesClearedPerOperation,
            artifact.CacheInvalidationApproximateWorkPerOperation,
            artifact.CacheInvalidationPreciseOperationsPerOperation,
            artifact.CacheInvalidationConservativeFallbackOperationsPerOperation))
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Inv {FormatMetric(artifact.CacheInvalidationOperationsPerOperation)} " +
                $"rows/tables {FormatMetric(artifact.CacheInvalidationRowsRemovedPerOperation)}/{FormatMetric(artifact.CacheInvalidationTablesClearedPerOperation)} " +
                $"work {FormatMetric(artifact.CacheInvalidationApproximateWorkPerOperation)} " +
                $"path {FormatMetric(artifact.CacheInvalidationPreciseOperationsPerOperation)}/{FormatMetric(artifact.CacheInvalidationConservativeFallbackOperationsPerOperation)}"));
        }

        if (HasMemorySignal(artifact))
            parts.Add(FormatMemoryTelemetry(artifact));

        return parts.Count == 0 ? "-" : string.Join("  ", parts);
    }

    private static bool HasMemorySignal(BenchmarkTelemetryDeltaArtifact artifact)
        => HasSignal(
            artifact.MemoryDatabasesConstructedPerOperation,
            artifact.MemoryRowsSeededPerOperation,
            artifact.MemoryPrimaryKeyRequestsPerOperation,
            artifact.MemoryPrimaryKeyProbesPerOperation,
            artifact.MemoryScanRowsVisitedPerOperation,
            artifact.MemoryPredicateEvaluationsPerOperation,
            artifact.MemoryPredicateRejectionsPerOperation,
            artifact.MemoryCacheLookupsPerOperation,
            artifact.MemoryCacheHitsPerOperation,
            artifact.MemoryCacheMissesPerOperation,
            artifact.MemoryMaterializationsPerOperation,
            artifact.MemoryCacheInsertionsPerOperation);

    private static string FormatMemoryTelemetry(BenchmarkTelemetryDeltaArtifact artifact)
    {
        var metrics = new List<string>();

        if (HasSignal(
            artifact.MemoryDatabasesConstructedPerOperation,
            artifact.MemoryRowsSeededPerOperation))
        {
            metrics.Add(
                $"db/seed {FormatMetric(artifact.MemoryDatabasesConstructedPerOperation)}/{FormatMetric(artifact.MemoryRowsSeededPerOperation)}");
        }

        if (HasSignal(
            artifact.MemoryPrimaryKeyRequestsPerOperation,
            artifact.MemoryPrimaryKeyProbesPerOperation))
        {
            metrics.Add(
                $"pk {FormatMetric(artifact.MemoryPrimaryKeyRequestsPerOperation)}/{FormatMetric(artifact.MemoryPrimaryKeyProbesPerOperation)}");
        }

        if (HasSignal(
            artifact.MemoryScanRowsVisitedPerOperation,
            artifact.MemoryPredicateEvaluationsPerOperation,
            artifact.MemoryPredicateRejectionsPerOperation))
        {
            metrics.Add(
                $"scan/pred/rej {FormatMetric(artifact.MemoryScanRowsVisitedPerOperation)}/{FormatMetric(artifact.MemoryPredicateEvaluationsPerOperation)}/{FormatMetric(artifact.MemoryPredicateRejectionsPerOperation)}");
        }

        if (HasSignal(
            artifact.MemoryCacheLookupsPerOperation,
            artifact.MemoryCacheHitsPerOperation,
            artifact.MemoryCacheMissesPerOperation))
        {
            metrics.Add(
                $"cache {FormatMetric(artifact.MemoryCacheLookupsPerOperation)}/{FormatMetric(artifact.MemoryCacheHitsPerOperation)}/{FormatMetric(artifact.MemoryCacheMissesPerOperation)}");
        }

        if (HasSignal(
            artifact.MemoryMaterializationsPerOperation,
            artifact.MemoryCacheInsertionsPerOperation))
        {
            metrics.Add(
                $"mat/ins {FormatMetric(artifact.MemoryMaterializationsPerOperation)}/{FormatMetric(artifact.MemoryCacheInsertionsPerOperation)}");
        }

        return $"Mem {string.Join(" ", metrics)}";
    }

    private static string FormatMetric(double? value)
    {
        if (!value.HasValue)
            return "-";

        var absoluteValue = Math.Abs(value.Value);
        if (absoluteValue < 0.0001d)
            return "0";

        if (absoluteValue < 0.01d)
            return "<0.01";

        var roundedWhole = Math.Round(value.Value);
        if (absoluteValue >= 0.95d && Math.Abs(value.Value - roundedWhole) < 0.05d)
            return roundedWhole.ToString("0", CultureInfo.InvariantCulture);

        if (absoluteValue < 0.1d)
            return value.Value.ToString("0.00", CultureInfo.InvariantCulture);

        return value.Value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private static bool HasSignal(params double?[] values)
        => values.Any(static value => value.HasValue && Math.Abs(value.Value) >= 0.0001d);

    internal static double? TryParseDurationInMicroseconds(string value)
    {
        var normalized = NormalizeCell(value);
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        if (!double.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var magnitude))
            return null;

        var unit = parts[1].Replace('\u00B5', '\u03BC');
        return unit switch
        {
            "ns" => magnitude / 1000d,
            "μs" => magnitude,
            "us" => magnitude,
            "ms" => magnitude * 1000d,
            "s" => magnitude * 1_000_000d,
            _ => null
        };
    }

    private static string FormatInvalidMeasurements(IReadOnlyList<MergedBenchmarkSummaryRow> rows)
    {
        var samples = rows
            .Take(5)
            .Select(static row => $"{row.Method}/{row.ProviderName} mean='{row.Mean}' error='{row.Error}'")
            .ToArray();

        if (samples.Length == 0)
            return "none";

        var suffix = rows.Count > samples.Length
            ? string.Create(CultureInfo.InvariantCulture, $", +{rows.Count - samples.Length} more")
            : string.Empty;

        return string.Join("; ", samples) + suffix;
    }

    private static double? TryParseAllocatedBytes(string value)
    {
        var normalized = NormalizeCell(value);
        if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "-", StringComparison.Ordinal))
            return null;

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        if (!double.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var magnitude))
            return null;

        return parts[1] switch
        {
            "B" => magnitude,
            "KB" => magnitude * 1024d,
            "MB" => magnitude * 1024d * 1024d,
            "GB" => magnitude * 1024d * 1024d * 1024d,
            _ => null
        };
    }

    private sealed record BenchmarkSummaryRow(
        string Method,
        string Job,
        string Runtime,
        string Jit,
        string Platform,
        string Toolchain,
        string ProviderName,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? MedianMicroseconds,
        double? StdDevMicroseconds,
        double? MinMicroseconds,
        double? MaxMicroseconds,
        double? AllocatedBytes);

    private sealed record MergedBenchmarkSummaryRow(
        string Method,
        string Job,
        string Runtime,
        string Jit,
        string Platform,
        string Toolchain,
        string ProviderName,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? MedianMicroseconds,
        double? StdDevMicroseconds,
        double? MinMicroseconds,
        double? MaxMicroseconds,
        double? AllocatedBytes,
        BenchmarkTelemetryDeltaArtifact? TelemetryDelta)
    {
        public MergedBenchmarkSummaryRow(BenchmarkSummaryRow row, BenchmarkTelemetryDeltaArtifact? telemetryDelta)
            : this(
                row.Method,
                row.Job,
                row.Runtime,
                row.Jit,
                row.Platform,
                row.Toolchain,
                row.ProviderName,
                row.Mean,
                row.Error,
                row.Allocated,
                row.MeanMicroseconds,
                row.ErrorMicroseconds,
                row.MedianMicroseconds,
                row.StdDevMicroseconds,
                row.MinMicroseconds,
                row.MaxMicroseconds,
                row.AllocatedBytes,
                telemetryDelta)
        {
        }
    }

    private sealed record BenchmarkSummaryArtifact(
        string RunId,
        DateTime GeneratedAtUtc,
        BenchmarkRunMetadata Metadata,
        IReadOnlyList<BenchmarkSummaryArtifactRow> Rows);

    private sealed record BenchmarkSummaryArtifactRow(
        string Method,
        string ProviderName,
        string Category,
        string Job,
        string Runtime,
        string Jit,
        string Platform,
        string Toolchain,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? MedianMicroseconds,
        double? StdDevMicroseconds,
        double? MinMicroseconds,
        double? MaxMicroseconds,
        double? AllocatedBytes,
        double? NoisePercent,
        double? UncertaintyPercent,
        double? StdDevPercent,
        int? OperationsPerInvoke,
        string? TrackingGroup,
        BenchmarkTelemetryDeltaArtifact? TelemetryDelta);

    private sealed record SummaryResult(
        string JsonPath,
        BenchmarkSummaryArtifact Artifact,
        IReadOnlyList<string> CsvPaths,
        IReadOnlyList<string> MarkdownPaths,
        IReadOnlyList<string> TelemetryPaths,
        IReadOnlyList<BenchmarkHistoryArtifactRow> HistoryRows);

    private sealed record RecordedCommandResult(
        ExternalCommandResult Result,
        BenchmarkCommandRecord Command);

    private sealed record GitContext(string? Branch, string? Commit);

    private static string QuoteArgument(string argument) =>
        argument.Contains(' ', StringComparison.Ordinal)
            ? $"\"{argument}\""
            : argument;
}
