using System;
using System.Linq;
using Spectre.Console;

namespace DataLinq.Testing.CLI;

internal static class InteractiveCliRunner
{
    public static int RunRoot(TestInfraOrchestrator orchestrator, TestInfraCliSettings settings, TestInfraRuntimeStateStore stateStore)
    {
        var command = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose a [green]test infrastructure[/] command")
                .PageSize(10)
                .AddChoices("list", "up", "wait", "down", "reset", "run"));

        return command switch
        {
            "list" => RunList(stateStore),
            "up" => RunUp(orchestrator),
            "wait" => RunWait(orchestrator),
            "down" => RunDown(orchestrator),
            "reset" => RunReset(orchestrator),
            "run" => RunTests(orchestrator, settings),
            _ => throw new InvalidOperationException($"Unsupported interactive command '{command}'.")
        };
    }

    public static int RunList(TestInfraRuntimeStateStore stateStore)
    {
        ListCommand.Render(stateStore);
        return 0;
    }

    public static int RunUp(TestInfraOrchestrator orchestrator)
    {
        var selection = PromptSelection(defaultAlias: TestTargetCatalog.LatestAlias);
        var recreate = AnsiConsole.Prompt(new ConfirmationPrompt("Recreate selected server targets first?")
        {
            DefaultValue = false
        });
        orchestrator.Up(selection, recreate);
        return 0;
    }

    public static int RunWait(TestInfraOrchestrator orchestrator)
    {
        var selection = PromptSelection(defaultAlias: TestTargetCatalog.LatestAlias);
        orchestrator.Wait(selection);
        return 0;
    }

    public static int RunDown(TestInfraOrchestrator orchestrator)
    {
        var scope = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What should [green]down[/] act on?")
                .AddChoices("all-running", "alias", "custom-targets"));

        CliTargetSelection? selection = scope switch
        {
            "all-running" => null,
            "alias" => PromptAliasSelection(),
            "custom-targets" => PromptCustomTargetSelection(),
            _ => null
        };

        var remove = AnsiConsole.Prompt(new ConfirmationPrompt("Remove the containers after stopping them?")
        {
            DefaultValue = false
        });
        orchestrator.Down(remove, selection);
        return 0;
    }

    public static int RunReset(TestInfraOrchestrator orchestrator)
    {
        var selection = PromptSelection(defaultAlias: TestTargetCatalog.LatestAlias);
        orchestrator.Reset(selection);
        return 0;
    }

    public static int RunTests(TestInfraOrchestrator orchestrator, TestInfraCliSettings settings)
    {
        var suite = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Which test suite should run?")
                .AddChoices(
                    TestCliSuiteCatalog.AllSuites,
                    TestCliSuiteCatalog.GeneratorsSuite,
                    TestCliSuiteCatalog.UnitSuite,
                    TestCliSuiteCatalog.ComplianceSuite,
                    TestCliSuiteCatalog.MySqlSuite));
        var selection = string.Equals(suite, TestCliSuiteCatalog.UnitSuite, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(suite, TestCliSuiteCatalog.GeneratorsSuite, StringComparison.OrdinalIgnoreCase)
            ? TargetSelectionResolver.ResolveAlias(TestTargetCatalog.LatestAlias)
            : PromptSelection(defaultAlias: TestTargetCatalog.LatestAlias);
        var configuration = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Build configuration")
                .AddChoices("Debug", "Release"));
        var build = AnsiConsole.Prompt(new ConfirmationPrompt("Build before running tests?")
        {
            DefaultValue = false
        });
        var batchSize = AnsiConsole.Prompt(
            new TextPrompt<int>("Batch size")
                .DefaultValue(2)
                .Validate(value => value is >= 1 and <= 32
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Batch size must be between 1 and 32.[/]")));
        var parallelSuites = AnsiConsole.Prompt(new ConfirmationPrompt("Run selected suites in parallel?")
        {
            DefaultValue = false
        });
        var tearDown = AnsiConsole.Prompt(new ConfirmationPrompt("Stop provisioned server targets after the run?")
        {
            DefaultValue = false
        });

        RunCommand.Execute(orchestrator, settings, selection, suite, null, configuration, build, batchSize, parallelSuites, tearDown, summaryJsonPath: null);
        return 0;
    }

    public static CliTargetSelection PromptSelection(string defaultAlias)
    {
        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("How should targets be selected?")
                .AddChoices("alias", "custom-targets"));

        return mode switch
        {
            "alias" => PromptAliasSelection(defaultAlias),
            "custom-targets" => PromptCustomTargetSelection(),
            _ => throw new InvalidOperationException($"Unsupported selection mode '{mode}'.")
        };
    }

    private static CliTargetSelection PromptAliasSelection(string defaultAlias = TestTargetCatalog.LatestAlias)
    {
        var alias = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Choose an alias")
                .PageSize(10)
                .AddChoices(TestCliCatalog.Aliases.Select(x => x.Name))
                .UseConverter(x => x == defaultAlias ? $"{x} (default)" : x));

        return TargetSelectionResolver.ResolveAlias(alias.Replace(" (default)", string.Empty, StringComparison.Ordinal));
    }

    private static CliTargetSelection PromptCustomTargetSelection()
    {
        while (true)
        {
            var selectedTargets = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Choose targets")
                    .NotRequired()
                    .InstructionsText("[grey](Press [blue]<space>[/] to toggle a target, [green]<enter>[/] to accept)[/]")
                    .AddChoices(TestCliCatalog.Targets.Select(x => x.Id)));

            if (selectedTargets.Count > 0)
                return TargetSelectionResolver.ResolveTargets(selectedTargets);

            AnsiConsole.MarkupLine("[red]Select at least one target.[/]");
        }
    }
}
