using System;
using System.CommandLine;
using DataLinq.DevTools;

namespace DataLinq.Testing.CLI;

internal static class CommandHelpers
{
    public static Option<string?> AliasOption() => new("--alias")
    {
        Description = "Uses one of the named target aliases: quick, latest, or all."
    };

    public static Option<string?> TargetsOption() => new("--targets")
    {
        Description = "Uses a comma-separated target list such as 'sqlite-file,mariadb-11.8'."
    };

    public static Option<string> SuiteOption() => new("--suite")
    {
        Description = "Chooses which test suite to run: generators, unit, compliance, mysql, or all.",
        DefaultValueFactory = _ => TestCliSuiteCatalog.AllSuites
    };

    public static Option<bool> InteractiveOption() => new("--interactive")
    {
        Description = "Prompts for command values interactively."
    };

    public static Option<bool> ParallelSuitesOption()
    {
        var option = new Option<bool>("--parallel")
        {
            Description = "Runs all selected suites in parallel instead of serially. This is faster, but it can increase contention against shared test targets."
        };

        option.Aliases.Add("--parallel-suites");
        return option;
    }

    public static Option<string> OutputOption() => new("--output")
    {
        Description = "Output mode: quiet, summary, failures, or raw.",
        DefaultValueFactory = _ => "quiet"
    };

    public static Option<string> ProfileOption() => new("--profile")
    {
        Description = "Execution profile: repo, sandbox, or ci.",
        DefaultValueFactory = _ => ToolingProfileExtensions.ResolveDefault().ToCliValue()
    };

    public static TestCliOutputMode ParseOutputMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "quiet" => TestCliOutputMode.Quiet,
            "summary" => TestCliOutputMode.Summary,
            "failures" => TestCliOutputMode.Failures,
            "raw" => TestCliOutputMode.Raw,
            _ => throw new InvalidOperationException($"Unsupported output mode '{value}'. Use quiet, summary, failures, or raw.")
        };

    public static ToolingProfile ParseProfile(string? value)
    {
        if (ToolingProfileExtensions.TryParse(value, out var profile))
            return profile;

        throw new InvalidOperationException($"Unsupported execution profile '{value}'. Use repo, sandbox, or ci.");
    }

    public static void ExecuteSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            Environment.ExitCode = 1;
        }
    }
}

internal enum TestCliOutputMode
{
    Quiet,
    Summary,
    Failures,
    Raw
}
