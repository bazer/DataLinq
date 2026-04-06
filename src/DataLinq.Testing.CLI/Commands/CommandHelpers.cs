using System.CommandLine;

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
        Description = "Chooses which test suite to run: unit, compliance, mysql, or all.",
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
}
