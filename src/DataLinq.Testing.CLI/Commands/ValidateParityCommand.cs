using System;
using System.CommandLine;
using System.Linq;
using Spectre.Console;

namespace DataLinq.Testing.CLI;

internal static class ValidateParityCommand
{
    public static Command Create(TestInfraCliSettings settings)
    {
        var command = new Command("validate-parity", "Validates that every legacy test file is explicitly mapped to the new suite structure.");
        command.SetAction(_ =>
        {
            var exitCode = Execute(settings);
            if (exitCode != 0)
                Environment.ExitCode = exitCode;
        });

        return command;
    }

    public static int Execute(TestInfraCliSettings settings)
    {
        var result = TestSuiteParityValidator.Validate(settings.RepositoryRoot);

        AnsiConsole.Write(new Rule("[yellow]Parity Validation[/]"));

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Field")
            .AddColumn("Value");

        summary.AddRow("Manifest", result.ManifestPath);
        summary.AddRow("Discovered legacy test files", result.DiscoveredLegacyFiles.Count.ToString());
        summary.AddRow("Manifest entries", result.EntryCount.ToString());

        AnsiConsole.Write(summary);

        var statusTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Status")
            .AddColumn("Entries");

        foreach (var status in result.StatusCounts.OrderBy(x => x.Key, System.StringComparer.OrdinalIgnoreCase))
            statusTable.AddRow(status.Key, status.Value.ToString());

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Status Breakdown[/]"));
        AnsiConsole.Write(statusTable);

        if (!result.HasErrors)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Parity manifest validation passed.[/]");
            return 0;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[red]Parity Validation Errors[/]"));

        var errorTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Issue");

        foreach (var error in result.Errors)
            errorTable.AddRow(new Text(error));

        AnsiConsole.Write(errorTable);
        return 1;
    }
}
