using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.CLI;

namespace DataLinq.Tests.Unit;

public class DataLinqCliCommandSurfaceTests
{
    [Test]
    public async Task GenerateModels_AcceptsNewTargetOptions()
    {
        var errors = ParseErrors(
            "generate",
            "models",
            "--database",
            "AppDb",
            "--provider",
            "SQLite",
            "--data-source",
            "app.db",
            "--fresh",
            "--overwrite-types",
            "--stamp-generated-header");

        await Assert.That(errors).IsEqualTo(0);
    }

    [Test]
    public async Task GenerateModels_AndValidate_AcceptBatchOptions()
    {
        await Assert.That(ParseErrors("generate", "models", "--all")).IsEqualTo(0);
        await Assert.That(ParseErrors("generate", "models", "--recursive")).IsEqualTo(0);
        await Assert.That(ParseErrors("create-models", "--all")).IsEqualTo(0);
        await Assert.That(ParseErrors("validate", "--all")).IsEqualTo(0);
        await Assert.That(ParseErrors("validate", "--recursive")).IsEqualTo(0);
    }

    [Test]
    public async Task GenerateSql_AndDatabaseCreate_AcceptNestedSurface()
    {
        var generateSqlErrors = ParseErrors(
            "generate",
            "sql",
            "-n",
            "AppDb",
            "-p",
            "SQLite",
            "--output",
            "schema.sql");

        var databaseCreateErrors = ParseErrors(
            "database",
            "create",
            "-n",
            "AppDb",
            "-p",
            "SQLite",
            "--data-source",
            "app.db");

        await Assert.That(generateSqlErrors).IsEqualTo(0);
        await Assert.That(databaseCreateErrors).IsEqualTo(0);
    }

    [Test]
    public async Task GenerateSql_RequiresOutputFile()
    {
        await Assert.That(ParseErrors("generate", "sql", "-n", "AppDb", "-p", "SQLite")).IsGreaterThan(0);
    }

    [Test]
    public async Task Diff_AcceptsNewTargetOptionsAndOutputPath()
    {
        var errors = ParseErrors(
            "diff",
            "--database",
            "AppDb",
            "--provider",
            "SQLite",
            "--data-source",
            "app.db",
            "--output",
            "diff.sql");

        await Assert.That(errors).IsEqualTo(0);
    }

    [Test]
    public async Task ConfigList_IsNestedAndRootListIsNotKept()
    {
        await Assert.That(ParseErrors("config", "list")).IsEqualTo(0);
        await Assert.That(ParseErrors("config", "list", "--recursive")).IsEqualTo(0);
        await Assert.That(ParseErrors("config", "list", "--all")).IsGreaterThan(0);
        await Assert.That(ParseErrors("list")).IsGreaterThan(0);
    }

    [Test]
    public async Task RecursiveConfigDiscovery_SkipsGeneratedAndInfrastructureDirectories()
    {
        using var fixture = CliDiscoveryFixture.Create();
        fixture.WriteConfig("datalinq.json");
        fixture.WriteConfig(Path.Combine("src", "App", "datalinq.json"));
        fixture.WriteConfig(Path.Combine(".git", "datalinq.json"));
        fixture.WriteConfig(Path.Combine("bin", "datalinq.json"));
        fixture.WriteConfig(Path.Combine("obj", "datalinq.json"));
        fixture.WriteConfig(Path.Combine("node_modules", "pkg", "datalinq.json"));
        fixture.WriteConfig(Path.Combine("artifacts", "run", "datalinq.json"));
        fixture.WriteConfig(Path.Combine("_site", "datalinq.json"));

        var discovered = CliConfigDiscovery
            .DiscoverConfigFiles(Path.Combine(fixture.BasePath, "datalinq.json"))
            .Select(path => Path.GetRelativePath(fixture.BasePath, path).Replace('\\', '/'))
            .ToArray();

        await Assert.That(discovered).IsEquivalentTo(["datalinq.json", "src/App/datalinq.json"]);
    }

    [Test]
    public async Task Validate_UsesFormatAndRejectsOldOutputFormatOption()
    {
        await Assert.That(ParseErrors("validate", "--format", "json")).IsEqualTo(0);
        await Assert.That(ParseErrors("validate", "--output", "json")).IsGreaterThan(0);
    }

    [Test]
    public async Task CreateModels_IsOnlyDeprecatedFlatCommand()
    {
        await Assert.That(ParseErrors("create-models", "-n", "AppDb", "-p", "SQLite")).IsEqualTo(0);
        await Assert.That(ParseErrors("create-sql", "-n", "AppDb", "-p", "SQLite", "-o", "schema.sql")).IsGreaterThan(0);
        await Assert.That(ParseErrors("create-database", "-n", "AppDb", "-p", "SQLite")).IsGreaterThan(0);
    }

    [Test]
    public async Task OldTargetAndGenerationOptionsAreRejected()
    {
        await Assert.That(ParseErrors("generate", "models", "--name", "AppDb")).IsGreaterThan(0);
        await Assert.That(ParseErrors("generate", "models", "--type", "SQLite")).IsGreaterThan(0);
        await Assert.That(ParseErrors("generate", "models", "--datasource", "app.db")).IsGreaterThan(0);
        await Assert.That(ParseErrors("create-models", "--skip-source")).IsGreaterThan(0);
    }

    [Test]
    public async Task ParserFailuresReturnExitCodeTwo()
    {
        var exitCode = await Program.InvokeAsync(["validate", "--output", "json"]);

        await Assert.That(exitCode).IsEqualTo(2);
    }

    private static int ParseErrors(params string[] args) =>
        Program.CreateRootCommand().Parse(args).Errors.Count;

    private sealed class CliDiscoveryFixture : IDisposable
    {
        private CliDiscoveryFixture(string basePath)
        {
            BasePath = basePath;
        }

        public string BasePath { get; }

        public static CliDiscoveryFixture Create()
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-cli-discovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);
            return new CliDiscoveryFixture(basePath);
        }

        public void WriteConfig(string relativePath)
        {
            var path = Path.Combine(BasePath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{ "Databases": [] }""");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(BasePath))
                    Directory.Delete(BasePath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
