using System;
using System.CommandLine;
using System.Collections.Generic;
using DataLinq.DevTools;

namespace DataLinq.Dev.CLI;

internal static class BuildCommand
{
    public static Command Create(DevCliSettings settings)
    {
        var profileOption = CommandHelpers.ProfileOption();
        var outputOption = CommandHelpers.OutputOption();
        var configurationOption = new Option<string>("--configuration")
        {
            Description = "Build configuration.",
            DefaultValueFactory = _ => "Debug"
        };
        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "Optional target framework."
        };
        var noRestoreOption = new Option<bool>("--no-restore")
        {
            Description = "Skips restore before build."
        };
        var binlogOption = new Option<string>("--binlog")
        {
            Description = "Binary log behavior: auto, always, or never.",
            DefaultValueFactory = _ => "auto"
        };
        var targetArgument = new Argument<string?>("target")
        {
            Description = "Optional solution or project path. Defaults to src/DataLinq.sln.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var additionalArgsArgument = new Argument<string[]>("additional-args")
        {
            Description = "Optional additional dotnet build arguments. Pass them after '--'.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("build", "Runs dotnet build with concise default output.");
        command.Options.Add(profileOption);
        command.Options.Add(outputOption);
        command.Options.Add(configurationOption);
        command.Options.Add(frameworkOption);
        command.Options.Add(noRestoreOption);
        command.Options.Add(binlogOption);
        command.Arguments.Add(targetArgument);
        command.Arguments.Add(additionalArgsArgument);

        command.SetAction(parseResult =>
        {
            var profile = CommandHelpers.ParseProfile(parseResult.GetValue(profileOption));
            var outputMode = CommandHelpers.ParseOutputMode(parseResult.GetValue(outputOption));
            var target = CommandHelpers.ResolveTargetPath(settings.RepositoryRoot, parseResult.GetValue(targetArgument));
            var configuration = parseResult.GetValue(configurationOption) ?? "Debug";
            var framework = parseResult.GetValue(frameworkOption);
            var noRestore = parseResult.GetValue(noRestoreOption);
            var additionalArgs = parseResult.GetValue(additionalArgsArgument) ?? Array.Empty<string>();
            var binlogBehavior = ParseBinlogBehavior(parseResult.GetValue(binlogOption));

            var runner = new DotnetCommandRunner(settings.Paths, profile);
            var result = runner.Execute(
                DotnetCommandType.Build,
                BuildArguments(target, configuration, framework, noRestore, outputMode, additionalArgs),
                artifactPrefix: "build",
                displayTarget: target,
                generateBinaryLog: binlogBehavior != BinlogBehavior.Never);

            CommandRenderer.Render(result, outputMode, printBinaryLogOnSuccess: binlogBehavior == BinlogBehavior.Always);
            if (result.ProcessResult.ExitCode != 0)
                Environment.ExitCode = result.ProcessResult.ExitCode;
        });

        return command;
    }

    private static string[] BuildArguments(
        string target,
        string configuration,
        string? framework,
        bool noRestore,
        DotnetOutputMode outputMode,
        string[] additionalArgs)
    {
        var verbosity = outputMode == DotnetOutputMode.Diagnostic ? "diag" : "minimal";
        var arguments = new List<string>
        {
            "build",
            target,
            "-c",
            configuration,
            "-v",
            verbosity
        };

        if (!string.IsNullOrWhiteSpace(framework))
            arguments.AddRange(["-f", framework]);

        if (noRestore)
            arguments.Add("--no-restore");

        arguments.AddRange(additionalArgs);
        return arguments.ToArray();
    }

    private static BinlogBehavior ParseBinlogBehavior(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "auto" => BinlogBehavior.Auto,
            "always" => BinlogBehavior.Always,
            "never" => BinlogBehavior.Never,
            _ => throw new InvalidOperationException($"Unsupported binlog mode '{value}'. Use auto, always, or never.")
        };

    private enum BinlogBehavior
    {
        Auto,
        Always,
        Never
    }
}
