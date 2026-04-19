using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DataLinq.DevTools;

public sealed record DevToolPaths(
    string RepositoryRoot,
    string DotnetRoot,
    string ArtifactRoot,
    string DotnetCliHome,
    string AppDataRoot,
    string LocalAppDataRoot,
    string HomeRoot,
    string NugetPackagesRoot,
    string NugetConfigPath)
{
    public static DevToolPaths Create(string repositoryRoot) =>
        new(
            RepositoryRoot: repositoryRoot,
            DotnetRoot: Path.Combine(repositoryRoot, ".dotnet"),
            ArtifactRoot: Path.Combine(repositoryRoot, "artifacts", "dev"),
            DotnetCliHome: Path.Combine(repositoryRoot, ".dotnet", "cli-home"),
            AppDataRoot: Path.Combine(repositoryRoot, ".dotnet", "AppData", "Roaming"),
            LocalAppDataRoot: Path.Combine(repositoryRoot, ".dotnet", "AppData", "Local"),
            HomeRoot: Path.Combine(repositoryRoot, ".dotnet", "Home"),
            NugetPackagesRoot: Path.Combine(repositoryRoot, ".dotnet", ".nuget", "packages"),
            NugetConfigPath: Path.Combine(repositoryRoot, ".dotnet", "AppData", "Roaming", "NuGet", "NuGet.Config"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DotnetRoot);
        Directory.CreateDirectory(ArtifactRoot);
        Directory.CreateDirectory(DotnetCliHome);
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LocalAppDataRoot);
        Directory.CreateDirectory(HomeRoot);
        Directory.CreateDirectory(NugetPackagesRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(NugetConfigPath)
            ?? throw new InvalidOperationException($"Could not resolve the NuGet.Config directory for '{NugetConfigPath}'."));

        if (!File.Exists(NugetConfigPath))
            File.WriteAllText(NugetConfigPath, CreateNugetConfig(), Encoding.ASCII);
    }

    public IReadOnlyDictionary<string, string?> CreateEnvironment(ToolingProfile profile) =>
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH"] = "0",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["DOTNET_CLI_HOME"] = DotnetCliHome,
            ["DOTNET_CLI_UI_LANGUAGE"] = "en",
            ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "0",
            ["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1",
            ["DOTNET_NOLOGO"] = "1",
            ["APPDATA"] = AppDataRoot,
            ["LOCALAPPDATA"] = LocalAppDataRoot,
            ["HOME"] = HomeRoot,
            ["USERPROFILE"] = HomeRoot,
            ["NUGET_PACKAGES"] = NugetPackagesRoot,
            ["NuGetAudit"] = "false",
            ["DATALINQ_DEV_PROFILE"] = profile.ToCliValue()
        };

    private static string CreateNugetConfig() =>
        """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
          </packageSources>
        </configuration>
        """.Replace("\r\n", Environment.NewLine, StringComparison.Ordinal)
          .Replace("\n", Environment.NewLine, StringComparison.Ordinal);
}
