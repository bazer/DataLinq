using System;
using System.IO;
using System.Text.Json;

namespace DataLinq.Benchmark;

internal static class BenchmarkTelemetryDeltaWriter
{
    internal const string RunIdEnvironmentVariable = "DATALINQ_BENCHMARK_RUN_ID";
    internal const string ResultsDirectoryEnvironmentVariable = "DATALINQ_BENCHMARK_RESULTS_DIR";

    public static void TryWrite(BenchmarkTelemetryDeltaArtifact artifact)
    {
        var runId = Environment.GetEnvironmentVariable(RunIdEnvironmentVariable);
        var resultsDirectory = Environment.GetEnvironmentVariable(ResultsDirectoryEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(resultsDirectory))
            return;

        Directory.CreateDirectory(resultsDirectory);

        var fileName = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{runId}-{Sanitize(artifact.Method)}-{Sanitize(artifact.ProviderName)}-telemetry.json");

        var filePath = Path.Combine(resultsDirectory, fileName);
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(filePath, json);
    }

    private static string Sanitize(string value)
    {
        var sanitized = value.Trim().ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('/', '-')
            .Replace('\\', '-')
            .Replace(':', '-');

        foreach (var invalid in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalid, '-');

        return sanitized;
    }
}
