using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.DevTools;

internal sealed record CapturedProcessOutput<T>(int ExitCode, T StandardOutput, string StandardError);

/// <summary>Drains both redirected pipes concurrently and owns failure-time process cleanup.</summary>
internal static class ProcessOutputCapture
{
    internal static Task<CapturedProcessOutput<string>> ReadTextAsync(
        Process process, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        ReadAsync(process, static (child, token) => child.StandardOutput.ReadToEndAsync(token), timeout, cancellationToken);

    internal static Task<CapturedProcessOutput<byte[]>> ReadBinaryAsync(
        Process process, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        ReadAsync(process, ReadBytesAsync, timeout, cancellationToken);

    private static async Task<byte[]> ReadBytesAsync(Process process, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(buffer, token).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static async Task<CapturedProcessOutput<T>> ReadAsync<T>(
        Process process,
        Func<Process, CancellationToken, Task<T>> readOutput,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } limit)
            lifetime.CancelAfter(limit);

        // Start both reads before awaiting either pipe or process exit. A child
        // may fill stderr while keeping stdout open (including binary stdout).
        var output = ReadOutputAsync();
        var error = ReadErrorAsync();
        try
        {
            var pending = new List<Task> { output, error, process.WaitForExitAsync(lifetime.Token) };
            while (pending.Count != 0)
            {
                var completed = await Task.WhenAny(pending).WaitAsync(lifetime.Token).ConfigureAwait(false);
                // Observe a failed drain immediately; WhenAll alone would wait
                // forever if the child kept the other pipe open after that fault.
                await completed.ConfigureAwait(false);
                pending.Remove(completed);
            }
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (Exception failure)
        {
            lifetime.Cancel();
            Exception? cleanupFailure = null;
            try
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited)
                {
                    // Natural exit can win the race with Kill.
                }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            // Closing the pipes also releases reads when a descendant inherited
            // a handle after the immediate child exited. Observe both tasks.
            process.StandardOutput.Dispose();
            process.StandardError.Dispose();
            try
            {
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException)
            {
            }
            catch (Exception exception)
            {
                cleanupFailure ??= exception;
            }

            if (cleanupFailure is not null)
                throw new AggregateException("Process output capture failed and child cleanup also failed.", failure, cleanupFailure);
            if (failure is OperationCanceledException && !cancellationToken.IsCancellationRequested && timeout.HasValue)
                throw new TimeoutException($"The child process exceeded its {timeout.Value} timeout.", failure);
            throw;
        }

        async Task<T> ReadOutputAsync() => await readOutput(process, lifetime.Token).ConfigureAwait(false);
        async Task<string> ReadErrorAsync() => await process.StandardError.ReadToEndAsync(lifetime.Token).ConfigureAwait(false);
    }
}
