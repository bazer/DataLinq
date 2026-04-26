using System.CommandLine;

namespace DataLinq.Testing.CLI;

internal static class UpCommand
{
    public static Command Create(TestInfraOrchestrator orchestrator)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();
        var interactiveOption = CommandHelpers.InteractiveOption();
        var recreateOption = new Option<bool>("--recreate")
        {
            Description = "Removes existing containers before starting the selected targets."
        };

        var command = new Command("up", "Starts the selected server targets and waits for readiness.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);
        command.Options.Add(interactiveOption);
        command.Options.Add(recreateOption);

        command.SetAction(parseResult =>
        {
            if (parseResult.GetValue(interactiveOption))
            {
                InteractiveCliRunner.RunUp(orchestrator);
                return;
            }

            CommandHelpers.ExecuteSafely(() =>
            {
                var selection = TargetSelectionResolver.Resolve(
                    parseResult.GetValue(aliasOption),
                    parseResult.GetValue(targetsOption),
                    defaultAlias: "latest");

                orchestrator.Up(selection, parseResult.GetValue(recreateOption));
            });
        });

        return command;
    }
}
