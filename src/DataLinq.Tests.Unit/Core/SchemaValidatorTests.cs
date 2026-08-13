using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Config;
using DataLinq.SQLite;
using DataLinq.Tools;
using DataLinq.Validation;
using Microsoft.Data.Sqlite;

namespace DataLinq.Tests.Unit.Core;

public class SchemaValidatorTests
{
    [Test]
    [NotInParallel]
    public async Task Validate_MatchingSQLiteSchema_ReturnsNoDifferences()
    {
        using var fixture = SchemaValidatorFixture.Create();

        var result = fixture.Validate();

        if (result.HasFailed)
            throw new InvalidOperationException(result.Failure.ToString());
        if (result.Value.HasDifferences)
            throw new InvalidOperationException(FormatDifferences(result.Value.Differences));

        await Assert.That(result.HasFailed).IsFalse();
        await Assert.That(result.Value.HasDifferences).IsFalse();
        await Assert.That(result.Value.ModelTableCount).IsEqualTo(1);
        await Assert.That(result.Value.DatabaseTableCount).IsEqualTo(1);
    }

    [Test]
    [NotInParallel]
    public async Task Validate_SQLiteSchemaDrift_ReturnsDifferences()
    {
        using var fixture = SchemaValidatorFixture.Create();
        fixture.ExecuteNonQuery("ALTER TABLE \"account\" ADD COLUMN \"nickname\" TEXT;");

        var result = fixture.Validate();

        if (result.HasFailed)
            throw new InvalidOperationException(result.Failure.ToString());
        if (result.Value.Differences.Count != 1)
            throw new InvalidOperationException(FormatDifferences(result.Value.Differences));

        await Assert.That(result.HasFailed).IsFalse();
        await Assert.That(result.Value.HasDifferences).IsTrue();
        await Assert.That(result.Value.Differences.Count).IsEqualTo(1);
        await Assert.That(result.Value.Differences[0].Kind).IsEqualTo(SchemaDifferenceKind.ExtraColumn);
        await Assert.That(result.Value.Differences[0].Path).IsEqualTo("account.nickname");
    }

