using System.CommandLine;

namespace DataLinq.Testing.CLI;

internal static class ResetCommand
{
    public static Command Create(TestInfraOrchestrator orchestrator)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();
        var interactiveOption = CommandHelpers.InteractiveOption();

        var command = new Command("reset", "Recreates the selected server targets from scratch.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);
        command.Options.Add(interactiveOption);

        command.SetAction(parseResult =>
        {
            if (parseResult.GetValue(interactiveOption))
            {
                InteractiveCliRunner.RunReset(orchestrator);
                return;
            }

            CommandHelpers.ExecuteSafely(() =>
            {
                var selection = TargetSelectionResolver.Resolve(
                    parseResult.GetValue(aliasOption),
                    parseResult.GetValue(targetsOption),
                    defaultAlias: "latest");

                orchestrator.Reset(selection);
            });
        });

        return command;
    }
}
