using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DataLinq.Benchmark.CLI;

internal sealed class BenchmarkHarnessRunner
{
    private static readonly string[] WarningPatterns =
    [
        "The minimum observed iteration time is very small",
        "MultimodalDistribution",
        "ZeroMeasurement",
        "EnvironmentVariable",
        "NoWorkloadResult"
    ];
    private const string BenchmarkRunIdEnvironmentVariable = "DATALINQ_BENCHMARK_RUN_ID";
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
        string? historyJsonPath,
        string? baselinePath,
        string? comparisonJsonPath,
        double warningThresholdPercent,
        IReadOnlyList<string> additionalArgs)
    {
        settings.EnsureDirectories();

        if (!noBuild)
            RestoreAndBuild(verbose);
        else
            Console.WriteLine("Skipping restore/build.");

        var arguments = new List<string>
        {
            settings.BenchmarkAssemblyPath,
            "--artifacts",
            settings.ArtifactsRoot,
            "--filter",
            filter,
            "--join",
            "--disableLogFile"
        };

        if (string.Equals(profile, "smoke", StringComparison.OrdinalIgnoreCase))
            arguments.AddRange(["--job", "Dry"]);
        else if (!string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The benchmark profile must be either 'default' or 'smoke'.");

        if (keepFiles)
            arguments.Add("--keepFiles");

        arguments.AddRange(additionalArgs);

        var runId = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}");

        var benchmarkEnvironment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [BenchmarkRunIdEnvironmentVariable] = runId,
            [BenchmarkResultsDirectoryEnvironmentVariable] = Path.Combine(settings.ArtifactsRoot, "results")
        };

        Console.WriteLine("Running benchmarks...");
        var result = ExecuteDotnet(arguments, verbose, benchmarkEnvironment);
        var logPath = WriteLog("benchmark-run", result);

        WriteStandardOutput(result, verbose || result.ExitCode != 0);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Benchmark run failed. Full log: {logPath}");

        WriteWarnings(result);
        var summaryResult = WriteSummary(runId, logPath, profile, filter);
        var historyArtifact = CreateHistoryArtifact(summaryResult.Artifact);

        if (!string.IsNullOrWhiteSpace(historyJsonPath))
            WriteJsonArtifact(historyJsonPath, historyArtifact);

        if (!string.IsNullOrWhiteSpace(baselinePath))
            WriteComparison(
                baselinePath,
                historyArtifact,
                comparisonJsonPath,
                warningThresholdPercent);

        WriteArtifacts(logPath, summaryResult.JsonPath, historyJsonPath, comparisonJsonPath);
        return 0;
    }

    private void RestoreAndBuild(bool verbose)
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

        var restoreResult = ExecuteDotnet(restoreArguments, verbose);
        WriteStandardOutput(restoreResult, verbose || restoreResult.ExitCode != 0);

        if (restoreResult.ExitCode != 0)
            throw new InvalidOperationException("Benchmark harness restore failed.");

        Console.WriteLine("Building benchmark harness...");
        var buildArguments = new[]
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

        var buildResult = ExecuteDotnet(buildArguments, verbose);
        WriteStandardOutput(buildResult, verbose || buildResult.ExitCode != 0);

        if (buildResult.ExitCode != 0)
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

        return ExternalProcessRunner.Execute(
            "dotnet",
            arguments,
            settings.RepositoryRoot,
            environmentVariables);
    }

    private string WriteLog(string prefix, ExternalCommandResult result)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var logPath = Path.Combine(settings.ArtifactsRoot, $"{prefix}-{timestamp}.log");
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

    private void WriteWarnings(ExternalCommandResult result)
    {
        var outputLines = string.Concat(result.StandardOutput, Environment.NewLine, result.StandardError)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        var warnings = outputLines
            .Where(line => WarningPatterns.Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (warnings.Length == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Benchmark warnings:");
        foreach (var warning in warnings)
            Console.WriteLine($"  {warning}");
    }

    private SummaryResult WriteSummary(string runId, string logPath, string profile, string filter)
    {
        var resultsDirectory = Path.Combine(settings.ArtifactsRoot, "results");
        if (!Directory.Exists(resultsDirectory))
            throw new InvalidOperationException($"Benchmark run did not produce a results directory. Full log: {logPath}");

        var summaryPath = Directory.GetFiles(resultsDirectory, "*-report.csv")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();

        if (summaryPath is null)
            throw new InvalidOperationException($"Benchmark run did not produce a CSV summary. Full log: {logPath}");

        var rows = FilterRowsForProfile(ParseSummaryRows(summaryPath), profile);
        var telemetryDeltas = LoadTelemetryDeltas(resultsDirectory, runId);

        if (rows.Length == 0)
            throw new InvalidOperationException($"Benchmark summary '{summaryPath}' did not contain any parseable rows. Full log: {logPath}");

        var mergedRows = BuildMergedSummaryRows(rows, telemetryDeltas);
        var measuredRows = mergedRows
            .Where(static row => row.MeanMicroseconds.HasValue)
            .ToArray();

        if (measuredRows.Length == 0)
            throw new InvalidOperationException($"Benchmark summary '{summaryPath}' only contains invalid measurements. Full log: {logPath}");

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
        AnsiConsole.MarkupLine("[grey]Telemetry deltas are per operation: Q=entity/scalar, Tx=starts/commits/rollbacks, Mut=inserts/updates/deletes with affected rows, Row=hits/misses/stores, Rel=hits/loads.[/]");
        var artifact = CreateSummaryArtifact(runId, profile, filter, mergedRows);
        var jsonPath = WriteSummaryArtifact(resultsDirectory, artifact);
        return new SummaryResult(jsonPath, artifact);
    }

    private void WriteArtifacts(string logPath, string? mergedSummaryPath, string? historyJsonPath, string? comparisonJsonPath)
    {
        var resultsDirectory = Path.Combine(settings.ArtifactsRoot, "results");
        var markdownPath = Directory.Exists(resultsDirectory)
            ? Directory.GetFiles(resultsDirectory, "*-report-github.md")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault()
            : null;

        var csvPath = Directory.Exists(resultsDirectory)
            ? Directory.GetFiles(resultsDirectory, "*-report.csv")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault()
            : null;

        Console.WriteLine();
        Console.WriteLine("Artifacts:");
        Console.WriteLine($"  Log: {logPath}");

        if (markdownPath is not null)
            Console.WriteLine($"  Markdown: {markdownPath}");

        if (csvPath is not null)
            Console.WriteLine($"  CSV: {csvPath}");

        if (mergedSummaryPath is not null)
            Console.WriteLine($"  Summary JSON: {mergedSummaryPath}");

        if (!string.IsNullOrWhiteSpace(historyJsonPath))
            Console.WriteLine($"  History JSON: {historyJsonPath}");

        if (!string.IsNullOrWhiteSpace(comparisonJsonPath))
            Console.WriteLine($"  Comparison JSON: {comparisonJsonPath}");
    }

    private static BenchmarkSummaryRow[] ParseSummaryRows(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
            return [];

        var delimiter = DetectCsvDelimiter(lines[0]);
        var headers = SplitCsvLine(lines[0], delimiter);
        var methodIndex = Array.IndexOf(headers, "Method");
        var jobIndex = Array.IndexOf(headers, "Job");
        var providerIndex = Array.IndexOf(headers, "ProviderName");
        var meanIndex = Array.IndexOf(headers, "Mean");
        var errorIndex = Array.IndexOf(headers, "Error");
        var allocatedIndex = Array.IndexOf(headers, "Allocated");

        if (methodIndex < 0 || providerIndex < 0 || meanIndex < 0 || errorIndex < 0)
            return [];

        var rows = new List<BenchmarkSummaryRow>();
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var columns = SplitCsvLine(line, delimiter);
            if (columns.Length <= errorIndex)
                continue;

            rows.Add(new BenchmarkSummaryRow(
                Method: NormalizeCell(columns[methodIndex]),
                Job: jobIndex >= 0 && columns.Length > jobIndex
                    ? NormalizeCell(columns[jobIndex])
                    : string.Empty,
                ProviderName: NormalizeCell(columns[providerIndex]),
                Mean: NormalizeCell(columns[meanIndex]),
                Error: NormalizeCell(columns[errorIndex]),
                Allocated: allocatedIndex >= 0 && columns.Length > allocatedIndex
                    ? NormalizeCell(columns[allocatedIndex])
                    : "-",
                MeanMicroseconds: TryParseDurationInMicroseconds(columns[meanIndex]),
                ErrorMicroseconds: TryParseDurationInMicroseconds(columns[errorIndex]),
                AllocatedBytes: allocatedIndex >= 0 && columns.Length > allocatedIndex
                    ? TryParseAllocatedBytes(columns[allocatedIndex])
                    : null));
        }

        return rows.ToArray();
    }

    private static BenchmarkSummaryRow[] FilterRowsForProfile(BenchmarkSummaryRow[] rows, string profile)
    {
        if (string.Equals(profile, "smoke", StringComparison.OrdinalIgnoreCase))
        {
            var dryRows = rows
                .Where(static row => string.Equals(row.Job, "Dry", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var measuredDryRows = dryRows
                .Where(static row => row.MeanMicroseconds.HasValue)
                .ToArray();

            if (measuredDryRows.Length > 0)
                return measuredDryRows;

            var measuredNonDryRows = rows
                .Where(static row => !string.Equals(row.Job, "Dry", StringComparison.OrdinalIgnoreCase) && row.MeanMicroseconds.HasValue)
                .ToArray();

            return measuredNonDryRows.Length > 0 ? measuredNonDryRows : rows;
        }

        var nonDryRows = rows
            .Where(static row => !string.Equals(row.Job, "Dry", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return nonDryRows.Length > 0 ? nonDryRows : rows;
    }

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

        columns.Add(current.ToString());
        return columns.ToArray();
    }

    private static string NormalizeCell(string value) =>
        value.Trim().Trim('\'', '"');

    private static Dictionary<(string Method, string ProviderName), BenchmarkTelemetryDeltaArtifact> LoadTelemetryDeltas(string resultsDirectory, string runId)
    {
        var deltas = new Dictionary<(string Method, string ProviderName), BenchmarkTelemetryDeltaArtifact>();

        foreach (var filePath in Directory.GetFiles(resultsDirectory, $"{runId}-*-telemetry.json"))
        {
            var artifact = JsonSerializer.Deserialize<BenchmarkTelemetryDeltaArtifact>(File.ReadAllText(filePath));
            if (artifact is null)
                continue;

            deltas[(artifact.Method, artifact.ProviderName)] = artifact;
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
        IReadOnlyList<MergedBenchmarkSummaryRow> rows)
    {
        return new BenchmarkSummaryArtifact(
            RunId: runId,
            GeneratedAtUtc: DateTime.UtcNow,
            Metadata: CreateRunMetadata(profile, filter),
            Rows: rows.Select(static row => new BenchmarkSummaryArtifactRow(
                Method: row.Method,
                ProviderName: row.ProviderName,
                Mean: row.Mean,
                Error: row.Error,
                Allocated: row.Allocated,
                MeanMicroseconds: row.MeanMicroseconds,
                ErrorMicroseconds: row.ErrorMicroseconds,
                AllocatedBytes: row.AllocatedBytes,
                NoisePercent: GetRelativeError(row.MeanMicroseconds, row.ErrorMicroseconds) is double relativeError ? relativeError * 100d : null,
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

    private static BenchmarkHistoryArtifact CreateHistoryArtifact(BenchmarkSummaryArtifact summaryArtifact)
        => new(
            SchemaVersion: 1,
            RunId: summaryArtifact.RunId,
            GeneratedAtUtc: summaryArtifact.GeneratedAtUtc,
            Metadata: summaryArtifact.Metadata,
            Rows: summaryArtifact.Rows.Select(static row => new BenchmarkHistoryArtifactRow(
                Method: row.Method,
                ProviderName: row.ProviderName,
                MeanMicroseconds: row.MeanMicroseconds,
                ErrorMicroseconds: row.ErrorMicroseconds,
                AllocatedBytes: row.AllocatedBytes,
                NoisePercent: row.NoisePercent,
                TelemetryDelta: row.TelemetryDelta)).ToArray());

    private BenchmarkRunMetadata CreateRunMetadata(string profile, string filter)
    {
        var gitContext = ResolveGitContext();
        return new BenchmarkRunMetadata(
            Repository: Environment.GetEnvironmentVariable("GITHUB_REPOSITORY"),
            Branch: Environment.GetEnvironmentVariable("GITHUB_REF_NAME") ?? gitContext.Branch,
            Commit: Environment.GetEnvironmentVariable("GITHUB_SHA") ?? gitContext.Commit,
            Workflow: Environment.GetEnvironmentVariable("GITHUB_WORKFLOW"),
            RunId: Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
            RunNumber: Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"),
            EventName: Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME"),
            RunnerOs: Environment.GetEnvironmentVariable("RUNNER_OS") ?? RuntimeInformation.OSDescription,
            RunnerArchitecture: Environment.GetEnvironmentVariable("RUNNER_ARCH") ?? RuntimeInformation.ProcessArchitecture.ToString(),
            Profile: profile,
            Filter: filter);
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

    private static void WriteJsonArtifact<T>(string path, T artifact)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(artifact, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private BenchmarkComparisonArtifact WriteComparison(
        string baselinePath,
        BenchmarkHistoryArtifact candidateArtifact,
        string? comparisonJsonPath,
        double warningThresholdPercent)
    {
        var baselineArtifact = JsonSerializer.Deserialize<BenchmarkHistoryArtifact>(File.ReadAllText(baselinePath))
            ?? throw new InvalidOperationException($"Unable to read benchmark baseline artifact '{baselinePath}'.");

        var comparisonArtifact = BuildComparisonArtifact(baselineArtifact, candidateArtifact, warningThresholdPercent);

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
            $"[grey]Warnings only trigger for non-noisy rows at >= {warningThresholdPercent:0.#}% regression. Rows above 20% noise are marked noisy.[/]"));

        if (!string.IsNullOrWhiteSpace(comparisonJsonPath))
            WriteJsonArtifact(comparisonJsonPath, comparisonArtifact);

        return comparisonArtifact;
    }

    private static BenchmarkComparisonArtifact BuildComparisonArtifact(
        BenchmarkHistoryArtifact baselineArtifact,
        BenchmarkHistoryArtifact candidateArtifact,
        double warningThresholdPercent)
    {
        var baselineRows = baselineArtifact.Rows.ToDictionary(
            static row => (row.Method, row.ProviderName),
            static row => row);
        var candidateRows = candidateArtifact.Rows.ToDictionary(
            static row => (row.Method, row.ProviderName),
            static row => row);

        var allKeys = baselineRows.Keys
            .Union(candidateRows.Keys)
            .OrderBy(static key => key.Method, StringComparer.Ordinal)
            .ThenBy(static key => key.ProviderName, StringComparer.Ordinal)
            .ToArray();

        var rows = new List<BenchmarkComparisonArtifactRow>(allKeys.Length);
        var warningCount = 0;

        foreach (var key in allKeys)
        {
            baselineRows.TryGetValue(key, out var baselineRow);
            candidateRows.TryGetValue(key, out var candidateRow);

            var meanDeltaPercent = GetDeltaPercent(baselineRow?.MeanMicroseconds, candidateRow?.MeanMicroseconds);
            var allocatedDeltaPercent = GetDeltaPercent(baselineRow?.AllocatedBytes, candidateRow?.AllocatedBytes);
            var maxNoisePercent = new[] { baselineRow?.NoisePercent, candidateRow?.NoisePercent }
                .Where(static value => value.HasValue)
                .Select(static value => value!.Value)
                .DefaultIfEmpty(0d)
                .Max();

            var status = GetComparisonStatus(
                baselineRow,
                candidateRow,
                meanDeltaPercent,
                allocatedDeltaPercent,
                maxNoisePercent,
                warningThresholdPercent);

            if (status == "warning")
                warningCount++;

            rows.Add(new BenchmarkComparisonArtifactRow(
                Method: key.Method,
                ProviderName: key.ProviderName,
                BaselineMeanMicroseconds: baselineRow?.MeanMicroseconds,
                CandidateMeanMicroseconds: candidateRow?.MeanMicroseconds,
                MeanDeltaPercent: meanDeltaPercent,
                BaselineAllocatedBytes: baselineRow?.AllocatedBytes,
                CandidateAllocatedBytes: candidateRow?.AllocatedBytes,
                AllocatedDeltaPercent: allocatedDeltaPercent,
                MaxNoisePercent: maxNoisePercent,
                Status: status));
        }

        return new BenchmarkComparisonArtifact(
            SchemaVersion: 1,
            GeneratedAtUtc: DateTime.UtcNow,
            WarningThresholdPercent: warningThresholdPercent,
            WarningCount: warningCount,
            Baseline: baselineArtifact.Metadata,
            Candidate: candidateArtifact.Metadata,
            Rows: rows);
    }

    private static string GetComparisonStatus(
        BenchmarkHistoryArtifactRow? baselineRow,
        BenchmarkHistoryArtifactRow? candidateRow,
        double? meanDeltaPercent,
        double? allocatedDeltaPercent,
        double maxNoisePercent,
        double warningThresholdPercent)
    {
        if (baselineRow is null || candidateRow is null)
            return "missing";

        if (maxNoisePercent >= 20d)
            return "noisy";

        if ((meanDeltaPercent ?? 0d) >= warningThresholdPercent || (allocatedDeltaPercent ?? 0d) >= warningThresholdPercent)
            return "warning";

        if ((meanDeltaPercent ?? 0d) <= -warningThresholdPercent || (allocatedDeltaPercent ?? 0d) <= -warningThresholdPercent)
            return "improved";

        return "stable";
    }

    private static double? GetDeltaPercent(double? baselineValue, double? candidateValue)
    {
        if (!baselineValue.HasValue || !candidateValue.HasValue || baselineValue.Value == 0d)
            return null;

        return ((candidateValue.Value - baselineValue.Value) / baselineValue.Value) * 100d;
    }

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
            "missing" => CreateMarkupCell("missing", "grey"),
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
            "Startup primary-key fetch" => "Startup PK",
            "Insert employees" => "Insert",
            "Update employees" => "Update",
            "Warm relation traversal" => "Warm rel",
            "Cold relation traversal" => "Cold rel",
            "Warm primary-key fetch" => "Warm PK",
            "Cold primary-key fetch" => "Cold PK",
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

        return parts.Count == 0 ? "-" : string.Join("  ", parts);
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

    private static double? TryParseDurationInMicroseconds(string value)
    {
        var normalized = NormalizeCell(value);
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;

        if (!double.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var magnitude))
            return null;

        return parts[1] switch
        {
            "ns" => magnitude / 1000d,
            "μs" => magnitude,
            "us" => magnitude,
            "ms" => magnitude * 1000d,
            "s" => magnitude * 1_000_000d,
            _ => null
        };
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
        string ProviderName,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? AllocatedBytes);

    private sealed record MergedBenchmarkSummaryRow(
        string Method,
        string ProviderName,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? AllocatedBytes,
        BenchmarkTelemetryDeltaArtifact? TelemetryDelta)
    {
        public MergedBenchmarkSummaryRow(BenchmarkSummaryRow row, BenchmarkTelemetryDeltaArtifact? telemetryDelta)
            : this(row.Method, row.ProviderName, row.Mean, row.Error, row.Allocated, row.MeanMicroseconds, row.ErrorMicroseconds, row.AllocatedBytes, telemetryDelta)
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
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? AllocatedBytes,
        double? NoisePercent,
        BenchmarkTelemetryDeltaArtifact? TelemetryDelta);

    private sealed record BenchmarkTelemetryDeltaArtifact(
        string Method,
        string ProviderName,
        int OperationsPerInvoke,
        double EntityQueriesPerOperation,
        double ScalarQueriesPerOperation,
        double TransactionStartsPerOperation,
        double TransactionCommitsPerOperation,
        double TransactionRollbacksPerOperation,
        double MutationInsertsPerOperation,
        double MutationUpdatesPerOperation,
        double MutationDeletesPerOperation,
        double MutationAffectedRowsPerOperation,
        double RowCacheHitsPerOperation,
        double RowCacheMissesPerOperation,
        double RowCacheStoresPerOperation,
        double DatabaseRowsPerOperation,
        double MaterializationsPerOperation,
        double RelationHitsPerOperation,
        double RelationLoadsPerOperation);

    private sealed record BenchmarkRunMetadata(
        string? Repository,
        string? Branch,
        string? Commit,
        string? Workflow,
        string? RunId,
        string? RunNumber,
        string? EventName,
        string? RunnerOs,
        string? RunnerArchitecture,
        string Profile,
        string Filter);

    private sealed record BenchmarkHistoryArtifact(
        int SchemaVersion,
        string RunId,
        DateTime GeneratedAtUtc,
        BenchmarkRunMetadata Metadata,
        IReadOnlyList<BenchmarkHistoryArtifactRow> Rows);

    private sealed record BenchmarkHistoryArtifactRow(
        string Method,
        string ProviderName,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? AllocatedBytes,
        double? NoisePercent,
        BenchmarkTelemetryDeltaArtifact? TelemetryDelta);

    private sealed record BenchmarkComparisonArtifact(
        int SchemaVersion,
        DateTime GeneratedAtUtc,
        double WarningThresholdPercent,
        int WarningCount,
        BenchmarkRunMetadata Baseline,
        BenchmarkRunMetadata Candidate,
        IReadOnlyList<BenchmarkComparisonArtifactRow> Rows);

    private sealed record BenchmarkComparisonArtifactRow(
        string Method,
        string ProviderName,
        double? BaselineMeanMicroseconds,
        double? CandidateMeanMicroseconds,
        double? MeanDeltaPercent,
        double? BaselineAllocatedBytes,
        double? CandidateAllocatedBytes,
        double? AllocatedDeltaPercent,
        double MaxNoisePercent,
        string Status);

    private sealed record SummaryResult(string JsonPath, BenchmarkSummaryArtifact Artifact);

    private sealed record GitContext(string? Branch, string? Commit);

    private static string QuoteArgument(string argument) =>
        argument.Contains(' ', StringComparison.Ordinal)
            ? $"\"{argument}\""
            : argument;
}
