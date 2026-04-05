using System.Linq;
using System.CommandLine;
using Spectre.Console;

namespace DataLinq.Testing.CLI;

internal static class ListCommand
{
    public static Command Create(TestInfraRuntimeStateStore stateStore)
    {
        var command = new Command("list", "Lists known targets and aliases for the test infrastructure CLI.");

        command.SetAction(_ => Render(stateStore));

        return command;
    }

    public static void Render(TestInfraRuntimeStateStore stateStore)
    {
        RenderAliases();
        AnsiConsole.WriteLine();
        RenderTargets();
        RenderState(stateStore.Load());
    }

    private static void RenderAliases()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Alias")
            .AddColumn("Description")
            .AddColumn("Targets");

        foreach (var alias in TestCliCatalog.Aliases)
        {
            table.AddRow(
                alias.Name,
                alias.Description,
                string.Join(", ", alias.TargetIds));
        }

        AnsiConsole.Write(new Rule("[yellow]Aliases[/]"));
        AnsiConsole.Write(table);
    }

    private static void RenderTargets()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Target")
            .AddColumn("Category")
            .AddColumn("Runtime");

        foreach (var target in TestCliCatalog.Targets)
        {
            table.AddRow(
                target.Id,
                target.Category,
                target.UsesPodman ? "Podman" : "Local");
        }

        AnsiConsole.Write(new Rule("[yellow]Targets[/]"));
        AnsiConsole.Write(table);
    }

    private static void RenderState(TestInfraRuntimeState? state)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Current State[/]"));

        if (state is null)
        {
            AnsiConsole.MarkupLine("[grey]No runtime state file is present.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Field")
            .AddColumn("Value");

        table.AddRow("Alias", state.AliasName ?? "(none)");
        table.AddRow("Host", state.Host);
        table.AddRow("Targets", string.Join(", ", state.Targets.Select(x => x.Id)));

        AnsiConsole.Write(table);
    }
}
