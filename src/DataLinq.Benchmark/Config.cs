using System;
using System.IO;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Perfolizer.Horology;
using Perfolizer.Metrology;

namespace DataLinq.Benchmark;

internal sealed class DataLinqBenchmarkConfig : ManualConfig
{
    private const string BenchmarkProfileEnvironmentVariable = "DATALINQ_BENCHMARK_PROFILE";
    private const string BenchmarkArtifactsDirectoryEnvironmentVariable = "DATALINQ_BENCHMARK_ARTIFACTS_DIR";
    private const string BenchmarkInProcessEnvironmentVariable = "DATALINQ_BENCHMARK_IN_PROCESS";

    public DataLinqBenchmarkConfig()
    {
        AddJob(CreateProfileJob());
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddExporter(CsvExporter.Default, MarkdownExporter.GitHub);
        HideColumns(
            Column.Job,
            Column.StdErr,
            Column.RatioSD,
            Column.Gen0,
            Column.Gen1,
            Column.Gen2);

        WithOptions(ConfigOptions.JoinSummary | ConfigOptions.DisableLogFile | ConfigOptions.DisableParallelBuild);
        WithArtifactsPath(GetArtifactsPath());
        WithSummaryStyle(SummaryStyle.Default
            .WithTimeUnit(TimeUnit.Microsecond)
            .WithSizeUnit(SizeUnit.KB)
            .WithMaxParameterColumnWidth(24));
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate the DataLinq repository root from '{AppContext.BaseDirectory}'.");
    }

    private static string GetArtifactsPath()
    {
        var repositoryRoot = GetRepositoryRoot();
        var configured = Environment.GetEnvironmentVariable(BenchmarkArtifactsDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(repositoryRoot, "artifacts", "benchmarks");

        var fullPath = Path.GetFullPath(configured);
        var artifactRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts"));
        var relativePath = Path.GetRelativePath(artifactRoot, fullPath);
        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Benchmark artifacts path '{fullPath}' must remain beneath repository artifacts.");
        }
        return fullPath;
    }

    private static Job CreateProfileJob()
    {
        var profile = Environment.GetEnvironmentVariable(BenchmarkProfileEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(profile))
            profile = "default";

        var job = profile.ToLowerInvariant() switch
        {
            "default" => Job.ShortRun,
            "heavy" => Job.MediumRun,
            "smoke" => Job.Dry,
            _ => throw new InvalidOperationException(
                $"Benchmark profile '{profile}' is not supported. Use 'default', 'heavy', or 'smoke'.")
        };

        return string.Equals(
                Environment.GetEnvironmentVariable(BenchmarkInProcessEnvironmentVariable),
                "true",
                StringComparison.OrdinalIgnoreCase)
            ? job.WithToolchain(InProcessEmitToolchain.Instance)
            : job;
    }
}
