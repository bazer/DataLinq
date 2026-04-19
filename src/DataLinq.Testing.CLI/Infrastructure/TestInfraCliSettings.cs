using System;
using System.IO;
using DataLinq.DevTools;
using DataLinq.Testing;

namespace DataLinq.Testing.CLI;

internal sealed record TestInfraCliSettings(
    string RepositoryRoot,
    string ArtifactRoot,
    DevToolPaths ToolPaths,
    string StatePath,
    string ContainerPrefix,
    string AdminUser,
    string AdminPassword,
    string ApplicationUser,
    string ApplicationPassword,
    string EmployeesDatabase,
    int ServerMaxConnections)
{
    public static TestInfraCliSettings FromEnvironment()
    {
        var repositoryRoot = RepositoryLayout.FindRepositoryRoot();
        var artifactRoot = Path.Combine(repositoryRoot, "artifacts", "testdata");

        return new TestInfraCliSettings(
            RepositoryRoot: repositoryRoot,
            ArtifactRoot: artifactRoot,
            ToolPaths: DevToolPaths.Create(repositoryRoot),
            StatePath: Path.Combine(artifactRoot, "testinfra-state.json"),
            ContainerPrefix: PodmanTestEnvironmentSettings.ResolveContainerPrefix("datalinq-tests"),
            AdminUser: GetEnvironmentVariable("datalinq", PodmanTestEnvironmentSettings.AdminUserEnvironmentVariable),
            AdminPassword: GetEnvironmentVariable("datalinq", PodmanTestEnvironmentSettings.AdminPasswordEnvironmentVariable),
            ApplicationUser: GetEnvironmentVariable("datalinq", PodmanTestEnvironmentSettings.ApplicationUserEnvironmentVariable),
            ApplicationPassword: GetEnvironmentVariable("datalinq", PodmanTestEnvironmentSettings.ApplicationPasswordEnvironmentVariable),
            EmployeesDatabase: GetEnvironmentVariable("datalinq_employees", PodmanTestEnvironmentSettings.EmployeesDatabaseEnvironmentVariable),
            ServerMaxConnections: GetEnvironmentVariable(250, PodmanTestEnvironmentSettings.ServerMaxConnectionsEnvironmentVariable));
    }

    public string GetContainerName(DatabaseServerTarget target) =>
        $"{ContainerPrefix}-{target.Id.ToLowerInvariant()}";

    private static string GetEnvironmentVariable(string fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return fallback;
    }

    private static int GetEnvironmentVariable(int fallback, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value, out var parsed) && parsed > 0)
                return parsed;
        }

        return fallback;
    }
}
