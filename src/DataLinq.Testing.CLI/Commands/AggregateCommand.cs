using System;
using System.CommandLine;
using System.IO;
using DataLinq.DevTools;

namespace DataLinq.Testing.CLI;

internal static class AggregateCommand
{
    public static Command Create(TestInfraCliSettings settings)
    {
        var inputRootOption = new Option<string>("--input-root")
        {
            Description = "Directory containing downloaded shard artifacts and *-summary.json reports.",
            Required = true
        };
        var outputOption = new Option<string>("--output")
        {
            Description = "Aggregate JSON destination beneath the repository artifacts directory.",
            Required = true
        };
        var commitOption = new Option<string>("--commit-sha")
        {
            Description = "Exact checkout commit required for every shard.",
            Required = true
        };
        var configurationOption = new Option<string>("--configuration")
        {
            Description = "Exact build configuration required for every shard.",
            DefaultValueFactory = _ => "Release"
        };

        var command = new Command("aggregate", "Validates and combines one-target CI shard summaries.");
        command.Options.Add(inputRootOption);
        command.Options.Add(outputOption);
        command.Options.Add(commitOption);
        command.Options.Add(configurationOption);
        command.SetAction(parseResult => CommandHelpers.ExecuteSafely(() =>
        {
            var inputRoot = Path.GetFullPath(parseResult.GetValue(inputRootOption)!);
            var output = Path.GetFullPath(parseResult.GetValue(outputOption)!);
            var artifactRoot = Path.GetFullPath(Path.Combine(settings.RepositoryRoot, "artifacts"));
            if (!output.StartsWith(artifactRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The aggregate output must remain beneath the repository artifacts directory.");

            var aggregate = TestShardEvidenceAggregator.AggregateDirectory(
                inputRoot,
                parseResult.GetValue(commitOption)!,
                parseResult.GetValue(configurationOption)!);
            TestShardEvidenceAggregator.Write(aggregate, output);
            Console.WriteLine(
                $"Validated {aggregate.Shards.Count} full-matrix shards and {aggregate.TotalCases} cases for {aggregate.CommitSha}.");
        }));

        return command;
    }
}
