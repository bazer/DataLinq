using System;
using System.CommandLine;
using System.IO;
using DataLinq.DevTools;

namespace DataLinq.Dev.CLI;

internal static class CommandHelpers
{
    public static Option<string> ProfileOption() =>
        new("--profile")
        {
            Description = "Execution profile: repo, sandbox, ci, or auto.",
            DefaultValueFactory = _ => "auto"
        };

    public static Option<string> OutputOption() =>
        new("--output")
        {
            Description = "Output mode: quiet, summary, errors, failures, raw, or diag.",
            DefaultValueFactory = _ => "quiet"
        };

    public static string ResolveTargetPath(string repositoryRoot, string? target) =>
        Path.IsPathRooted(target)
            ? target
            : Path.Combine(repositoryRoot, target ?? Path.Combine("src", "DataLinq.sln"));

    public static ToolingProfile ParseProfile(string? value)
    {
        if (ToolingProfileExtensions.TryParse(value, out var profile))
            return profile;

        throw new InvalidOperationException($"Unsupported profile '{value}'. Use repo, sandbox, ci, or auto.");
    }

    public static DotnetOutputMode ParseOutputMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "quiet" => DotnetOutputMode.Quiet,
            "summary" => DotnetOutputMode.Summary,
            "errors" => DotnetOutputMode.Errors,
            "failures" => DotnetOutputMode.Failures,
            "raw" => DotnetOutputMode.Raw,
            "diag" or "diagnostic" => DotnetOutputMode.Diagnostic,
            _ => throw new InvalidOperationException($"Unsupported output mode '{value}'. Use quiet, summary, errors, failures, raw, or diag.")
        };
}
