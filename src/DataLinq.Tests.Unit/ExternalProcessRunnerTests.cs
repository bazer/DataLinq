using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class ExternalProcessRunnerTests
{
    private const string ChildMode = "DATALINQ_TEST_PROCESS_CAPTURE_CHILD";
    private const int PayloadLength = 2 * 1024 * 1024;

    // Reuse the built test executable as an isolated child fixture. The marker
    // is set only in the child's environment, before test discovery can run.
    [ModuleInitializer]
    internal static void RunChildFixture()
    {
        var mode = Environment.GetEnvironmentVariable(ChildMode);
        if (mode is not ("text" or "binary" or "wait"))
            return;

        if (mode == "wait")
        {
            Console.WriteLine("ready");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }

        // This fills stderr before stdout closes: sequential drains deadlock.
        Console.Error.Write(new string('e', PayloadLength));
        Console.Error.Flush();
        if (mode == "binary")
        {
            using var stdout = Console.OpenStandardOutput();
            var payload = new byte[PayloadLength];
            for (var i = 0; i < payload.Length; i++)
                payload[i] = (byte)i;
            stdout.Write(payload);
        }
        else
        {
            Console.Write(new string('o', PayloadLength));
            Console.Out.Flush();
        }

        Environment.Exit(37);
    }

    [Test]
    public async Task ExecuteAsync_DrainsBothPipesAndPreservesNonzeroExit()
    {
        var result = await ExternalProcessRunner.ExecuteAsync(
            DotnetPath, ChildArguments, environmentVariables: new Dictionary<string, string?> { [ChildMode] = "text" },
            timeout: TimeSpan.FromSeconds(20));
        await Assert.That(result.ExitCode).IsEqualTo(37);
        await Assert.That(result.StandardOutput).IsEqualTo(new string('o', PayloadLength));
        await Assert.That(result.StandardError).IsEqualTo(new string('e', PayloadLength));
        await Assert.That(result.Duration > TimeSpan.Zero).IsTrue();
    }

    [Test]
    public async Task BinaryCapture_DrainsStderrWithoutDecodingStdout()
    {
        using var child = StartChild("binary");
        var process = child.Process;
        var result = await ProcessOutputCapture.ReadBinaryAsync(process, TimeSpan.FromSeconds(20));
        await Assert.That(result.ExitCode).IsEqualTo(37);
        await Assert.That(result.StandardError.Length).IsEqualTo(PayloadLength);
        await Assert.That(result.StandardOutput.Length).IsEqualTo(PayloadLength);
        await Assert.That(result.StandardOutput.Where((value, index) => value != (byte)index).Any()).IsFalse();
    }

    [Test]
    public async Task Timeout_KillsAndReapsTheChild()
    {
        using var child = StartChild("wait");
        var process = child.Process;
        await Assert.That(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo("ready");
        await Assert.That(async () => { await ProcessOutputCapture.ReadTextAsync(process, TimeSpan.FromMilliseconds(100)); })
            .Throws<TimeoutException>();
        await Assert.That(process.HasExited).IsTrue();
    }

    [Test]
    public async Task Cancellation_KillsAndReapsTheChild()
    {
        using var child = StartChild("wait");
        var process = child.Process;
        await Assert.That(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo("ready");
        using var cancellation = new CancellationTokenSource();
        var capture = ProcessOutputCapture.ReadBinaryAsync(process, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.That(async () => { await capture; }).Throws<OperationCanceledException>();
        await Assert.That(process.HasExited).IsTrue();
    }

    [Test]
    public async Task ExecuteAsync_PreCancelledCallDoesNotStartAProcess()
    {
        await Assert.That(async () => { await ExternalProcessRunner.ExecuteAsync(
            "missing-executable-that-must-not-be-started", [], cancellationToken: new CancellationToken(true)); })
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task PipeReadFailure_KillsTheChildAndPreservesTheFailure()
    {
        using var child = StartChild("wait");
        var process = child.Process;
        await Assert.That(await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo("ready");
        process.StandardError.Dispose();
        await Assert.That(async () => { await ProcessOutputCapture.ReadTextAsync(process, TimeSpan.FromSeconds(10)); })
            .Throws<ObjectDisposedException>();
        await Assert.That(process.HasExited).IsTrue();
    }

    private static string DotnetPath => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    private static string[] ChildArguments => [typeof(ExternalProcessRunnerTests).Assembly.Location];

    private static ChildLease StartChild(string mode)
    {
        var info = new ProcessStartInfo(DotnetPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in ChildArguments)
            info.ArgumentList.Add(argument);
        info.Environment[ChildMode] = mode;
        return new(Process.Start(info) ?? throw new InvalidOperationException("Could not start the pipe capture fixture."));
    }

    private sealed class ChildLease(Process process) : IDisposable
    {
        internal Process Process => process;
        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(10_000))
                    throw new TimeoutException("The child fixture could not be stopped.");
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
