using System;
using System.IO;
using System.Threading.Tasks;
using DataLinq.DevTools;

namespace DataLinq.Tests.Unit;

public sealed class TestHostResolverTests
{
    [Test]
    public async Task Resolve_ReturnsTheSingleExecutableHostForTheDeclaredFramework()
    {
        using var fixture = TestHostFixture.Create("net10.0");
        fixture.WriteHost("net9.0", newerThanInputs: true);
        var expected = fixture.WriteHost("net10.0", newerThanInputs: true);

        var result = TestHostResolver.Resolve(
            fixture.RepositoryRoot,
            fixture.ProjectPath,
            "Debug",
            requireCurrentOutput: true);

        await Assert.That(result.HostPath).IsEqualTo(expected);
        await Assert.That(File.Exists(result.RuntimeConfigPath)).IsTrue();
        await Assert.That(File.Exists(result.DependencyManifestPath)).IsTrue();
    }

    [Test]
    public async Task Resolve_MissingOutputExplainsHowToBuildIt()
    {
        using var fixture = TestHostFixture.Create("net10.0");
        FileNotFoundException? exception = null;

        try
        {
            TestHostResolver.Resolve(
                fixture.RepositoryRoot,
                fixture.ProjectPath,
                "Debug",
                requireCurrentOutput: true);
        }
        catch (FileNotFoundException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("Run without '--no-build'");
    }

    [Test]
    public async Task Resolve_RejectsAReferencedOutputOlderThanItsProjectSources()
    {
        using var fixture = TestHostFixture.Create("net10.0", includeReferencedProject: true);
        fixture.WriteHost("net10.0", newerThanInputs: false);
        InvalidOperationException? exception = null;

        try
        {
            TestHostResolver.Resolve(
                fixture.RepositoryRoot,
                fixture.ProjectPath,
                "Debug",
                requireCurrentOutput: true);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).Contains("graph is stale because");
        await Assert.That(exception.Message).Contains("Referenced.cs");
    }

    [Test]
    public async Task Resolve_AcceptsUpdatedReferencedOutputWithoutRecompiledRootHost()
    {
        using var fixture = TestHostFixture.Create("net10.0", includeReferencedProject: true);
        var hostPath = fixture.WriteHost("net10.0", newerThanInputs: true);
        File.SetLastWriteTimeUtc(hostPath, new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        fixture.WriteReferencedOutput("net10.0", new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        var result = TestHostResolver.Resolve(
            fixture.RepositoryRoot,
            fixture.ProjectPath,
            "Debug",
            requireCurrentOutput: true);

        await Assert.That(result.HostPath).IsEqualTo(hostPath);
    }

    private sealed class TestHostFixture : IDisposable
    {
        private readonly DateTime oldTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly DateTime newTimestamp = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        private TestHostFixture(string root, string projectPath)
        {
            RepositoryRoot = root;
            ProjectPath = projectPath;
        }

        public string RepositoryRoot { get; }

        public string ProjectPath { get; }

        public static TestHostFixture Create(string targetFramework, bool includeReferencedProject = false)
        {
            var root = Path.Combine(
                RepositoryRootLocator.Find(),
                "artifacts",
                "test-results",
                $"test-host-resolver-{Guid.NewGuid():N}");
            var appDirectory = Path.Combine(root, "App.Tests");
            Directory.CreateDirectory(appDirectory);
            var projectReference = includeReferencedProject
                ? "<ItemGroup><ProjectReference Include=\"..\\Referenced\\Referenced.csproj\" /></ItemGroup>"
                : string.Empty;
            var projectPath = Path.Combine(appDirectory, "App.Tests.csproj");
            File.WriteAllText(projectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>{targetFramework}</TargetFramework></PropertyGroup>
                  {projectReference}
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Tests.cs"), "internal sealed class Tests { }");
            if (includeReferencedProject)
            {
                var referencedDirectory = Path.Combine(root, "Referenced");
                Directory.CreateDirectory(referencedDirectory);
                File.WriteAllText(
                    Path.Combine(referencedDirectory, "Referenced.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\" />");
                File.WriteAllText(Path.Combine(referencedDirectory, "Referenced.cs"), "internal sealed class Referenced { }");
            }

            var fixture = new TestHostFixture(root, projectPath);
            fixture.SetInputTimestamps(fixture.oldTimestamp);
            if (includeReferencedProject)
                File.SetLastWriteTimeUtc(Path.Combine(root, "Referenced", "Referenced.cs"), fixture.newTimestamp);
            return fixture;
        }

        public string WriteHost(string targetFramework, bool newerThanInputs)
        {
            var directory = Path.Combine(
                Path.GetDirectoryName(ProjectPath)!,
                "bin",
                "Debug",
                targetFramework);
            Directory.CreateDirectory(directory);
            var hostPath = Path.Combine(directory, "App.Tests.dll");
            var runtimeConfigPath = Path.Combine(directory, "App.Tests.runtimeconfig.json");
            var dependencyManifestPath = Path.Combine(directory, "App.Tests.deps.json");
            File.WriteAllText(hostPath, "host");
            File.WriteAllText(runtimeConfigPath, "{}");
            File.WriteAllText(dependencyManifestPath, "{}");
            var timestamp = newerThanInputs ? newTimestamp.AddDays(1) : oldTimestamp.AddDays(-1);
            File.SetLastWriteTimeUtc(hostPath, timestamp);
            File.SetLastWriteTimeUtc(runtimeConfigPath, timestamp);
            File.SetLastWriteTimeUtc(dependencyManifestPath, timestamp);
            return hostPath;
        }

        public void WriteReferencedOutput(string targetFramework, DateTime timestamp)
        {
            var path = Path.Combine(
                Path.GetDirectoryName(ProjectPath)!,
                "bin",
                "Debug",
                targetFramework,
                "Referenced.dll");
            File.WriteAllText(path, "referenced");
            File.SetLastWriteTimeUtc(path, timestamp);
        }

        public void Dispose()
        {
            if (Directory.Exists(RepositoryRoot))
                Directory.Delete(RepositoryRoot, recursive: true);
        }

        private void SetInputTimestamps(DateTime timestamp)
        {
            foreach (var path in Directory.EnumerateFiles(RepositoryRoot, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(path, timestamp);
        }
    }
}
