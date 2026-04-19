using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DataLinq.Testing.CLI;

internal sealed class PodmanClient
{
    public string ExecutablePath { get; }

    public PodmanClient()
    {
        ExecutablePath = ResolveExecutablePath();
    }

    public void EnsureAvailable()
    {
        var result = Execute(["version", "--format", "json"]);
        result.ThrowIfFailed("The 'podman' command could not be executed.");
    }

    public PodmanCommandResult Execute(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start '{ExecutablePath}'.");

            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new PodmanCommandResult(process.ExitCode, standardOutput, standardError);
        }
        catch (Win32Exception exception)
        {
            var accessDenied = exception.NativeErrorCode == 5;
            var configuredByEnvironment = !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(DataLinq.Testing.PodmanTestEnvironmentSettings.PodmanExecutablePathEnvironmentVariable));
            var configurationHint = configuredByEnvironment
                ? $" Update '{DataLinq.Testing.PodmanTestEnvironmentSettings.PodmanExecutablePathEnvironmentVariable}' to a sandbox-accessible Podman executable."
                : $" Install Podman on PATH or set '{DataLinq.Testing.PodmanTestEnvironmentSettings.PodmanExecutablePathEnvironmentVariable}' to a sandbox-accessible Podman executable.";

            throw new InvalidOperationException(
                accessDenied
                    ? $"The sandbox could not execute the Podman binary '{ExecutablePath}' (access denied).{configurationHint}"
                    : $"Could not start the Podman executable '{ExecutablePath}'. Ensure Podman is installed and available.{configurationHint}",
                exception);
        }
    }

    private static string ResolveExecutablePath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(DataLinq.Testing.PodmanTestEnvironmentSettings.PodmanExecutablePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var defaultWindowsPath = Path.Combine(localAppData, "Programs", "Podman", "podman.exe");
            if (File.Exists(defaultWindowsPath))
                return defaultWindowsPath;
        }

        return "podman";
    }
}
