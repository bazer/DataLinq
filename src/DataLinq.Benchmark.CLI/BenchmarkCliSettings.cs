using System;
using System.Collections.Generic;
using System.IO;
using DataLinq.DevTools;

namespace DataLinq.Benchmark.CLI;

internal sealed class BenchmarkCliSettings
{
    private BenchmarkCliSettings(
        string repositoryRoot,
        string benchmarkProjectPath,
        string benchmarkAssemblyPath,
        string artifactsRoot,
        DevToolPaths toolPaths)
    {
        RepositoryRoot = repositoryRoot;
        BenchmarkProjectPath = benchmarkProjectPath;
        BenchmarkAssemblyPath = benchmarkAssemblyPath;
        ArtifactsRoot = artifactsRoot;
        ToolPaths = toolPaths;
    }

    public string RepositoryRoot { get; }
    public string BenchmarkProjectPath { get; }
    public string BenchmarkAssemblyPath { get; }
    public string ArtifactsRoot { get; }
    public DevToolPaths ToolPaths { get; }

    public static BenchmarkCliSettings FromAppContext()
    {
        var repositoryRoot = RepositoryRootLocator.Find();

        return new BenchmarkCliSettings(
            repositoryRoot,
            Path.Combine(repositoryRoot, "src", "DataLinq.Benchmark", "DataLinq.Benchmark.csproj"),
            Path.Combine(repositoryRoot, "src", "DataLinq.Benchmark", "bin", "Release", "net8.0", "DataLinq.Benchmark.dll"),
            Path.Combine(repositoryRoot, "artifacts", "benchmarks"),
            DevToolPaths.Create(repositoryRoot));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ArtifactsRoot);
        ToolPaths.EnsureCreated();
    }

    public IReadOnlyDictionary<string, string?> CreateProcessEnvironment() =>
        new Dictionary<string, string?>(ToolPaths.CreateEnvironment(ToolingProfile.Repo), StringComparer.OrdinalIgnoreCase)
        {
            ["DATALINQ_BENCHMARK_PROVIDERS"] = Environment.GetEnvironmentVariable("DATALINQ_BENCHMARK_PROVIDERS")
        };
}
