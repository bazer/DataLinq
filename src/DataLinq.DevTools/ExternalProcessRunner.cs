using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.DevTools;

public static class ExternalProcessRunner
{
    private static readonly object ProcessErrorModeGate = new();

    public static ExternalCommandResult Execute(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null) =>
        ExecuteAsync(fileName, arguments, workingDirectory, environmentVariables).GetAwaiter().GetResult();

    public static async Task<ExternalCommandResult> ExecuteAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout is { } limit && (limit < TimeSpan.Zero || limit.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
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
            ApplyEnvironmentOverrides(startInfo.Environment, environmentVariables);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var process = StartProcess(startInfo, fileName);

            var captured = await ProcessOutputCapture.ReadTextAsync(process, timeout, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new ExternalCommandResult(captured.ExitCode, captured.StandardOutput, captured.StandardError)
            {
                Duration = stopwatch.Elapsed
            };
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Could not start '{fileName}'.", exception);
        }
    }

    internal static void ApplyEnvironmentOverrides(
        IDictionary<string, string?> environment,
        IReadOnlyDictionary<string, string?> overrides)
    {
        foreach (var pair in overrides)
        {
            foreach (var inheritedKey in environment.Keys
                         .Where(key => key.Equals(pair.Key, StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                environment.Remove(inheritedKey);
            }

            if (pair.Value is not null)
                environment[pair.Key] = pair.Value;
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
