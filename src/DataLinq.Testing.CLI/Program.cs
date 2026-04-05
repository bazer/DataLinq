using System.CommandLine;
using System.Threading.Tasks;

namespace DataLinq.Testing.CLI;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var settings = TestInfraCliSettings.FromEnvironment();
        var stateStore = new TestInfraRuntimeStateStore(settings.StatePath);
        var orchestrator = new TestInfraOrchestrator(settings, new PodmanClient(), stateStore);

        if (args.Length == 0 || (args.Length == 1 && string.Equals(args[0], "--interactive", System.StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(InteractiveCliRunner.RunRoot(orchestrator, settings, stateStore));

        var rootCommand = new RootCommand("Cross-platform test infrastructure CLI for DataLinq.");
        rootCommand.Subcommands.Add(ListCommand.Create(stateStore));
        rootCommand.Subcommands.Add(UpCommand.Create(orchestrator));
        rootCommand.Subcommands.Add(WaitCommand.Create(orchestrator));
        rootCommand.Subcommands.Add(DownCommand.Create(orchestrator));
        rootCommand.Subcommands.Add(ResetCommand.Create(orchestrator));
        rootCommand.Subcommands.Add(RunCommand.Create(orchestrator, settings));

        return rootCommand.Parse(args).InvokeAsync();
    }
}
