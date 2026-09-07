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
    [Arguments(DatabaseType.SQLite, true)]
    [Arguments(DatabaseType.SQLite, false)]
    [Arguments(DatabaseType.MySQL, true)]
    [Arguments(DatabaseType.MySQL, false)]
    [Arguments(DatabaseType.MariaDB, true)]
    [Arguments(DatabaseType.MariaDB, false)]
    public async Task MissingViewIsAnExplicitManualActionAndNeverBecomesATable(DatabaseType provider, bool hasDefinition)
    {
        var definition = hasDefinition ? "SELECT id,\r\n name FROM account\n-- definition comment\rSELECT '*/';" : null;
        // Validated model drafts require a definition. The public script API also
        // accepts incomplete metadata built directly by callers.
        var differences = hasDefinition
            ? SchemaComparer.Compare(CreateDatabase(CreateTable("active_account", [CreateColumn("id", typeof(int), false)],
                tableType: TableType.View, viewDefinition: definition)), CreateDatabase(), provider).ToArray()
            : [new SchemaDifference(SchemaDifferenceKind.MissingTable, SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Additive, "active_account", "Missing view", new ViewDefinition("active_account"))];
        await Assert.That(differences.Single().ModelDefinition).IsTypeOf<ViewDefinition>();
        var script = new SchemaDiffScriptGenerator().Generate(provider, differences);
        await Assert.That(script).Contains("Manual action required: create view");
        await Assert.That(script).DoesNotContain("CREATE TABLE");
        if (hasDefinition)
            await Assert.That(script).Contains("-- SELECT id,");
        else
            await Assert.That(script).Contains("No view definition is available");
        // Everything following the fixed banner is a blank or line comment.
        await Assert.That(script.Split('\n').Skip(3).All(line =>
            string.IsNullOrWhiteSpace(line) || line.StartsWith("-- ", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [Arguments(DatabaseType.SQLite)]
    [Arguments(DatabaseType.MySQL)]
    [Arguments(DatabaseType.MariaDB)]
    public async Task MissingViewDoesNotPreventOrdinaryTableCreation(DatabaseType provider)
    {
        var model = CreateDatabase(
            CreateTable("account", [CreateColumn("id", typeof(int), false, primaryKey: true)]),
            CreateTable("active_account", [CreateColumn("id", typeof(int), false)], tableType: TableType.View, viewDefinition: "SELECT id FROM account"));
        var script = new SchemaDiffScriptGenerator().Generate(provider, SchemaComparer.Compare(model, CreateDatabase(), provider));
        await Assert.That(script.Split("CREATE TABLE IF NOT EXISTS").Length - 1).IsEqualTo(1);
        await Assert.That(script).Contains("Manual action required: create view");
    }

    [Test]
    [Arguments(SchemaDifferenceSafety.Additive)]
    [Arguments(SchemaDifferenceSafety.Informational)]
    [Arguments(SchemaDifferenceSafety.Ambiguous)]
    public async Task ReviewPathsCannotEscapeTheirLineComments(SchemaDifferenceSafety safety)
    {
        var difference = new SchemaDifference(SchemaDifferenceKind.MissingTable, SchemaDifferenceSeverity.Warning,
            safety, "view\r\nDROP TABLE account;", "message\nDELETE FROM account;",
            safety == SchemaDifferenceSafety.Additive ? new ViewDefinition("view") : null);
        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.SQLite, [difference]);
        await Assert.That(script.Split('\n').Skip(3).All(line =>
            string.IsNullOrWhiteSpace(line) || line.StartsWith("-- ", StringComparison.Ordinal))).IsTrue();
    }

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
        await Assert.That(script).Contains("-- REVIEW REQUIRED Warning/Destructive ExtraColumn account.legacy_name");
        await Assert.That(script).Contains("-- No SQL generated: destructive change.");
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

    [Test]
    public async Task Generate_ForeignKeyActionMismatch_ExplainsReviewOnlyDifference()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]),
            CreateTable(
                "invoice",
                [
                    CreateColumn(
                        "account_id",
                        typeof(int),
                        nullable: false,
                        primaryKey: true,
                        attributes: [new ForeignKeyAttribute("account", "id", "FK_invoice_account")])
                ]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]),
            CreateTable(
                "invoice",
                [
                    CreateColumn(
                        "account_id",
                        typeof(int),
                        nullable: false,
                        primaryKey: true,
                        attributes: [new ForeignKeyAttribute("account", "id", "FK_invoice_account", ReferentialAction.Restrict, ReferentialAction.Restrict)])
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MariaDB);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MariaDB, differences);

        await Assert.That(script).Contains("-- REVIEW REQUIRED Error/Ambiguous ForeignKeyActionMismatch invoice.FK_invoice_account");
        await Assert.That(script).Contains("Model: ON UPDATE not specified, ON DELETE not specified; database: ON UPDATE Restrict, ON DELETE Restrict.");
        await Assert.That(script).Contains("-- No SQL generated: ambiguous change.");
    }

    [Test]
    public async Task Generate_InformationalDifference_UsesInfoComment()
    {
        var model = CreateDatabase(
            CreateTable(
                "account",
                [CreateColumn("id", typeof(int), nullable: false, primaryKey: true)]));
        var database = CreateDatabase(
            CreateTable(
                "account",
                [
                    CreateColumn(
                        "id",
                        typeof(int),
                        nullable: false,
                        primaryKey: true,
                        attributes: [new CommentAttribute(DatabaseType.MariaDB, "database comment")])
                ]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MariaDB);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MariaDB, differences);

        await Assert.That(script).Contains("-- INFO ColumnCommentMismatch account.id");
        await Assert.That(script).Contains("-- No SQL generated for informational metadata drift.");
    }

    [Test]
    public async Task Generate_CanonicalTypeMismatch_RequiresReviewWithoutSql()
    {
        var difference = new SchemaDifference(
            SchemaDifferenceKind.ColumnCanonicalTypeMismatch,
            SchemaDifferenceSeverity.Error,
            SchemaDifferenceSafety.Ambiguous,
            "account.id",
            "Canonical Int32 is incompatible with VARCHAR storage.");

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MySQL, [difference]);

        await Assert.That(script).Contains("-- REVIEW REQUIRED Error/Ambiguous ColumnCanonicalTypeMismatch account.id");
        await Assert.That(script).Contains("-- No SQL generated: ambiguous change.");
        await Assert.That(script).DoesNotContain("ALTER TABLE");
        await Assert.That(script).DoesNotContain("CREATE TABLE");
        await Assert.That(script).DoesNotContain("CREATE INDEX");
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
        Attribute[]? attributes = null,
        TableType tableType = TableType.Table,
        string? viewDefinition = null)
    {
        return new MetadataTableModelDraft(
            ToCsName(tableName),
            new MetadataModelDraft(new CsTypeDeclaration(ToCsName(tableName), "DataLinq.Tests", ModelCsType.Class))
            {
                Attributes = attributes ?? [],
                ValueProperties = columns
            },
            new MetadataTableDraft(tableName) { Type = tableType, Definition = viewDefinition });
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
