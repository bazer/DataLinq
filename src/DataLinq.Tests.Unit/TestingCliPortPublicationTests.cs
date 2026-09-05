using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.DevTools;
using DataLinq.Testing;
using DataLinq.Testing.CLI;

namespace DataLinq.Tests.Unit;

public sealed class TestingCliPortPublicationTests
{
    [Test]
    [Arguments(null, "127.0.0.1", "127.0.0.1:13307:3306")]
    [Arguments("::1", "::1", "[::1]:13307:3306")]
    [Arguments("0.0.0.0", "0.0.0.0", "0.0.0.0:13307:3306")]
    public async Task CliAndSocketRequestsPreserveTheChosenBindAddress(string? configuredBind, string expectedHost, string expectedMapping)
    {
        var root = RepositoryRootLocator.Find();
        var settings = new TestInfraCliSettings(root, root, DevToolPaths.Create(root), "unused", "fixture",
            "fixture", "fixture", "fixture", "fixture", "fixture", 10);
        if (configuredBind is not null)
            settings = settings with { HostBindAddress = configuredBind };
        var target = new DatabaseServerTarget("fixture", "fixture", DatabaseServerFamily.MySql, "8.4", "fixture:latest", 13307, true, false);
        var arguments = TestInfraOrchestrator.CreateContainerArguments(settings, target, "fixture").ToArray();
        await Assert.That(arguments[Array.IndexOf(arguments, "-p") + 1]).IsEqualTo(expectedMapping);

        // Capture the actual HTTP request from the production socket transport.
        var socketPath = Path.Combine(Path.GetTempPath(), $"dl-{Guid.NewGuid():N}.sock");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(socketPath));
        server.Listen(2);
        var requests = ReceiveRequests(server, cancellation.Token);
        try
        {
            var result = await Task.Run(() => new PodmanSocketTransport(socketPath).Execute(arguments));
            await Assert.That(result.ExitCode).IsEqualTo(0);
            using var request = JsonDocument.Parse(await requests);
            var binding = request.RootElement.GetProperty("HostConfig").GetProperty("PortBindings").GetProperty("3306/tcp")[0];
            await Assert.That(binding.GetProperty("HostIp").GetString()).IsEqualTo(expectedHost);
            await Assert.That(binding.GetProperty("HostPort").GetString()).IsEqualTo("13307");
        }
        finally
        {
            await cancellation.CancelAsync();
            try { await requests; } catch (OperationCanceledException) { }
            server.Dispose();
            File.Delete(socketPath);
        }
    }

    [Test]
    [Arguments("13307:3306")]
    [Arguments(":13307:3306")]
    [Arguments("localhost:13307:3306")]
    [Arguments("127.0.0.1:0:3306")]
    [Arguments("127.0.0.1:65536:3306")]
    [Arguments("127.0.0.1:13307:3307")]
    public async Task AmbiguousOrInvalidPublicationIsRejectedBeforeConnecting(string mapping)
    {
        await Assert.That(() => new PodmanSocketTransport("unused").Execute(
            ["run", "-d", "--name", "fixture", "-p", mapping, "fixture:latest"]))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("127.0.0.1", null, "127.0.0.1")]
    [Arguments("::1", null, "::1")]
    [Arguments("0.0.0.0", null, "127.0.0.1")]
    [Arguments("::", null, "::1")]
    [Arguments("192.0.2.4", null, "192.0.2.4")]
    [Arguments("127.0.0.1", "remote.example", "remote.example")]
    public async Task ReadinessUsesLocalPublicationUnlessTheHostIsOverridden(string bind, string? configuredHost, string expected)
    {
        await Assert.That(PodmanHostResolver.Resolve(bind, configuredHost)).IsEqualTo(expected);
    }

    private static async Task<string> ReceiveRequests(Socket server, CancellationToken cancellationToken)
    {
        string? createBody = null;
        for (var requestIndex = 0; requestIndex < 2; requestIndex++)
        {
            using var connection = await server.AcceptAsync(cancellationToken);
            using var stream = new NetworkStream(connection, ownsSocket: false);
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken);
            var expectedPath = requestIndex == 0 ? "/containers/create?name=fixture" : "/containers/fixture-id/start";
            if (requestLine != $"POST {expectedPath} HTTP/1.1")
                throw new InvalidOperationException($"Unexpected request: {requestLine}");
            var contentLength = 0;
            while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } header)
                if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(header["Content-Length:".Length..].Trim());
            var body = new char[contentLength];
            var received = 0;
            while (received < body.Length)
            {
                var count = await reader.ReadAsync(body.AsMemory(received), cancellationToken);
                if (count == 0) throw new IOException("Incomplete HTTP request body.");
                received += count;
            }
            if (requestIndex == 0)
                createBody = new string(body);
            var responseBody = requestIndex == 0 ? "{\"Id\":\"fixture-id\"}" : "";
            var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n{responseBody}");
            await stream.WriteAsync(response, cancellationToken);
        }
        return createBody ?? throw new InvalidOperationException("No container creation request was received.");
    }
}
