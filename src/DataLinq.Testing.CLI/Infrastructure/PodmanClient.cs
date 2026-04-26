using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DataLinq.Testing.CLI;

internal sealed class PodmanClient
{
    private readonly IPodmanTransport primaryTransport;
    private readonly IPodmanTransport? fallbackTransport;

    public string ExecutablePath { get; }
    public string? SocketPath { get; }

    public PodmanClient()
    {
        ExecutablePath = ResolveExecutablePath();
        SocketPath = ResolveSocketPath();
        (primaryTransport, fallbackTransport) = CreateTransportSelection(ExecutablePath, SocketPath);
    }

    public void EnsureAvailable()
    {
        var result = Execute(["version", "--format", "json"]);
        result.ThrowIfFailed($"The {primaryTransport.Description} transport could not be executed.");
    }

    public PodmanCommandResult Execute(IReadOnlyList<string> arguments)
    {
        try
        {
            var result = primaryTransport.Execute(arguments);
            if (fallbackTransport is not null && PodmanSocketTransport.IsUnsupported(result))
                return TryFallback(arguments, result);

            return result;
        }
        catch (PodmanTransportUnavailableException) when (fallbackTransport is not null)
        {
            return TryFallback(arguments);
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

    private static string? ResolveSocketPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable(DataLinq.Testing.PodmanTestEnvironmentSettings.PodmanSocketPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
            return configuredPath;

        var containerHost = Environment.GetEnvironmentVariable("CONTAINER_HOST");
        if (!string.IsNullOrWhiteSpace(containerHost)
            && Uri.TryCreate(containerHost, UriKind.Absolute, out var containerHostUri)
            && string.Equals(containerHostUri.Scheme, "unix", StringComparison.OrdinalIgnoreCase))
        {
            return containerHostUri.LocalPath;
        }

        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var windowsDefault = Path.Combine(localAppData, "Temp", "podman", "podman-machine-default-api.sock");
            return File.Exists(windowsDefault) ? windowsDefault : null;
        }

        var xdgRuntimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntimeDirectory))
        {
            var runtimeSocket = Path.Combine(xdgRuntimeDirectory, "podman", "podman.sock");
            if (File.Exists(runtimeSocket))
                return runtimeSocket;
        }

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                var runtimeSocket = $"/run/user/{GetCurrentUserId()}/podman/podman.sock";
                if (File.Exists(runtimeSocket))
                    return runtimeSocket;
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        return null;
    }

    private static (IPodmanTransport Primary, IPodmanTransport? Fallback) CreateTransportSelection(string executablePath, string? socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
            return (new PodmanCliTransport(executablePath), null);

        var socketTransport = new PodmanSocketTransport(socketPath);
        var cliTransport = new PodmanCliTransport(executablePath);

        return OperatingSystem.IsWindows()
            ? (socketTransport, cliTransport)
            : (cliTransport, socketTransport);
    }

    private PodmanCommandResult TryFallback(IReadOnlyList<string> arguments, PodmanCommandResult? preferredFailure = null)
    {
        if (fallbackTransport is null)
            throw new InvalidOperationException("A Podman fallback transport was requested, but none is available.");

        try
        {
            return fallbackTransport.Execute(arguments);
        }
        catch (PodmanTransportUnavailableException) when (preferredFailure is not null)
        {
            return preferredFailure;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetCurrentUserId();
}
