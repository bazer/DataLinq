using System.CommandLine;

namespace DataLinq.Testing.CLI;

internal static class ResetCommand
{
    public static Command Create(TestInfraOrchestrator orchestrator)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();

        var command = new Command("reset", "Recreates the selected server targets from scratch.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);

        command.SetAction(parseResult =>
        {
            var selection = TargetSelectionResolver.Resolve(
                parseResult.GetValue(aliasOption),
                parseResult.GetValue(targetsOption),
                defaultAlias: "latest");

            orchestrator.Reset(selection);
        });

        return command;
    }
}
