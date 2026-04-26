using System;
using System.CommandLine;

namespace DataLinq.Benchmark.CLI;

internal static class ListCommand
{
    public static Command Create(BenchmarkCliSettings settings)
    {
        var noBuildOption = new Option<bool>("--no-build")
        {
            Description = "Skips restore/build and uses the existing benchmark assembly."
        };
        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Prints the underlying restore/build/BenchmarkDotNet output."
        };
        var additionalArgsArgument = new Argument<string[]>("additional-args")
        {
            Description = "Optional additional BenchmarkDotNet arguments. Pass them after '--'.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("list", "Lists available benchmark methods.");
        command.Options.Add(noBuildOption);
        command.Options.Add(verboseOption);
        command.Arguments.Add(additionalArgsArgument);

        command.SetAction(parseResult =>
        {
            var runner = new BenchmarkHarnessRunner(settings);
            var exitCode = runner.List(
                parseResult.GetValue(noBuildOption),
                parseResult.GetValue(verboseOption),
                parseResult.GetValue(additionalArgsArgument) ?? Array.Empty<string>());

            if (exitCode != 0)
                Environment.ExitCode = exitCode;
        });

        return command;
    }
}
