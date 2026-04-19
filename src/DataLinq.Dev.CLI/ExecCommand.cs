using System;
using System.CommandLine;
using System.Linq;
using DataLinq.DevTools;

namespace DataLinq.Dev.CLI;

internal static class ExecCommand
{
    public static Command Create(DevCliSettings settings)
    {
        var profileOption = CommandHelpers.ProfileOption();
        var outputOption = CommandHelpers.OutputOption();
        var argumentsArgument = new Argument<string[]>("dotnet-args")
        {
            Description = "Arguments to pass to dotnet. Pass them after '--'.",
            Arity = ArgumentArity.OneOrMore
        };

        var command = new Command("exec", "Runs an arbitrary dotnet command through the repo-local execution profile.");
        command.Options.Add(profileOption);
        command.Options.Add(outputOption);
        command.Arguments.Add(argumentsArgument);

        command.SetAction(parseResult =>
        {
            var profile = CommandHelpers.ParseProfile(parseResult.GetValue(profileOption));
            var outputMode = CommandHelpers.ParseOutputMode(parseResult.GetValue(outputOption));
            var arguments = parseResult.GetValue(argumentsArgument) ?? throw new InvalidOperationException("At least one dotnet argument is required.");

            var runner = new DotnetCommandRunner(settings.Paths, profile);
            var commandType = InferCommandType(arguments);
            var artifactPrefix = InferArtifactPrefix(arguments);
            var result = runner.Execute(
                commandType,
                arguments,
                artifactPrefix,
                displayTarget: string.Join(" ", arguments.Select(CommandHelpers.QuoteForDisplay)),
                includeNuGetAuditProperty: true,
                includeOfflineRestoreProperty: true,
                generateBinaryLog: commandType == DotnetCommandType.Build);

            CommandRenderer.Render(result, outputMode, printBinaryLogOnSuccess: false);
            if (result.ProcessResult.ExitCode != 0)
                Environment.ExitCode = result.ProcessResult.ExitCode;
        });

        return command;
    }

    private static DotnetCommandType InferCommandType(string[] arguments) =>
        arguments[0].ToLowerInvariant() switch
        {
            "restore" => DotnetCommandType.Restore,
            "build" => DotnetCommandType.Build,
            "test" => DotnetCommandType.Test,
            "--info" or "--version" or "--list-sdks" or "--list-runtimes" => DotnetCommandType.Info,
            _ => DotnetCommandType.Exec
        };

    private static string InferArtifactPrefix(string[] arguments)
    {
        var head = arguments[0].TrimStart('-', '/');
        return string.IsNullOrWhiteSpace(head)
            ? "exec"
            : $"exec-{head.Replace('.', '-').Replace(':', '-')}";
    }
}
