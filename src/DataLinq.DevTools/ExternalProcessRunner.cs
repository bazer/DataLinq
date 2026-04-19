using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace DataLinq.DevTools;

public static class ExternalProcessRunner
{
    public static ExternalCommandResult Execute(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        if (environmentVariables is not null)
        {
            foreach (var pair in environmentVariables)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            stopwatch.Stop();

            return new ExternalCommandResult(process.ExitCode, standardOutput, standardError)
            {
                Duration = stopwatch.Elapsed
            };
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Could not start '{fileName}'.", exception);
        }
    }
}
