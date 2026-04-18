using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
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

    public int Run(string filter, string profile, bool noBuild, bool keepFiles, bool verbose, IReadOnlyList<string> additionalArgs)
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
        var mergedSummaryPath = WriteSummary(runId, logPath);
        WriteArtifacts(logPath, mergedSummaryPath);
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

    private string WriteSummary(string runId, string logPath)
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

        var rows = ParseSummaryRows(summaryPath);
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
        AnsiConsole.MarkupLine("[grey]Telemetry deltas are per operation: Q=entity/scalar, Row=hits/misses/stores, Rel=hits/loads.[/]");
        return WriteMergedSummaryArtifact(resultsDirectory, runId, mergedRows);
    }

    private void WriteArtifacts(string logPath, string? mergedSummaryPath)
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
    }

    private static BenchmarkSummaryRow[] ParseSummaryRows(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
            return [];

        var headers = SplitCsvLine(lines[0]);
        var methodIndex = Array.IndexOf(headers, "Method");
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

            var columns = SplitCsvLine(line);
            if (columns.Length <= errorIndex)
                continue;

            rows.Add(new BenchmarkSummaryRow(
                Method: NormalizeCell(columns[methodIndex]),
                ProviderName: NormalizeCell(columns[providerIndex]),
                Mean: NormalizeCell(columns[meanIndex]),
                Error: NormalizeCell(columns[errorIndex]),
                Allocated: allocatedIndex >= 0 && columns.Length > allocatedIndex
                    ? NormalizeCell(columns[allocatedIndex])
                    : "-",
                MeanMicroseconds: TryParseDurationInMicroseconds(columns[meanIndex]),
                ErrorMicroseconds: TryParseDurationInMicroseconds(columns[errorIndex])));
        }

        return rows.ToArray();
    }

    private static string[] SplitCsvLine(string line) =>
        line.Split(';');

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

    private static string WriteMergedSummaryArtifact(string resultsDirectory, string runId, IReadOnlyList<MergedBenchmarkSummaryRow> rows)
    {
        var payload = new BenchmarkSummaryArtifact(
            RunId: runId,
            GeneratedAtUtc: DateTime.UtcNow,
            Rows: rows.Select(static row => new BenchmarkSummaryArtifactRow(
                Method: row.Method,
                ProviderName: row.ProviderName,
                Mean: row.Mean,
                Error: row.Error,
                Allocated: row.Allocated,
                MeanMicroseconds: row.MeanMicroseconds,
                ErrorMicroseconds: row.ErrorMicroseconds,
                NoisePercent: GetRelativeError(row.MeanMicroseconds, row.ErrorMicroseconds) is double relativeError ? relativeError * 100d : null,
                TelemetryDelta: row.TelemetryDelta)).ToArray());

        var jsonPath = Path.Combine(resultsDirectory, $"{runId}-summary.json");
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(jsonPath, json);
        return jsonPath;
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

    private static string FormatMethodLabel(string method)
        => method switch
        {
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
        => artifact is null
            ? "-"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Q {FormatQueries(artifact)}  Row {FormatRowMetrics(artifact)}  DB {FormatMetric(artifact.DatabaseRowsPerOperation)}  Mat {FormatMetric(artifact.MaterializationsPerOperation)}  Rel {FormatRelations(artifact)}");

    private static string FormatMetric(double? value)
    {
        if (!value.HasValue)
            return "-";

        var roundedWhole = Math.Round(value.Value);
        if (Math.Abs(value.Value - roundedWhole) < 0.05d)
            return roundedWhole.ToString("0", CultureInfo.InvariantCulture);

        return value.Value.ToString("0.0", CultureInfo.InvariantCulture);
    }

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

    private sealed record BenchmarkSummaryRow(
        string Method,
        string ProviderName,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds);

    private sealed record MergedBenchmarkSummaryRow(
        string Method,
        string ProviderName,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        BenchmarkTelemetryDeltaArtifact? TelemetryDelta)
    {
        public MergedBenchmarkSummaryRow(BenchmarkSummaryRow row, BenchmarkTelemetryDeltaArtifact? telemetryDelta)
            : this(row.Method, row.ProviderName, row.Mean, row.Error, row.Allocated, row.MeanMicroseconds, row.ErrorMicroseconds, telemetryDelta)
        {
        }
    }

    private sealed record BenchmarkSummaryArtifact(
        string RunId,
        DateTime GeneratedAtUtc,
        IReadOnlyList<BenchmarkSummaryArtifactRow> Rows);

    private sealed record BenchmarkSummaryArtifactRow(
        string Method,
        string ProviderName,
        string Mean,
        string Error,
        string Allocated,
        double? MeanMicroseconds,
        double? ErrorMicroseconds,
        double? NoisePercent,
        BenchmarkTelemetryDeltaArtifact? TelemetryDelta);

    private sealed record BenchmarkTelemetryDeltaArtifact(
        string Method,
        string ProviderName,
        int OperationsPerInvoke,
        double EntityQueriesPerOperation,
        double ScalarQueriesPerOperation,
        double RowCacheHitsPerOperation,
        double RowCacheMissesPerOperation,
        double RowCacheStoresPerOperation,
        double DatabaseRowsPerOperation,
        double MaterializationsPerOperation,
        double RelationHitsPerOperation,
        double RelationLoadsPerOperation);

    private static string QuoteArgument(string argument) =>
        argument.Contains(' ', StringComparison.Ordinal)
            ? $"\"{argument}\""
            : argument;
}
