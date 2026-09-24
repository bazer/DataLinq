using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DataLinq.Tests.MySql;

// Test-only, plaintext/uncompressed classic-protocol relay. It forwards complete
// packets unchanged, except for one explicitly armed completion acknowledgement.
// It does not emulate a transaction, command result or driver exception.
internal sealed class NativeCompletionProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentBag<Task> connections = [];
    private readonly TaskCompletionSource responseHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> releaseResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string server;
    private readonly int port;
    private readonly string completionCommand;
    private readonly Task accepting;
    private int armed;
    private int completionCommands;

    internal NativeCompletionProxy(string server, int port, string completionCommand = "commit")
    {
        if (completionCommand is not ("commit" or "rollback")) throw new ArgumentOutOfRangeException(nameof(completionCommand));
        this.server = server;
        this.port = port;
        this.completionCommand = completionCommand;
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = AcceptAsync();
    }

    internal int Port { get; }
    internal int CompletionCommands => Volatile.Read(ref completionCommands);
    internal void Arm() => Interlocked.Exchange(ref armed, 1);
    internal Task WaitForSuccessfulResponseAsync() => responseHeld.Task.WaitAsync(TimeSpan.FromSeconds(20));
    internal void LoseResponse() => releaseResponse.TrySetResult(true);
    internal void ForwardResponse() => releaseResponse.TrySetResult(false);

    private async Task AcceptAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                connections.Add(RelayAsync(client));
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }

    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var upstream = new TcpClient())
        using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            Task? requests = null;
            Task? responses = null;
            try
            {
                await upstream.ConnectAsync(server, port, lifetime.Token);
                var downstreamStream = client.GetStream();
                var upstreamStream = upstream.GetStream();
                var awaitingCompletionResponse = 0;
                requests = ForwardAsync(downstreamStream, upstreamStream, false);
                responses = ForwardAsync(upstreamStream, downstreamStream, true);
                var firstFinished = await Task.WhenAny(requests, responses);
                await firstFinished;

                async Task ForwardAsync(NetworkStream input, NetworkStream output, bool fromServer)
                {
                    while (await ReadPacketAsync(input, lifetime.Token) is { } packet)
                    {
                        if (!fromServer && IsCompletion(packet))
                        {
                            Interlocked.Increment(ref completionCommands);
                            if (Interlocked.Exchange(ref armed, 0) == 1)
                                Volatile.Write(ref awaitingCompletionResponse, 1);
                        }
                        if (fromServer && Interlocked.Exchange(ref awaitingCompletionResponse, 0) == 1)
                        {
                            // Require the real successful completion response.
                            if (packet.Length < 5 || packet[4] != 0x00)
                                throw new InvalidDataException("The armed completion did not return an OK packet.");
                            responseHeld.TrySetResult();
                            if (await releaseResponse.Task.WaitAsync(lifetime.Token))
                                return; // Close both sockets without forwarding the OK.
                        }
                        await output.WriteAsync(packet, lifetime.Token);
                    }
                }
            }
            catch (Exception failure) when (failure is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Peer shutdown and the deliberate lost response both terminate
                // the relay. Any active caller still observes the native socket result.
            }
            catch (Exception failure)
            {
                responseHeld.TrySetException(failure);
                throw;
            }
            finally
            {
                lifetime.Cancel();
                client.Close();
                upstream.Close();
                if (requests is not null && responses is not null)
                {
                    try { await Task.WhenAll(requests, responses); }
                    catch (Exception failure) when (failure is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
        }
    }

    private bool IsCompletion(byte[] packet)
    {
        if (packet.Length < 6 || packet[3] != 0 || packet[4] != 0x03) return false;
        var offset = 5;
        // CLIENT_QUERY_ATTRIBUTES adds zero parameters and one parameter set to
        // this attribute-free command. MariaDB uses the original command layout.
        if (packet.Length > 7 && packet[5] == 0 && packet[6] == 1) offset = 7;
        return string.Equals(Encoding.UTF8.GetString(packet, offset, packet.Length - offset).Trim().TrimEnd(';'),
            completionCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> ReadPacketAsync(NetworkStream stream, CancellationToken token)
    {
        var header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), token) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        var length = header[0] | header[1] << 8 | header[2] << 16;
        var packet = new byte[length + 4];
        header.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(packet.AsMemory(4), token);
        return packet;
    }

    public async ValueTask DisposeAsync()
    {
        LoseResponse();
        stop.Cancel();
        listener.Stop();
        try { await accepting; await Task.WhenAll(connections); }
        finally { stop.Dispose(); }
    }
}
