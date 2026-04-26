using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace DataLinq.Testing.CLI;

internal sealed class PodmanCliTransport(string executablePath) : IPodmanTransport
{
    public string Description => $"Podman executable '{executablePath}'";

    public PodmanCommandResult Execute(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
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
                ?? throw new PodmanTransportUnavailableException($"Failed to start '{executablePath}'.");

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

            throw new PodmanTransportUnavailableException(
                accessDenied
                    ? $"The sandbox could not execute the Podman binary '{executablePath}' (access denied).{configurationHint}"
                    : $"Could not start the Podman executable '{executablePath}'. Ensure Podman is installed and available.{configurationHint}",
                exception);
        }
    }
}