    [Test]
    [NotInParallel]
    public async Task Validate_DeferredGuidTextWithNeutralName_ReturnsUnresolvedFormat()
    {
        using var fixture = SchemaValidatorFixture.Create(
            CreateGuidModelSource("text", "Text36"),
            CreateGuidSchema("TEXT"));

        var result = fixture.Validate();

        if (result.HasFailed)
            throw new InvalidOperationException(result.Failure.ToString());
        if (result.Value.Differences.Count != 1 ||
            result.Value.Differences[0].Kind != SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved)
            throw new InvalidOperationException(FormatDifferences(result.Value.Differences));

        await Assert.That(result.Value.Differences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
        await Assert.That(result.Value.Differences.Single().Path).IsEqualTo("account.external_id");
        await Assert.That(result.Value.Differences.Single().Message).Contains("Text36");
        await Assert.That(result.Value.Differences.Single().Message).Contains("does not encode its UUID representation");
    }

    [Test]
    [NotInParallel]
    public async Task Validate_DeferredGuidBlobWithNeutralName_ReturnsUnresolvedFormat()
    {
        using var fixture = SchemaValidatorFixture.Create(
            CreateGuidModelSource("BLOB", "Binary16Rfc4122"),
            CreateGuidSchema("BLOB"));

        var result = fixture.Validate();

        if (result.HasFailed)
            throw new InvalidOperationException(result.Failure.ToString());
        if (result.Value.Differences.Count != 1 ||
            result.Value.Differences[0].Kind != SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved)
            throw new InvalidOperationException(FormatDifferences(result.Value.Differences));

        await Assert.That(result.Value.Differences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
        await Assert.That(result.Value.Differences.Single().Path).IsEqualTo("account.external_id");
        await Assert.That(result.Value.Differences.Single().Message).Contains("Binary16Rfc4122");
    }

    [Test]
    [NotInParallel]
    public async Task Validate_DeferredBareGuidBlobWithNeutralName_ReturnsUnresolvedFormat()
    {
        using var fixture = SchemaValidatorFixture.Create(
            CreateGuidModelSource("BLOB", guidStorageFormat: null),
            CreateGuidSchema("BLOB"));

        var result = fixture.Validate();

        if (result.HasFailed)
            throw new InvalidOperationException(result.Failure.ToString());
        if (result.Value.Differences.Count != 1 ||
            result.Value.Differences[0].Kind != SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved)
            throw new InvalidOperationException(FormatDifferences(result.Value.Differences));

        await Assert.That(result.Value.Differences.Select(static difference => difference.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved]);
        await Assert.That(result.Value.Differences.Single().Path).IsEqualTo("account.external_id");
        await Assert.That(result.Value.Differences.Single().Message).Contains("Model UUID storage intent is unresolved");
        await Assert.That(result.Value.Differences.Single().Message).Contains("assembly-registered scalar converter");
    }

    [Test]
    [NotInParallel]
    public async Task Validate_DeferredGuidWithConverterMarker_DoesNotAssumeCanonicalGuid()
    {
        using var fixture = SchemaValidatorFixture.Create(
            CreateGuidModelSource(
                "BLOB",
                guidStorageFormat: null,
                scalarConverterType: "GuidBytesConverter"),
            CreateGuidSchema("BLOB"));

        var result = fixture.Validate();

        if (result.HasFailed)
            throw new InvalidOperationException(result.Failure.ToString());
        if (result.Value.HasDifferences)
            throw new InvalidOperationException(FormatDifferences(result.Value.Differences));

        await Assert.That(result.Value.Differences).IsEmpty();
    }

    private static string CreateGuidModelSource(
        string sqliteType,
        string? guidStorageFormat,
        string? scalarConverterType = null)
    {
        var guidStorageDeclaration = guidStorageFormat is null
            ? string.Empty
            : $", GuidStorage(DatabaseType.SQLite, GuidStorageFormat.{guidStorageFormat})";
        var scalarConverterDeclaration = scalarConverterType is null
            ? string.Empty
            : $", ScalarConverter(typeof({scalarConverterType}))";

        return $$"""
        using System;
        using DataLinq;
        using DataLinq.Attributes;
        using DataLinq.Instances;
        using DataLinq.Interfaces;

        namespace DataLinq.Tests.SchemaValidation;

        [Database("validation_db")]
        public partial class ValidationDb(DataSourceAccess dataSource) : IDatabaseModel
        {
            public DbRead<Account> Accounts { get; } = new(dataSource);
        }

        [Table("account")]
        public abstract partial class Account(IRowData rowData, IDataSourceAccess dataSource)
            : Immutable<Account, ValidationDb>(rowData, dataSource), ITableModel<ValidationDb>
        {
            [Column("id"), PrimaryKey, Type(DatabaseType.SQLite, "integer")]
            public abstract int Id { get; }

            [Column("external_id"), Type(DatabaseType.SQLite, "{{sqliteType}}"){{guidStorageDeclaration}}{{scalarConverterDeclaration}}]
            public abstract Guid ExternalId { get; }
        }
        """;
    }

    private static string CreateGuidSchema(string sqliteType) =>
        $$"""
        CREATE TABLE "account" (
            "id" INTEGER PRIMARY KEY,
            "external_id" {{sqliteType}} NOT NULL
        );
        """;

    private static string FormatDifferences(IReadOnlyList<SchemaDifference> differences) =>
        string.Join(
            "; ",
            differences.Select(difference => $"{difference.Kind} {difference.Path}: {difference.Message}"));

    private sealed class SchemaValidatorFixture : IDisposable
    {
        private readonly string basePath;
        private readonly string databasePath;

        private SchemaValidatorFixture(string basePath, string databasePath)
        {
            this.basePath = basePath;
            this.databasePath = databasePath;
        }

        public static SchemaValidatorFixture Create() =>
            Create(
                """
                using DataLinq;
                using DataLinq.Attributes;
                using DataLinq.Instances;
                using DataLinq.Interfaces;
                using DataLinq.Mutation;

                namespace DataLinq.Tests.SchemaValidation;

                [Database("validation_db")]
                public partial class ValidationDb(DataSourceAccess dataSource) : IDatabaseModel
                {
                    public DbRead<Account> Accounts { get; } = new(dataSource);
                }

                [Table("account")]
                public abstract partial class Account(IRowData rowData, IDataSourceAccess dataSource)
                    : Immutable<Account, ValidationDb>(rowData, dataSource), ITableModel<ValidationDb>
                {
                    [Column("id"), PrimaryKey, AutoIncrement, Type(DatabaseType.SQLite, "integer")]
                    public abstract int Id { get; }

                    [Column("display_name"), Type(DatabaseType.SQLite, "text"), Default("anonymous")]
                    public abstract string DisplayName { get; }
                }
                """,
                """
                CREATE TABLE "account" (
                    "id" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "display_name" TEXT NOT NULL DEFAULT 'anonymous'
                );
                """);

        public static SchemaValidatorFixture Create(string modelSource, string schema)
        {
            var basePath = Path.Combine(Path.GetTempPath(), $"datalinq-schema-validator-{Guid.NewGuid():N}");
            var modelPath = Path.Combine(basePath, "models");
            Directory.CreateDirectory(modelPath);

            File.WriteAllText(
                Path.Combine(modelPath, "ValidationDb.cs"),
                modelSource);

            var databasePath = Path.Combine(basePath, "validation.db");
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = schema;
            command.ExecuteNonQuery();

            return new SchemaValidatorFixture(basePath, databasePath);
        }

        public ThrowAway.Option<SchemaValidationRunResult, DataLinq.ErrorHandling.IDLOptionFailure> Validate()
        {
            SQLiteProvider.RegisterProvider();

            var config = new DataLinqConfig(
                basePath,
                new ConfigFile
                {
                    Databases =
                    [
                        new ConfigFileDatabase
                        {
                            Name = "validation_db",
                            CsType = "ValidationDb",
                            Namespace = "DataLinq.Tests.SchemaValidation",
                            SourceDirectories = ["ignored-source-models"],
                            ModelDirectory = "models",
                            FileEncoding = "UTF-8",
                            Connections =
                            [
                                new ConfigFileDatabaseConnection
                                {
                                    Type = "SQLite",
                                    DataSourceName = "validation.db",
                                    ConnectionString = "Data Source=validation.db"
                                }
                            ]
                        }
                    ]
                });
            var connection = config.GetConnection("validation_db", DatabaseType.SQLite).Value.connection;
            return new SchemaValidator(_ => { }).Validate(connection, basePath, null);
        }

        public void ExecuteNonQuery(string statement)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(basePath))
                    Directory.Delete(basePath, recursive: true);
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
