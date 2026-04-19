using System;

namespace DataLinq.DevTools;

public enum ToolingProfile
{
    Repo,
    Sandbox,
    Ci
}

public static class ToolingProfileExtensions
{
    public static bool TryParse(string? value, out ToolingProfile profile)
    {
        profile = ResolveDefault();

        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Trim().ToLowerInvariant() switch
        {
            "repo" or "repo-isolated" => Set(ToolingProfile.Repo, out profile),
            "sandbox" or "offline" => Set(ToolingProfile.Sandbox, out profile),
            "ci" => Set(ToolingProfile.Ci, out profile),
            "auto" => Set(ResolveDefault(), out profile),
            _ => false
        };
    }

    public static string ToCliValue(this ToolingProfile profile) =>
        profile switch
        {
            ToolingProfile.Repo => "repo",
            ToolingProfile.Sandbox => "sandbox",
            ToolingProfile.Ci => "ci",
            _ => "repo"
        };

    public static bool IsOffline(this ToolingProfile profile) =>
        profile == ToolingProfile.Sandbox;

    public static ToolingProfile ResolveDefault() =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
            ? ToolingProfile.Ci
            : ToolingProfile.Repo;

    private static bool Set(ToolingProfile value, out ToolingProfile profile)
    {
        profile = value;
        return true;
    }
}
