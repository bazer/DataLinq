using System;
using System.Net;

namespace DataLinq.Testing.CLI;

internal static class PodmanHostResolver
{
    public static string Resolve(string bindAddress) =>
        Resolve(bindAddress, Environment.GetEnvironmentVariable("DATALINQ_TEST_DB_HOST"));

    internal static string Resolve(string bindAddress, string? configuredHost)
    {
        if (!string.IsNullOrWhiteSpace(configuredHost))
            return configuredHost;

        var address = IPAddress.Parse(bindAddress);
        // Local Podman installations forward published ports to the local host.
        // Remote installations select their reachable host explicitly.
        if (address.Equals(IPAddress.Any))
            return IPAddress.Loopback.ToString();
        if (address.Equals(IPAddress.IPv6Any))
            return IPAddress.IPv6Loopback.ToString();
        return address.ToString();
    }
}
