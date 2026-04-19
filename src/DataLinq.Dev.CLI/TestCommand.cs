using System;
using System.Collections.Generic;
using System.CommandLine;
using DataLinq.DevTools;

namespace DataLinq.Dev.CLI;

internal static class TestCommand
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
        var filterOption = new Option<string?>("--filter")
        {
            Description = "Optional dotnet test filter expression."
        };
        var noBuildOption = new Option<bool>("--no-build")
        {
            Description = "Skips building before test."
        };
        var noRestoreOption = new Option<bool>("--no-restore")
        {
            Description = "Skips restore before test."
        };
        var targetArgument = new Argument<string?>("target")
        {
            Description = "Optional solution or project path. Defaults to src/DataLinq.sln.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var additionalArgsArgument = new Argument<string[]>("additional-args")
        {
            Description = "Optional additional dotnet test arguments. Pass them after '--'.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("test", "Runs dotnet test with concise failure-focused output.");
        command.Options.Add(profileOption);
        command.Options.Add(outputOption);
        command.Options.Add(configurationOption);
        command.Options.Add(frameworkOption);
        command.Options.Add(filterOption);
        command.Options.Add(noBuildOption);
        command.Options.Add(noRestoreOption);
        command.Arguments.Add(targetArgument);
        command.Arguments.Add(additionalArgsArgument);

        command.SetAction(parseResult =>
        {
            var profile = CommandHelpers.ParseProfile(parseResult.GetValue(profileOption));
            var outputMode = CommandHelpers.ParseOutputMode(parseResult.GetValue(outputOption));
            var target = CommandHelpers.ResolveTargetPath(settings.RepositoryRoot, parseResult.GetValue(targetArgument));
            var configuration = parseResult.GetValue(configurationOption) ?? "Debug";
            var framework = parseResult.GetValue(frameworkOption);
            var filter = parseResult.GetValue(filterOption);
            var noBuild = parseResult.GetValue(noBuildOption);
            var noRestore = parseResult.GetValue(noRestoreOption);
            var additionalArgs = parseResult.GetValue(additionalArgsArgument) ?? Array.Empty<string>();

            var runner = new DotnetCommandRunner(settings.Paths, profile);
            var result = runner.Execute(
                DotnetCommandType.Test,
                BuildArguments(target, configuration, framework, filter, noBuild, noRestore, outputMode, additionalArgs),
                artifactPrefix: "test",
                displayTarget: target);

            CommandRenderer.Render(result, outputMode, printBinaryLogOnSuccess: false);
            if (result.ProcessResult.ExitCode != 0)
                Environment.ExitCode = result.ProcessResult.ExitCode;
        });

        return command;
    }

    private static string[] BuildArguments(
        string target,
        string configuration,
        string? framework,
        string? filter,
        bool noBuild,
        bool noRestore,
        DotnetOutputMode outputMode,
        string[] additionalArgs)
    {
        var verbosity = outputMode == DotnetOutputMode.Diagnostic ? "diag" : "minimal";
        var arguments = new List<string>
        {
            "test",
            "-c",
            configuration,
            "-v",
            verbosity
        };

        arguments.AddRange(CommandHelpers.CreateDotnetTestTargetArguments(target));

        if (!string.IsNullOrWhiteSpace(framework))
            arguments.AddRange(["-f", framework]);

        if (!string.IsNullOrWhiteSpace(filter))
            arguments.AddRange(["--filter", filter]);

        if (noBuild)
            arguments.Add("--no-build");

        if (noRestore)
            arguments.Add("--no-restore");

        arguments.AddRange(additionalArgs);
        return arguments.ToArray();
    }
}
