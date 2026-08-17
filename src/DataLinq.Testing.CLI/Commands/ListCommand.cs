using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.CommandLine;
using System.Text.Json;
using Spectre.Console;

namespace DataLinq.Testing.CLI;

internal static class ListCommand
{
    public static Command Create(TestInfraRuntimeStateStore stateStore, TestInfraCliSettings settings)
    {
        var planOption = new Option<string?>("--plan")
        {
            Description = "Shows the detailed suite and target contract for one run plan."
        };
        var command = new Command("list", "Lists run plans, suites, target sets, and infrastructure state.");
        command.Options.Add(planOption);

        command.SetAction(parseResult => CommandHelpers.ExecuteSafely(() =>
        {
            var planName = parseResult.GetValue(planOption);
            if (string.IsNullOrWhiteSpace(planName))
                Render(stateStore, settings.RepositoryRoot);
            else
                RenderPlan(TestCliRunPlanCatalog.GetPlan(planName), settings.RepositoryRoot);
        }));

        return command;
    }

    public static void Render(TestInfraRuntimeStateStore stateStore, string repositoryRoot)
    {
        RenderPlans(repositoryRoot);
        AnsiConsole.WriteLine();
        RenderSuites();
        AnsiConsole.WriteLine();
        RenderAliases();
        AnsiConsole.WriteLine();
        RenderTargets();
        RenderState(stateStore.Load());
    }

    private static void RenderPlans(string repositoryRoot)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Plan")
            .AddColumn("Intent")
            .AddColumn("Warm budget")
            .AddColumn("Cases")
            .AddColumn("Estimated / last");

        foreach (var plan in TestCliRunPlanCatalog.Plans)
        {
            var evidence = ReadLastMeasurement(repositoryRoot, plan.Name);
            table.AddRow(
                plan.Name,
                plan.Description,
                $"{plan.WarmBudgetSeconds}s",
                plan.RequiresExplicitSelection ? "filter-dependent" : $"~{plan.ExpectedCaseCount}",
                evidence is null
                    ? $"~{plan.EstimatedDurationSeconds:0}s / -"
                    : $"~{plan.EstimatedDurationSeconds:0}s / {evidence.WarmDurationSeconds:0.0}s warm ({evidence.TotalDurationSeconds:0.0}s total, {evidence.Total?.ToString() ?? "?"} cases)");
        }

        AnsiConsole.Write(new Rule("[yellow]Run Plans[/]"));
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Use 'list --plan <name>' for the exact suite, target, prerequisite, and evidence contract.[/]");
    }

    private static void RenderPlan(TestCliRunPlan plan, string repositoryRoot)
    {
        var evidence = ReadLastMeasurement(repositoryRoot, plan.Name);
        var facts = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Field")
            .AddColumn("Value");
        facts.AddRow("Command", plan.Command);
        facts.AddRow("Intent", plan.Description);
        facts.AddRow("Prerequisites", plan.Prerequisites);
        facts.AddRow("Warm budget", $"{plan.WarmBudgetSeconds}s");
        facts.AddRow("Default targets", plan.DefaultTargetAlias is not null
            ? $"target set '{plan.DefaultTargetAlias}'"
            : string.Join(", ", plan.DefaultTargetIds));
        facts.AddRow("Expected cases", plan.RequiresExplicitSelection ? "filter-dependent" : $"~{plan.ExpectedCaseCount}");
        facts.AddRow("Estimated duration", $"~{plan.EstimatedDurationSeconds:0.0}s");
        facts.AddRow("Last measurement", evidence is null
            ? "not recorded"
            : $"{evidence.WarmDurationSeconds:0.0}s warm host / {evidence.TotalDurationSeconds:0.0}s total, {evidence.Total?.ToString() ?? "?"} cases, {evidence.Outcome}, {evidence.CompletedAtUtc:O}");

        AnsiConsole.Write(new Rule($"[yellow]Plan: {Markup.Escape(plan.Name)}[/]"));
        AnsiConsole.Write(facts);

        var suites = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Suite")
            .AddColumn("Purpose")
            .AddColumn("Resource")
            .AddColumn("Cases")
            .AddColumn("Estimate")
            .AddColumn("TUnit filter");
        if (plan.RequiresExplicitSelection)
        {
            suites.AddRow("<explicit>", "code under change", "suite-dependent", "?", "?", "required");
        }
        else
        {
            foreach (var suite in plan.Suites)
            {
                suites.AddRow(
                    suite.Suite,
                    suite.Purpose,
                    suite.Resource,
                    $"~{suite.ExpectedCaseCount}",
                    $"~{suite.EstimatedDurationSeconds:0.0}s",
                    suite.Filter is null
                        ? "all tests"
                        : $"{suite.Filter.Count(static character => character == '|') + 1} explicit method(s)");
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(suites);
    }

    private static LastPlanMeasurement? ReadLastMeasurement(string repositoryRoot, string planName)
    {
        var path = TestCliRunPlanCatalog.GetLastSummaryPath(repositoryRoot, planName);
        if (!File.Exists(path))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var invocation = root.GetProperty("Invocation");
            if (!invocation.TryGetProperty("Plan", out var recordedPlan) ||
                !string.Equals(recordedPlan.GetString(), planName, StringComparison.OrdinalIgnoreCase))
                return null;

            return new LastPlanMeasurement(
                root.GetProperty("DurationSeconds").GetDouble(),
                root.GetProperty("Timings").GetProperty("TestHostProcessSeconds").GetDouble(),
                root.TryGetProperty("Total", out var total) && total.ValueKind == JsonValueKind.Number ? total.GetInt32() : null,
                root.GetProperty("Outcome").GetString() ?? "unknown",
                root.GetProperty("CompletedAtUtc").GetDateTimeOffset());
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or IOException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static void RenderSuites()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Suite")
            .AddColumn("Description")
            .AddColumn("Project");

        foreach (var suite in TestCliSuiteCatalog.Suites)
        {
            table.AddRow(
                suite.Name,
                suite.Description,
                suite.ProjectPath);
        }

        table.AddRow(
            TestCliSuiteCatalog.AllSuites,
            "Runs the generators, unit, and Memory lanes once, then the compliance and MySQL/MariaDB lanes against the selected targets.",
            "(composite)");

        AnsiConsole.Write(new Rule("[yellow]Suites[/]"));
        AnsiConsole.Write(table);
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

        AnsiConsole.Write(new Rule("[yellow]Provider Target Sets (Aliases)[/]"));
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

    private sealed record LastPlanMeasurement(
        double TotalDurationSeconds,
        double WarmDurationSeconds,
        int? Total,
        string Outcome,
        DateTimeOffset CompletedAtUtc);
}
