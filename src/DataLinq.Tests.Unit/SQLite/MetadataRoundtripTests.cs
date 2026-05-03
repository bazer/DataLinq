using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.SQLite;
using DataLinq.Testing;
using DataLinq.Validation;
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
        var validationDifferences = SchemaComparer.Compare(firstRead, secondRead, DatabaseType.SQLite);

        await Assert.That(differences).IsEmpty();
        await Assert.That(validationDifferences).IsEmpty();
    }

    [Test]
    public async Task ParseDatabase_LiveSchemaDrift_ProducesValidationDifferences()
    {
        using var fixture = SqliteRoundtripFixture.Create(
            """
            CREATE TABLE "account" (
                "id" INTEGER PRIMARY KEY AUTOINCREMENT,
                "display_name" TEXT NOT NULL DEFAULT 'anonymous'
            );
            """);

        var model = fixture.ParseDatabase();
        fixture.ExecuteNonQuery("ALTER TABLE \"account\" ADD COLUMN \"nickname\" TEXT;");
        fixture.ExecuteNonQuery(
            """
            CREATE TABLE "audit_log" (
                "id" INTEGER PRIMARY KEY AUTOINCREMENT
            );
            """);

        var database = fixture.ParseDatabase();
        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.ExtraColumn, SchemaDifferenceKind.ExtraTable]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraColumn).Path)
            .IsEqualTo("account.nickname");
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraTable).Path)
            .IsEqualTo("audit_log");
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

    [Test]
    public async Task ParseDatabase_QuotedIdentifiers_RoundTripWithStableCSharpNames()
    {
        using var source = SqliteRoundtripFixture.CreateQuotedIdentifierSchema();

        var firstRead = source.ParseDatabase();
        var orderItems = firstRead.TableModels.Single(x => x.Table.DbName == "order-items");
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(firstRead)
            .Single(file => file.path == "OrderItems.cs");
        var generatedSql = new SqlFromSQLiteFactory()
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = SqliteRoundtripFixture.Create(generatedSql.Text);
        var secondRead = generated.ParseDatabase();
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, DatabaseType.SQLite);

        await Assert.That(orderItems.Model.CsType.Name).IsEqualTo("OrderItems");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "order-id").ValueProperty.PropertyName).IsEqualTo("OrderId");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "class").ValueProperty.PropertyName).IsEqualTo("Class");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "ship.to").ValueProperty.PropertyName).IsEqualTo("ShipTo");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "2fa code").ValueProperty.PropertyName).IsEqualTo("_2faCode");
        await Assert.That(orderItems.Table.Columns.Single(x => x.DbName == "total$amount").ValueProperty.PropertyName).IsEqualTo("TotalAmount");
        await Assert.That(generatedFile.contents).Contains("[Table(\"order-items\")]");
        await Assert.That(generatedFile.contents).Contains("[Column(\"ship.to\")]");
        await Assert.That(generatedFile.contents).Contains("[Column(\"2fa code\")]");
        await Assert.That(generatedSql.Text).Contains("CREATE TABLE IF NOT EXISTS \"order-items\"");
        await Assert.That(generatedSql.Text).Contains("\"ship.to\"");
        await Assert.That(generatedSql.Text).Contains("\"2fa code\"");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task ParseDatabase_CompositeIndexes_RoundTripPreservesOrderedColumnsAndNames()
    {
        using var source = SqliteRoundtripFixture.CreateCompositeIndexSchema();

        var firstRead = source.ParseDatabase();
        var account = firstRead.TableModels.Single(x => x.Table.DbName == "account").Table;
        var lookupIndex = account.ColumnIndices.Single(x => x.Name == "idx_account_lookup");
        var uniqueIndex = account.ColumnIndices.Single(x => x.Name == "ux_account_year_number");
        var generatedSql = new SqlFromSQLiteFactory()
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = SqliteRoundtripFixture.Create(generatedSql.Text);
        var secondRead = generated.ParseDatabase();
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, DatabaseType.SQLite);

        await Assert.That(lookupIndex.Characteristic).IsEqualTo(IndexCharacteristic.Simple);
        await Assert.That(lookupIndex.Columns.Select(x => x.DbName).SequenceEqual(["accounting_year", "account_number"])).IsTrue();
        await Assert.That(uniqueIndex.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(uniqueIndex.Columns.Select(x => x.DbName).SequenceEqual(["accounting_year", "account_number"])).IsTrue();
        await Assert.That(generatedSql.Text).Contains("CREATE INDEX IF NOT EXISTS \"idx_account_lookup\" ON \"account\" (\"accounting_year\", \"account_number\")");
        await Assert.That(generatedSql.Text).Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_account_year_number\" ON \"account\" (\"accounting_year\", \"account_number\")");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task ParseDatabase_CompositeForeignKey_RoundTripAsSingleRelation()
    {
        using var source = SqliteRoundtripFixture.CreateCompositeForeignKeySchema();

        var firstRead = source.ParseDatabase();
        var orderLine = firstRead.TableModels.Single(x => x.Table.DbName == "order_line");
        var foreignKeyIndex = orderLine.Table.ColumnIndices.Single(x => x.Characteristic == IndexCharacteristic.ForeignKey);
        var foreignKeyPart = foreignKeyIndex.RelationParts.Single(x => x.Type == RelationPartType.ForeignKey);
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(firstRead)
            .Single(file => file.path == "OrderLine.cs");
        var generatedSql = new SqlFromSQLiteFactory()
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = SqliteRoundtripFixture.Create(generatedSql.Text);
        var secondRead = generated.ParseDatabase();
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, DatabaseType.SQLite);

        await Assert.That(foreignKeyIndex.Columns.Select(x => x.DbName).SequenceEqual(["tenant_id", "order_no"])).IsTrue();
        await Assert.That(foreignKeyPart.Relation.CandidateKey.ColumnIndex.Columns.Select(x => x.DbName).SequenceEqual(["tenant_id", "order_no"])).IsTrue();
        await Assert.That(orderLine.Model.RelationProperties.Keys).Contains("OrderHeader");
        await Assert.That(generatedFile.contents).Contains("[ForeignKey(\"order_header\", \"tenant_id\", \"0\", 1");
        await Assert.That(generatedFile.contents).Contains("[ForeignKey(\"order_header\", \"order_no\", \"0\", 2");
        await Assert.That(generatedFile.contents).Contains("[Relation(\"order_header\", new string[] { \"tenant_id\", \"order_no\" }, \"0\")]");
        await Assert.That(generatedSql.Text).Contains("FOREIGN KEY (\"tenant_id\", \"order_no\") REFERENCES \"order_header\" (\"tenant_id\", \"order_no\")");
        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task ParseDatabase_ForeignKeyReferentialActions_RoundTrip()
    {
        using var source = SqliteRoundtripFixture.Create(
            """
            CREATE TABLE "account" (
                "id" INTEGER PRIMARY KEY
            );
            """,
            """
            CREATE TABLE "invoice" (
                "id" INTEGER PRIMARY KEY,
                "account_id" INTEGER NULL,
                CONSTRAINT "FK_invoice_account" FOREIGN KEY ("account_id") REFERENCES "account"("id") ON UPDATE CASCADE ON DELETE SET NULL
            );
            """);

        var firstRead = source.ParseDatabase();
        var invoice = firstRead.TableModels.Single(x => x.Table.DbName == "invoice").Table;
        var relation = invoice.ColumnIndices
            .SelectMany(x => x.RelationParts)
            .Single(x => x.Type == RelationPartType.ForeignKey)
            .Relation;
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(firstRead)
            .Single(file => file.path == "Invoice.cs");
        var generatedSql = new SqlFromSQLiteFactory()
            .GetCreateTables(firstRead, foreignKeyRestrict: false)
            .ValueOrException();

        using var generated = SqliteRoundtripFixture.Create(generatedSql.Text);
        var secondRead = generated.ParseDatabase();
        var differences = MetadataRoundtripComparison.CompareSupportedSubset(firstRead, secondRead, DatabaseType.SQLite);

        await Assert.That(relation.OnUpdate).IsEqualTo(ReferentialAction.Cascade);
        await Assert.That(relation.OnDelete).IsEqualTo(ReferentialAction.SetNull);
        await Assert.That(generatedFile.contents).Contains("[ForeignKey(\"account\", \"id\", \"0\", ReferentialAction.Cascade, ReferentialAction.SetNull)]");
        await Assert.That(generatedSql.Text).Contains("ON UPDATE CASCADE ON DELETE SET NULL");
        await Assert.That(differences).IsEmpty();
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

        public static SqliteRoundtripFixture CreateQuotedIdentifierSchema()
        {
            return Create(
                """
                CREATE TABLE "order-items" (
                    "order-id" INTEGER PRIMARY KEY,
                    "class" TEXT NOT NULL,
                    "ship.to" TEXT NOT NULL,
                    "2fa code" TEXT NOT NULL,
                    "total$amount" INTEGER NOT NULL
                );
                """);
        }

        public static SqliteRoundtripFixture CreateCompositeForeignKeySchema()
        {
            return Create(
                """
                CREATE TABLE "order_header" (
                    "tenant_id" INTEGER NOT NULL,
                    "order_no" INTEGER NOT NULL,
                    "summary" TEXT NOT NULL,
                    PRIMARY KEY ("tenant_id", "order_no")
                );
                """,
                """
                CREATE TABLE "order_line" (
                    "line_id" INTEGER PRIMARY KEY,
                    "tenant_id" INTEGER NOT NULL,
                    "order_no" INTEGER NOT NULL,
                    "sku" TEXT NOT NULL,
                    CONSTRAINT "FK_order_line_header" FOREIGN KEY ("tenant_id", "order_no") REFERENCES "order_header"("tenant_id", "order_no")
                );
                """);
        }

        public static SqliteRoundtripFixture CreateCompositeIndexSchema()
        {
            return Create(
                """
                CREATE TABLE "account" (
                    "id" INTEGER PRIMARY KEY,
                    "accounting_year" INTEGER NOT NULL,
                    "account_number" INTEGER NOT NULL,
                    "display_name" TEXT NOT NULL
                );
                """,
                """
                CREATE INDEX "idx_account_lookup" ON "account" ("accounting_year", "account_number");
                """,
                """
                CREATE UNIQUE INDEX "ux_account_year_number" ON "account" ("accounting_year", "account_number");
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

        public void ExecuteNonQuery(string statement)
        {
            using var connection = new SqliteConnection($"Data Source={DatabasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
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
