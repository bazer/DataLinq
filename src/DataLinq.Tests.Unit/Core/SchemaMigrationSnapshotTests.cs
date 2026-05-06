using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Tools;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class SchemaMigrationSnapshotTests
{
    [Test]
    public async Task FromDatabase_CapturesProviderSpecificShapeInDeterministicOrder()
    {
        var database = CreateDatabase(
            CreateTable(
                "invoice",
                [
                    CreateColumn("invoice_id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn(
                        "account_id",
                        typeof(int),
                        nullable: false,
                        attributes: [new ForeignKeyAttribute("account", "id", "FK_invoice_account")])
                ]),
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true, autoIncrement: true),
                    CreateColumn("display_name", typeof(string), nullable: false, defaultValue: "anonymous")
                ],
                [
                    new IndexAttribute("idx_account_display_name", IndexCharacteristic.Simple, IndexType.BTREE, "display_name"),
                    new CheckAttribute(DatabaseType.MariaDB, "CK_account_id", "`id` > 0"),
                    new CommentAttribute(DatabaseType.MariaDB, "Account table")
                ]));

        var snapshot = SchemaMigrationSnapshot.FromDatabase(
            database,
            DatabaseType.MariaDB,
            new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

        await Assert.That(snapshot.FormatVersion).IsEqualTo(SchemaMigrationSnapshot.CurrentFormatVersion);
        await Assert.That(snapshot.DatabaseType).IsEqualTo("MariaDB");
        await Assert.That(snapshot.GeneratedAtUtc).IsEqualTo("2026-05-01T12:00:00.0000000Z");
        await Assert.That(string.Join(",", snapshot.Tables.Select(x => x.Name))).IsEqualTo("account,invoice");

        var accountSnapshot = snapshot.Tables.Single(x => x.Name == "account");
        await Assert.That(string.Join(",", accountSnapshot.Columns.Select(x => x.Name))).IsEqualTo("id,display_name");
        await Assert.That(accountSnapshot.Columns.Single(x => x.Name == "display_name").Default).IsEqualTo("DefaultAttribute|anonymous");
        await Assert.That(accountSnapshot.Indexes.Single().Columns.ToArray()).IsEquivalentTo(["display_name"]);
        await Assert.That(accountSnapshot.Checks.Single().Name).IsEqualTo("CK_account_id");
        await Assert.That(accountSnapshot.Comment).IsEqualTo("Account table");

        var invoiceSnapshot = snapshot.Tables.Single(x => x.Name == "invoice");
        await Assert.That(invoiceSnapshot.ForeignKeys.Single().Name).IsEqualTo("FK_invoice_account");
        await Assert.That(invoiceSnapshot.ForeignKeys.Single().Columns.ToArray()).IsEquivalentTo(["account_id"]);
        await Assert.That(invoiceSnapshot.ForeignKeys.Single().PrincipalColumns.ToArray()).IsEquivalentTo(["id"]);
    }

    [Test]
    public async Task ToJson_RoundTripsWithoutSourceLocationsOrRuntimeMetadata()
    {
        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true)
                ]));

        var snapshot = SchemaMigrationSnapshot.FromDatabase(
            database,
            DatabaseType.SQLite,
            new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));

        var json = snapshot.ToJson();
        var roundTripped = SchemaMigrationSnapshot.FromJson(json);

        await Assert.That(json).Contains("\"formatVersion\": 1");
        await Assert.That(json).DoesNotContain("source");
        await Assert.That(roundTripped.FormatVersion).IsEqualTo(snapshot.FormatVersion);
        await Assert.That(roundTripped.DatabaseName).IsEqualTo(snapshot.DatabaseName);
        await Assert.That(roundTripped.DatabaseType).IsEqualTo(snapshot.DatabaseType);
        await Assert.That(roundTripped.ModelType).IsEqualTo(snapshot.ModelType);
        await Assert.That(roundTripped.ModelNamespace).IsEqualTo(snapshot.ModelNamespace);
        await Assert.That(roundTripped.GeneratedAtUtc).IsEqualTo(snapshot.GeneratedAtUtc);
        await Assert.That(roundTripped.Tables.Single().Columns.Single().Name).IsEqualTo("id");
        await Assert.That(roundTripped.Tables.Single().Columns.Single().DbType?.Name).IsEqualTo("integer");
    }

    private static DatabaseDefinition CreateDatabase(params MetadataTableModelDraft[] tableModels)
    {
        var draft = new MetadataDatabaseDraft(
            "TestDb",
            new CsTypeDeclaration("TestDb", "DataLinq.Tests", ModelCsType.Class))
        {
            TableModels = tableModels
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static MetadataTableModelDraft CreateTable(
        string tableName,
        MetadataValuePropertyDraft[] columns,
        Attribute[]? attributes = null)
    {
        return new MetadataTableModelDraft(
            ToCsName(tableName),
            new MetadataModelDraft(new CsTypeDeclaration(ToCsName(tableName), "DataLinq.Tests", ModelCsType.Class))
            {
                Attributes = attributes ?? [],
                ValueProperties = columns
            },
            new MetadataTableDraft(tableName));
    }

    private static MetadataValuePropertyDraft CreateColumn(
        string columnName,
        Type csType,
        bool nullable,
        bool primaryKey = false,
        bool autoIncrement = false,
        object? defaultValue = null,
        Attribute[]? attributes = null)
    {
        var propertyAttributes = defaultValue == null
            ? attributes ?? []
            : [new DefaultAttribute(defaultValue), .. (attributes ?? [])];

        return new MetadataValuePropertyDraft(
            ToCsName(columnName),
            new CsTypeDeclaration(csType),
            new MetadataColumnDraft(columnName)
            {
                PrimaryKey = primaryKey,
                AutoIncrement = autoIncrement,
                Nullable = nullable,
                ForeignKey = propertyAttributes.Any(static x => x is ForeignKeyAttribute),
                DbTypes =
                [
                    GetColumnType(DatabaseType.SQLite, csType),
                    GetColumnType(DatabaseType.MySQL, csType),
                    GetColumnType(DatabaseType.MariaDB, csType)
                ]
            })
        {
            Attributes = propertyAttributes
        };
    }

    private static DatabaseColumnType GetColumnType(DatabaseType databaseType, Type csType)
    {
        if (databaseType == DatabaseType.SQLite)
        {
            return csType == typeof(string)
                ? new DatabaseColumnType(databaseType, "text")
                : new DatabaseColumnType(databaseType, "integer");
        }

        return csType == typeof(string)
            ? new DatabaseColumnType(databaseType, "varchar", 40)
            : new DatabaseColumnType(databaseType, "int", signed: true);
    }

    private static string ToCsName(string value) =>
        string.Join(
            "",
            value.Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
}
