using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DataLinq.Tests.Unit;

public sealed class ProviderRegistrationTests
{
    [Test]
    public async Task FreshProcesses_ConcurrentGenericFirstUsePublishesCompleteRegistrations()
    {
        for (var iteration = 0; iteration < 4; iteration++)
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add("--runtimeconfig");
            start.ArgumentList.Add(System.IO.Path.ChangeExtension(typeof(ProviderRegistrationTests).Assembly.Location, ".runtimeconfig.json"));
            start.ArgumentList.Add("--depsfile");
            start.ArgumentList.Add(System.IO.Path.ChangeExtension(typeof(ProviderRegistrationTests).Assembly.Location, ".deps.json"));
            start.ArgumentList.Add(typeof(DataLinq.Tests.Fixtures.ProviderRegistrationProcess).Assembly.Location);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start registration fixture.");
            try
            {
                var output = child.StandardOutput.ReadToEndAsync();
                var error = child.StandardError.ReadToEndAsync();
                await Task.WhenAll(output, error, child.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(30));
                if (child.ExitCode != 0)
                    throw new InvalidOperationException(await error);
                await Assert.That((await output).Trim()).IsEqualTo("registration checks passed");
            }
            finally
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
                if (!child.WaitForExit(10_000))
                    throw new TimeoutException("Registration fixture did not exit.");
            }
        }
    }
}

