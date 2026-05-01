using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Metadata;
using DataLinq.Validation;

namespace DataLinq.Tests.Unit.Core;

public class SchemaComparerTests
{
    [Test]
    public async Task Compare_TablePresence_ReturnsDeterministicDifferences()
    {
        var model = CreateDatabase(("account", ["id"]), ("invoice", ["id"]));
        var database = CreateDatabase(("account", ["id"]), ("audit_log", ["id"]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingTable, SchemaDifferenceKind.ExtraTable]);
        await Assert.That(differences.Select(x => x.Path).ToArray())
            .IsEquivalentTo(["invoice", "audit_log"]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.MissingTable).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Additive);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraTable).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Destructive);
    }

    [Test]
    public async Task Compare_ColumnPresence_ReturnsDeterministicDifferences()
    {
        var model = CreateDatabase(("account", ["id", "display_name"]));
        var database = CreateDatabase(("account", ["id", "legacy_name"]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingColumn, SchemaDifferenceKind.ExtraColumn]);
        await Assert.That(differences.Select(x => x.Path).ToArray())
            .IsEquivalentTo(["account.display_name", "account.legacy_name"]);
    }

    [Test]
    public async Task Compare_SQLiteIdentifiers_AreCaseInsensitiveForPresence()
    {
        var model = CreateDatabase(("Account", ["Id", "Display_Name"]));
        var database = CreateDatabase(("account", ["id", "display_name"]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Capabilities_ReflectPhaseFourSupportBoundary()
    {
        var sqlite = SchemaValidationCapabilities.For(DatabaseType.SQLite);
        var mariaDb = SchemaValidationCapabilities.For(DatabaseType.MariaDB);

        await Assert.That(sqlite.TableNameComparison).IsEqualTo(SchemaIdentifierComparison.OrdinalIgnoreCase);
        await Assert.That(sqlite.CompareColumnTypes).IsTrue();
        await Assert.That(sqlite.CompareNullability).IsTrue();
        await Assert.That(sqlite.ComparePrimaryKeys).IsTrue();
        await Assert.That(sqlite.CompareAutoIncrement).IsTrue();
        await Assert.That(sqlite.CompareDefaults).IsTrue();
        await Assert.That(sqlite.CompareIndexes).IsTrue();
        await Assert.That(sqlite.CompareChecks).IsFalse();
        await Assert.That(sqlite.CompareComments).IsFalse();

        await Assert.That(mariaDb.TableNameComparison).IsEqualTo(SchemaIdentifierComparison.RequiresProviderConfiguration);
        await Assert.That(mariaDb.ColumnNameComparison).IsEqualTo(SchemaIdentifierComparison.OrdinalIgnoreCase);
        await Assert.That(mariaDb.CompareColumnTypes).IsTrue();
        await Assert.That(mariaDb.CompareNullability).IsTrue();
        await Assert.That(mariaDb.ComparePrimaryKeys).IsTrue();
        await Assert.That(mariaDb.CompareAutoIncrement).IsTrue();
        await Assert.That(mariaDb.CompareDefaults).IsTrue();
        await Assert.That(mariaDb.CompareIndexes).IsTrue();
        await Assert.That(mariaDb.CompareChecks).IsTrue();
        await Assert.That(mariaDb.CompareComments).IsTrue();
    }

    [Test]
    public async Task Compare_ColumnShape_ReturnsAmbiguousMismatches()
    {
        var model = CreateDatabase(("account", ["id", "display_name"]));
        var database = CreateDatabase(("account", ["id", "display_name"]));
        var modelColumn = FindColumn(model, "account", "display_name");
        var databaseColumn = FindColumn(database, "account", "display_name");

        modelColumn.SetNullable();
        modelColumn.ValueProperty.SetAttributes([new DefaultAttribute("anonymous")]);
        databaseColumn.GetDbTypeFor(DatabaseType.SQLite)!.SetName("text");
        databaseColumn.SetAutoIncrement();
        databaseColumn.ValueProperty.SetAttributes([new DefaultAttribute("legacy")]);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([
                SchemaDifferenceKind.ColumnTypeMismatch,
                SchemaDifferenceKind.ColumnNullabilityMismatch,
                SchemaDifferenceKind.ColumnAutoIncrementMismatch,
                SchemaDifferenceKind.ColumnDefaultMismatch
            ]);
        await Assert.That(differences.All(x => x.Safety == SchemaDifferenceSafety.Ambiguous)).IsTrue();
    }

    [Test]
    public async Task Compare_DefaultCodeExpressionDifference_DoesNotCreateSchemaDrift()
    {
        var model = CreateDatabase(("account", ["id", "display_name"]));
        var database = CreateDatabase(("account", ["id", "display_name"]));
        var modelColumn = FindColumn(model, "account", "display_name");
        var databaseColumn = FindColumn(database, "account", "display_name");

        modelColumn.ValueProperty.SetAttributes([new DefaultAttribute("anonymous").SetCodeExpression("\"anonymous\"")]);
        databaseColumn.ValueProperty.SetAttributes([new DefaultAttribute("anonymous")]);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences).IsEmpty();
    }

    [Test]
    public async Task Compare_Indexes_ReturnsAdditiveAndDestructiveDifferences()
    {
        var model = CreateDatabase(("account", ["id", "accounting_year", "account_number"]));
        var database = CreateDatabase(("account", ["id", "accounting_year", "account_number"]));
        var modelTable = FindTable(model, "account");
        var databaseTable = FindTable(database, "account");

        modelTable.ColumnIndices.Add(new ColumnIndex(
            "ux_account_year_number",
            IndexCharacteristic.Unique,
            IndexType.BTREE,
            [FindColumn(model, "account", "accounting_year"), FindColumn(model, "account", "account_number")]));
        databaseTable.ColumnIndices.Add(new ColumnIndex(
            "idx_account_year",
            IndexCharacteristic.Simple,
            IndexType.BTREE,
            [FindColumn(database, "account", "accounting_year")]));

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingIndex, SchemaDifferenceKind.ExtraIndex]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.MissingIndex).Severity)
            .IsEqualTo(SchemaDifferenceSeverity.Error);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ExtraIndex).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Destructive);
    }

    [Test]
    public async Task Compare_ForeignKeys_ReturnsOrderedRelationDifferences()
    {
        var model = CreateDatabase(("order_header", ["tenant_id", "order_no"]), ("order_line", ["tenant_id", "order_no"]));
        var database = CreateDatabase(("order_header", ["tenant_id", "order_no"]), ("order_line", ["tenant_id", "order_no"]));

        AddForeignKey(
            model,
            "FK_order_line_header",
            "order_line",
            ["tenant_id", "order_no"],
            "order_header",
            ["tenant_id", "order_no"]);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.SQLite);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([SchemaDifferenceKind.MissingForeignKey]);
        await Assert.That(differences.Single().Path).IsEqualTo("order_line.FK_order_line_header");
        await Assert.That(differences.Single().Safety).IsEqualTo(SchemaDifferenceSafety.Additive);
    }

    [Test]
    public async Task Compare_MariaDbChecksAndComments_UsesProviderSpecificMetadata()
    {
        var model = CreateDatabase(("account", ["id"]));
        var database = CreateDatabase(("account", ["id"]));
        var modelTable = FindTable(model, "account");
        var databaseTable = FindTable(database, "account");
        var modelColumn = FindColumn(model, "account", "id");
        var databaseColumn = FindColumn(database, "account", "id");

        modelTable.Model.SetAttributes([
            new CheckAttribute(DatabaseType.MariaDB, "CK_account_id", "`id` > 0"),
            new CommentAttribute(DatabaseType.MariaDB, "model table comment")
        ]);
        databaseTable.Model.SetAttributes([
            new CheckAttribute(DatabaseType.MariaDB, "CK_account_legacy", "`id` >= 0"),
            new CommentAttribute(DatabaseType.MariaDB, "database table comment")
        ]);
        modelColumn.ValueProperty.SetAttributes([new CommentAttribute(DatabaseType.MariaDB, "model column comment")]);
        databaseColumn.ValueProperty.SetAttributes([new CommentAttribute(DatabaseType.MariaDB, "database column comment")]);

        var differences = SchemaComparer.Compare(model, database, DatabaseType.MariaDB);

        await Assert.That(differences.Select(x => x.Kind).ToArray())
            .IsEquivalentTo([
                SchemaDifferenceKind.MissingCheck,
                SchemaDifferenceKind.ExtraCheck,
                SchemaDifferenceKind.TableCommentMismatch,
                SchemaDifferenceKind.ColumnCommentMismatch
            ]);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.TableCommentMismatch).Severity)
            .IsEqualTo(SchemaDifferenceSeverity.Info);
        await Assert.That(differences.Single(x => x.Kind == SchemaDifferenceKind.ColumnCommentMismatch).Safety)
            .IsEqualTo(SchemaDifferenceSafety.Informational);
    }

    private static DatabaseDefinition CreateDatabase(params (string tableName, string[] columns)[] tables)
    {
        var database = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "DataLinq.Tests", ModelCsType.Class));
        var tableModels = tables.Select(table =>
        {
            var model = new ModelDefinition(new CsTypeDeclaration(ToCsName(table.tableName), "DataLinq.Tests", ModelCsType.Class));
            var tableDefinition = new TableDefinition(table.tableName);
            var tableModel = new TableModel(ToCsName(table.tableName), database, model, tableDefinition);
            var columns = table.columns.Select((columnName, index) =>
            {
                var property = new ValueProperty(ToCsName(columnName), new CsTypeDeclaration(typeof(int)), model, []);
                var column = new ColumnDefinition(columnName, tableDefinition);
                column.SetIndex(index);
                column.AddDbType(new DatabaseColumnType(DatabaseType.SQLite, "integer"));
                column.AddDbType(new DatabaseColumnType(DatabaseType.MariaDB, "int", signed: true));
                column.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, "int", signed: true));
                column.SetValueProperty(property);
                model.AddProperty(property);
                return column;
            });

            tableDefinition.SetColumns(columns);
            return tableModel;
        });

        database.SetTableModels(tableModels);
        return database;
    }

    private static TableDefinition FindTable(DatabaseDefinition database, string tableName) =>
        database.TableModels.Single(x => x.Table.DbName == tableName).Table;

    private static ColumnDefinition FindColumn(DatabaseDefinition database, string tableName, string columnName) =>
        FindTable(database, tableName).Columns.Single(x => x.DbName == columnName);

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
            foreignKeyColumns.Select(column => FindColumn(database, foreignKeyTableName, column)).ToList());
        var candidateKeyIndex = new ColumnIndex(
            $"{constraintName}_candidate",
            IndexCharacteristic.PrimaryKey,
            IndexType.BTREE,
            candidateKeyColumns.Select(column => FindColumn(database, candidateKeyTableName, column)).ToList());
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

    private static string ToCsName(string value)
    {
        return string.Join(
            "",
            value.Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1)));
    }
}
