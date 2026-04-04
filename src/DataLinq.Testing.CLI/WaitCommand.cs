using System.CommandLine;

namespace DataLinq.Testing.CLI;

internal static class WaitCommand
{
    public static Command Create(TestInfraOrchestrator orchestrator)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();

        var command = new Command("wait", "Waits for the selected server targets to become ready and writes runtime state.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);

        command.SetAction(parseResult =>
        {
            var selection = TargetSelectionResolver.Resolve(
                parseResult.GetValue(aliasOption),
                parseResult.GetValue(targetsOption),
                defaultAlias: "latest");

            orchestrator.Wait(selection);
        });

        return command;
    }
}
