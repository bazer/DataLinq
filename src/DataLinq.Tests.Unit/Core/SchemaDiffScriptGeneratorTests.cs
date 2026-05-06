using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Tools;
using DataLinq.Validation;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class SchemaDiffScriptGeneratorTests
{
    [Test]
    public async Task Generate_SQLiteMissingTable_CreatesTableAndIndexes()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true, autoIncrement: true),
                    CreateColumn("display_name", typeof(string), nullable: false, defaultValue: "anonymous")
                ],
                [new IndexAttribute("idx_account_display_name", IndexCharacteristic.Simple, IndexType.BTREE, "display_name")]));
        var database = CreateDatabase();
        var id = model.TableModels.Single().Table.Columns.Single(x => x.DbName == "id");

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.SQLite, differences);

        await Assert.That(script).Contains("CREATE TABLE IF NOT EXISTS \"account\"");
        await Assert.That(script).Contains("\"id\" INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL");
        await Assert.That(script).Contains("\"display_name\" TEXT DEFAULT 'anonymous' NOT NULL");
        await Assert.That(script).Contains("CREATE INDEX IF NOT EXISTS \"idx_account_display_name\" ON \"account\" (\"display_name\");");
        await Assert.That(id.PrimaryKey).IsTrue();
    }

    [Test]
    public async Task Generate_MySqlMissingColumn_UsesAlterTableAndCommentsUnsafeDrift()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("nickname", typeof(string), nullable: true, defaultValue: "new")
                ]));

        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("legacy_name", typeof(string), nullable: true)
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MySQL, differences);

        await Assert.That(script).Contains("ALTER TABLE `account` ADD COLUMN `nickname` VARCHAR(40) DEFAULT 'new' NULL;");
        await Assert.That(script).Contains("-- DESTRUCTIVE ExtraColumn account.legacy_name");
        await Assert.That(script).Contains("-- Manual review required. No SQL was generated for this difference.");
    }

    [Test]
    public async Task Generate_MariaDbMissingUniqueIndex_CreatesProviderSpecificIndex()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("tenant_id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("account_no", typeof(int), nullable: false)
                ],
                [new IndexAttribute("ux_account_tenant_account", IndexCharacteristic.Unique, IndexType.BTREE, "tenant_id", "account_no")]));

        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn("tenant_id", typeof(int), nullable: false, primaryKey: true),
                    CreateColumn("account_no", typeof(int), nullable: false)
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MariaDB);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MariaDB, differences);

        await Assert.That(script).Contains("CREATE UNIQUE INDEX `ux_account_tenant_account` USING BTREE ON `account` (`tenant_id`, `account_no`);");
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
