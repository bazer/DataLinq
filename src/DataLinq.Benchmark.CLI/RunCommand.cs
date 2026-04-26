using System;
using System.CommandLine;

namespace DataLinq.Benchmark.CLI;

internal static class RunCommand
{
    public static Command Create(BenchmarkCliSettings settings)
    {
        var filterOption = new Option<string>("--filter")
        {
            Description = "BenchmarkDotNet filter pattern.",
            DefaultValueFactory = _ => "*"
        };
        var profileOption = new Option<string>("--profile")
        {
            Description = "Benchmark profile: 'default' for ShortRun, 'heavy' for a slower local validation run, or 'smoke' for Dry.",
            DefaultValueFactory = _ => "default"
        };
        var noBuildOption = new Option<bool>("--no-build")
        {
            Description = "Skips restore/build and uses the existing benchmark assembly."
        };
        var keepFilesOption = new Option<bool>("--keep-files")
        {
            Description = "Preserves BenchmarkDotNet-generated temporary files."
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Prints the underlying restore/build/BenchmarkDotNet output."
        };
        var phase2WatchOption = new Option<bool>("--phase2-watch")
        {
            Description = $"Runs only the {BenchmarkHarnessRunner.Phase2WatchCategory} benchmark category."
        };
        var historyJsonOption = new Option<string?>("--history-json")
        {
            Description = "Optional output path for a stable benchmark history entry JSON artifact."
        };
        var baselineOption = new Option<string?>("--baseline")
        {
            Description = "Optional path to a benchmark history entry JSON artifact to compare against."
        };
        var comparisonJsonOption = new Option<string?>("--comparison-json")
        {
            Description = "Optional output path for a machine-readable comparison JSON artifact."
        };
        var warningThresholdOption = new Option<double>("--warning-threshold-percent")
        {
            Description = "Percent regression threshold used for comparison warnings when noise is acceptable.",
            DefaultValueFactory = _ => 10d
        };
        var additionalArgsArgument = new Argument<string[]>("additional-args")
        {
            Description = "Optional additional BenchmarkDotNet arguments. Pass them after '--'.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("run", "Runs the benchmark harness with compact output.");
        command.Options.Add(filterOption);
        command.Options.Add(profileOption);
        command.Options.Add(noBuildOption);
        command.Options.Add(keepFilesOption);
        command.Options.Add(verboseOption);
        command.Options.Add(phase2WatchOption);
        command.Options.Add(historyJsonOption);
        command.Options.Add(baselineOption);
        command.Options.Add(comparisonJsonOption);
        command.Options.Add(warningThresholdOption);
        command.Arguments.Add(additionalArgsArgument);

        command.SetAction(parseResult =>
        {
            var runner = new BenchmarkHarnessRunner(settings);
            var exitCode = runner.Run(
                parseResult.GetValue(filterOption) ?? "*",
                parseResult.GetValue(profileOption) ?? "default",
                parseResult.GetValue(noBuildOption),
                parseResult.GetValue(keepFilesOption),
                parseResult.GetValue(verboseOption),
                parseResult.GetValue(phase2WatchOption),
                parseResult.GetValue(historyJsonOption),
                parseResult.GetValue(baselineOption),
                parseResult.GetValue(comparisonJsonOption),
                parseResult.GetValue(warningThresholdOption),
                parseResult.GetValue(additionalArgsArgument) ?? Array.Empty<string>());

            if (exitCode != 0)
                Environment.ExitCode = exitCode;
        });

        return command;
    }
}
