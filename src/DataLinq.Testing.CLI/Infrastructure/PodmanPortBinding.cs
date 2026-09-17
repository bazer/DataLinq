using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace DataLinq.Testing.CLI;

internal sealed record PodmanPortBinding(string HostIp, string HostPort)
{
    internal const string DefaultHost = "127.0.0.1";
    internal const string EnvironmentVariable = "DATALINQ_TEST_DB_BIND_ADDRESS";

    internal static PodmanPortBinding Create(string host, int port)
    {
        if (!IPAddress.TryParse(host, out var address) || port is < 1 or > 65535)
            throw new InvalidOperationException("Test database publication requires an IP address and a host port between 1 and 65535.");
        return new(address.ToString(), port.ToString(CultureInfo.InvariantCulture));
    }

    internal static PodmanPortBinding Parse(string value)
    {
        const string suffix = ":3306";
        if (!value.EndsWith(suffix, StringComparison.Ordinal)
            || !IPEndPoint.TryParse(value[..^suffix.Length], out var endpoint)
            || endpoint.Port == 0)
            throw new InvalidOperationException("Test database port mappings must specify an IP address, host port, and container port 3306.");
        return Create(endpoint.Address.ToString(), endpoint.Port);
    }

    internal string ToPublishArgument()
    {
        var address = IPAddress.Parse(HostIp);
        var host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{HostIp}]" : HostIp;
        return $"{host}:{HostPort}:3306";
    }
}
