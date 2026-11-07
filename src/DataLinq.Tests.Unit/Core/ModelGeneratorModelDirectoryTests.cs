using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Config;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.SQLite;
using DataLinq.Tools;
using Microsoft.Data.Sqlite;
using ThrowAway;

namespace DataLinq.Tests.Unit.Core;

public class ModelGeneratorModelDirectoryTests
{
    [Test]
    [NotInParallel]
    public async Task CreateModels_ModelDirectoryMissing_WritesGeneratedFiles()
    {
        using var fixture = ModelGeneratorFixture.Create();

        var result = fixture.Generate(readExistingModels: true);

        ThrowIfFailed(result);
        await Assert.That(File.Exists(Path.Combine(fixture.ModelDirectory, "AppDb.cs"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(fixture.ModelDirectory, "Account.cs"))).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task CreateModels_Fresh_IgnoresInvalidExistingModelFiles()
    {
        using var fixture = ModelGeneratorFixture.Create();
        Directory.CreateDirectory(fixture.ModelDirectory);
        File.WriteAllText(Path.Combine(fixture.ModelDirectory, "Broken.cs"), "this is not valid csharp");

        var result = fixture.Generate(readExistingModels: false);

        ThrowIfFailed(result);
        await Assert.That(File.Exists(Path.Combine(fixture.ModelDirectory, "Account.cs"))).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task CreateModels_ReadExistingModelFiles_FailsOnInvalidExistingModelFiles()
    {
        using var fixture = ModelGeneratorFixture.Create();
        Directory.CreateDirectory(fixture.ModelDirectory);
        File.WriteAllText(Path.Combine(fixture.ModelDirectory, "Broken.cs"), "this is not valid csharp");

        var result = fixture.Generate(readExistingModels: true);

        await Assert.That(result.HasFailed).IsTrue();
    }

    [Test]
    [NotInParallel]
    public async Task CreateModels_ReadExistingModelFiles_PreservesSupportedNamesAndTypes()
    {
        using var fixture = ModelGeneratorFixture.Create();
        ThrowIfFailed(fixture.Generate(readExistingModels: false));

        var databaseFile = Path.Combine(fixture.ModelDirectory, "AppDb.cs");
        File.WriteAllText(
            databaseFile,
            File.ReadAllText(databaseFile)
                .Replace("DbRead<Account>", "DbRead<AccountRecord>"));

        var accountFile = Path.Combine(fixture.ModelDirectory, "Account.cs");
        File.WriteAllText(
            accountFile,
            File.ReadAllText(accountFile)
                .Replace("class Account(", "class AccountRecord(")
                .Replace("Immutable<Account,", "Immutable<AccountRecord,")
                .Replace("public abstract string DisplayName { get; }", "public abstract AccountDisplayName Name { get; }"));

        var result = fixture.Generate(readExistingModels: true, overwriteExistingModels: true);

        ThrowIfFailed(result);
        var regeneratedPath = Path.Combine(fixture.ModelDirectory, "AccountRecord.cs");
        await Assert.That(File.Exists(regeneratedPath)).IsTrue();
        var regenerated = File.ReadAllText(regeneratedPath);
        await Assert.That(regenerated).Contains("public abstract partial class AccountRecord(");
        await Assert.That(regenerated).Contains("[Column(\"display_name\")]");
        await Assert.That(regenerated).Contains("public abstract AccountDisplayName Name { get; }");
    }

    [Test]
    [NotInParallel] // The existing fixture initializes process-global provider registries.
    public async Task CreateModels_OverwriteDisabledRejectsMixedPlanAndPreservesHandEditedFile()
    {
        using var fixture = ModelGeneratorFixture.Create();
        Directory.CreateDirectory(fixture.ModelDirectory);
        var account = Path.Combine(fixture.ModelDirectory, "Account.cs");
        File.WriteAllText(account, "hand-edited content");

        var plan = fixture.Plan();
        await Assert.That(plan.HasFailed).IsTrue();
        var result = fixture.Generate(readExistingModels: false);
        await Assert.That(result.HasFailed).IsTrue();
        await Assert.That(File.ReadAllText(account)).IsEqualTo("hand-edited content");
        await Assert.That(File.Exists(Path.Combine(fixture.ModelDirectory, "AppDb.cs"))).IsFalse();

        ThrowIfFailed(fixture.Generate(readExistingModels: false, overwriteExistingModels: true));
        await Assert.That(File.ReadAllText(account)).Contains("class Account");
        await Assert.That(File.Exists(Path.Combine(fixture.ModelDirectory, "AppDb.cs"))).IsTrue();
    }

    [Test]
    [NotInParallel] // The existing fixture initializes process-global provider registries.
    public async Task GeneratedPlan_RejectsDestinationCreatedAfterPlanning()
    {
        using var fixture = ModelGeneratorFixture.Create();
        var plan = fixture.Plan();
        if (plan.HasFailed)
            throw new InvalidOperationException(plan.Failure.ToString());
        await Assert.That(plan.Value.OverwriteExistingModels).IsFalse();
        Directory.CreateDirectory(fixture.ModelDirectory);
        var account = Path.Combine(fixture.ModelDirectory, "Account.cs");
        File.WriteAllText(account, "created after planning");

        var write = plan.Value.Write();
        await Assert.That(write.HasFailed).IsTrue();
        await Assert.That(File.ReadAllText(account)).IsEqualTo("created after planning");
        await Assert.That(File.Exists(Path.Combine(fixture.ModelDirectory, "AppDb.cs"))).IsFalse();
    }

    [Test]
    [NotInParallel]
    [Arguments(true, false)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(false, true)]
    public async Task CreateModels_SelfReference_UsesSafeNameAndPreservesItOnRegeneration(bool capitalize, bool rename)
    {
        using var fixture = ModelGeneratorFixture.Create(
            """
            CREATE TABLE forsandelser (
                ForsandelseID INTEGER NOT NULL PRIMARY KEY,
                GammalFK INTEGER NULL,
                FOREIGN KEY (GammalFK) REFERENCES forsandelser(ForsandelseID)
            );
            """, capitalize);
        var result = fixture.Generate(readExistingModels: false);
        ThrowIfFailed(result);
        var modelName = capitalize ? "Forsandelser" : "forsandelser";
        var propertyName = modelName + "Relation";
        var modelPath = Path.Combine(fixture.ModelDirectory, modelName + ".cs");
        var declaration = $"IImmutableRelation<{modelName}> {propertyName} {{ get; }}";
        var contents = File.ReadAllText(modelPath);
        await Assert.That(contents).Contains(declaration);
        await Assert.That(fixture.Logs.Any(message => message.Contains("enclosing model type") && message.Contains(propertyName))).IsTrue();

        if (rename)
        {
            propertyName = "Children";
            File.WriteAllText(modelPath, contents.Replace(declaration, $"IImmutableRelation<{modelName}> Children {{ get; }}"));
        }
        fixture.Logs.Clear();
        ThrowIfFailed(fixture.Generate(readExistingModels: true, overwriteExistingModels: true));
        var regenerated = File.ReadAllText(modelPath);
        await Assert.That(regenerated).Contains($"IImmutableRelation<{modelName}> {propertyName} {{ get; }}");
        await Assert.That(regenerated).Contains("[Relation(\"forsandelser\", \"GammalFK\", \"0\")]");
        await Assert.That(fixture.Logs.Any(message => message.Contains("would generate relation property"))).IsFalse();
        if (!rename)
            await Assert.That(regenerated).IsEqualTo(contents);
    }

    private static void ThrowIfFailed(Option<DatabaseDefinition, IDLOptionFailure> result)
    {
        if (result.HasFailed)
            throw new InvalidOperationException(result.Failure.ToString());
    }

    private sealed class ModelGeneratorFixture : IDisposable
    {
        private ModelGeneratorFixture(string basePath, bool capitalize)
        {
            BasePath = basePath;
            ModelDirectory = Path.Combine(basePath, "Models");
            Capitalize = capitalize;
        }

        public string BasePath { get; }
        public string ModelDirectory { get; }
        public bool Capitalize { get; }
        public List<string> Logs { get; } = [];

        public static ModelGeneratorFixture Create(string? schema = null, bool capitalize = true)
        {
            SQLiteProvider.RegisterProvider();

            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-model-generator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(basePath);

            var databasePath = Path.Combine(basePath, "app.db");
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = schema ??
                """
                CREATE TABLE "account" (
                    "id" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "display_name" TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();

            return new ModelGeneratorFixture(basePath, capitalize);
        }

        public Option<DatabaseDefinition, IDLOptionFailure> Generate(bool readExistingModels, bool overwriteExistingModels = false)
        {
            var (generator, connection) = CreateGenerator(readExistingModels, overwriteExistingModels);
            return generator.CreateModels(connection, BasePath, "app.db");
        }

        public Option<GeneratedModelWritePlan, IDLOptionFailure> Plan()
        {
            var (generator, connection) = CreateGenerator(false, false);
            return generator.CreateModelWritePlan(connection, BasePath, "app.db");
        }

        private (ModelGenerator generator, DataLinqDatabaseConnection connection) CreateGenerator(bool readExistingModels, bool overwriteExistingModels)
        {
            var config = new DataLinqConfig(
                BasePath,
                new ConfigFile
                {
                    Databases =
                    [
                        new ConfigFileDatabase
                        {
                            Name = "app_db",
                            CsType = "AppDb",
                            Namespace = "DataLinq.Tests.GeneratedModels",
                            SourceDirectories = ["ignored-source-models"],
                            ModelDirectory = "Models",
                            CapitalizeNames = Capitalize,
                            FileEncoding = "UTF-8",
                            Connections =
                            [
                                new ConfigFileDatabaseConnection
                                {
                                    Type = "SQLite",
                                    DataSourceName = "app.db",
                                    ConnectionString = "Data Source=app.db"
                                }
                            ]
                        }
                    ]
                });

            var connection = config.GetConnection("app_db", DatabaseType.SQLite).Value.connection;
            return (new ModelGenerator(
                Logs.Add,
                new ModelGeneratorOptions
                {
                    ReadSourceModels = readExistingModels,
                    OverwriteExistingModels = overwriteExistingModels,
                    CapitalizeNames = Capitalize
                }), connection);
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
