using System;
using System.IO;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using Perfolizer.Horology;
using Perfolizer.Metrology;

namespace DataLinq.Benchmark;

internal sealed class DataLinqBenchmarkConfig : ManualConfig
{
    public DataLinqBenchmarkConfig()
    {
        AddJob(Job.ShortRun
            .WithId("Default")
            .WithRuntime(BenchmarkDotNet.Environments.CoreRuntime.Core80)
            .WithWarmupCount(4)
            .WithIterationCount(6)
            .WithMsBuildArguments([
                "/p:RestoreIgnoreFailedSources=true",
                "/p:NuGetAudit=false"
            ]));
        AddJob(Job.MediumRun
            .WithId("Heavy")
            .WithRuntime(BenchmarkDotNet.Environments.CoreRuntime.Core80)
            .WithWarmupCount(6)
            .WithIterationCount(15)
            .WithMsBuildArguments([
                "/p:RestoreIgnoreFailedSources=true",
                "/p:NuGetAudit=false"
            ]));
        AddColumnProvider(DefaultColumnProviders.Instance);
        HideColumns(
            Column.Job,
            Column.StdErr,
            Column.StdDev,
            Column.Median,
            Column.RatioSD,
            Column.Gen0,
            Column.Gen1,
            Column.Gen2);

        WithOptions(ConfigOptions.JoinSummary | ConfigOptions.DisableLogFile | ConfigOptions.DisableParallelBuild);
        WithArtifactsPath(Path.Combine(GetRepositoryRoot(), "artifacts", "benchmarks"));
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
}
