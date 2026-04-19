using System;
using System.CommandLine;
using DataLinq.DevTools;

namespace DataLinq.Dev.CLI;

internal static class RestoreCommand
{
    public static Command Create(DevCliSettings settings)
    {
        var profileOption = CommandHelpers.ProfileOption();
        var outputOption = CommandHelpers.OutputOption();
        var targetArgument = new Argument<string?>("target")
        {
            Description = "Optional solution or project path. Defaults to src/DataLinq.sln.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var additionalArgsArgument = new Argument<string[]>("additional-args")
        {
            Description = "Optional additional dotnet restore arguments. Pass them after '--'.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("restore", "Runs dotnet restore with a repo-local execution profile.");
        command.Options.Add(profileOption);
        command.Options.Add(outputOption);
        command.Arguments.Add(targetArgument);
        command.Arguments.Add(additionalArgsArgument);

        command.SetAction(parseResult =>
        {
            var profile = CommandHelpers.ParseProfile(parseResult.GetValue(profileOption));
            var outputMode = CommandHelpers.ParseOutputMode(parseResult.GetValue(outputOption));
            var target = CommandHelpers.ResolveTargetPath(settings.RepositoryRoot, parseResult.GetValue(targetArgument));
            var additionalArgs = parseResult.GetValue(additionalArgsArgument) ?? Array.Empty<string>();

            var runner = new DotnetCommandRunner(settings.Paths, profile);
            var result = runner.Execute(
                DotnetCommandType.Restore,
                BuildArguments(target, outputMode, additionalArgs),
                artifactPrefix: "restore",
                displayTarget: target);

            CommandRenderer.Render(result, outputMode, printBinaryLogOnSuccess: false);
            if (result.ProcessResult.ExitCode != 0)
                Environment.ExitCode = result.ProcessResult.ExitCode;
        });

        return command;
    }

    private static string[] BuildArguments(string target, DotnetOutputMode outputMode, string[] additionalArgs)
    {
        var verbosity = outputMode == DotnetOutputMode.Diagnostic ? "diag" : "minimal";
        var arguments = new System.Collections.Generic.List<string>
        {
            "restore",
            target,
            "-v",
            verbosity
        };

        arguments.AddRange(additionalArgs);
        return arguments.ToArray();
    }
}
