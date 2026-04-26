using System;
using System.CommandLine;
using System.Threading.Tasks;

namespace DataLinq.Benchmark.CLI;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var settings = BenchmarkCliSettings.FromAppContext();

        var rootCommand = new RootCommand("Cross-platform benchmark harness CLI for DataLinq.");
        rootCommand.Subcommands.Add(ListCommand.Create(settings));
        rootCommand.Subcommands.Add(RunCommand.Create(settings));

        if (args.Length == 0)
            args = ["run"];

        var exitCode = await rootCommand.Parse(args).InvokeAsync();
        return Environment.ExitCode != 0 ? Environment.ExitCode : exitCode;
    }
}
