using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DataLinq.Testing.CLI;

internal sealed class PodmanSocketTransport : IPodmanTransport
{
    private const int FailureExitCode = 125;
    private const string UnsupportedPrefix = "Socket transport does not support:";
    private const string HttpStatusMarker = "__PODMAN_HTTP_STATUS__:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string curlExecutable = OperatingSystem.IsWindows() ? "curl.exe" : "curl";

    public PodmanSocketTransport(string socketPath)
    {
        SocketPath = socketPath;
    }

    public string SocketPath { get; }

    public string Description => $"Podman socket '{SocketPath}'";

    public PodmanCommandResult Execute(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return Fail("No Podman command was provided.");

        try
        {
            return arguments[0] switch
            {
                "version" => ExecuteVersion(arguments),
                "start" => ExecuteContainerAction(arguments, "POST", static name => $"/containers/{Uri.EscapeDataString(name)}/start"),
                "stop" => ExecuteContainerAction(arguments, "POST", static name => $"/containers/{Uri.EscapeDataString(name)}/stop"),
                "kill" => ExecuteContainerAction(arguments, "POST", static name => $"/containers/{Uri.EscapeDataString(name)}/kill"),
                "rm" => ExecuteRemove(arguments),
                "inspect" => ExecuteInspect(arguments),
                "container" => ExecuteContainerCommand(arguments),
                "pod" => ExecutePodCommand(arguments),
                "exec" => ExecuteExec(arguments),
                "run" => ExecuteRun(arguments),
                "machine" => Unsupported(string.Join(" ", arguments)),
                _ => Unsupported(string.Join(" ", arguments))
            };
        }
        catch (PodmanTransportUnavailableException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"The Podman socket returned an unexpected response for '{string.Join(" ", arguments)}'.", exception);
        }
    }

    public static bool IsUnsupported(PodmanCommandResult result) =>
        result.ExitCode == FailureExitCode
        && result.StandardError.StartsWith(UnsupportedPrefix, StringComparison.Ordinal);

    private PodmanCommandResult ExecuteVersion(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is > 1 && arguments[1] == "--format" && arguments.Count > 2 && !string.Equals(arguments[2], "json", StringComparison.OrdinalIgnoreCase))
            return Unsupported(string.Join(" ", arguments));

        return Send("GET", "/version");
    }

    private PodmanCommandResult ExecuteContainerAction(
        IReadOnlyList<string> arguments,
        string method,
        Func<string, string> routeBuilder)
    {
        if (arguments.Count != 2)
            return Unsupported(string.Join(" ", arguments));

        return Send(method, routeBuilder(arguments[1]));
    }

    private PodmanCommandResult ExecuteRemove(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 3 || arguments[1] != "-f")
            return Unsupported(string.Join(" ", arguments));

        return Send("DELETE", $"/containers/{Uri.EscapeDataString(arguments[2])}?force=true");
    }

    private PodmanCommandResult ExecuteInspect(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 4 || arguments[1] != "--format" || arguments[2] != "{{.State.Running}}")
            return Unsupported(string.Join(" ", arguments));

        var response = SendRequest("GET", $"/containers/{Uri.EscapeDataString(arguments[3])}/json");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new PodmanCommandResult(1, string.Empty, ReadError(response));

        if (!response.IsSuccessStatusCode)
            return FailureFromResponse(response);

        var inspect = Deserialize<ContainerInspectResponse>(response);
        return new PodmanCommandResult(0, inspect.State?.Running == true ? "true" : "false", string.Empty);
    }

    private PodmanCommandResult ExecuteContainerCommand(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 3 || arguments[1] != "exists")
            return Unsupported(string.Join(" ", arguments));

        var response = SendRequest("GET", $"/containers/{Uri.EscapeDataString(arguments[2])}/json");
        return response.StatusCode switch
        {
            HttpStatusCode.OK => new PodmanCommandResult(0, string.Empty, string.Empty),
            HttpStatusCode.NotFound => new PodmanCommandResult(1, string.Empty, string.Empty),
            _ => FailureFromResponse(response)
        };
    }

    private PodmanCommandResult ExecutePodCommand(IReadOnlyList<string> arguments)
    {
        if (arguments.Count >= 3 && arguments[1] == "exists")
            return ExecutePodExists(arguments[2]);

        if (arguments.Count == 4 && arguments[1] == "rm" && arguments[2] == "-f")
            return Send("DELETE", $"/v5.0.0/libpod/pods/{Uri.EscapeDataString(arguments[3])}?force=true");

        return Unsupported(string.Join(" ", arguments));
    }

    private PodmanCommandResult ExecutePodExists(string podName)
    {
        var response = SendRequest("GET", "/v5.0.0/libpod/pods/json");
        if (!response.IsSuccessStatusCode)
            return FailureFromResponse(response);

        var pods = Deserialize<List<PodSummaryResponse>>(response) ?? [];
        var exists = pods.Any(x => string.Equals(x.Name, podName, StringComparison.Ordinal));
        return new PodmanCommandResult(exists ? 0 : 1, string.Empty, string.Empty);
    }

    private PodmanCommandResult ExecuteExec(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 3)
            return Unsupported(string.Join(" ", arguments));

        var containerName = arguments[1];
        var command = arguments.Skip(2).ToArray();

        var createResponse = SendRequest(
            "POST",
            $"/containers/{Uri.EscapeDataString(containerName)}/exec",
            new ExecCreateRequest(
                AttachStdout: true,
                AttachStderr: true,
                AttachStdin: false,
                Cmd: command,
                Tty: false));

        if (!createResponse.IsSuccessStatusCode)
            return FailureFromResponse(createResponse);

        var execCreate = Deserialize<ExecCreateResponse>(createResponse);
        if (string.IsNullOrWhiteSpace(execCreate.Id))
            return Fail($"Podman did not return an exec identifier for container '{containerName}'.");

        var startResponse = SendRequest(
            "POST",
            $"/exec/{Uri.EscapeDataString(execCreate.Id)}/start",
            new ExecStartRequest(Detach: false, Tty: false),
            captureBinaryBody: true);

        if (!startResponse.IsSuccessStatusCode)
            return FailureFromResponse(startResponse);

        var (standardOutput, standardError) = Demultiplex(startResponse.BodyBytes);

        var inspectResponse = SendRequest("GET", $"/exec/{Uri.EscapeDataString(execCreate.Id)}/json");
        if (!inspectResponse.IsSuccessStatusCode)
            return FailureFromResponse(inspectResponse);

        var execInspect = Deserialize<ExecInspectResponse>(inspectResponse);
        return new PodmanCommandResult(execInspect.ExitCode, standardOutput, standardError);
    }

    private PodmanCommandResult ExecuteRun(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2 || arguments[1] != "-d")
            return Unsupported(string.Join(" ", arguments));

        string? containerName = null;
        var environment = new List<string>();
        string? image = null;
        string? hostPort = null;
        var command = new List<string>();

        for (var index = 2; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (image is not null)
            {
                command.Add(argument);
                continue;
            }

            switch (argument)
            {
                case "--name":
                    containerName = ReadOptionValue(arguments, ref index, argument);
                    break;
                case "-p":
                    hostPort = ParseHostPort(ReadOptionValue(arguments, ref index, argument));
                    break;
                case "-e":
                    environment.Add(ReadOptionValue(arguments, ref index, argument));
                    break;
                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                        return Unsupported(string.Join(" ", arguments));

                    image = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(image) || string.IsNullOrWhiteSpace(hostPort))
            return Fail("The socket transport could not parse the requested 'podman run' arguments.");

        var createRequest = new ContainerCreateRequest(
            Image: image,
            Env: environment,
            Cmd: command,
            ExposedPorts: new Dictionary<string, object?>
            {
                ["3306/tcp"] = new { }
            },
            HostConfig: new HostConfigRequest(
                PortBindings: new Dictionary<string, PortBindingRequest[]>
                {
                    ["3306/tcp"] =
                    [
                        new PortBindingRequest(HostPort: hostPort, HostIp: "0.0.0.0")
                    ]
                }));

        var createResponse = SendCreateContainerRequest(containerName, createRequest);
        if (IsMissingImageError(createResponse))
        {
            var pullResult = PullImage(image);
            if (pullResult.ExitCode != 0)
                return pullResult;

            createResponse = SendCreateContainerRequest(containerName, createRequest);
        }

        if (!createResponse.IsSuccessStatusCode)
            return FailureFromResponse(createResponse);

        var createResult = Deserialize<CreateContainerResponse>(createResponse);
        if (string.IsNullOrWhiteSpace(createResult.Id))
            return Fail($"Podman did not return a container identifier for '{containerName}'.");

        var startResponse = SendRequest("POST", $"/containers/{Uri.EscapeDataString(createResult.Id)}/start");
        if (!startResponse.IsSuccessStatusCode)
            return FailureFromResponse(startResponse);

        return new PodmanCommandResult(0, createResult.Id, string.Empty);
    }

    private PodmanApiResponse SendCreateContainerRequest(string containerName, ContainerCreateRequest request) =>
        SendRequest(
            "POST",
            $"/containers/create?name={Uri.EscapeDataString(containerName)}",
            request);

    private PodmanCommandResult PullImage(string image)
    {
        var response = SendRequest("POST", $"/images/create?fromImage={Uri.EscapeDataString(image)}");
        return response.IsSuccessStatusCode
            ? new PodmanCommandResult(0, response.BodyText, string.Empty)
            : FailureFromResponse(response);
    }

    private PodmanCommandResult Send(string method, string path, object? jsonBody = null)
    {
        var response = SendRequest(method, path, jsonBody);
        return response.IsSuccessStatusCode
            ? new PodmanCommandResult(0, response.BodyText, string.Empty)
            : FailureFromResponse(response);
    }

    private PodmanApiResponse SendRequest(string method, string path, object? jsonBody = null, bool captureBinaryBody = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = curlExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--silent");
        startInfo.ArgumentList.Add("--show-error");
        startInfo.ArgumentList.Add("--unix-socket");
        startInfo.ArgumentList.Add(SocketPath);
        startInfo.ArgumentList.Add("-X");
        startInfo.ArgumentList.Add(method);

        if (jsonBody is not null)
        {
            startInfo.ArgumentList.Add("-H");
            startInfo.ArgumentList.Add("Content-Type: application/json");
            startInfo.ArgumentList.Add("--data-binary");
            startInfo.ArgumentList.Add(JsonSerializer.Serialize(jsonBody, JsonOptions));
        }

        startInfo.ArgumentList.Add("--write-out");
        startInfo.ArgumentList.Add($"\n{HttpStatusMarker}%{{http_code}}");
        startInfo.ArgumentList.Add($"http://d{path}");

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new PodmanTransportUnavailableException($"Failed to start '{curlExecutable}'.");

            byte[] bodyBytes;
            if (captureBinaryBody)
            {
                using var memory = new MemoryStream();
                process.StandardOutput.BaseStream.CopyTo(memory);
                bodyBytes = memory.ToArray();
            }
            else
            {
                bodyBytes = Encoding.UTF8.GetBytes(process.StandardOutput.ReadToEnd());
            }

            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new PodmanTransportUnavailableException(
                    $"Could not communicate with the Podman socket '{SocketPath}'. Ensure the Podman service is running.{Environment.NewLine}{standardError}".TrimEnd());
            }

            return ParseResponse(bodyBytes, standardError, captureBinaryBody);
        }
        catch (Win32Exception exception)
        {
            throw new PodmanTransportUnavailableException(
                $"Could not start '{curlExecutable}' to communicate with the Podman socket '{SocketPath}'. Ensure curl is installed and available.",
                exception);
        }
    }

    private static PodmanApiResponse ParseResponse(byte[] output, string standardError, bool captureBinaryBody)
    {
        var markerBytes = Encoding.UTF8.GetBytes($"\n{HttpStatusMarker}");
        var markerIndex = output.AsSpan().LastIndexOf(markerBytes);
        if (markerIndex < 0)
            throw new InvalidOperationException("Could not determine the HTTP status returned by the Podman socket.");

        var statusStart = markerIndex + markerBytes.Length;
        var statusText = Encoding.UTF8.GetString(output, statusStart, output.Length - statusStart).Trim();
        if (!int.TryParse(statusText, out var statusCode))
            throw new InvalidOperationException($"Could not parse the HTTP status returned by the Podman socket: '{statusText}'.");

        var bodyBytes = output[..markerIndex];
        var bodyText = captureBinaryBody ? string.Empty : Encoding.UTF8.GetString(bodyBytes);

        return new PodmanApiResponse((HttpStatusCode)statusCode, bodyText, bodyBytes, standardError);
    }

    private static PodmanCommandResult Unsupported(string command) =>
        Fail($"{UnsupportedPrefix} {command}");

    private static PodmanCommandResult Fail(string standardError) =>
        new(FailureExitCode, string.Empty, standardError);

    private static PodmanCommandResult FailureFromResponse(PodmanApiResponse response) =>
        new((int)response.StatusCode, string.Empty, ReadError(response));

    private static bool IsMissingImageError(PodmanApiResponse response)
    {
        if (response.IsSuccessStatusCode)
            return false;

        var error = ReadError(response);
        return error.Contains("no such image", StringComparison.OrdinalIgnoreCase)
            || error.Contains("image not known", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadOptionValue(IReadOnlyList<string> arguments, ref int index, string optionName)
    {
        if (index + 1 >= arguments.Count)
            throw new InvalidOperationException($"Missing value for '{optionName}'.");

        index++;
        return arguments[index];
    }

    private static string ParseHostPort(string value)
    {
        var segments = value.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2 || segments[1] != "3306")
            throw new InvalidOperationException($"Unsupported port mapping '{value}'.");

        return segments[0];
    }

    private static string ReadError(PodmanApiResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.BodyText))
            return $"{(int)response.StatusCode} {response.StatusCode}".Trim();

        try
        {
            using var document = JsonDocument.Parse(response.BodyText);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "message", "cause", "error" })
                {
                    if (document.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                        return property.GetString() ?? response.BodyText;
                }
            }
        }
        catch (JsonException)
        {
        }

        return response.BodyText;
    }

    private static T Deserialize<T>(PodmanApiResponse response) where T : class =>
        JsonSerializer.Deserialize<T>(response.BodyText, JsonOptions)
        ?? throw new InvalidOperationException($"Could not deserialize the Podman response body as {typeof(T).Name}.");

    private static (string StandardOutput, string StandardError) Demultiplex(byte[] payload)
    {
        if (payload.Length < 8)
            return (Encoding.UTF8.GetString(payload), string.Empty);

        using var standardOutput = new MemoryStream();
        using var standardError = new MemoryStream();
        var offset = 0;

        while (offset + 8 <= payload.Length)
        {
            var streamType = payload[offset];
            var payloadLength = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset + 4, 4));
            offset += 8;

            if (payloadLength < 0 || offset + payloadLength > payload.Length)
                return (Encoding.UTF8.GetString(payload), string.Empty);

            var chunk = payload.AsSpan(offset, payloadLength);
            switch (streamType)
            {
                case 1:
                    standardOutput.Write(chunk);
                    break;
                case 2:
                    standardError.Write(chunk);
                    break;
                default:
                    standardOutput.Write(chunk);
                    break;
            }

            offset += payloadLength;
        }

        if (offset != payload.Length)
            return (Encoding.UTF8.GetString(payload), string.Empty);

        return (Encoding.UTF8.GetString(standardOutput.ToArray()), Encoding.UTF8.GetString(standardError.ToArray()));
    }

    private sealed record PodmanApiResponse(
        HttpStatusCode StatusCode,
        string BodyText,
        byte[] BodyBytes,
        string StandardError)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
    }

    private sealed record ExecCreateRequest(
        bool AttachStdout,
        bool AttachStderr,
        bool AttachStdin,
        IReadOnlyList<string> Cmd,
        bool Tty);

    private sealed record ExecCreateResponse(string Id);

    private sealed record ExecStartRequest(bool Detach, bool Tty);

    private sealed record ExecInspectResponse(int ExitCode);

    private sealed record CreateContainerResponse(string Id);

    private sealed record ContainerCreateRequest(
        string Image,
        IReadOnlyList<string> Env,
        IReadOnlyList<string> Cmd,
        IReadOnlyDictionary<string, object?> ExposedPorts,
        HostConfigRequest HostConfig);

    private sealed record HostConfigRequest(
        IReadOnlyDictionary<string, PortBindingRequest[]> PortBindings);

    private sealed record PortBindingRequest(string HostPort, string HostIp);

    private sealed record ContainerInspectResponse(ContainerInspectStateResponse? State);

    private sealed record ContainerInspectStateResponse(bool Running);

    private sealed record PodSummaryResponse(string? Name);
}
