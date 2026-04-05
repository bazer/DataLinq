using System.CommandLine;

namespace DataLinq.Testing.CLI;

internal static class WaitCommand
{
    public static Command Create(TestInfraOrchestrator orchestrator)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();
        var interactiveOption = CommandHelpers.InteractiveOption();

        var command = new Command("wait", "Waits for the selected server targets to become ready and writes runtime state.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);
        command.Options.Add(interactiveOption);

        command.SetAction(parseResult =>
        {
            if (parseResult.GetValue(interactiveOption))
            {
                InteractiveCliRunner.RunWait(orchestrator);
                return;
            }

            var selection = TargetSelectionResolver.Resolve(
                parseResult.GetValue(aliasOption),
                parseResult.GetValue(targetsOption),
                defaultAlias: "latest");

            orchestrator.Wait(selection);
        });

        return command;
    }
}
