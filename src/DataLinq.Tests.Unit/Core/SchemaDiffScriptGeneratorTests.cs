using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.Tools;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public class SchemaDiffScriptGeneratorTests
{
    [Test]
    public async Task Generate_SQLiteMissingTable_CreatesTableAndIndexes()
    {
        var model = CreateDatabase();
        var table = AddTable(model, "account");
        var id = AddColumn(table, "id", typeof(int), nullable: false, primaryKey: true, autoIncrement: true);
        AddColumn(table, "display_name", typeof(string), nullable: false, defaultValue: "anonymous");
        table.ColumnIndices.Add(new ColumnIndex("idx_account_display_name", IndexCharacteristic.Simple, IndexType.BTREE, [FindColumn(table, "display_name")]));
        var database = CreateDatabase();

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
        var model = CreateDatabase();
        var modelTable = AddTable(model, "account");
        AddColumn(modelTable, "id", typeof(int), nullable: false);
        AddColumn(modelTable, "nickname", typeof(string), nullable: true, defaultValue: "new");

        var database = CreateDatabase();
        var databaseTable = AddTable(database, "account");
        AddColumn(databaseTable, "id", typeof(int), nullable: false);
        AddColumn(databaseTable, "legacy_name", typeof(string), nullable: true);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MySQL);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MySQL, differences);

        await Assert.That(script).Contains("ALTER TABLE `account` ADD COLUMN `nickname` VARCHAR(40) DEFAULT 'new' NULL;");
        await Assert.That(script).Contains("-- DESTRUCTIVE ExtraColumn account.legacy_name");
        await Assert.That(script).Contains("-- Manual review required. No SQL was generated for this difference.");
    }

    [Test]
    public async Task Generate_MariaDbMissingUniqueIndex_CreatesProviderSpecificIndex()
    {
        var model = CreateDatabase();
        var modelTable = AddTable(model, "account");
        AddColumn(modelTable, "tenant_id", typeof(int), nullable: false);
        AddColumn(modelTable, "account_no", typeof(int), nullable: false);
        modelTable.ColumnIndices.Add(new ColumnIndex(
            "ux_account_tenant_account",
            IndexCharacteristic.Unique,
            IndexType.BTREE,
            [FindColumn(modelTable, "tenant_id"), FindColumn(modelTable, "account_no")]));

        var database = CreateDatabase();
        var databaseTable = AddTable(database, "account");
        AddColumn(databaseTable, "tenant_id", typeof(int), nullable: false);
        AddColumn(databaseTable, "account_no", typeof(int), nullable: false);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MariaDB);

        var script = new SchemaDiffScriptGenerator().Generate(DatabaseType.MariaDB, differences);

        await Assert.That(script).Contains("CREATE UNIQUE INDEX `ux_account_tenant_account` USING BTREE ON `account` (`tenant_id`, `account_no`);");
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
        System.Type csType,
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

    private static DatabaseColumnType GetColumnType(DatabaseType databaseType, System.Type csType)
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

    private static string ToCsName(string value) =>
        string.Join(
            "",
            value.Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
}
