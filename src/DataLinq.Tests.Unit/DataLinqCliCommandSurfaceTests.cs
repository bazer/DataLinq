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
        await Assert.That(ParseErrors("list")).IsGreaterThan(0);
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
}
