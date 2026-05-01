using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.SQLite;
using DataLinq.Testing;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public class MetadataRoundtripTests
{
    [Test]
    public async Task CreateReadGenerateCreateRead_PreservesFirstSliceSupportedSubset()
    {
        using var source = SqliteRoundtripFixture.CreateFirstSliceSchema();

        var firstRead = source.ParseDatabase();
        var generatedSql = new SqlFromSQLiteFactory()
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = SqliteRoundtripFixture.Create(generatedSql.Text);
        var secondRead = generated.ParseDatabase();
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, DatabaseType.SQLite);

        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task ParseDatabase_FirstSliceSchema_CapturesIdentifiersIndexesAndDuplicateRelations()
    {
        using var fixture = SqliteRoundtripFixture.CreateFirstSliceSchema();

        var database = fixture.ParseDatabase();
        var account = database.TableModels.Single(x => x.Table.DbName == "account").Table;
        var invoice = database.TableModels.Single(x => x.Table.DbName == "invoice").Table;
        var accountProfile = database.TableModels.Single(x => x.Table.DbName == "account_profile").Table;

        await Assert.That(account.Columns.Any(x => x.DbName == "account id" && x.ValueProperty.PropertyName == "AccountId")).IsTrue();
        await Assert.That(account.Columns.Any(x => x.DbName == "select" && x.ValueProperty.PropertyName == "Select")).IsTrue();
        await Assert.That(account.Columns.Any(x => x.DbName == "display name" && x.ValueProperty.PropertyName == "DisplayName")).IsTrue();

        await Assert.That(accountProfile.Columns.Single(x => x.DbName == "account id").PrimaryKey).IsTrue();
        await Assert.That(accountProfile.Columns.Single(x => x.DbName == "account id").ForeignKey).IsTrue();

        await Assert.That(invoice.ColumnIndices.Any(x =>
            x.Name == "idx_invoice_created_by" &&
            x.Characteristic == IndexCharacteristic.Simple &&
            x.Columns.Select(c => c.DbName).SequenceEqual(["created by account id"]))).IsTrue();

        await Assert.That(invoice.Columns.Count(x => x.ForeignKey)).IsEqualTo(3);
        await Assert.That(invoice.Model.RelationProperties.Keys.OrderBy(x => x).ToArray())
            .IsEquivalentTo(["ApprovedByAccount", "CreatedByAccount", "ExternalAccount"]);
        await Assert.That(account.Model.RelationProperties.Keys.OrderBy(x => x).ToArray())
            .IsEquivalentTo(["AccountProfile", "InvoiceApprovedByAccount", "InvoiceCreatedByAccount", "InvoiceExternalAccount"]);
    }

    private sealed class SqliteRoundtripFixture : IDisposable
    {
        private SqliteRoundtripFixture(string databasePath)
        {
            DatabasePath = databasePath;
        }

        public string DatabasePath { get; }

        public static SqliteRoundtripFixture CreateFirstSliceSchema()
        {
            return Create(
                """
                CREATE TABLE "account" (
                    "account id" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "select" TEXT NOT NULL,
                    "display name" TEXT NOT NULL DEFAULT 'anonymous'
                );
                """,
                """
                CREATE TABLE "account_profile" (
                    "account id" INTEGER PRIMARY KEY,
                    "bio" TEXT,
                    CONSTRAINT "FK_profile_account" FOREIGN KEY ("account id") REFERENCES "account"("account id")
                );
                """,
                """
                CREATE TABLE "invoice" (
                    "invoice id" INTEGER PRIMARY KEY,
                    "created by account id" INTEGER NOT NULL,
                    "approved by account id" INTEGER,
                    "external account id" INTEGER NOT NULL,
                    "number" TEXT NOT NULL,
                    CONSTRAINT "FK_invoice_created_by" FOREIGN KEY ("created by account id") REFERENCES "account"("account id"),
                    CONSTRAINT "FK_invoice_approved_by" FOREIGN KEY ("approved by account id") REFERENCES "account"("account id"),
                    CONSTRAINT "FK_invoice_external_account" FOREIGN KEY ("external account id") REFERENCES "account"("account id")
                );
                """,
                """
                CREATE INDEX "idx_invoice_created_by" ON "invoice" ("created by account id");
                """,
                """
                CREATE INDEX "idx_invoice_external_account" ON "invoice" ("external account id");
                """);
        }

        public static SqliteRoundtripFixture Create(params string[] statements)
        {
            var path = Path.Combine(Path.GetTempPath(), $"datalinq-sqlite-roundtrip-{Guid.NewGuid():N}.db");
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();

            foreach (var statement in statements)
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

            return new SqliteRoundtripFixture(path);
        }

        public DatabaseDefinition ParseDatabase()
        {
            return new MetadataFromSQLiteFactory(new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true })
                .ParseDatabase(
                    "RoundtripDb",
                    "RoundtripDb",
                    "DataLinq.Tests.Roundtrip",
                    DatabasePath,
                    $"Data Source={DatabasePath}")
                .ValueOrException();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(DatabasePath))
                    File.Delete(DatabasePath);
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
