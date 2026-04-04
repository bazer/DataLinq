using System.IO;
using System.Linq;
using System.Text.Json;

namespace DataLinq.Testing.CLI;

internal sealed class TestInfraRuntimeStateStore(string statePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public TestInfraRuntimeState? Load()
    {
        if (!File.Exists(statePath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TestInfraRuntimeState>(File.ReadAllText(statePath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(TestInfraRuntimeState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, JsonOptions));
    }

    public void Delete()
    {
        if (File.Exists(statePath))
            File.Delete(statePath);
    }

    public TestInfraRuntimeState BuildState(TestInfraCliSettings settings, CliTargetSelection selection, string host)
    {
        var targets = selection.Targets
            .Select(target => new TestInfraRuntimeTargetState(
                Id: target.Id,
                Runtime: target.UsesPodman ? "Podman" : "Local",
                Port: target.ServerTarget?.HostPort))
            .ToArray();

        return new TestInfraRuntimeState(
            Version: 1,
            AliasName: selection.AliasName,
            Host: host,
            AdminUser: settings.AdminUser,
            AdminPassword: settings.AdminPassword,
            ApplicationUser: settings.ApplicationUser,
            ApplicationPassword: settings.ApplicationPassword,
            Targets: targets);
    }
}
