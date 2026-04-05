using System;
using System.Text.RegularExpressions;

namespace DataLinq.Testing.CLI;

internal static class PodmanHostResolver
{
    private static readonly Regex AddressPattern = new(@"^\d+:\s+([^\s]+)\s+inet\s+(\d+\.\d+\.\d+\.\d+)", RegexOptions.Compiled);

    public static string Resolve(PodmanClient podman)
    {
        var configuredHost = Environment.GetEnvironmentVariable("DATALINQ_TEST_DB_HOST");
        if (!string.IsNullOrWhiteSpace(configuredHost))
            return configuredHost;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var result = podman.Execute(["machine", "ssh", "ip -o -4 addr show scope global"]);
                if (result.ExitCode == 0)
                {
                    foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var match = AddressPattern.Match(line);
                        if (!match.Success)
                            continue;

                        var interfaceName = match.Groups[1].Value;
                        var address = match.Groups[2].Value;
                        if (!string.Equals(interfaceName, "lo", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(interfaceName, "podman0", StringComparison.OrdinalIgnoreCase))
                        {
                            return address;
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return "127.0.0.1";
    }
}
