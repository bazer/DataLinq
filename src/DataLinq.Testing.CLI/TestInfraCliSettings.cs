using System;
using System.IO;
using DataLinq.Testing;

namespace DataLinq.Testing.CLI;

internal sealed record TestInfraCliSettings(
    string RepositoryRoot,
    string ArtifactRoot,
    string StatePath,
    string ContainerPrefix,
    string AdminUser,
    string AdminPassword,
    string ApplicationUser,
    string ApplicationPassword,
    string EmployeesDatabase)
{
    public static TestInfraCliSettings FromEnvironment()
    {
        var repositoryRoot = RepositoryLayout.FindRepositoryRoot();
        var artifactRoot = Path.Combine(repositoryRoot, "artifacts", "testdata");

        return new TestInfraCliSettings(
            RepositoryRoot: repositoryRoot,
            ArtifactRoot: artifactRoot,
            StatePath: Path.Combine(artifactRoot, "testinfra-state.json"),
            ContainerPrefix: GetEnvironmentVariable(PodmanTestEnvironmentSettings.PodNameEnvironmentVariable, "datalinq-tests"),
            AdminUser: GetEnvironmentVariable(PodmanTestEnvironmentSettings.AdminUserEnvironmentVariable, "datalinq"),
            AdminPassword: GetEnvironmentVariable(PodmanTestEnvironmentSettings.AdminPasswordEnvironmentVariable, "datalinq"),
            ApplicationUser: GetEnvironmentVariable(PodmanTestEnvironmentSettings.ApplicationUserEnvironmentVariable, "datalinq"),
            ApplicationPassword: GetEnvironmentVariable(PodmanTestEnvironmentSettings.ApplicationPasswordEnvironmentVariable, "datalinq"),
            EmployeesDatabase: GetEnvironmentVariable("DATALINQ_TEST_EMPLOYEES_DB", "datalinq_employees"));
    }

    public string GetContainerName(DatabaseServerTarget target) =>
        $"{ContainerPrefix}-{target.Id.ToLowerInvariant()}";

    private static string GetEnvironmentVariable(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
