using System;
using System.Collections.Generic;
using System.IO;
using DataLinq.DevTools;

namespace DataLinq.Benchmark.CLI;

internal sealed class BenchmarkCliSettings
{
    private BenchmarkCliSettings(
        string repositoryRoot,
        string benchmarkTargetRepositoryRoot,
        string benchmarkProjectPath,
        string benchmarkAssemblyPath,
        string artifactsRoot,
        DevToolPaths toolPaths)
    {
        RepositoryRoot = repositoryRoot;
        BenchmarkTargetRepositoryRoot = benchmarkTargetRepositoryRoot;
        BenchmarkProjectPath = benchmarkProjectPath;
        BenchmarkAssemblyPath = benchmarkAssemblyPath;
        ArtifactsRoot = artifactsRoot;
        ToolPaths = toolPaths;
    }

    public string RepositoryRoot { get; }
    public string BenchmarkTargetRepositoryRoot { get; }
    public string BenchmarkProjectPath { get; }
    public string BenchmarkAssemblyPath { get; }
    public string ArtifactsRoot { get; }
    public DevToolPaths ToolPaths { get; }

    public static BenchmarkCliSettings FromAppContext()
    {
        var repositoryRoot = RepositoryRootLocator.Find();

        return new BenchmarkCliSettings(
            repositoryRoot,
            repositoryRoot,
            Path.Combine(repositoryRoot, "src", "DataLinq.Benchmark", "DataLinq.Benchmark.csproj"),
            Path.Combine(repositoryRoot, "src", "DataLinq.Benchmark", "bin", "Release", "net8.0", "DataLinq.Benchmark.dll"),
            Path.Combine(repositoryRoot, "artifacts", "benchmarks"),
            DevToolPaths.Create(repositoryRoot));
    }

    public bool UsesExternalBenchmarkTarget => !string.Equals(
        Path.TrimEndingDirectorySeparator(RepositoryRoot),
        Path.TrimEndingDirectorySeparator(BenchmarkTargetRepositoryRoot),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public BenchmarkCliSettings WithBenchmarkTargetRoot(string? benchmarkTargetRoot)
    {
        if (string.IsNullOrWhiteSpace(benchmarkTargetRoot))
            return this;

        var targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            benchmarkTargetRoot,
            RepositoryRoot));
        var allowedRoot = Path.Combine(ArtifactsRoot, "targets");
        var relative = Path.GetRelativePath(allowedRoot, targetRoot);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The benchmark target worktree must remain beneath 'artifacts/benchmarks/targets'.");
        }

        var projectPath = Path.Combine(targetRoot, "src", "DataLinq.Benchmark", "DataLinq.Benchmark.csproj");
        if (!Directory.Exists(targetRoot) ||
            !File.Exists(Path.Combine(targetRoot, ".git")) && !Directory.Exists(Path.Combine(targetRoot, ".git")) ||
            !File.Exists(projectPath) ||
            !IsSafeTargetPath(allowedRoot, targetRoot, projectPath))
        {
            throw new InvalidDataException(
                $"Benchmark target '{targetRoot}' must be an existing Git worktree containing the DataLinq benchmark project.");
        }

        return new BenchmarkCliSettings(
            RepositoryRoot,
            targetRoot,
            projectPath,
            Path.Combine(targetRoot, "src", "DataLinq.Benchmark", "bin", "Release", "net8.0", "DataLinq.Benchmark.dll"),
            ArtifactsRoot,
            ToolPaths);
    }

    private static bool IsSafeTargetPath(string allowedRoot, string targetRoot, string projectPath)
    {
        try
        {
            var current = Path.GetFullPath(allowedRoot);
            var allowedAttributes = File.GetAttributes(current);
            if ((allowedAttributes & FileAttributes.Directory) == 0 ||
                (allowedAttributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
            var relativeTarget = Path.GetRelativePath(current, targetRoot);
            foreach (var segment in relativeTarget.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.Directory) == 0 ||
                    (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }

            var projectAttributes = File.GetAttributes(projectPath);
            return (projectAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch
        {
            return false;
        }
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
