using System.IO;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class TestingCliEvidenceSecurityTests
{
    [Test]
    public async Task PodmanSocketTransport_DoesNotEmbedCommandArgumentsInDiagnostics()
    {
        var source = ReadTestingCliSource("Infrastructure", "PodmanSocketTransport.cs");

        await Assert.That(source).DoesNotContain("string.Join(\" \", arguments)");
        await Assert.That(source).Contains("unexpected response for the '{arguments[0]}' operation");
    }

    [Test]
    public async Task RunCommand_TargetlessChildrenClearAmbientProviderSelection()
    {
        var source = ReadTestingCliSource("Commands", "RunCommand.cs");

        await Assert.That(source).Contains(
            "PodmanTestEnvironmentSettings.ProviderSetEnvironmentVariable] = null;");
        await Assert.That(source).Contains(
            "PodmanTestEnvironmentSettings.TargetIdsEnvironmentVariable] = null;");
        await Assert.That(source).Contains(
            "PodmanTestEnvironmentSettings.TargetAliasEnvironmentVariable] = null;");
        await Assert.That(source).Contains(
            "new TestInfraRuntimeStateStore(settings.StatePath).Load()?.Host");
        await Assert.That(source).DoesNotContain("if (overallExitCode == 0)");
        await Assert.That(source).Contains("overallExitCode = 1;");
    }

    [Test]
    public async Task RunCommand_BuildsDistinctProjectsAndExecutesResolvedHostsDirectly()
    {
        var source = ReadTestingCliSource("Commands", "RunCommand.cs");

        await Assert.That(source).Contains(".Distinct(PathComparer)");
        await Assert.That(source).Contains("TestHostResolver.Resolve(");
        await Assert.That(source).Contains("\"exec\",");
        await Assert.That(source).Contains("testHostPath");
        await Assert.That(source).DoesNotContain("\"--project\", projectPath");
        await Assert.That(source).Contains("'--build' and '--no-build' cannot be combined.");
    }

    private static string ReadTestingCliSource(string directory, string fileName)
    {
        var root = RepositoryRootLocator.Find();
        return File.ReadAllText(Path.Combine(
            root,
            "src",
            "DataLinq.Testing.CLI",
            directory,
            fileName));
    }
}
