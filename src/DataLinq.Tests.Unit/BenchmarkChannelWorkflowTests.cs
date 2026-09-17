using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class BenchmarkChannelWorkflowTests
{
    [Test]
    public Task Website_SeparatesBranchesRuntimesAndArchivedHistory() =>
        RunFixture("node", "benchmark-channels.mjs");

    [Test]
    public Task Publication_PreservesIdentityRetentionAndChannelPolicy() =>
        RunFixture(OperatingSystem.IsWindows() ? "py" : "python3", "benchmark_channels.py");

    private static async Task RunFixture(string executable, string fixture)
    {
        var root = RepositoryRootLocator.Find();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(root, "src", "DataLinq.Tests.Unit", "Fixtures", fixture));
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        var diagnostic = await output + await error;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{fixture} exited {process.ExitCode}: {diagnostic}");
    }
}
