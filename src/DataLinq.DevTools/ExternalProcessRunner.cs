using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DataLinq.DevTools;

public static class ExternalProcessRunner
{
    private static readonly object ProcessErrorModeGate = new();

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
            using var process = StartProcess(startInfo, fileName);

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

    private static Process StartProcess(ProcessStartInfo startInfo, string fileName)
    {
        if (!OperatingSystem.IsWindows())
            return Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        lock (ProcessErrorModeGate)
        {
            var previousMode = SetErrorMode(ProcessErrorModeFlags);

            try
            {
                return Process.Start(startInfo)
                    ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
            }
            finally
            {
                SetErrorMode(previousMode);
            }
        }
    }

    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private const uint SemNoOpenFileErrorBox = 0x8000;
    private const uint ProcessErrorModeFlags = SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint uMode);
}
