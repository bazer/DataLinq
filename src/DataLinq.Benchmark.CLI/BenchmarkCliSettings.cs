using System;
using System.Collections.Generic;
using System.IO;

namespace DataLinq.Benchmark.CLI;

internal sealed class BenchmarkCliSettings
{
    private BenchmarkCliSettings(
        string repositoryRoot,
        string benchmarkProjectPath,
        string benchmarkAssemblyPath,
        string artifactsRoot,
        string dotnetCliHome,
        string appDataRoot,
        string localAppDataRoot,
        string homeRoot,
        string nugetPackagesPath)
    {
        RepositoryRoot = repositoryRoot;
        BenchmarkProjectPath = benchmarkProjectPath;
        BenchmarkAssemblyPath = benchmarkAssemblyPath;
        ArtifactsRoot = artifactsRoot;
        DotnetCliHome = dotnetCliHome;
        AppDataRoot = appDataRoot;
        LocalAppDataRoot = localAppDataRoot;
        HomeRoot = homeRoot;
        NugetPackagesPath = nugetPackagesPath;
    }

    public string RepositoryRoot { get; }
    public string BenchmarkProjectPath { get; }
    public string BenchmarkAssemblyPath { get; }
    public string ArtifactsRoot { get; }
    public string DotnetCliHome { get; }
    public string AppDataRoot { get; }
    public string LocalAppDataRoot { get; }
    public string HomeRoot { get; }
    public string NugetPackagesPath { get; }

    public static BenchmarkCliSettings FromAppContext()
    {
        var repositoryRoot = LocateRepositoryRoot(AppContext.BaseDirectory);
        var originalHome = ResolveOriginalHomeDirectory();
        var benchmarkingRoot = Path.Combine(repositoryRoot, ".benchmarking");

        return new BenchmarkCliSettings(
            repositoryRoot,
            Path.Combine(repositoryRoot, "src", "DataLinq.Benchmark", "DataLinq.Benchmark.csproj"),
            Path.Combine(repositoryRoot, "src", "DataLinq.Benchmark", "bin", "Release", "net8.0", "DataLinq.Benchmark.dll"),
            Path.Combine(repositoryRoot, "artifacts", "benchmarks"),
            Path.Combine(repositoryRoot, ".dotnet-cli"),
            Path.Combine(benchmarkingRoot, "appdata"),
            Path.Combine(benchmarkingRoot, "localappdata"),
            Path.Combine(benchmarkingRoot, "home"),
            ResolveNugetPackagesPath(originalHome));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ArtifactsRoot);
        Directory.CreateDirectory(DotnetCliHome);
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LocalAppDataRoot);
        Directory.CreateDirectory(HomeRoot);
    }

    public IReadOnlyDictionary<string, string?> CreateProcessEnvironment() =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_CLI_HOME"] = DotnetCliHome,
            ["DOTNET_CLI_UI_LANGUAGE"] = "en",
            ["DOTNET_NOLOGO"] = "1",
            ["APPDATA"] = AppDataRoot,
            ["LOCALAPPDATA"] = LocalAppDataRoot,
            ["HOME"] = HomeRoot,
            ["USERPROFILE"] = HomeRoot,
            ["NUGET_PACKAGES"] = NugetPackagesPath,
            ["NuGetAudit"] = "false"
        };

    private static string LocateRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Unable to locate the DataLinq repository root from '{startPath}'.");
    }

    private static string ResolveOriginalHomeDirectory()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("USERPROFILE"),
            Environment.GetEnvironmentVariable("HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Unable to resolve the current user's home directory.");
    }

    private static string ResolveNugetPackagesPath(string originalHome)
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(originalHome, ".nuget", "packages");
    }
}
