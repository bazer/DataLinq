using System;
using System.CommandLine;
using System.Threading.Tasks;

namespace DataLinq.Dev.CLI;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var settings = DevCliSettings.FromAppContext();

        var rootCommand = new RootCommand("Developer environment and build wrapper CLI for DataLinq.");
        rootCommand.Subcommands.Add(DoctorCommand.Create(settings));
        rootCommand.Subcommands.Add(RestoreCommand.Create(settings));
        rootCommand.Subcommands.Add(BuildCommand.Create(settings));
        rootCommand.Subcommands.Add(TestCommand.Create(settings));
        rootCommand.Subcommands.Add(ExecCommand.Create(settings));

        var exitCode = await rootCommand.Parse(args).InvokeAsync();
        return Environment.ExitCode != 0 ? Environment.ExitCode : exitCode;
    }
}
