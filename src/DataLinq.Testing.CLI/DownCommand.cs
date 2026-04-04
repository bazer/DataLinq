using System.CommandLine;

namespace DataLinq.Testing.CLI;

internal static class DownCommand
{
    public static Command Create(TestInfraOrchestrator orchestrator)
    {
        var aliasOption = CommandHelpers.AliasOption();
        var targetsOption = CommandHelpers.TargetsOption();
        var removeOption = new Option<bool>("--remove")
        {
            Description = "Removes the selected containers after stopping them."
        };

        var command = new Command("down", "Stops or removes the selected server targets.");
        command.Options.Add(aliasOption);
        command.Options.Add(targetsOption);
        command.Options.Add(removeOption);

        command.SetAction(parseResult =>
        {
            var aliasName = parseResult.GetValue(aliasOption);
            var targetList = parseResult.GetValue(targetsOption);
            var selection = string.IsNullOrWhiteSpace(aliasName) && string.IsNullOrWhiteSpace(targetList)
                ? null
                : TargetSelectionResolver.Resolve(aliasName, targetList);

            orchestrator.Down(parseResult.GetValue(removeOption), selection);
        });

        return command;
    }
}
