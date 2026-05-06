using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.Tools;

#pragma warning disable CS0618 // These tests intentionally build legacy metadata fixtures while Workstream C keeps compatibility mutators.

namespace DataLinq.Tests.Unit.Core;

public class SchemaMigrationSnapshotTests
{
    [Test]
    public async Task FromDatabase_CapturesProviderSpecificShapeInDeterministicOrder()
    {
        var database = CreateDatabase();
        var invoice = AddTable(database, "invoice");
        AddColumn(invoice, "invoice_id", typeof(int), nullable: false, primaryKey: true);
        AddColumn(invoice, "account_id", typeof(int), nullable: false);

        var account = AddTable(database, "account");
        AddColumn(account, "id", typeof(int), nullable: false, primaryKey: true, autoIncrement: true);
        AddColumn(account, "display_name", typeof(string), nullable: false, defaultValue: "anonymous");
        AddForeignKey(database, "FK_invoice_account", "invoice", ["account_id"], "account", ["id"]);
        account.ColumnIndices.Add(new ColumnIndex(
            "idx_account_display_name",
            IndexCharacteristic.Simple,
            IndexType.BTREE,
            [FindColumn(account, "display_name")]));
        account.Model.SetAttributes([
            new CheckAttribute(DatabaseType.MariaDB, "CK_account_id", "`id` > 0"),
            new CommentAttribute(DatabaseType.MariaDB, "Account table")
        ]);

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
        var database = CreateDatabase();
        var account = AddTable(database, "account");
        AddColumn(account, "id", typeof(int), nullable: false, primaryKey: true);

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

    private static DatabaseDefinition CreateDatabase() =>
        new("TestDb", new CsTypeDeclaration("TestDb", "DataLinq.Tests", ModelCsType.Class));

    private static TableDefinition AddTable(DatabaseDefinition database, string tableName)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(ToCsName(tableName), "DataLinq.Tests", ModelCsType.Class));
        var table = new TableDefinition(tableName);
        var tableModel = new TableModel(ToCsName(tableName), database, model, table);
        database.SetTableModels([.. database.TableModels, tableModel]);
        return table;
    }

    private static ColumnDefinition AddColumn(
        TableDefinition table,
        string columnName,
        Type csType,
        bool nullable,
        bool primaryKey = false,
        bool autoIncrement = false,
        object? defaultValue = null)
    {
        System.Attribute[] propertyAttributes = defaultValue == null
            ? []
            : [new DefaultAttribute(defaultValue)];
        var property = new ValueProperty(ToCsName(columnName), new CsTypeDeclaration(csType), table.Model, propertyAttributes);
        var column = new ColumnDefinition(columnName, table);

        column.SetIndex(table.Columns.Length);
        column.SetValueProperty(property);
        column.SetNullable(nullable);
        column.SetAutoIncrement(autoIncrement);
        column.AddDbType(GetColumnType(DatabaseType.SQLite, csType));
        column.AddDbType(GetColumnType(DatabaseType.MySQL, csType));
        column.AddDbType(GetColumnType(DatabaseType.MariaDB, csType));
        table.Model.AddProperty(property);
        table.SetColumns([.. table.Columns, column]);

        if (primaryKey)
            column.SetPrimaryKey();

        return column;
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

    private static ColumnDefinition FindColumn(TableDefinition table, string columnName) =>
        table.Columns.Single(x => x.DbName == columnName);

    private static TableDefinition FindTable(DatabaseDefinition database, string tableName) =>
        database.TableModels.Single(x => x.Table.DbName == tableName).Table;

    private static void AddForeignKey(
        DatabaseDefinition database,
        string constraintName,
        string foreignKeyTableName,
        string[] foreignKeyColumns,
        string candidateKeyTableName,
        string[] candidateKeyColumns)
    {
        var foreignKeyTable = FindTable(database, foreignKeyTableName);
        var candidateKeyTable = FindTable(database, candidateKeyTableName);
        var foreignKeyIndex = new ColumnIndex(
            constraintName,
            IndexCharacteristic.ForeignKey,
            IndexType.BTREE,
            foreignKeyColumns.Select(column => FindColumn(foreignKeyTable, column)).ToList());
        var candidateKeyIndex = new ColumnIndex(
            $"{constraintName}_candidate",
            IndexCharacteristic.PrimaryKey,
            IndexType.BTREE,
            candidateKeyColumns.Select(column => FindColumn(candidateKeyTable, column)).ToList());
        var relation = new RelationDefinition(constraintName, RelationType.OneToMany);
        var foreignKeyPart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, candidateKeyTable.Model.CsType.Name);
        var candidateKeyPart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, foreignKeyTable.Model.CsType.Name);

        relation.ForeignKey = foreignKeyPart;
        relation.CandidateKey = candidateKeyPart;
        foreignKeyIndex.RelationParts.Add(foreignKeyPart);
        candidateKeyIndex.RelationParts.Add(candidateKeyPart);
        foreignKeyTable.ColumnIndices.Add(foreignKeyIndex);
        candidateKeyTable.ColumnIndices.Add(candidateKeyIndex);
    }

    private static string ToCsName(string value) =>
        string.Join(
            "",
            value.Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
}
